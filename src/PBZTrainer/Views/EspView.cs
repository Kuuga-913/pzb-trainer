using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>透视页：设置 + 状态；目标框画在游戏画面上的悬浮层里。</summary>
    public sealed class EspView : Grid
    {
        private readonly BridgeClient _bridge;
        private readonly OverlayService _overlay;
        private readonly TextBox _log = Ui.LogBox(88);
        private readonly TextBlock _status = Ui.Sub("透视未开启");
        private readonly TextBlock _stats = Ui.Sub("");
        private readonly ToggleButton _enable;
        private readonly Dictionary<string, ToggleButton> _flags = new();
        private readonly Dictionary<string, TextBox> _numbers = new();
        private bool _busy;
        private bool _syncing;

        private static readonly (string Key, string Label)[] FlagDefs =
        {
            ("enemies", "敌人"), ("bosses", "首领"), ("npcs", "NPC"), ("chests", "宝箱"),
            ("pickups", "可拾取"), ("interactables", "可交互"), ("health", "血量"), ("names", "名称"),
            ("box", "方框"), ("rays", "连线"), ("bones", "骨骼"), ("shachi", "杀气")
        };

        private static readonly (string Key, string Label, string Def)[] NumberDefs =
        {
            ("distance", "距离（米）", "300"), ("limit", "数量上限", "80"), ("font", "字号", "16"),
            ("thickness", "线宽", "1.5"), ("opacity", "不透明度", "1"), ("color", "配色", "1"), ("boxstyle", "框样式", "0")
        };

        public EspView(BridgeClient bridge, OverlayService overlay)
        {
            _bridge = bridge;
            _overlay = overlay;
            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Ui.Col(2, Ui.Text("透视（ESP）", 17, "Text", true),
                Ui.Sub("按类型勾选目标，开启后在游戏画面上直接画框/标签；关闭本页不会停止，需在上方开关关闭。")));

            _enable = new ToggleButton { Style = Ui.Style("Switch") };
            _enable.Checked += (_, __) => _ = ToggleAsync(true);
            _enable.Unchecked += (_, __) => _ = ToggleAsync(false);
            var right = Ui.Row(8, Ui.Sub("开启"), _enable);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            header.Children.Add(right);
            Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var form = new StackPanel();
            var flagWrap = new WrapPanel();
            foreach (var (key, label) in FlagDefs)
            {
                var cb = new ToggleButton
                {
                    Content = label,
                    Style = Ui.Style("ChipStyle"),
                    FontSize = 11.5,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 6),
                    IsChecked = key is not ("rays" or "bones")
                };
                _flags[key] = cb;
                flagWrap.Children.Add(cb);
            }
            form.Children.Add(Ui.Sub("显示内容"));
            form.Children.Add(flagWrap);
            form.Children.Add(new Border { Height = 8 });

            var grid = new UniformGrid { Columns = 2 };
            foreach (var (key, label, def) in NumberDefs)
            {
                var box = Ui.Input(def, 88, 26);
                _numbers[key] = box;
                var cell = Ui.Col(2, Ui.Sub(label), box);
                cell.Margin = new Thickness(0, 0, 8, 8);
                grid.Children.Add(cell);
            }
            form.Children.Add(Ui.Sub("参数"));
            form.Children.Add(grid);
            form.Children.Add(Ui.Row(8,
                Ui.Button("应用设置", () => _ = ApplyAsync(), "AccentBtn", 28, 92, 11.5),
                Ui.Button("刷新状态", () => _ = RefreshAsync(), "Ghost", 28, 84, 11.5)));

            var left = Ui.Panel(new ScrollViewer
            {
                Content = form,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            left.Margin = new Thickness(0, 0, 12, 0);
            body.Children.Add(left);

            var side = Ui.Col(8,
                Ui.Panel(Ui.Col(6, Ui.Sub("状态"), _status, _stats)),
                Ui.Panel(Ui.Col(6, Ui.Sub("事件"), _log)));
            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            Grid.SetRow(body, 1);
            Children.Add(body);

            _overlay.Changed += () => Dispatcher.BeginInvoke(new Action(Sync));
            Loaded += (_, __) => Sync();
        }

        private void Sync()
        {
            _status.Text = _overlay.EspStatus;
            if (_overlay.EspEnabled != (_enable.IsChecked == true))
                SyncSwitch(_overlay.EspEnabled);
        }

        private void SyncSwitch(bool on)
        {
            _syncing = true;
            try { _enable.IsChecked = on; }
            finally { _syncing = false; }
        }

        private JsonObject BuildSettings()
        {
            var s = new JsonObject { ["enabled"] = _enable.IsChecked == true };
            foreach (var (key, _) in FlagDefs) s[key] = _flags[key].IsChecked == true;
            s["aim"] = false;
            foreach (var (key, _, def) in NumberDefs)
            {
                string text = _numbers[key].Text;
                if (key is "thickness" or "opacity")
                    s[key] = double.TryParse(text, out var d) ? d : double.Parse(def, System.Globalization.CultureInfo.InvariantCulture);
                else
                    s[key] = int.TryParse(text, out var i) ? i : int.Parse(def);
            }
            return s;
        }

        private async Task ToggleAsync(bool enabled)
        {
            if (_busy || _syncing) return;
            _busy = true;
            try
            {
                if (!_bridge.IsConnected)
                {
                    _status.Text = "请先连接游戏";
                    SyncSwitch(false);
                    return;
                }
                bool ok = await _overlay.SetEspAsync(BuildSettings(), enabled);
                if (!ok && enabled) SyncSwitch(false);
                _status.Text = _overlay.EspStatus;
                Ui.Log(_log, _overlay.EspStatus);
            }
            finally { _busy = false; }
        }

        private async Task ApplyAsync()
        {
            if (!_overlay.EspEnabled)
            {
                Ui.Log(_log, "请先打开透视开关");
                return;
            }
            bool ok = await _overlay.SetEspAsync(BuildSettings(), true);
            Ui.Log(_log, ok ? "设置已应用" : _overlay.EspStatus);
        }

        private async Task RefreshAsync()
        {
            try
            {
                var st = await _bridge.EspStatusAsync();
                if (!st.Flag("ok")) { _stats.Text = st.Str("message") ?? "状态不可用"; return; }
                _stats.Text = $"绘制 {st["drawn"].Num():0} · {st["fps"].Num():0.0} fps · 渲染 {st["renderMs"].Num():0.0} ms · 场景 {st["sceneCount"].Num():0}" +
                              (string.IsNullOrEmpty(st.Str("lastError")) ? "" : " · " + st.Str("lastError"));
            }
            catch (Exception ex) { _stats.Text = "刷新失败：" + ex.Message; }
        }
    }
}
