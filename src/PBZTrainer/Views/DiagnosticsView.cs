using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>连接诊断：把能力、目录、关键状态抓成一份可直接发给作者的文本。</summary>
    public sealed class DiagnosticsView : Grid
    {
        private readonly BridgeClient _bridge;
        private readonly TextBox _output = new()
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 11.5,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Ui.Brush("SubText"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        private readonly TextBlock _status = Ui.Sub("点「开始诊断」收集信息");

        public DiagnosticsView(BridgeClient bridge)
        {
            _bridge = bridge;
            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Ui.Col(2, Ui.Text("连接诊断", 17, "Text", true),
                Ui.Sub("连接异常、一直显示「等待进入场景」、某个目录读不出来时，先跑一次诊断，把结果发给作者。")));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Ui.Button("开始诊断", () => _ = RunAsync(), "AccentBtn", 30, 92, 12));
            var copy = Ui.Button("复制", () =>
            {
                try { Clipboard.SetText(_output.Text ?? ""); _status.Text = "已复制到剪贴板"; }
                catch (Exception ex) { _status.Text = "复制失败：" + ex.Message; }
            }, "Ghost", 30, 62, 12);
            copy.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(copy);
            var save = Ui.Button("保存到桌面", () =>
            {
                try
                {
                    string path = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "影之刃零修改器-诊断.txt");
                    System.IO.File.WriteAllText(path, _output.Text ?? "", Encoding.UTF8);
                    _status.Text = "已保存：" + path;
                }
                catch (Exception ex) { _status.Text = "保存失败：" + ex.Message; }
            }, "Ghost", 30, 104, 12);
            save.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(save);
            Grid.SetColumn(buttons, 1);
            header.Children.Add(buttons);
            Children.Add(header);

            var body = Ui.Panel(null, new Thickness(0));
            body.Child = new ScrollViewer
            {
                Content = _output,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(body, 1);
            Children.Add(body);

            var bar = Ui.Panel(_status);
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(bar, 1);
            Children.Add(bar);

            Loaded += (_, __) => { if (string.IsNullOrEmpty(_output.Text)) _ = RunAsync(); };
        }

        private async Task RunAsync()
        {
            var sb = new StringBuilder();
            void Line(string s) => sb.AppendLine(s);

            Line("影之刃零修改器 · 连接诊断");
            Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("程序版本：v1.0.5（本地构建）");
            Line("系统：" + Environment.OSVersion.VersionString + " · " + (Environment.Is64BitProcess ? "x64" : "x86"));
            Line("管理员：" + (IsAdmin() ? "是" : "否"));
            Line("");

            var games = Injector.Candidates().Select(p => { try { return p.Id + " " + p.MainModule.FileName; } catch { return p.Id + " (路径不可读)"; } }).ToList();
            Line("检测到 PBZ 进程：" + (games.Count == 0 ? "无" : string.Join("；", games)));

            var game = Injector.FindGame();
            Line("选中进程：" + (game == null ? "无（可能权限不足或未启动）" : $"{game.Pid} · {game.ExePath}"));
            Line("组件已加载：" + (game != null ? (Injector.BridgeIsLoaded(game.Pid) ? "是" : "否") : "-"));
            Line("");

            if (!_bridge.IsConnected)
            {
                Line("当前未连接。状态：" + _bridge.State + " · " + _bridge.LastMessage);
                Line("可尝试：关闭其他修改器窗口 → 以管理员运行 → 完全退出游戏重开。");
                _output.Text = sb.ToString();
                _status.Text = "未连接，已生成基础信息";
                return;
            }

            try
            {
                var caps = await _bridge.CapabilitiesAsync();
                Line("=== capabilities ===");
                Line("ready = " + (caps?.Flag("ready") == true) + " · catalogReady = " + (caps?.Flag("catalogReady") == true));
                Line("message = " + (caps?.Str("message") ?? ""));
                Line("world = " + (caps?.Str("world") ?? ""));
                Line("scene = " + (caps?.Str("scene") ?? ""));
                Line("player = " + (caps?.Str("player") ?? ""));
                if (caps?["position"] is JsonArray pos)
                    Line("position = " + string.Join(", ", pos.Select(p => p == null ? "" : p.Num().ToString("0.##"))));
                Line("");
                Line("features 数量 = " + ((caps?["features"] as JsonArray)?.Count ?? 0));
                Line("");

                // 已开启的功能（含数值）：判断“伤害异常”到底是不是本工具引起的
                Line("=== 当前已开启的功能（含数值） ===");
                var feats = caps?["features"] as JsonArray;
                int on = 0;
                if (feats != null)
                {
                    foreach (var f in feats.OfType<JsonObject>())
                    {
                        if (!f.Flag("enabled")) continue;
                        on++;
                        Line($"  {f.Str("id")}  value={f["value"]}");
                    }
                }
                if (on == 0) Line("  （没有任何功能处于开启状态）");
                Line("");
                Line("说明：如果 Boss / 敌人血量异常容易被打空，但上面是空的，");
                Line("      那就不是本工具造成的——剧情 / 教程首领本身血量就很少。");
                Line("");

                Line("=== 目录读取 ===");
                foreach (var type in new[] { "items", "inventory", "enemies", "locations", "quests", "achievements", "appearances", "resources", "weapon_states", "player_skills", "boss_moves", "character_models" })
                {
                    try
                    {
                        var r = await _bridge.CatalogAsync(type);
                        int n = (r?["items"] as JsonArray)?.Count ?? 0;
                        string msg = r?.Str("message") ?? "";
                        Line($"{type,-18} ok={r?.Flag("ok")} n={n} {msg}");
                    }
                    catch (Exception ex)
                    {
                        Line($"{type,-18} 异常：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Line("诊断异常：" + ex.Message);
            }

            _output.Text = sb.ToString();
            _status.Text = "诊断完成 · 点「复制」发给作者";
        }

        private static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
