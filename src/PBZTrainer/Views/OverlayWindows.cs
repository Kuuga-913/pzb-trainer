using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>贴在游戏画面上的透明悬浮层基类（默认点击穿透、不抢焦点）。</summary>
    public abstract class OverlayBase : Window
    {
        protected readonly Canvas Root = new();
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private bool _clickThrough = true;

        protected OverlayBase()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            Focusable = false;
            Content = Root;
            SourceInitialized += (_, __) => ApplyClickThrough();
        }

        public bool ClickThrough
        {
            get => _clickThrough;
            set { _clickThrough = value; ApplyClickThrough(); }
        }

        private void ApplyClickThrough()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            int style = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            style = _clickThrough ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
            SetWindowLong(h, GWL_EXSTYLE, style);
        }

        /// <summary>把窗口贴到游戏客户区；坐标按物理像素处理（内部用 1/dpi 缩放对齐）。</summary>
        protected void PlaceOverGame(int pid, int offsetX = 0, int offsetY = 0, int width = 0, int height = 0, bool fromRight = false)
        {
            var rect = GameWindow.ClientRect(pid);
            if (rect == null) return;

            int w = width > 0 ? width : rect.Value.Width;
            int h = height > 0 ? height : rect.Value.Height;
            int x = fromRight ? rect.Value.Left + rect.Value.Width - w - offsetX : rect.Value.Left + offsetX;
            int y = rect.Value.Top + offsetY;

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // 自己窗口在前台时先藏起来，别挡住修改器界面
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint fgPid);
            bool ownFront = fgPid == (uint)Environment.ProcessId;
            if (ownFront && IsVisible) Hide();
            else if (!ownFront && !IsVisible) Show();

            var dpi = VisualTreeHelper.GetDpi(this);
            Root.Width = w;
            Root.Height = h;
            Root.RenderTransform = new ScaleTransform(1.0 / dpi.DpiScaleX, 1.0 / dpi.DpiScaleY);
            Root.RenderTransformOrigin = new Point(0, 0);
        }
    }

    /// <summary>小地图悬浮层：以玩家为中心的雷达式地图，可点击标记传送。</summary>
    public sealed class MinimapOverlayWindow : OverlayBase
    {
        private const double Size = 300;
        private const double Pad = 10;
        private readonly BridgeClient _bridge;
        private readonly Func<string> _stamp;
        private readonly Func<double> _scale;
        private readonly Canvas _map = new();
        private readonly TextBlock _title = new();
        private readonly TextBlock _hint = new();
        private readonly List<(MinimapMark mark, Point pt)> _hit = new();

        public event Action<string, double, double> TravelRequested;
        public bool ClickEnabled { get; set; }

        public MinimapOverlayWindow(BridgeClient bridge, Func<string> stamp, Func<double> scale)
        {
            _bridge = bridge;
            _stamp = stamp;
            _scale = scale;

            var panel = new Border
            {
                Width = Size, Height = Size,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0B, 0x0B, 0x0E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xC0, 0x39, 0x2B)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            var grid = new Grid();
            grid.Children.Add(_map);
            _title.FontSize = 15;
            _title.Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xE7, 0xE0));
            _title.Margin = new Thickness(12, 8, 12, 0);
            _title.VerticalAlignment = VerticalAlignment.Top;
            _hint.FontSize = 12.5;
            _hint.Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0x8C, 0x83, 0x90));
            _hint.Margin = new Thickness(12, 0, 12, 8);
            _hint.VerticalAlignment = VerticalAlignment.Bottom;
            _hint.Text = "点击标记可传送";
            grid.Children.Add(_title);
            grid.Children.Add(_hint);
            panel.Child = grid;
            Root.Children.Add(panel);

            _map.MouseLeftButtonDown += (_, e) => OnClick(e.GetPosition(_map));
            Loaded += (_, __) => PlaceOverGame(_bridge.TargetPid, 24, 84, (int)Size, (int)Size, true);
        }

        private void OnClick(Point p)
        {
            if (ClickThrough)
            {
                _hint.Text = "点击穿透中：先打开「点击标记可传送」";
                return;
            }
            var hit = _hit.OrderBy(h => (h.pt - p).Length).FirstOrDefault();
            if (hit.mark == null) { _hint.Text = "没点中标记，靠近些再点"; return; }
            if ((hit.pt - p).Length > 16) { _hint.Text = "没点中标记，靠近些再点"; return; }
            if (string.IsNullOrEmpty(hit.mark.Id)) { _hint.Text = "该标记没有可用的传送 id"; return; }
            _hint.Text = "正在传送：" + hit.mark.Title;
            TravelRequested?.Invoke(hit.mark.Id, hit.mark.U, hit.mark.V);
        }

        /// <summary>外部显示一条提示（传送结果等）。</summary>
        public void SetHint(string text) => _hint.Text = text;

        public void Render(List<MinimapMark> marks, double pu, double pv, double heading, string title, int layer)
        {
            PlaceOverGame(_bridge.TargetPid, 24, 84, (int)Size, (int)Size, true);
            ClickThrough = !ClickEnabled;

            _map.Children.Clear();
            _hit.Clear();
            _title.Text = string.IsNullOrEmpty(title) ? "小地图" : $"{title}　第 {layer} 层";

            double cx = Size / 2, cy = Size / 2 + 6, radius = Size / 2 - 26;
            double scale = _scale();

            // 同心圆
            for (int i = 1; i <= 2; i++)
            {
                var ring = new Ellipse
                {
                    Width = radius * 2 * i / 2, Height = radius * 2 * i / 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0x8C, 0x83, 0x90)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ring, cx - radius * i / 2);
                Canvas.SetTop(ring, cy - radius * i / 2);
                _map.Children.Add(ring);
            }

            // 标记
            foreach (var m in marks)
            {
                double dx = (m.U - pu) * scale;
                double dy = -(m.V - pv) * scale;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                bool clamped = dist > radius;
                if (clamped && m.Category != "enemies") continue;   // 远处的收集点先不画
                if (clamped)
                {
                    dx = dx / dist * radius;
                    dy = dy / dist * radius;
                }

                double px = cx + dx, py = cy + dy;
                double dot = m.Category == "enemies" ? 7 : 8;

                var e = new Ellipse
                {
                    Width = dot, Height = dot,
                    Fill = MarkBrush(m),
                    Opacity = clamped ? 0.45 : 1.0,
                    ToolTip = m.Title + (m.Distance > 0 ? $"　{m.Distance:0} 米" : "")
                };
                Canvas.SetLeft(e, px - dot / 2);
                Canvas.SetTop(e, py - dot / 2);
                _map.Children.Add(e);

                if (!clamped) _hit.Add((m, new Point(px, py)));
            }

            // 玩家（三角形，按朝向旋转）
            var arrow = new Polygon
            {
                Points = new PointCollection { new Point(0, -10), new Point(7, 7), new Point(0, 3.5), new Point(-7, 7) },
                Fill = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xF0)),
                StrokeThickness = 1.2
            };
            Canvas.SetLeft(arrow, cx);
            Canvas.SetTop(arrow, cy);
            arrow.RenderTransformOrigin = new Point(0.5, 0.5);
            arrow.RenderTransform = new RotateTransform(heading * 180 / Math.PI + 90);
            _map.Children.Add(arrow);

            var mark = new TextBlock
            {
                Text = "KuugaOOO",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xC0, 0x39, 0x2B)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(mark, 10);
            Canvas.SetTop(mark, Size - 20);
            _map.Children.Add(mark);


        }

        private static Brush MarkBrush(MinimapMark m)
        {
            if (m.Collected) return new SolidColorBrush(Color.FromArgb(0x99, 0x8C, 0x83, 0x90));
            return m.Category switch
            {
                "enemies" => new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x3A)),
                "bells" => new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x4C)),
                "challenges" => new SolidColorBrush(Color.FromRgb(0xC2, 0x56, 0xF0)),
                "flowers" => new SolidColorBrush(Color.FromRgb(0x57, 0xC7, 0xB4)),
                _ => new SolidColorBrush(Color.FromRgb(0x57, 0xC7, 0xB4))
            };
        }
    }

    /// <summary>透视悬浮层：按目标屏幕坐标画方框 / 点标记，逐帧插值让移动跟手。</summary>
    public sealed class EspOverlayWindow : OverlayBase
    {
        private sealed class Visual
        {
            public FrameworkElement Shape;
            public TextBlock Label;
            public double X, Y, W, H;      // 当前显示位置
            public double TX, TY, TW, TH;  // 目标位置
            public long LastSeen;
            public bool Point;
        }

        private readonly Canvas _canvas = new();
        private readonly TextBlock _watermark = new();
        private readonly Dictionary<string, Visual> _visuals = new();
        private bool _hooked;

        public EspOverlayWindow()
        {
            _canvas.IsHitTestVisible = false;
            Root.Children.Add(_canvas);
            _watermark.Text = "";
            _watermark.FontSize = 15;
            _watermark.FontWeight = FontWeights.SemiBold;
            _watermark.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xC8));
            _watermark.Background = new SolidColorBrush(Color.FromArgb(0xC0, 0xC0, 0x39, 0x2B));
            _watermark.Padding = new Thickness(8, 2, 8, 2);
            _watermark.VerticalAlignment = VerticalAlignment.Top;
            _watermark.HorizontalAlignment = HorizontalAlignment.Left;
            _watermark.Margin = new Thickness(12, 8, 0, 0);
            Root.Children.Add(_watermark);
            ClickThrough = true;

            Loaded += (_, __) =>
            {
                if (_hooked) return;
                _hooked = true;
                CompositionTarget.Rendering += OnFrame;
            };
            Closed += (_, __) => CompositionTarget.Rendering -= OnFrame;
        }

        public void Render(List<EspBox> boxes, int pid)
        {
            PlaceOverGame(pid);
            long now = Environment.TickCount64;

            foreach (var b in boxes)
            {
                string key = string.IsNullOrEmpty(b.Id) ? (b.Category + b.Label) : b.Id;
                if (!_visuals.TryGetValue(key, out var v))
                {
                    v = Create(b);
                    v.X = b.Left; v.Y = b.Top; v.W = b.Right - b.Left; v.H = b.Bottom - b.Top;
                    v.TX = v.X; v.TY = v.Y; v.TW = v.W; v.TH = v.H;
                    _visuals[key] = v;
                    Apply(v);
                }
                v.TX = b.Left; v.TY = b.Top;
                v.TW = Math.Max(3, b.Right - b.Left); v.TH = Math.Max(3, b.Bottom - b.Top);
                v.LastSeen = now;

                if (v.Label != null && !string.IsNullOrEmpty(b.Label) && v.Label.Text != b.Label)
                    v.Label.Text = b.Label;
            }

            var stale = _visuals.Where(kv => now - kv.Value.LastSeen > 1200).Select(kv => kv.Key).ToList();
            foreach (var key in stale)
            {
                var v = _visuals[key];
                if (v.Shape != null) _canvas.Children.Remove(v.Shape);
                if (v.Label != null) _canvas.Children.Remove(v.Label);
                _visuals.Remove(key);
            }

            _watermark.Text = _visuals.Count > 0 ? $"ESP · {_visuals.Count} 个目标 · KuugaOOO" : "ESP 已开启 · 视野内暂无目标 · KuugaOOO";
        }

        private Visual Create(EspBox b)
        {
            var brush = ColorFor(b.Category);
            var v = new Visual { Point = b.IsPoint };

            if (b.IsPoint)
            {
                double r = Math.Max(5, b.Radius);
                v.Shape = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = brush,
                    StrokeThickness = 2.2,
                    Fill = Brushes.Transparent
                };
            }
            else
            {
                v.Shape = new Rectangle
                {
                    Stroke = brush,
                    StrokeThickness = 2.2,
                    Fill = Brushes.Transparent
                };
            }
            _canvas.Children.Add(v.Shape);

            if (!string.IsNullOrEmpty(b.Label))
            {
                v.Label = new TextBlock
                {
                    Text = b.Label,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = brush,
                    Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(4, 1, 4, 1),
                    IsHitTestVisible = false
                };
                _canvas.Children.Add(v.Label);
            }
            return v;
        }

        private void Apply(Visual v)
        {
            if (v.Shape is Ellipse e)
            {
                double r = Math.Max(v.W, v.H) / 2;
                e.Width = r * 2;
                e.Height = r * 2;
                Canvas.SetLeft(e, v.X + v.W / 2 - r);
                Canvas.SetTop(e, v.Y + v.H / 2 - r);
            }
            else if (v.Shape != null)
            {
                v.Shape.Width = v.W;
                v.Shape.Height = v.H;
                Canvas.SetLeft(v.Shape, v.X);
                Canvas.SetTop(v.Shape, v.Y);
            }
            if (v.Label != null)
            {
                Canvas.SetLeft(v.Label, v.X);
                Canvas.SetTop(v.Label, Math.Max(0, v.Y - 22));
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (_visuals.Count == 0) return;
            const double k = 0.32;
            foreach (var v in _visuals.Values)
            {
                double dx = v.TX - v.X, dy = v.TY - v.Y;
                double dw = v.TW - v.W, dh = v.TH - v.H;
                if (Math.Abs(dx) < 0.3 && Math.Abs(dy) < 0.3 && Math.Abs(dw) < 0.3 && Math.Abs(dh) < 0.3)
                {
                    if (v.X == v.TX && v.Y == v.TY) continue;
                    v.X = v.TX; v.Y = v.TY; v.W = v.TW; v.H = v.TH;
                }
                else
                {
                    v.X += dx * k; v.Y += dy * k; v.W += dw * k; v.H += dh * k;
                }
                Apply(v);
            }
        }

        private static Brush ColorFor(string category) => category switch
        {
            "enemies" => new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x3A)),
            "bosses" => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35)),
            "npcs" => new SolidColorBrush(Color.FromRgb(0x5A, 0xA9, 0xF0)),
            "chests" => new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x4C)),
            "pickups" => new SolidColorBrush(Color.FromRgb(0x57, 0xC7, 0xB4)),
            "interactables" => new SolidColorBrush(Color.FromRgb(0x9C, 0x7C, 0xF0)),
            _ => new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
        };
    }

    internal static class GameWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);

        public static (int Left, int Top, int Width, int Height)? ClientRect(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                IntPtr h = p.MainWindowHandle;
                if (h == IntPtr.Zero) return null;
                if (!GetClientRect(h, out var r)) return null;
                var pt = new POINT { X = 0, Y = 0 };
                if (!ClientToScreen(h, ref pt)) return null;
                int w = r.Right - r.Left, ht = r.Bottom - r.Top;
                if (w <= 0 || ht <= 0) return null;
                return (pt.X, pt.Y, w, ht);
            }
            catch { return null; }
        }
    }
}
