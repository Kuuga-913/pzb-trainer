using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    public sealed class CatalogRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Desc { get; set; }
        public double Owned { get; set; }
        public double MaxValue { get; set; }
        public bool Editable { get; set; }
        public bool CanDelete { get; set; }
        public bool Equipped { get; set; }
        public bool Available { get; set; } = true;
        public string Reason { get; set; } = "";
        public string Snapshot { get; set; } = "";
        public string ClassPath { get; set; } = "";
        public double[] Position { get; set; }
        public JsonObject Raw { get; set; }

        public string OwnedText => Owned > 0 ? Owned.ToString("0.##") : "";
        public string StateColumn { get; set; } = "";
        public string StateText => Available ? (Owned > 0 ? "已持有 " + Owned.ToString("0.##") : "未持有") : (Reason ?? "暂不可用");
    }

    public enum CatalogMode { Grant, Inventory, Spawn, Travel, Revive }

    /// <summary>目录类页面：物品 / 背包 / 生成怪物 / 探索地图，共用一套浏览+操作界面。</summary>
    public sealed class CatalogView : Grid
    {
        private readonly BridgeClient _bridge;
        private readonly CatalogMode _mode;
        private readonly string _loadType;

        private readonly List<CatalogRow> _all = new();
        private readonly List<CatalogRow> _view = new();
        private readonly ListView _list = new();
        private readonly TextBox _search = Ui.Input("", 200);
        private readonly StackPanel _categoryBar = new() { Orientation = Orientation.Horizontal };
        private string _categoryValue = "全部";
        private readonly TextBlock _count = Ui.Sub("尚未读取目录");
        private readonly TextBlock _detailTitle = Ui.Text("选择左侧条目", 16, "Text", true);
        private readonly TextBlock _detailMeta = Ui.Sub("");
        private readonly TextBlock _owned = Ui.Text("", 12.5, "Jade");
        private readonly TextBox _desc = new()
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Ui.Brush("SubText"),
            FontSize = 11.5,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 84
        };
        private readonly TextBox _log = Ui.LogBox(104);
        private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal };
        private readonly TextBox _countBox = Ui.Input("1", 76);
        private readonly TextBox _distBox = Ui.Input("800", 76);
        private readonly ToggleButton _friendly = new() { Content = "友军", Style = Ui.Style("ChipStyle"), FontSize = 11.5, Padding = new Thickness(10, 4, 10, 4) };
        private readonly Button _primary;
        private readonly Button _delete;
        private readonly Button _record;
        private readonly Button _back;
        private readonly Button _assist;
        private readonly TextBlock _warn = Ui.Sub("");
        private readonly TextBlock _status2 = Ui.Sub("");
        private StackPanel _extraRow;
        private bool _assistOn;
        private readonly Button _restoreAll;

        private CatalogRow _selected;
        private double[] _savedPosition;
        private string _savedWorld;

        public CatalogView(BridgeClient bridge, CatalogMode mode, string title, string subtitle, string loadType)
        {
            _bridge = bridge;
            _mode = mode;
            _loadType = loadType;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Margin = new Thickness(22, 4, 22, 0);

            // 标题行
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = Ui.Col(2, Ui.Text(title, 17, "Text", true), Ui.Sub(subtitle));
            titleStack.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(titleStack);

            var right = Ui.Row(8);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(_count);
            _search.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(_search);
            _search.Background = Ui.Brush("Chip");
            var refresh = Ui.Button("刷新目录", () => _ = LoadAsync(), "AccentBtn", 28, 86, 11.5);
            refresh.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(refresh);
            Grid.SetColumn(right, 2);
            header.Children.Add(right);
            Children.Add(header);

            _primary = Ui.Button(PrimaryLabel, () => _ = PrimaryAsync(), "AccentBtn", 30, 104, 12);
            _delete = Ui.Button("删除", () => _ = DeleteAsync(), "Ghost", 30, 76, 12);
            _record = Ui.Button("记录当前位置", () => _ = RecordAsync(), "Ghost", 30, 116, 12);
            _back = Ui.Button("传回记录点", () => _ = BackAsync(), "Ghost", 30, 108, 12);
            _assist = Ui.Button("开启助战预设", () => _ = AssistAsync(), "AccentBtn", 30, 116, 12);
            _restoreAll = Ui.Button("还原全部修改", () => _ = RestoreAllAsync(), "Ghost", 30, 112, 12);

            var catScroll = new ScrollViewer
            {
                Content = _categoryBar,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 6, 0, 6)
            };
            Grid.SetRow(catScroll, 1);
            Children.Add(catScroll);

            // 主体
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            body.Children.Add(BuildList());
            var detail = BuildDetail();
            Grid.SetColumn(detail, 1);
            body.Children.Add(detail);
            Grid.SetRow(body, 2);
            Children.Add(body);

            _search.TextChanged += (_, __) => ApplyFilter();

            Loaded += (_, __) => { if (_all.Count == 0) _ = LoadAsync(); };
        }

        private string PrimaryLabel => _mode switch
        {
            CatalogMode.Grant => "添加",
            CatalogMode.Inventory => "修改数量",
            CatalogMode.Spawn => "生成",
            CatalogMode.Travel => "前往",
            CatalogMode.Revive => "复活",
            _ => "执行"
        };

        private UIElement BuildList()
        {
            var panel = Ui.Panel(null, new Thickness(0));
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "名称", DisplayMemberBinding = new Binding("Name"), Width = 210 });
            gv.Columns.Add(new GridViewColumn { Header = "分类", DisplayMemberBinding = new Binding("Category"), Width = 110 });
            string head = _mode switch
            {
                CatalogMode.Inventory => "持有",
                CatalogMode.Spawn => "可生成",
                CatalogMode.Revive => "可复活",
                CatalogMode.Travel => "状态",
                _ => "状态"
            };
            gv.Columns.Add(new GridViewColumn
            {
                Header = head,
                DisplayMemberBinding = new Binding("StateColumn"),
                Width = 120
            });
            _list.View = gv;
            _list.Background = Ui.Brush("Card");
            _list.Foreground = Ui.Brush("Text");
            _list.BorderThickness = new Thickness(0);
            _list.FontSize = 12;
            _list.Margin = new Thickness(10, 8, 10, 10);
            _list.Resources[SystemColors.HighlightBrushKey] = Ui.Brush("Accent");
            _list.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            _list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Ui.Brush("Accent");
            _list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;
            _list.Resources[SystemColors.WindowBrushKey] = Ui.Brush("Card");
            _list.Resources[SystemColors.ControlBrushKey] = Ui.Brush("Card");
            var hs = new Style(typeof(GridViewColumnHeader));
            hs.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("Chip")));
            hs.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("SubText")));
            hs.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            hs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            hs.Setters.Add(new Setter(FrameworkElement.HeightProperty, 30.0));
            _list.Resources[typeof(GridViewColumnHeader)] = hs;

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Brush("Text")));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 3, 4, 3)));
            var hover = new Trigger { Property = ListViewItem.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(BackgroundProperty, Ui.Brush("Hover")));
            itemStyle.Triggers.Add(hover);
            var sel = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(BackgroundProperty, Ui.Brush("Accent")));
            sel.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(sel);
            _list.ItemContainerStyle = itemStyle;
            _list.SelectionChanged += (_, __) =>
            {
                if (_list.SelectedItem is CatalogRow row) ShowDetail(row);
            };
            panel.Child = _list;
            return panel;
        }

        private UIElement BuildDetail()
        {
            var stack = new StackPanel();
            stack.Children.Add(Ui.Row(8, _detailTitle));
            stack.Children.Add(_detailMeta);
            stack.Children.Add(new Border { Height = 1, Background = Ui.Brush("Divider"), Margin = new Thickness(0, 8, 0, 8) });
            stack.Children.Add(_desc);

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            switch (_mode)
            {
                case CatalogMode.Grant:
                    actionRow.Children.Add(Ui.Sub("数量"));
                    _countBox.Margin = new Thickness(6, 0, 12, 0);
                    actionRow.Children.Add(_countBox);
                    actionRow.Children.Add(_primary);
                    break;
                case CatalogMode.Inventory:
                    actionRow.Children.Add(Ui.Sub("目标数量"));
                    _countBox.Margin = new Thickness(6, 0, 12, 0);
                    actionRow.Children.Add(_countBox);
                    actionRow.Children.Add(_primary);
                    _delete.Margin = new Thickness(8, 0, 0, 0);
                    actionRow.Children.Add(_delete);
                    break;
                case CatalogMode.Spawn:
                case CatalogMode.Revive:
                    actionRow.Children.Add(Ui.Sub("数量"));
                    _countBox.Text = "1";
                    _countBox.Margin = new Thickness(6, 0, 10, 0);
                    actionRow.Children.Add(_countBox);
                    actionRow.Children.Add(Ui.Sub("距离"));
                    _distBox.Margin = new Thickness(6, 0, 10, 0);
                    actionRow.Children.Add(_distBox);
                    _friendly.Margin = new Thickness(0, 0, 10, 0);
                    actionRow.Children.Add(_friendly);
                    actionRow.Children.Add(_primary);

                    _extraRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                    _extraRow.Children.Add(_assist);
                    _restoreAll.Margin = new Thickness(8, 0, 0, 0);
                    _extraRow.Children.Add(_restoreAll);
                    _record.Margin = new Thickness(8, 0, 0, 0);
                    _extraRow.Children.Add(_record);
                    _back.Margin = new Thickness(8, 0, 0, 0);
                    _extraRow.Children.Add(_back);
                    break;
                case CatalogMode.Travel:
                    actionRow.Children.Add(_primary);
                    _record.Margin = new Thickness(8, 0, 0, 0);
                    actionRow.Children.Add(_record);
                    _back.Margin = new Thickness(8, 0, 0, 0);
                    actionRow.Children.Add(_back);
                    break;
            }
            stack.Children.Add(actionRow);
            if (_extraRow != null) stack.Children.Add(_extraRow);
            if (_mode == CatalogMode.Spawn || _mode == CatalogMode.Revive)
            {
                _warn.Margin = new Thickness(0, 8, 0, 0);
                _warn.TextWrapping = TextWrapping.Wrap;
                stack.Children.Add(_warn);
                _status2.Margin = new Thickness(0, 4, 0, 0);
                _status2.TextWrapping = TextWrapping.Wrap;
                stack.Children.Add(_status2);
            }
            stack.Children.Add(_owned);
            stack.Children.Add(new Border { Height = 1, Background = Ui.Brush("Divider"), Margin = new Thickness(0, 10, 0, 8) });
            stack.Children.Add(Ui.Sub("操作记录"));
            stack.Children.Add(_log);

            var panel = Ui.Panel(new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            panel.Margin = new Thickness(10, 0, 0, 0);
            return panel;
        }

        // ---------- 数据 ----------

        public async Task LoadAsync()
        {
            if (!_bridge.IsConnected)
            {
                _count.Text = "请先连接游戏";
                return;
            }
            _count.Text = "正在读取目录…";
            try
            {
                var r = await _bridge.CatalogAsync(_loadType);
                if (!r.Flag("ok", false))
                {
                    _count.Text = r.Str("message") ?? "目录读取失败";
                    return;
                }
                var items = r["items"] as JsonArray;
                _all.Clear();
                if (items != null)
                {
                    foreach (var node in items.OfType<JsonObject>())
                    {
                        var row = new CatalogRow
                        {
                            Id = node["id"].Str() ?? "",
                            Name = node["name"].Str() ?? "",
                            Category = node["category"].Str() ?? "",
                            Desc = node["description"].Str() ?? "",
                            Owned = node["owned"].Num(),
                            MaxValue = node["maxValue"].Num(0),
                            Editable = node["editable"].Flag(true),
                            CanDelete = node["canDelete"].Flag(true),
                            Equipped = node["equipped"].Flag(false),
                            Available = node["available"].Flag(true),
                            Reason = node["reason"].Str() ?? "",
                            Snapshot = node["snapshot"].Str() ?? "",
                            ClassPath = node["classPath"].Str() ?? "",
                            Raw = node
                        };
                        if (node["position"] is JsonArray pos && pos.Count >= 3)
                            row.Position = pos.Select(p => p.Num()).ToArray();
                        row.StateColumn = _mode switch
                        {
                            CatalogMode.Inventory => row.OwnedText,
                            CatalogMode.Travel => row.Available ? "可前往" : (string.IsNullOrEmpty(row.Reason) ? "不可用" : row.Reason),
                            CatalogMode.Spawn => row.Available ? "可生成" : (string.IsNullOrEmpty(row.Reason) ? "不可用" : row.Reason),
                            CatalogMode.Revive => row.Available ? "可复活" : (string.IsNullOrEmpty(row.Reason) ? "不可用" : row.Reason),
                            _ => row.Owned > 0 ? "已持有 " + row.Owned.ToString("0.##") : "未持有"
                        };
                        _all.Add(row);
                    }
                }

                if (_mode == CatalogMode.Spawn || _mode == CatalogMode.Revive) _ = SyncAssistStateAsync();
                RebuildCategories();   // 生成/复活页需要「全部 · 普通敌人 · 首领」这类筛选
                ApplyFilter();
                _count.Text = $"{_all.Count} 项";
                Ui.Log(_log, $"已读取目录：{_all.Count} 项");
            }
            catch (Exception ex)
            {
                _count.Text = "目录读取失败";
                Ui.Log(_log, "读取失败：" + ex.Message);
            }
        }

        private void RebuildCategories()
        {
            _categoryBar.Children.Clear();
            var cats = new List<string> { "全部" };
            cats.AddRange(_all.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x));
            if (!cats.Contains(_categoryValue)) _categoryValue = "全部";
            foreach (var c in cats)
            {
                var btn = Ui.Chip(c, c == _categoryValue, on =>
                {
                    _categoryValue = c;
                    foreach (var child in _categoryBar.Children.OfType<ToggleButton>())
                        child.IsChecked = (string)child.Content == _categoryValue;
                    ApplyFilter();
                }, 11.5);
                if (c != _categoryValue) btn.IsChecked = false;
                _categoryBar.Children.Add(btn);
            }
        }

        private void ApplyFilter()
        {
            string q = (_search.Text ?? "").Trim();
            string cat = _categoryValue ?? "全部";
            _view.Clear();
            foreach (var r in _all)
            {
                if (cat != "全部" && r.Category != cat) continue;
                if (q.Length > 0 &&
                    !(r.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) &&
                    !(r.Id?.Contains(q, StringComparison.OrdinalIgnoreCase) == true) &&
                    !(r.Desc?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)) continue;
                _view.Add(r);
            }
            _list.ItemsSource = null;
            _list.ItemsSource = _view;
        }

        private void ShowDetail(CatalogRow row)
        {
            _selected = row;
            _detailTitle.Text = row.Name;
            _detailMeta.Text = $"{row.Category}  ·  {row.Id}";
            _desc.Text = string.IsNullOrWhiteSpace(row.Desc) ? "（无说明）" : row.Desc;
            if (_mode == CatalogMode.Inventory)
            {
                _owned.Text = $"当前持有 {row.Owned:0.##}" + (row.MaxValue > 0 ? $" / 上限 {row.MaxValue:0}" : "") + (row.Equipped ? "  · 已装备" : "");
                _countBox.Text = ((int)row.Owned).ToString();
                _delete.IsEnabled = row.CanDelete;
                _primary.IsEnabled = row.Editable;
            }
            else if (_mode == CatalogMode.Travel)
            {
                _owned.Text = row.Available ? "可前往" : (string.IsNullOrEmpty(row.Reason) ? "暂不可前往" : row.Reason);
                _primary.IsEnabled = row.Available;
            }
            else
            {
                _owned.Text = row.Available ? "" : (string.IsNullOrEmpty(row.Reason) ? "暂不可操作" : row.Reason);
                _primary.IsEnabled = row.Available;
                if (_mode == CatalogMode.Spawn || _mode == CatalogMode.Revive)
                {
                    _warn.Text = row.Category == "首领"
                        ? "⚠ 剧情首领带阶段机制：血量见底后可能不再下降（需要过场/阶段推进）。建议改生成「普通敌人」，或先点「开启助战预设」再打；打完记得用「传回记录点」脱战。"
                        : "";
                }
            }
        }

        // ---------- 操作 ----------

        private async Task PrimaryAsync()
        {
            if (_selected == null) { Ui.Log(_log, "请先选择左侧条目"); return; }
            if (!_bridge.IsConnected) { Ui.Log(_log, "请先连接游戏"); return; }
            var row = _selected;
            _primary.IsEnabled = false;
            try
            {
                switch (_mode)
                {
                    case CatalogMode.Grant:
                    {
                        int count = ParseInt(_countBox.Text, 1, 9999, 1);
                        Ui.Log(_log, $"添加 {row.Name} × {count}");
                        var r = await _bridge.GrantAsync(new[] { row.Id }, count);
                        LogResults(r, row);
                        break;
                    }
                    case CatalogMode.Inventory:
                    {
                        int value = ParseInt(_countBox.Text, 0, 999999, (int)row.Owned);
                        Ui.Log(_log, $"设置 {row.Name} 数量 {row.Owned:0.##} → {value}");
                        var r = await _bridge.InventoryChangeAsync(row.Id, row.Snapshot, false, value);
                        Ui.Log(_log, r.Flag("ok") ? $"已修改，回读 {r["value"].Num():0.##}" : ("失败：" + (r.Str("message") ?? "")));
                        break;
                    }
                    case CatalogMode.Spawn:
                    case CatalogMode.Revive:
                    {
                        int count = ParseInt(_countBox.Text, 1, 10, 1);
                        int dist = ParseInt(_distBox.Text, 0, 200000, 800);
                        bool friendly = _friendly.IsChecked == true;
                        Ui.Log(_log, $"{(_mode == CatalogMode.Spawn ? "生成" : "复活")} {row.Name} × {count}，距离 {dist}{(friendly ? "，友军" : "")}");
                        var r = _mode == CatalogMode.Spawn
                            ? await _bridge.SpawnAsync(row.Id, count, dist, friendly)
                            : await _bridge.ReviveAsync(row.Id, count, dist, friendly);
                        Ui.Log(_log, r.Str("message") ?? (r.Flag("ok") ? "已提交" : "失败"));
                        string batch = r["batch"].Str();
                        if (!string.IsNullOrEmpty(batch)) await PollBatchAsync(batch, _mode == CatalogMode.Spawn);
                        break;
                    }
                    case CatalogMode.Travel:
                    {
                        Ui.Log(_log, "前往：" + row.Name);
                        var r = await _bridge.TravelAsync(row.Id);
                        Ui.Log(_log, r.Flag("ok") ? ("已传送 · " + (r.Str("message") ?? "")) : ("失败：" + (r.Str("message") ?? "")));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Ui.Log(_log, "操作异常：" + ex.Message);
            }
            finally
            {
                _primary.IsEnabled = true;
                if (_mode != CatalogMode.Spawn && _mode != CatalogMode.Revive && _mode != CatalogMode.Travel)
                    _ = LoadAsync();
            }
        }

        private async Task PollBatchAsync(string batch, bool isSpawn)
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(600);
                JsonObject s;
                try
                {
                    s = isSpawn ? await _bridge.SpawnStatusAsync(batch) : await _bridge.ReviveStatusAsync(batch);
                }
                catch (Exception ex) { Ui.Log(_log, "查询状态失败：" + ex.Message); return; }

                string msg = s.Str("message") ?? "";
                if (!string.IsNullOrEmpty(msg)) Ui.Log(_log, msg);
                if (!s.Flag("pending")) return;
            }
            Ui.Log(_log, "生成仍在处理中，请先核对场景，勿重复提交");
        }

        private static readonly (string Id, string Name, double Value)[] AssistPreset =
        {
            ("damage", "伤害倍率", 5),
            ("fast_break", "快速破防", 100000),
            ("never_break", "玩家不被破防", 1),
            ("auto_deflect", "自动完美格挡", 1),
            ("auto_qte", "自动 QTE", 1)
        };

        /// <summary>打 BOSS 用的一组功能；再点一次会全部关掉（秒杀开着时生成的怪会一击即死）。</summary>
        /// <summary>还原本次会话的可逆修改：关掉开关但数值没回去时用它。</summary>
        private async Task RestoreAllAsync()
        {
            if (!_bridge.IsConnected) { Ui.Log(_log, "请先连接游戏"); return; }

            var owner = Window.GetWindow(this);
            var ask = owner != null
                ? MessageBox.Show(owner,
                    "这会还原本次连接期间做过的所有可逆修改（攻击系数、速度、外观等）。\n\n" +
                    "已开启的功能开关也会一并关闭，需要再打开。\n\n确定继续？",
                    "还原全部修改", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                : MessageBoxResult.Cancel;
            if (ask != MessageBoxResult.OK) return;

            try
            {
                Ui.Log(_log, "正在还原本次会话的可逆修改…");
                var r = await _bridge.RestoreAsync();
                Ui.Log(_log, (r != null && r.Flag("ok") ? "✔ " : "✘ ") + (r?.Str("message") ?? "已提交"));
                _assistOn = false;
                _assist.Content = "开启助战预设";
                RefreshAssistHint();
                _ = LoadAsync();
            }
            catch (Exception ex)
            {
                Ui.Log(_log, "还原异常：" + ex.Message);
            }
        }

        private async Task AssistAsync()
        {
            if (!_bridge.IsConnected) { Ui.Log(_log, "请先连接游戏"); return; }
            bool enable = !_assistOn;

            _assist.IsEnabled = false;
            try
            {
                Ui.Log(_log, enable ? "开启助战预设…" : "关闭助战预设…");
                foreach (var (id, name, value) in AssistPreset)
                {
                    try
                    {
                        var r = await _bridge.FeatureAsync(id, enable, enable ? value : 0);
                        Ui.Log(_log, (r != null && r.Flag("ok") ? "✔ " : "✘ ") + name + (enable ? " 开" : " 关") +
                                      (string.IsNullOrEmpty(r?.Str("message")) ? "" : "　" + r.Str("message")));
                    }
                    catch (Exception ex)
                    {
                        Ui.Log(_log, "✘ " + name + "：" + ex.Message);
                    }
                }

                _assistOn = enable;
                _assist.Content = enable ? "关闭助战预设" : "开启助战预设";
                RefreshAssistHint();
                Ui.Log(_log, enable
                    ? "助战已开启（不含「秒杀」）：伤害倍率与破防已生效。若之后关掉但伤害仍异常，点旁边「还原全部修改」。"
                    : "助战已关闭：伤害与破防恢复原样。若伤害仍偏高，点「还原全部修改」。");
            }
            finally
            {
                _assist.IsEnabled = true;
            }
        }

        /// <summary>读一次能力列表，判断秒杀 / 伤害倍率是否开着，用来提示与按钮文案。</summary>
        private async Task SyncAssistStateAsync()
        {
            try
            {
                var caps = await _bridge.CapabilitiesAsync();
                var feats = caps?["features"] as JsonArray;
                if (feats == null) return;
                bool oneHit = false, dmg = false;
                foreach (var f in feats.OfType<JsonObject>())
                {
                    string id = f.Str("id") ?? "";
                    if (!f.Flag("enabled")) continue;
                    if (id == "one_hit") oneHit = true;
                    if (id == "damage") dmg = true;
                }
                _assistOn = oneHit || dmg;
                _assist.Content = _assistOn ? "关闭助战预设" : "开启助战预设";
                RefreshAssistHint();
            }
            catch { }
        }

        private void RefreshAssistHint()
        {
            if (_mode != CatalogMode.Spawn && _mode != CatalogMode.Revive) return;
            if (_assistOn)
            {
                _status2.Text = "⚠ 当前「秒杀 / 伤害倍率」是开着的，生成的敌人会一击即死；点「关闭助战预设」可还原。";
                _status2.Foreground = Ui.Brush("Warn");
            }
            else
            {
                _status2.Text = "";
            }
        }

        private async Task DeleteAsync()
        {
            if (_selected == null) return;
            var row = _selected;
            var answer = MessageBox.Show($"确认删除「{row.Name}」的整个持有记录？\n该操作不能通过关闭修改器撤销。",
                "删除物品", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) { Ui.Log(_log, "已取消删除"); return; }
            try
            {
                var r = await _bridge.InventoryChangeAsync(row.Id, row.Snapshot, true, 0);
                Ui.Log(_log, r.Flag("ok") ? "已删除" : ("删除失败：" + (r.Str("message") ?? "")));
            }
            catch (Exception ex) { Ui.Log(_log, "删除异常：" + ex.Message); }
            await LoadAsync();
        }

        private async Task RecordAsync()
        {
            try
            {
                var r = await _bridge.PositionAsync();
                if (!r.Flag("ok")) { Ui.Log(_log, "读取位置失败：" + (r.Str("message") ?? "")); return; }
                _savedWorld = r["world"].Str();
                if (r["position"] is JsonArray p && p.Count >= 3) _savedPosition = p.Select(x => x.Num()).ToArray();
                Ui.Log(_log, $"已记录位置 {string.Join(",", _savedPosition?.Select(v => v.ToString("0")) ?? Array.Empty<string>())}");
            }
            catch (Exception ex) { Ui.Log(_log, "记录失败：" + ex.Message); }
        }

        private async Task BackAsync()
        {
            if (_savedPosition == null || string.IsNullOrEmpty(_savedWorld)) { Ui.Log(_log, "请先记录当前位置"); return; }
            try
            {
                var r = await _bridge.TeleportAsync(_savedWorld, _savedPosition);
                Ui.Log(_log, r.Flag("ok") ? "已传回记录位置" : ("传送失败：" + (r.Str("message") ?? "")));
            }
            catch (Exception ex) { Ui.Log(_log, "传送异常：" + ex.Message); }
        }

        private void LogResults(JsonObject r, CatalogRow row)
        {
            if (r == null) { Ui.Log(_log, "未收到结果"); return; }
            if (!r.Flag("ok") && r["results"] == null)
            {
                Ui.Log(_log, "失败：" + (r.Str("message") ?? ""));
                return;
            }
            if (r["results"] is JsonArray arr && arr.Count > 0)
            {
                foreach (var item in arr.OfType<JsonObject>())
                {
                    bool ok = item.Flag("ok");
                    double after = item["after"].Num();
                    Ui.Log(_log, (ok ? "✔ " : "✘ ") + row.Name + " · " + (item.Str("message") ?? (ok ? "已添加" : "未完成")) +
                                  (after > 0 ? $"（现有 {after:0.##}）" : ""));
                }
            }
            else
            {
                Ui.Log(_log, r.Str("message") ?? "已提交");
            }
        }

        private static int ParseInt(string text, int min, int max, int fallback)
        {
            if (!int.TryParse((text ?? "").Trim(), out int v)) v = fallback;
            return Math.Min(max, Math.Max(min, v));
        }
    }
}
