using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>
    /// 游戏画面上的两个悬浮层（小地图 / 透视）统一由这里驱动：
    /// 不管当前停留在哪一页，只要开启就持续轮询并重绘。
    /// </summary>
    public sealed class OverlayService
    {
        private readonly BridgeClient _bridge;
        private readonly DispatcherTimer _minimapTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer _espTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

        private MinimapOverlayWindow _minimap;
        private EspOverlayWindow _esp;

        public bool MinimapEnabled { get; private set; }
        public bool EspEnabled { get; private set; }
        public JsonObject EspSettings { get; private set; }

        public string MinimapStatus { get; private set; } = "小地图未开启";
        public string EspStatus { get; private set; } = "透视未开启";
        public event Action Changed;

        private long _lastMinimapFrame;
        private int _espOffTicks;
        private string _minimapStamp = "";
        private double _scale = 2000; // 每 1 uv 对应的像素

        public OverlayService(BridgeClient bridge)
        {
            _bridge = bridge;
            _minimapTimer.Tick += async (_, __) => await TickMinimapAsync();
            _espTimer.Tick += async (_, __) => await TickEspAsync();
        }

        private void Raise() => Changed?.Invoke();

        // ---------- 小地图 ----------

        public async Task<bool> SetMinimapAsync(bool enabled, bool clickEnabled)
        {
            if (!_bridge.IsConnected)
            {
                MinimapStatus = "请先连接游戏";
                Raise();
                return false;
            }

            // 目录还在读时 configure 会失败，重试几次
            JsonObject r = null;
            for (int i = 0; i < 6; i++)
            {
                r = await _bridge.MinimapConfigureAsync(enabled, clickEnabled);
                if (r.Flag("ok")) break;
                if (!enabled) break;
                await Task.Delay(1200);
            }

            if (r == null || !r.Flag("ok"))
            {
                MinimapStatus = "小地图暂不可用：" + (r?.Str("message") ?? "未知原因");
                Raise();
                return false;
            }

            MinimapEnabled = enabled;
            if (enabled)
            {
                EnsureMinimapWindow(clickEnabled);
                _minimapTimer.Start();
                MinimapStatus = "小地图已开启";
                await TickMinimapAsync();
            }
            else
            {
                _minimapTimer.Stop();
                CloseMinimapWindow();
                MinimapStatus = "小地图已关闭";
            }
            Raise();
            return true;
        }

        private void EnsureMinimapWindow(bool clickEnabled)
        {
            if (_minimap == null)
            {
                _minimap = new MinimapOverlayWindow(_bridge, () => _minimapStamp, () => _scale);
                _minimap.TravelRequested += async id =>
                {
                    try { await _bridge.MinimapTravelAsync(_minimapStamp, id); } catch { }
                };
                _minimap.Show();
            }
            _minimap.ClickThrough = !clickEnabled;
        }

        private void CloseMinimapWindow()
        {
            try { _minimap?.Close(); } catch { }
            _minimap = null;
        }

        private async Task TickMinimapAsync()
        {
            if (!MinimapEnabled || !_bridge.IsConnected) return;
            try
            {
                var f = await _bridge.MinimapFrameAsync(_lastMinimapFrame);
                _espOffTicks = 0;
                if (!f.Flag("ok"))
                {
                    MinimapStatus = f.Str("message") ?? "小地图数据不可用";
                    Raise();
                    return;
                }

                _minimapStamp = f.Str("stamp") ?? _minimapStamp;
                if (f["sampledAtMs"] != null) _lastMinimapFrame = (long)f["sampledAtMs"].Num();

                var player = f["player"] as JsonArray;
                double pu = player != null && player.Count > 1 ? player[0].Num() : 0.5;
                double pv = player != null && player.Count > 1 ? player[1].Num() : 0.5;
                double heading = f["heading"].Num();

                var marks = new List<MinimapMark>();
                if (f["marks"] is JsonArray ma)
                {
                    foreach (var m in ma.OfType<JsonObject>())
                    {
                        if (m["uv"] is not JsonArray uv || uv.Count < 2) continue;
                        marks.Add(new MinimapMark
                        {
                            U = uv[0].Num(), V = uv[1].Num(),
                            Title = m.Str("title") ?? "",
                            Category = m.Str("category") ?? "",
                            Collected = m.Flag("collected"),
                            Id = m.Str("id") ?? ""
                        });
                    }
                }
                if (f["enemies"] is JsonArray ea)
                {
                    foreach (var m in ea.OfType<JsonObject>())
                    {
                        if (m["uv"] is not JsonArray uv || uv.Count < 2) continue;
                        marks.Add(new MinimapMark
                        {
                            U = uv[0].Num(), V = uv[1].Num(),
                            Title = m.Str("title") ?? "敌人",
                            Category = "enemies",
                            Distance = m["distance"].Num(),
                            Id = m.Str("id") ?? ""
                        });
                    }
                }

                // 自适应比例：用带距离的敌人样本反推 uv→像素
                double scale = 2200;
                var samples = marks.Where(m => m.Distance > 1 && m.Category == "enemies").ToList();
                if (samples.Count > 0)
                {
                    double acc = 0; int n = 0;
                    foreach (var m in samples.Take(6))
                    {
                        double du = Math.Sqrt(Math.Pow(m.U - pu, 2) + Math.Pow(m.V - pv, 2));
                        if (du > 0.0005) { acc += m.Distance / du; n++; }
                    }
                    if (n > 0) scale = Math.Max(600, Math.Min(6000, acc / n));
                }
                _scale = scale;

                _minimap?.Render(marks, pu, pv, heading, f.Str("title") ?? "", (int)f["layer"].Num());
                MinimapStatus = $"小地图实时刷新 · {marks.Count} 个标记";
                Raise();
            }
            catch (Exception ex)
            {
                MinimapStatus = "小地图刷新失败：" + ex.Message;
                Raise();
            }
        }

        // ---------- 透视 ----------

        public async Task<bool> SetEspAsync(JsonObject settings, bool enabled)
        {
            if (!_bridge.IsConnected)
            {
                EspStatus = "请先连接游戏";
                Raise();
                return false;
            }
            var r = await _bridge.EspConfigureAsync(settings, enabled);
            if (r == null || !r.Flag("ok"))
            {
                EspStatus = "透视操作失败：" + (r?.Str("message") ?? "");
                Raise();
                return false;
            }
            EspEnabled = enabled;
            _espOffTicks = 0;
            EspSettings = settings;
            if (enabled)
            {
                if (_esp == null)
                {
                    _esp = new EspOverlayWindow();
                    _esp.Show();
                }
                _espTimer.Start();
                EspStatus = "透视已开启";
                await TickEspAsync();
            }
            else
            {
                _espTimer.Stop();
                try { _esp?.Close(); } catch { }
                _esp = null;
                EspStatus = "透视已关闭";
            }
            Raise();
            return true;
        }

        private async Task TickEspAsync()
        {
            if (!EspEnabled || !_bridge.IsConnected) return;
            try
            {
                var f = await _bridge.EspFrameAsync();
                _espOffTicks = 0;
                if (!f.Flag("ok"))
                {
                    EspStatus = f.Str("message") ?? "透视数据不可用";
                    Raise();
                    return;
                }
                if (!f.Flag("enabled"))
                {
                    if (++_espOffTicks < 3) { EspStatus = "透视等待游戏侧确认…"; Raise(); return; }
                    EspEnabled = false;
                    _espTimer.Stop();
                    try { _esp?.Close(); } catch { }
                    _esp = null;
                    EspStatus = "透视已在游戏侧关闭";
                    Raise();
                    return;
                }

                var boxes = new List<EspBox>();
                var targets = f["targets"] as JsonArray;
                int drawn = 0;
                if (targets != null)
                {
                    foreach (var t in targets.OfType<JsonObject>())
                    {
                        double l = t["left"].Num(double.NaN), tp = t["top"].Num(double.NaN);
                        double rr = t["right"].Num(double.NaN), bb = t["bottom"].Num(double.NaN);
                        if (double.IsNaN(l) || double.IsNaN(tp)) continue;

                        var box = new EspBox
                        {
                            Id = t.Str("id") ?? "",
                            Left = l, Top = tp,
                            Right = double.IsNaN(rr) ? l + 24 : rr,
                            Bottom = double.IsNaN(bb) ? tp + 24 : bb,
                            Label = t.Str("label") ?? t.Str("name") ?? "",
                            Category = t.Str("category") ?? "",
                            IsPoint = t.Flag("point"),
                            Radius = t["radius"].Num(6)
                        };
                        boxes.Add(box);
                        drawn++;
                    }
                }

                _esp?.Render(boxes, _bridge.TargetPid);
                EspStatus = drawn > 0 ? $"透视实时刷新 · {drawn} 个目标" : "透视已开启 · 当前视野内没有目标";
                Raise();
            }
            catch (Exception ex)
            {
                EspStatus = "透视刷新失败：" + ex.Message;
                Raise();
            }
        }

        public void StopAll()
        {
            _minimapTimer.Stop();
            _espTimer.Stop();
            CloseMinimapWindow();
            try { _esp?.Close(); } catch { }
            _esp = null;
        }
    }

    public sealed class MinimapMark
    {
        public double U, V, Distance;
        public string Title = "", Category = "", Id = "";
        public bool Collected;
    }

    public sealed class EspBox
    {
        public string Id = "";
        public double Left, Top, Right, Bottom, Radius;
        public string Label = "", Category = "";
        public bool IsPoint;
    }
}
