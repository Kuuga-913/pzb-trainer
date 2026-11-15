using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>快捷键设置：唤出/隐藏修改器、以及各热键入口的集中管理。</summary>
    public sealed class HotkeyPickerView : Grid
    {
        private readonly StateStore _state;
        private readonly Action _onChanged;
        private readonly Action<bool> _onCapture;

        private readonly TextBlock _panelValue = Ui.Text("", 13, "Text", true);
        private readonly TextBlock _spawnValue = Ui.Sub("");
        private readonly WrapPanel _picker = new() { Visibility = Visibility.Collapsed };
        private readonly Border _captureLayer = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0B, 0x0B, 0x0E)),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            Focusable = true
        };
        private readonly TextBlock _captureHint = new()
        {
            Text = "请按下要绑定的组合键…",
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        private bool _capturing;
        private bool _bindPanelHotkey = true;

        private static readonly string[] Presets =
        {
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Ctrl+F1", "Ctrl+F2", "Ctrl+Shift+F1", "Insert", "Home"
        };

        public HotkeyPickerView(StateStore state, Action onChanged, Action<bool> onCapture)
        {
            _state = state;
            _onChanged = onChanged;
            _onCapture = onCapture;

            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = Ui.Col(2, Ui.Text("快捷键", 17, "Text", true),
                Ui.Sub("唤出修改器、一键生成怪物都从这里设置；绑定后游戏内也生效。"));
            header.Margin = new Thickness(0, 0, 0, 12);
            Children.Add(header);

            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(BuildRow(
                "唤出 / 隐藏修改器",
                "在游戏里按一下就能把修改器窗口叫到最前，再按一下收起。",
                _panelValue,
                TogglePanelPicker));

            panel.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = Ui.Brush("Divider"),
                Margin = new Thickness(0, 8, 0, 8)
            });

            panel.Children.Add(BuildRow(
                "一键生成怪物",
                "在「生成怪物」页选中怪物后绑定；这里只显示当前值。",
                _spawnValue,
                null));

            Grid.SetRow(panel, 1);
            Children.Add(panel);

            // 候选热键面板
            _picker.Margin = new Thickness(0, 10, 0, 0);
            BuildPicker();
            Grid.SetRow(_picker, 2);
            Children.Add(_picker);

            // 捕获遮罩
            _captureLayer.Child = _captureHint;
            Grid.SetRow(_captureLayer, 0);
            Grid.SetRowSpan(_captureLayer, 3);
            Panel.SetZIndex(_captureLayer, 99);
            _captureLayer.PreviewKeyDown += OnCaptureKeyDown;
            _captureLayer.MouseLeftButtonDown += (_, __) => EndCapture();
            Children.Add(_captureLayer);

            Refresh();
        }

        private FrameworkElement BuildRow(string title, string hint, TextBlock value, Action onPick)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = Ui.Col(2, Ui.Text(title, 13, "Text", true), Ui.Sub(hint));
            left.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(left);

            value.VerticalAlignment = VerticalAlignment.Center;
            value.Margin = new Thickness(8, 0, 12, 0);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            if (onPick != null)
            {
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                btns.Children.Add(Ui.Button("选择热键", onPick, "Ghost", 28, 88, 11.5));
                var clear = Ui.Button("清除", () => ClearPanelHotkey(), "Ghost", 28, 56, 11.5);
                clear.Margin = new Thickness(6, 0, 0, 0);
                btns.Children.Add(clear);
                Grid.SetColumn(btns, 2);
                grid.Children.Add(btns);
            }
            return grid;
        }

        private void BuildPicker()
        {
            _picker.Children.Clear();
            foreach (var combo in Presets)
            {
                var b = Ui.Button(combo, () => Pick(combo), "Ghost", 26, 76, 11);
                b.Margin = new Thickness(0, 0, 6, 6);
                _picker.Children.Add(b);
            }
            var custom = Ui.Button("自定义…", () => StartCapture(_bindPanelHotkey), "AccentBtn", 26, 80, 11);
            custom.Margin = new Thickness(0, 0, 6, 6);
            _picker.Children.Add(custom);
        }

        public void Refresh()
        {
            if (_state == null) return;
            _panelValue.Text = string.IsNullOrEmpty(_state.PanelToggleHotkey)
                ? "未绑定"
                : _state.PanelToggleHotkey;
            _spawnValue.Text = string.IsNullOrEmpty(_state.SpawnHotkey)
                ? "未绑定（在「生成怪物」页选择怪物后绑定）"
                : $"{_state.SpawnHotkey} → {_state.SpawnHotkeyName} ×{_state.SpawnHotkeyCount}";
        }

        private void TogglePanelPicker()
        {
            _bindPanelHotkey = true;
            _picker.Visibility = _picker.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void Pick(string combo)
        {
            _picker.Visibility = Visibility.Collapsed;
            if (_bindPanelHotkey) ApplyPanelHotkey(combo);
        }

        private void StartCapture(bool panelHotkey)
        {
            _bindPanelHotkey = panelHotkey;
            _capturing = true;
            _picker.Visibility = Visibility.Collapsed;
            _captureHint.Text = "请按下要绑定的组合键…\n（Esc 或点击空白处取消）";
            _captureLayer.Visibility = Visibility.Visible;
            _onCapture?.Invoke(true);

            Focusable = true;
            Keyboard.Focus(this);
            _captureLayer.Focus();
        }

        private void EndCapture()
        {
            if (!_capturing) return;
            _capturing = false;
            _captureLayer.Visibility = Visibility.Collapsed;
            _onCapture?.Invoke(false);
        }

        private void OnCaptureKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturing) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { EndCapture(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            var mods = Keyboard.Modifiers;
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(HotkeyService.KeyName(KeyInterop.VirtualKeyFromKey(key)));

            string combo = string.Join("+", parts);
            EndCapture();
            if (_bindPanelHotkey) ApplyPanelHotkey(combo);
        }

        private void ApplyPanelHotkey(string combo)
        {
            if (_state == null) { return; }

            // 与「一键生成」热键冲突时提示（否则后者会覆盖前者）
            if (!string.IsNullOrEmpty(_state.SpawnHotkey) &&
                HotkeyService.Normalize(_state.SpawnHotkey) == HotkeyService.Normalize(combo))
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"「{combo}」已经被「一键生成怪物」占用，请换一个组合键。",
                    "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _state.PanelToggleHotkey = combo;
            Refresh();
            _onChanged?.Invoke();
        }

        private void ClearPanelHotkey()
        {
            if (_state == null) return;
            _state.PanelToggleHotkey = "";
            Refresh();
            _onChanged?.Invoke();
        }
    }
}
