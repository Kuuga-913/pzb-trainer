using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PBZTrainer.Views
{
    /// <summary>代码构建界面用的小工具，避免为每个页面写一份 XAML。</summary>
    internal static class Ui
    {
        public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
        public static Style Style(string key) => (Style)Application.Current.Resources[key];

        public static TextBlock Text(string text, double size = 12.5, string colorKey = "Text", bool bold = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = Brush(colorKey),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (bold) tb.FontWeight = FontWeights.SemiBold;
            return tb;
        }

        public static TextBlock Sub(string text, double size = 11.5) => Text(text, size, "SubText");

        public static Button Button(string content, Action click, string styleKey = "Ghost", double height = 28, double width = double.NaN, double fontSize = 12)
        {
            var b = new Button
            {
                Content = content,
                Style = Style(styleKey),
                Height = height,
                FontSize = fontSize,
                Padding = new Thickness(12, 0, 12, 0)
            };
            if (!double.IsNaN(width)) b.Width = width;
            b.Click += (_, __) => click?.Invoke();
            return b;
        }

        public static ToggleButton Switch(bool isOn, Action<bool> changed = null)
        {
            var t = new ToggleButton { Style = Style("Switch"), IsChecked = isOn };
            if (changed != null) t.Click += (_, __) => changed(t.IsChecked == true);
            return t;
        }

        public static ToggleButton Chip(string text, bool isOn, Action<bool> changed = null, double fontSize = 12)
        {
            var t = new ToggleButton
            {
                Content = text,
                Style = Style("ChipStyle"),
                IsChecked = isOn,
                FontSize = fontSize,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 7, 0)
            };
            if (changed != null) t.Click += (_, __) => changed(t.IsChecked == true);
            return t;
        }

        public static TextBox Input(string text, double width = 90, double height = 28)
            => new TextBox { Text = text, Style = Style("ValueBox"), Width = width, Height = height };

        public static StackPanel Row(double gap = 8, params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var it in items)
            {
                if (it is FrameworkElement fe) fe.Margin = new Thickness(0, 0, gap, 0);
                sp.Children.Add(it);
            }
            return sp;
        }

        public static StackPanel Col(double gap = 6, params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var it in items)
            {
                if (it is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, gap);
                sp.Children.Add(it);
            }
            return sp;
        }

        public static Border Panel(UIElement child, Thickness? padding = null, string bgKey = "Card")
        {
            return new Border
            {
                Background = Brush(bgKey),
                BorderBrush = Brush("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = padding ?? new Thickness(14, 12, 14, 12),
                Child = child
            };
        }

        public static Border Scroller(UIElement child)
        {
            return new Border { Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = child } };
        }

        public static void Log(TextBox box, string line)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            box.AppendText($"[{stamp}] {line}{Environment.NewLine}");
            box.ScrollToEnd();
        }

        public static TextBox LogBox(double height = 96)
        {
            var tb = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brush("Chip"),
                Foreground = Brush("SubText"),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("Border"),
                FontSize = 11.5,
                Height = height,
                Padding = new Thickness(8, 6, 8, 6)
            };
            return tb;
        }

        public static string Str(this System.Text.Json.Nodes.JsonNode n)
        {
            if (n == null) return null;
            try { return n.GetValue<string>(); } catch { }
            try { return n.ToJsonString(); } catch { return null; }
        }

        /// <summary>按字段名取字符串：node.Str("message")。</summary>
        public static string Str(this System.Text.Json.Nodes.JsonNode n, string key)
        {
            if (n is System.Text.Json.Nodes.JsonObject o && key != null && o.TryGetPropertyValue(key, out var v))
                return v.Str();
            return null;
        }

        /// <summary>按字段名取数字。</summary>
        public static double Num(this System.Text.Json.Nodes.JsonNode n, string key)
        {
            if (n is System.Text.Json.Nodes.JsonObject o && key != null && o.TryGetPropertyValue(key, out var v))
                return v.Num();
            return 0;
        }

        /// <summary>按字段名取布尔。</summary>
        public static bool Flag(this System.Text.Json.Nodes.JsonNode n, string key, bool def = false)
        {
            if (n is System.Text.Json.Nodes.JsonObject o && key != null && o.TryGetPropertyValue(key, out var v))
                return v.Flag(def);
            return def;
        }

        public static double Num(this System.Text.Json.Nodes.JsonNode n, double def = 0)
        {
            if (n == null) return def;
            try { return n.GetValue<double>(); } catch { }
            try { return n.GetValue<int>(); } catch { }
            try { return double.TryParse(n.ToJsonString(), out var d) ? d : def; } catch { return def; }
        }

        public static bool Flag(this System.Text.Json.Nodes.JsonNode n, bool def = false)
        {
            if (n == null) return def;
            try { return n.GetValue<bool>(); } catch { }
            try { return n.GetValue<int>() != 0; } catch { return def; }
        }

        public static void Cursor(FrameworkElement e) => e.Cursor = Cursors.Hand;
    }
}
