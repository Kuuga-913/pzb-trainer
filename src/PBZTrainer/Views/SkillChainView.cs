using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>技能连段：从技能目录里挑招式排成一串，按间隔依次释放。</summary>
    public sealed class SkillChainView : Grid
    {
        private readonly BridgeClient _bridge;
        private readonly List<CatalogRow> _all = new();
        private readonly List<CatalogRow> _queue = new();

        private readonly TextBox _search = Ui.Input("", 180);
        private readonly ListView _catalog = new();
        private readonly ListView _chain = new();
        private readonly TextBox _gap = Ui.Input("800", 72, 26);
        private readonly TextBox _log = Ui.LogBox(96);
        private readonly TextBlock _count = Ui.Sub("尚未读取技能目录");
        private readonly Button _run;
        private readonly Button _stop;
        private string _chainId;
        private bool _running;

        public SkillChainView(BridgeClient bridge)
        {
            _bridge = bridge;
            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Ui.Col(2, Ui.Text("技能连段", 17, "Text", true),
                Ui.Sub("把招式排成队列依次释放：左侧选技能加入右侧队列，设好间隔后点「执行连段」。")));

            var right = Ui.Row(8);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(_count);
            _search.Margin = new Thickness(8, 0, 0, 0);
            _search.Background = Ui.Brush("Chip");
            right.Children.Add(_search);
            var refresh = Ui.Button("刷新", () => _ = LoadAsync(), "AccentBtn", 28, 66, 11.5);
            refresh.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(refresh);
            Grid.SetColumn(right, 2);
            header.Children.Add(right);
            Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            // 左：技能目录
            StyleList(_catalog);
            _catalog.MouseDoubleClick += (_, __) => AddSelected();
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "技能", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 260 });
            gv.Columns.Add(new GridViewColumn { Header = "来源", DisplayMemberBinding = new System.Windows.Data.Binding("Category"), Width = 200 });
            _catalog.View = gv;

            var left = Ui.Panel(null, new Thickness(0));
            left.Child = _catalog;
            left.Margin = new Thickness(0, 0, 10, 0);
            body.Children.Add(left);

            // 右：队列
            var side = new StackPanel();
            side.Children.Add(Ui.Sub("连段队列（按顺序释放）"));
            StyleList(_chain);
            _chain.Height = 300;
            var gv2 = new GridView();
            gv2.Columns.Add(new GridViewColumn { Header = "#", DisplayMemberBinding = new System.Windows.Data.Binding("Index"), Width = 36 });
            gv2.Columns.Add(new GridViewColumn { Header = "技能", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 240 });
            _chain.View = gv2;
            side.Children.Add(_chain);

            var queueOps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            queueOps.Children.Add(Ui.Button("加入", AddSelected, "AccentBtn", 28, 62, 11.5));
            var up = Ui.Button("上移", MoveUp, "Ghost", 28, 62, 11.5);
            up.Margin = new Thickness(6, 0, 0, 0);
            queueOps.Children.Add(up);
            var rm = Ui.Button("移除", RemoveSelected, "Ghost", 28, 62, 11.5);
            rm.Margin = new Thickness(6, 0, 0, 0);
            queueOps.Children.Add(rm);
            var clr = Ui.Button("清空", () => { _queue.Clear(); RefreshChain(); }, "Ghost", 28, 62, 11.5);
            clr.Margin = new Thickness(6, 0, 0, 0);
            queueOps.Children.Add(clr);
            side.Children.Add(queueOps);

            var runRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            runRow.Children.Add(Ui.Sub("间隔"));
            _gap.Margin = new Thickness(6, 0, 6, 0);
            runRow.Children.Add(_gap);
            runRow.Children.Add(Ui.Sub("毫秒"));
            _run = Ui.Button("执行连段", () => _ = RunAsync(), "AccentBtn", 30, 96, 12);
            _run.Margin = new Thickness(12, 0, 0, 0);
            runRow.Children.Add(_run);
            _stop = Ui.Button("停止", () => _ = StopAsync(), "Ghost", 30, 66, 12);
            _stop.Margin = new Thickness(8, 0, 0, 0);
            _stop.IsEnabled = false;
            runRow.Children.Add(_stop);
            side.Children.Add(runRow);

            side.Children.Add(new Border { Height = 10 });
            side.Children.Add(Ui.Sub("执行日志"));
            side.Children.Add(_log);

            var sidePanel = Ui.Panel(new ScrollViewer
            {
                Content = side,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            Grid.SetColumn(sidePanel, 1);
            body.Children.Add(sidePanel);

            Grid.SetRow(body, 1);
            Children.Add(body);

            _search.TextChanged += (_, __) => ApplyFilter();
            Loaded += (_, __) => { if (_all.Count == 0) _ = LoadAsync(); };
        }

        private static void StyleList(ListView lv)
        {
            lv.Background = Ui.Brush("Card");
            lv.Foreground = Ui.Brush("Text");
            lv.BorderThickness = new Thickness(0);
            lv.FontSize = 12;
            lv.Margin = new Thickness(10, 8, 10, 10);
            var hs = new Style(typeof(GridViewColumnHeader));
            hs.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("Chip")));
            hs.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("SubText")));
            hs.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            hs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            hs.Setters.Add(new Setter(FrameworkElement.HeightProperty, 28.0));
            lv.Resources[typeof(GridViewColumnHeader)] = hs;

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("Text")));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            var hover = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("Hover")));
            itemStyle.Triggers.Add(hover);
            var sel = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("Accent")));
            sel.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(sel);
            lv.ItemContainerStyle = itemStyle;
        }

        private sealed class ChainStep
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public CatalogRow Row { get; set; }
        }

        public async Task LoadAsync()
        {
            if (!_bridge.IsConnected) { _count.Text = "请先连接游戏"; return; }
            _count.Text = "正在读取技能目录…";
            try
            {
                var r = await _bridge.CatalogAsync("player_skills");
                if (!r.Flag("ok")) { _count.Text = r.Str("message") ?? "目录读取失败"; return; }
                _all.Clear();
                if (r["items"] is JsonArray items)
                {
                    foreach (var node in items.OfType<JsonObject>())
                    {
                        _all.Add(new CatalogRow
                        {
                            Id = node.Str("id") ?? "",
                            Name = node.Str("name") ?? "",
                            Category = node.Str("category") ?? "",
                            Desc = node.Str("description") ?? "",
                            Raw = node
                        });
                    }
                }
                ApplyFilter();
            }
            catch (Exception ex) { _count.Text = "读取失败：" + ex.Message; }
        }

        private void ApplyFilter()
        {
            string q = (_search.Text ?? "").Trim();
            var rows = _all.Where(r => q.Length == 0
                || (r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            _catalog.ItemsSource = null;
            _catalog.ItemsSource = rows;
            _count.Text = $"{rows.Count} / {_all.Count} 条";
        }

        private void AddSelected()
        {
            if (_catalog.SelectedItem is not CatalogRow row) return;
            if (_queue.Count >= 30) { Ui.Log(_log, "队列最多 30 步"); return; }
            _queue.Add(row);
            RefreshChain();
        }

        private void MoveUp()
        {
            if (_chain.SelectedIndex <= 0) return;
            int i = _chain.SelectedIndex;
            (_queue[i - 1], _queue[i]) = (_queue[i], _queue[i - 1]);
            RefreshChain();
            _chain.SelectedIndex = i - 1;
        }

        private void RemoveSelected()
        {
            if (_chain.SelectedIndex < 0) return;
            _queue.RemoveAt(_chain.SelectedIndex);
            RefreshChain();
        }

        private void RefreshChain()
        {
            _chain.ItemsSource = null;
            _chain.ItemsSource = _queue.Select((r, i) => new ChainStep { Index = i + 1, Name = r.Name, Row = r }).ToList();
        }

        private async Task RunAsync()
        {
            if (!_bridge.IsConnected) { Ui.Log(_log, "请先连接游戏"); return; }
            if (_queue.Count == 0) { Ui.Log(_log, "队列是空的"); return; }
            if (_running) return;

            int gap = int.TryParse(_gap.Text, out var g) ? Math.Max(120, Math.Min(5000, g)) : 800;
            _running = true;
            _run.IsEnabled = false;
            _stop.IsEnabled = true;
            try
            {
                var begin = await _bridge.SkillGroupBeginAsync();
                if (begin == null || !begin.Flag("ok"))
                {
                    Ui.Log(_log, "无法开始：" + (begin?.Str("message") ?? ""));
                    return;
                }
                _chainId = begin.Str("chainId") ?? "";
                Ui.Log(_log, $"开始连段（{_queue.Count} 步，间隔 {gap}ms）");

                for (int i = 0; i < _queue.Count; i++)
                {
                    var step = _queue[i];
                    var r = await _bridge.SkillGroupCastAsync(step.Id);
                    bool ok = r != null && r.Flag("ok");
                    Ui.Log(_log, $"{i + 1}/{_queue.Count} {(ok ? "✔" : "✘")} {step.Name}" +
                                  (ok ? "" : "　" + (r?.Str("message") ?? "")));
                    if (!ok) break;
                    await Task.Delay(gap);
                }

                var end = await _bridge.SkillGroupEndAsync();
                Ui.Log(_log, "连段结束：" + (end?.Str("message") ?? "已提交"));
            }
            catch (Exception ex)
            {
                Ui.Log(_log, "执行异常：" + ex.Message);
            }
            finally
            {
                _running = false;
                _run.IsEnabled = true;
                _stop.IsEnabled = false;
                _chainId = null;
            }
        }

        private async Task StopAsync()
        {
            if (!_running) return;
            try
            {
                var r = await _bridge.SkillGroupStopAsync(_chainId ?? "");
                Ui.Log(_log, "已请求停止：" + (r?.Str("message") ?? ""));
            }
            catch (Exception ex) { Ui.Log(_log, "停止失败：" + ex.Message); }
        }
    }
}
