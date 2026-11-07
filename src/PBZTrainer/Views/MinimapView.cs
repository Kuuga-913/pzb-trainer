using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>小地图页：开关 + 图例 + 状态；真正的地图画在游戏画面右上角的悬浮层上。</summary>
    public sealed class MinimapView : Grid
    {
        private readonly BridgeClient _bridge;
        private readonly OverlayService _overlay;
        private readonly TextBlock _status = Ui.Sub("小地图未开启");
        private readonly TextBox _log = Ui.LogBox(80);
        private readonly ToggleButton _enable;
        private readonly ToggleButton _click;
        private bool _busy;
        private bool _syncing;

        public MinimapView(BridgeClient bridge, OverlayService overlay)
        {
            _bridge = bridge;
            _overlay = overlay;
            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Ui.Col(2, Ui.Text("小地图", 17, "Text", true),
                Ui.Sub("开启后在游戏画面右上角浮出一张小地图：中心是你，箭头为朝向，周围是铃铛、收集点与敌人。")));

            _enable = new ToggleButton { Style = Ui.Style("Switch") };
            _enable.Checked += (_, __) => _ = ToggleAsync(true);
            _enable.Unchecked += (_, __) => _ = ToggleAsync(false);
            _click = Ui.Chip("点击标记可传送", true, null, 11.5);
            _click.Click += (_, __) => { if (_overlay.MinimapEnabled) _ = ToggleAsync(true); };
            var right = Ui.Row(8, Ui.Sub("开启"), _enable, _click);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            header.Children.Add(right);
            Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

            var legend = new StackPanel();
            legend.Children.Add(Ui.Sub("图例"));
            legend.Children.Add(LegendRow("▲", "#C0392B", "你自己（箭头为朝向）"));
            legend.Children.Add(LegendRow("●", "#E0B04C", "铃铛（已收集显示为灰色）"));
            legend.Children.Add(LegendRow("●", "#57C7B4", "记忆之花 / 收集点"));
            legend.Children.Add(LegendRow("●", "#C256F0", "挑战地点"));
            legend.Children.Add(LegendRow("●", "#E04B3A", "敌人（贴边半透明表示在范围外）"));
            legend.Children.Add(new Border { Height = 12 });
            legend.Children.Add(Ui.Sub("说明：小地图悬浮在游戏画面上，默认不挡鼠标；打开「点击标记可传送」后可以直接在小地图上点铃铛传送。" +
                                       "切换页签不会关掉它，需要在上方开关关闭。"));

            var left = Ui.Panel(new ScrollViewer
            {
                Content = legend,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            left.Margin = new Thickness(0, 0, 12, 0);
            body.Children.Add(left);

            var side = Ui.Col(8,
                Ui.Panel(Ui.Col(6, Ui.Sub("状态"), _status)),
                Ui.Panel(Ui.Col(6, Ui.Sub("事件"), _log)));
            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            Grid.SetRow(body, 1);
            Children.Add(body);

            _overlay.Changed += () => Dispatcher.BeginInvoke(new Action(Sync));
            Loaded += (_, __) => Sync();
        }

        private UIElement LegendRow(string dot, string color, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock
            {
                Text = dot,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color),
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 0)
            });
            row.Children.Add(Ui.Sub(text));
            return row;
        }

        private void Sync()
        {
            _status.Text = _overlay.MinimapStatus;
            if (_overlay.MinimapEnabled != (_enable.IsChecked == true))
                SyncSwitch(_overlay.MinimapEnabled);
        }

        private void SyncSwitch(bool on)
        {
            _syncing = true;
            try { _enable.IsChecked = on; }
            finally { _syncing = false; }
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
                bool ok = await _overlay.SetMinimapAsync(enabled, _click.IsChecked == true);
                if (!ok && enabled) SyncSwitch(false);
                _status.Text = _overlay.MinimapStatus;
                Ui.Log(_log, _overlay.MinimapStatus);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
