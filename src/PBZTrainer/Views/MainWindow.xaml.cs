using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PBZTrainer.Models;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>原版“基础功能”页的 16 项功能（按原版卡片顺序）。</summary>
        /// <summary>功能分类：导航直接用分类，不再保留「基础/高级」两个大栏目。</summary>
        private static readonly (string Name, string Hint)[] FeatureCategories =
        {
            ("战斗与生存", "生命、伤害、药品、记忆之花与坠落等生存相关能力"),
            ("杀气与绝技", "杀气循环、绝技与影之武装资源"),
            ("速度与冷却", "移动 / 攻击 / 游戏速度与技能冷却"),
            ("破防与处决", "破防效率、处决阈值与敌方杀气"),
            ("异常与持续伤害", "出血、灼烧、中毒、冰冻、感电、血爆"),
            ("格挡与鬼步", "格挡窗口、招架、闪避与自动 QTE"),
            ("资源与消耗", "药剂与消耗品、养成货币"),
            ("飞行与穿墙", "飞天、无碰撞移动与安全返回"),
            ("六十六天模式", "死亡次数、周目与难度"),
            ("地图与收集", "铃铛、地图解锁与记忆之花补齐")
        };

        private readonly List<FeatureVm> _all = new();
        private readonly ObservableCollection<FeatureVm> _visible = new();
        private readonly BridgeClient _bridge = new();
        private readonly OverlayService _overlay;
        private readonly HotkeyService _hotkeys = new();
        private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };

        private StateStore _state;
        private string _tab = "basic";
        private bool _connecting;
        private bool _suppressSave;
        private FeatureVm _hotkeyTarget;
        private bool _capturingPanelKey;
        private DateTime _lastAutoConnect = DateTime.MinValue;
        private int _syncDepth;
        private readonly Dictionary<string, FrameworkElement> _views = new();

        public MainWindow()
        {
            InitializeComponent();

            _overlay = new OverlayService(_bridge);

            _bridge.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshStatus));
            _poll.Tick += async (_, __) => await PollAsync();
            _saveTimer.Tick += (_, __) => { _saveTimer.Stop(); SaveState(); };
            _toastTimer.Tick += (_, __) => { _toastTimer.Stop(); Toast.BeginAnimation(OpacityProperty, null); Toast.Opacity = 0; };

            Loaded += OnLoaded;
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _state = new StateStore(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "影之刃零修改器"));

            var catalog = FeatureCatalog.Load();
            foreach (var info in catalog) _all.Add(new FeatureVm(info));
            _suppressSave = true;
            _state.Load(_all);
            _suppressSave = false;

            string startup = NormalizeStartup(App.StartupPage);
            bool validStartup = !string.IsNullOrEmpty(startup) &&
                                NavModel.SelectMany(g => g.Items).Any(i => i.Tag == startup);

            string saved = NormalizeStartup(_state.SelectedPage);
            bool validSaved = !string.IsNullOrEmpty(saved) &&
                              NavModel.SelectMany(g => g.Items).Any(i => i.Tag == saved);
            _tab = validStartup ? startup : (validSaved ? saved : "cat:" + FeatureCategories[0].Name);

            PanelKeyHint.Text = (string.IsNullOrEmpty(_state.PanelToggleHotkey) ? "未设置" : _state.PanelToggleHotkey) + " 显隐";
            CountBadge.Text = $"{_all.Count} 项";

            foreach (var vm in _all)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    ScheduleSave();
                    if (args.PropertyName == nameof(FeatureVm.IsEnabled)) OnToggleChanged(vm);
                };
            }

            BuildNav();
            SelectTab(_tab);
            RebuildHotkeys();

            _hotkeys.Start();
            _hotkeys.Filter = _ => ShouldFire();

            _poll.Start();
            _ = AutoConnectAsync();
        }

        // ---------- 连接 ----------

        private bool _bridgePrompted;

        /// <summary>缺少游戏组件时，让用户选一次并自动复制到程序目录。</summary>
        private bool EnsureBridgeComponent()
        {
            if (Injector.ResolveBridgePath() != null) return true;
            if (_bridgePrompted) return false;
            _bridgePrompted = true;

            var owner = Window.GetWindow(this);
            var ask = owner != null
                ? MessageBox.Show(owner,
                    "还差一个游戏组件：PBZBridge.dll\n\n" +
                    "它在你安装的原修改器目录里（或游戏目录）。\n" +
                    "点「确定」选择这个文件，程序会自动复制一份到本程序旁边，以后不用再选。\n\n" +
                    "如果暂时没有，也可以稍后手动把 PBZBridge.dll 放到 PBZTrainer.exe 同目录。",
                    "选择 PBZBridge.dll", MessageBoxButton.OKCancel, MessageBoxImage.Information)
                : MessageBox.Show(
                    "还差一个游戏组件：PBZBridge.dll\n\n请把它放到 PBZTrainer.exe 同目录。",
                    "选择 PBZBridge.dll", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (ask != MessageBoxResult.OK) return false;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 PBZBridge.dll",
                Filter = "游戏组件 (PBZBridge.dll)|PBZBridge.dll|所有 DLL|*.dll",
                CheckFileExists = true
            };
            bool? picked = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (picked != true) return false;

            try
            {
                string dest = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Injector.BridgeFileName);
                System.IO.File.Copy(dlg.FileName, dest, true);
                ShowToast("游戏组件已就位");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner ?? (Window)this,
                    "复制失败：" + ex.Message + "\n\n可以手动把 PBZBridge.dll 放到程序目录。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private async Task AutoConnectAsync()
        {
            if (_connecting) return;
            _connecting = true;
            try
            {
                SetStatus("正在连接", "#D9A441", "◌");
                ConnectButton.Content = "连接中…";
                ConnectButton.IsEnabled = false;

                if (!EnsureBridgeComponent())
                {
                    SetStatus("未找到游戏组件", "#D9A441", "!");
                    ConnectButton.Content = "连接游戏";
                    return;
                }

                bool ok = await _bridge.ConnectAsync(true, m => Dispatcher.Invoke(() => SetStatus(m, "#8C8390", "◌")));
                if (ok)
                {
                    _views.Clear();
                    if (PageHost.Visibility == Visibility.Visible && !IsFeatureTab(_tab))
                        PageHost.Content = GetView(_tab);
                    Sync(() => _bridge.ApplyStates(_all));
                    RebuildHotkeys();
                    ShowToast("已连接游戏 · " + (_bridge.Ready ? "角色状态就绪" : "请进入存档"));
                }
            }
            finally
            {
                _connecting = false;
                ConnectButton.IsEnabled = true;
                RefreshStatus();
            }
        }

        private void RefreshStatus()
        {
            ConnectButton.Content = _bridge.IsConnected ? "重新连接" : "连接游戏";

            switch (_bridge.State)
            {
                case BridgeState.Connected:
                    if (_bridge.Ready)
                        SetStatus("游戏已就绪", "#57C7B4", "●");
                    else
                        SetStatus("等待进入场景", "#D9A441", "◌");
                    break;
                case BridgeState.Connecting:
                    SetStatus("正在连接", "#D9A441", "◌");
                    break;
                case BridgeState.GameMissing:
                    SetStatus("请先启动游戏", "#8C8390", "○");
                    break;
                case BridgeState.Error:
                    SetStatus(_bridge.LastMessage, "#C0392B", "○");
                    break;
                default:
                    SetStatus("未连接", "#6E6673", "○");
                    break;
            }

            string info = "等待游戏连接";
            if (_bridge.IsConnected)
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(_bridge.Player)) parts.Add("角色 " + _bridge.Player);
                if (!string.IsNullOrEmpty(_bridge.World))
                {
                    string w = _bridge.World;
                    int i = w.LastIndexOf('/');
                    if (i >= 0) w = w.Substring(i + 1);
                    int dot = w.IndexOf('.');
                    if (dot > 0) w = w.Substring(0, dot);
                    parts.Add("地图 " + w);
                }
                if (_bridge.Position.Length == 3)
                    parts.Add($"坐标 {_bridge.Position[0]:0},{_bridge.Position[1]:0}");
                info = parts.Count > 0 ? string.Join(" · ", parts) : "已连接游戏进程";
            }
            FooterInfo.Text = info;
            SubtitleText.Text = _bridge.IsConnected
                ? (_bridge.Ready ? "对话已建立 · 功能即时生效" : "已连接 · 等待角色状态")
                : "水墨江湖 · 全功能训练器";
        }

        private void SetStatus(string text, string color, string glyph)
        {
            StatusText.Text = text;
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private async Task PollAsync()
        {
            if (_bridge.IsConnected)
            {
                try
                {
                    await _bridge.RefreshAsync();
                    Sync(() => _bridge.ApplyStates(_all));
                }
                catch
                {
                    _bridge.Disconnect();
                    RefreshStatus();
                }
                return;
            }

            if (_connecting) return;
            if ((DateTime.UtcNow - _lastAutoConnect).TotalSeconds < 6) return;
            _lastAutoConnect = DateTime.UtcNow;

            if (Injector.FindGame() == null) return;
            await AutoConnectAsync();
        }

        // ---------- 过滤与渲染 ----------

        private string CurrentCategory => _tab.StartsWith("cat:") ? _tab.Substring(4) : "";

        private void ApplyFilter()
        {
            IEnumerable<FeatureVm> items;

            if (IsFeatureTab(_tab))
            {
                string cat = CurrentCategory;
                items = _all.Where(f => f.Page == cat);
            }
            else
            {
                items = Enumerable.Empty<FeatureVm>();
            }

            string q = SearchBox.Text?.Trim() ?? "";
            if (q.Length > 0)
                items = items.Where(f => f.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || f.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || f.Id.Contains(q, StringComparison.OrdinalIgnoreCase));

            var list = items.ToList();
            _visible.Clear();
            foreach (var vm in list) _visible.Add(vm);
            RenderCards(list);

            EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Text = "没有匹配的功能，试试其他关键词";

            if (IsFeatureTab(_tab))
            {
                int total = _all.Count(f => f.Page == CurrentCategory);
                CountBadge.Text = total + " 项";
                PageTitle.Text = CurrentCategory;
                PageSubtitle.Text = FeatureCategories.FirstOrDefault(c => c.Name == CurrentCategory).Hint
                                    ?? "扩展参数与高级配置";
            }
        }

        /// <summary>每个分类自成一页，这里只负责把卡片铺成两列。</summary>
        private void RenderCards(List<FeatureVm> list)
        {
            FeatureSections.Children.Clear();
            if (list.Count == 0) return;

            var template = (DataTemplate)FindResource("CardTemplate");
            var grid = new UniformGrid { Columns = 2 };
            foreach (var vm in list)
                grid.Children.Add(new ContentControl { Content = vm, ContentTemplate = template });
            FeatureSections.Children.Add(grid);
        }

        private void ScheduleSave()
        {
            if (_suppressSave || _state == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveState()
        {
            if (_state == null) return;
            _state.SelectedPage = IsFeatureTab(_tab) ? CurrentCategory : _tab;
            _state.Save(_all);
            SaveHint.Text = _state.WriteFailed ? "状态暂时无法写入" : "状态已自动保存";
        }

        // ---------- 交互 ----------

        private static bool IsFeatureTab(string tag) => tag != null && tag.StartsWith("cat:");

        private static (string Group, (string Tag, string Label, string Hint)[] Items)[] BuildNavModel()
        {
            var featureItems = FeatureCategories
                .Select(c => ("cat:" + c.Name, c.Name, c.Hint))
                .ToArray();

            return new[]
            {
                ("功能", featureItems),
                ("物品", new[]
                {
                    ("add", "添加物品", "从完整物品目录中选取并添加"),
                    ("inv", "背包管理", "改持有数量 / 删除记录")
                }),
                ("世界", new[]
                {
                    ("spawn", "生成怪物", "按原生流程生成敌人"),
                    ("map", "探索与地图", "读取地点并传送"),
                    ("minimap", "小地图", "游戏画面上的实时小地图"),
                    ("esp", "透视", "目标方框与标签")
                }),
                ("更多", new[]
                {
                    ("quest", "自定义任务", "全部任务 · 标记完成 / 重置"),
                    ("achievement", "成就", "点亮 / 取消成就"),
                    ("appearance", "外观解锁", "解锁角色外观"),
                    ("model", "角色模型", "替换角色模型 / 还原"),
                    ("skill", "技能释放", "直接释放攻击技能"),
                    ("chain", "技能连段", "编排招式依次释放"),
                    ("bossmove", "BOSS 招式", "播放首领招式"),
                    ("resource", "资源设置", "直接设置资源数量"),
                    ("weapon", "武器状态", "当前武器能力参数"),
                    ("attribute", "角色属性", "直接改写角色属性数值")
                }),
                ("自动化", new[]
                {
                    ("completion", "一键全收集", "九类收集 / 任务 / 战绩 / 地图")
                })
            };
        }

        private static readonly (string Group, (string Tag, string Label, string Hint)[] Items)[] NavModel = BuildNavModel();

        private readonly Dictionary<string, (Button Button, Border Bar, TextBlock Text)> _nav = new();

        private void BuildNav()
        {
            NavPanel.Children.Clear();
            _nav.Clear();

            foreach (var group in NavModel)
            {
                NavPanel.Children.Add(new TextBlock
                {
                    Text = group.Group,
                    FontSize = 10,
                    Foreground = Ui.Brush("SubText"),
                    Margin = new Thickness(10, 8, 8, 2)
                });

                foreach (var item in group.Items)
                {
                    var bar = new Border
                    {
                        Width = 3,
                        Height = 13,
                        CornerRadius = new CornerRadius(2),
                        Background = Ui.Brush("Accent"),
                        Visibility = Visibility.Collapsed,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    var text = new TextBlock
                    {
                        Text = item.Label,
                        FontSize = 12.5,
                        Foreground = Ui.Brush("SubText"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    content.Children.Add(bar);
                    content.Children.Add(text);

                    var btn = new Button
                    {
                        Content = content,
                        Style = Ui.Style("Btn"),
                        Height = 29,
                        Margin = new Thickness(0, 0, 0, 0),
                        Padding = new Thickness(10, 0, 8, 0),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Tag = item.Tag
                    };
                    btn.Click += (_, __) => SelectTab(item.Tag);
                    NavPanel.Children.Add(btn);
                    _nav[item.Tag] = (btn, bar, text);
                }
            }
        }

        /// <summary>兼容旧版「基础功能 / 高级功能 / basic / advanced」写法。</summary>
        private static string NormalizeStartup(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (raw == "基础功能" || raw == "basic" || raw == "高级功能" || raw == "advanced")
                return "cat:" + FeatureCategories[0].Name;
            if (FeatureCategories.Any(c => c.Name == raw)) return "cat:" + raw;
            return raw;
        }

        private void SelectTab(string tag)
        {
            _tab = tag;

            foreach (var kv in _nav)
            {
                bool on = kv.Key == tag;
                kv.Value.Button.Background = on ? Ui.Brush("Chip") : Brushes.Transparent;
                kv.Value.Text.Foreground = on ? Ui.Brush("Text") : Ui.Brush("SubText");
                kv.Value.Text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                kv.Value.Bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            if (IsFeatureTab(tag))
            {
                PageHead.Visibility = Visibility.Visible;
                SearchHost.Visibility = Visibility.Visible;
                ChipHost.Visibility = Visibility.Collapsed;
                FeatureHost.Visibility = Visibility.Visible;
                PageHost.Visibility = Visibility.Collapsed;
                PageHost.Content = null;

                PageTitle.Text = CurrentCategory;
                PageSubtitle.Text = FeatureCategories.FirstOrDefault(c => c.Name == CurrentCategory).Hint ?? "";
                CountBadge.Text = _all.Count(f => f.Page == CurrentCategory) + " 项";
                ApplyFilter();
            }
            else
            {
                PageHead.Visibility = Visibility.Collapsed;
                SearchHost.Visibility = Visibility.Collapsed;
                ChipHost.Visibility = Visibility.Collapsed;
                FeatureHost.Visibility = Visibility.Collapsed;
                PageHost.Content = GetView(tag);
                PageHost.Visibility = Visibility.Visible;
                CountBadge.Text = NavModel.SelectMany(g => g.Items).First(i => i.Tag == tag).Label;
            }
            ScheduleSave();
        }

        private FrameworkElement GetView(string tag)
        {
            if (_views.TryGetValue(tag, out var cached)) return cached;
            FrameworkElement view = tag switch
            {
                "add" => new CatalogView(_bridge, CatalogMode.Grant, "添加物品",
                    "完整物品目录，选中后按数量添加；只补缺，不覆盖已有数量。", "items"),
                "inv" => new CatalogView(_bridge, CatalogMode.Inventory, "背包管理",
                    "修改持有数量或删除整条记录；删除会写入存档，不能撤销。", "inventory"),
                "spawn" => new CatalogView(_bridge, CatalogMode.Spawn, "生成怪物",
                    "按游戏原生生成流程在角色附近生成怪物，可设数量（1~10）、距离与阵营。", "enemies"),
                "map" => new CatalogView(_bridge, CatalogMode.Travel, "探索与地图",
                    "读取全域地点并直接传送；也可记录当前位置随时返回。", "locations"),
                "quest" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "自定义任务",
                    Subtitle = "读取全部任务，可标记完成或重置；会改变任务与剧情进度。",
                    LoadType = "quests",
                    StateHeader = "任务状态",
                    PrimaryLabel = "标记完成",
                    Primary = (b, r, v) => b.QuestSetAsync(r.Id, true, false),
                    SecondaryLabel = "重置任务",
                    Secondary = (b, r, v) => b.QuestSetAsync(r.Id, false, true),
                    EmptyHint = "任务目录为空：请先进入存档再点刷新"
                }),
                "achievement" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "成就",
                    Subtitle = "点亮或取消成就；只影响成就列表，不改任务进度。",
                    LoadType = "achievements",
                    StateHeader = "成就状态",
                    PrimaryLabel = "点亮",
                    Primary = (b, r, v) => b.AchievementSetAsync(r.Id, true),
                    SecondaryLabel = "取消",
                    Secondary = (b, r, v) => b.AchievementSetAsync(r.Id, false),
                    StateReader = n => n["owned"].Num() > 0 ? "已点亮" : "未点亮"
                }),
                "appearance" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "外观解锁",
                    Subtitle = "解锁角色外观（换装 / 配色等），解锁后到游戏内外观界面切换。",
                    LoadType = "appearances",
                    StateHeader = "状态",
                    PrimaryLabel = "解锁外观",
                    Primary = (b, r, v) => b.AppearanceUnlockAsync(r.Id),
                    StateReader = n => n["owned"].Num() > 0 ? "已解锁" : "未解锁"
                }),
                "model" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "角色模型",
                    Subtitle = "把角色替换成其他模型（首领 / NPC）；用完记得点「还原默认」。",
                    LoadType = "character_models",
                    StateHeader = "类别",
                    PrimaryLabel = "应用模型",
                    Primary = (b, r, v) => b.CharacterModelApplyAsync(r.Id),
                    SecondaryLabel = "还原默认",
                    Secondary = (b, r, v) => b.CharacterModelRestoreAsync(),
                    SecondaryWithoutSelection = true,
                    StateReader = n => ""
                }),
                "skill" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "技能释放",
                    Subtitle = "直接释放一次攻击技能；部分技能依赖装备或架势，可能提示不可用。",
                    LoadType = "player_skills",
                    StateHeader = "可释放",
                    PrimaryLabel = "释放",
                    StateReader = n => n["available"].Flag(true) ? "可释放" : "不可用",
                    Primary = (b, r, v) => b.PlayerSkillCastAsync(r.Id)
                }),
                "bossmove" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "BOSS 招式",
                    Subtitle = "让角色播放首领招式（观赏 / 截图用），会覆盖当前动作。",
                    LoadType = "boss_moves",
                    StateHeader = "类型",
                    RetryWhenEmpty = 3,
                    EmptyHint = "招式目录为空：需要在首领战中锁定首领才会出现，进入战斗后会自动重试",
                    PrimaryLabel = "播放招式",
                    Primary = (b, r, v) => b.BossMoveAsync(r.Id)
                }),
                "resource" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "资源设置",
                    Subtitle = "直接设置养成资源的数量（铸造金属、琥珀、货币等）。",
                    LoadType = "resources",
                    StateHeader = "当前持有",
                    StateReader = n => n["owned"].Num().ToString("0.##"),
                    ShowValue = true,
                    ValueLabel = "数量",
                    ValueDefault = "999",
                    PrimaryLabel = "设置数量",
                    Primary = (b, r, v) => b.ResourceSetAsync(r.Id, double.TryParse(v, out var d) ? d : 0)
                }),
                "weapon" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "武器状态",
                    Subtitle = "当前武器的能力参数；断开连接或点「安全恢复」时还原本次修改。",
                    LoadType = "weapon_states",
                    StateHeader = "当前值",
                    ShowValue = true,
                    ValueLabel = "设为",
                    ValueDefault = "0",
                    PrimaryLabel = "应用数值",
                    Primary = (b, r, v) => b.WeaponStateSetAsync(
                        r.Id, r.Raw?.Str("identity") ?? "", double.TryParse(v, out var d) ? d : 0),
                    StateReader = n => n["value"].Num().ToString("0.###"),
                    EmptyHint = "当前没有可调的武器参数：先装备武器再刷新"
                }),
                "attribute" => new ActionListView(_bridge, new ActionSpec
                {
                    Title = "角色属性",
                    Subtitle = "直接改写角色属性数值（223 项）。填的是绝对值，不确定就先记下原值，改错可用存档回滚。",
                    LoadType = "attributes",
                    StateHeader = "分类",
                    ShowValue = true,
                    ValueLabel = "设为",
                    ValueDefault = "100",
                    PrimaryLabel = "写入数值",
                    Primary = (b, r, v) => b.AttributeSetAsync(r.Id, double.TryParse(v, out var d) ? d : 0),
                    LocalRows = PBZTrainer.Models.AttributeCatalog.Rows,
                    StateReader = n => ""
                }),
                "chain" => new SkillChainView(_bridge),
                "completion" => new CompletionView(_bridge),
                "minimap" => new MinimapView(_bridge, _overlay),
                "esp" => new EspView(_bridge, _overlay),
                _ => null
            };
            if (view != null) _views[tag] = view;
            return view;
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchHint != null)
                SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        /// <summary>开关状态变化（鼠标、键盘、UIA、热键都会走这里）。</summary>
        private void OnToggleChanged(FeatureVm vm)
        {
            if (_syncDepth > 0 || vm.IsBusy || vm.IsAction) return;
            _ = ApplyAsync(vm, vm.IsEnabled);
        }

        private void Sync(Action action)
        {
            _syncDepth++;
            try { action(); }
            finally { _syncDepth--; }
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not FeatureVm vm) return;

            if (vm.IsAction)
            {
                await ApplyAsync(vm, true);
                return;
            }

            BeginHotkeyCapture(vm, null);
        }

        private void StepUp_Click(object sender, RoutedEventArgs e) => Step(sender, true);
        private void StepDown_Click(object sender, RoutedEventArgs e) => Step(sender, false);

        private void Step(object sender, bool up)
        {
            if (((FrameworkElement)sender).DataContext is not FeatureVm vm) return;
            double v = vm.NumericValue;
            double step = v < 1 ? 0.1 : v < 10 ? 1 : v < 100 ? 5 : 100;
            v = Math.Max(0, up ? v + step : v - step);
            vm.ValueText = v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            ScheduleSave();
            if (vm.IsEnabled && !vm.IsAction) _ = ApplyAsync(vm, true);
        }

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ((FrameworkElement)sender).DataContext is FeatureVm vm)
            {
                Keyboard.ClearFocus();
                if (vm.IsEnabled && !vm.IsAction) _ = ApplyAsync(vm, true);
            }
        }

        private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is FeatureVm vm && vm.IsEnabled && !vm.IsAction)
                _ = ApplyAsync(vm, true);
            ScheduleSave();
        }

        private async Task ApplyAsync(FeatureVm vm, bool target)
        {
            if (!_bridge.IsConnected)
            {
                Sync(() => vm.IsEnabled = false);
                ShowToast("请先连接游戏");
                await AutoConnectAsync();
                return;
            }

            if (vm.IsBusy) return;

            bool previous = !target;
            vm.IsBusy = true;

            try
            {
                var result = await _bridge.ApplyFeatureAsync(vm);

                if (result == null || !BridgeClient.IsTrue(result["ok"]))
                {
                    Sync(() => vm.IsEnabled = previous);
                    ShowToast(vm.Title + " · " + (Str(result?["message"]) ?? "未能应用"));
                    return;
                }

                if (vm.Info.IsCollection)
                {
                    int steps = 0;
                    var last = await _bridge.RunCollectionAsync(result, n =>
                    {
                        steps = n;
                        Dispatcher.Invoke(() => ShowToast($"{vm.Title} · 处理中 {n} 步", 1.2));
                    }, CancellationToken.None);
                    bool ok = last != null && BridgeClient.IsTrue(last["ok"]);
                    ShowToast(vm.Title + " · " + (ok ? $"已完成 {steps} 步" : "收集操作已中止，已完成的部分请在游戏中核对"));
                }
                else
                {
                    ShowToast(vm.Title + " · " + (Str(result["message"]) ?? "已应用"));
                }

                Sync(() =>
                {
                    if (result["enabled"] != null) vm.IsEnabled = BridgeClient.IsTrue(result["enabled"]);
                });
                await _bridge.RefreshAsync();
                Sync(() => _bridge.ApplyStates(_all));
            }
            catch (Exception ex)
            {
                Sync(() => vm.IsEnabled = previous);
                ShowToast(vm.Title + " · " + ex.Message);
            }
            finally
            {
                vm.IsBusy = false;
                ScheduleSave();
            }
        }

        private static string Str(JsonNode n)
        {
            if (n == null) return null;
            try { return n.GetValue<string>(); } catch { return null; }
        }

        // ---------- 快捷键 ----------

        private void RebuildHotkeys()
        {
            _hotkeys.Clear();
            if (!string.IsNullOrEmpty(_state?.PanelToggleHotkey))
            {
                _hotkeys.Register(_state.PanelToggleHotkey, () => Dispatcher.BeginInvoke(new Action(TogglePanelVisibility)));
                PanelKeyHint.Text = _state.PanelToggleHotkey + " 显隐";
            }
            else
            {
                PanelKeyHint.Text = "未设置显隐热键";
            }

            foreach (var vm in _all)
            {
                if (string.IsNullOrEmpty(vm.Hotkey)) continue;
                var captured = vm;
                _hotkeys.Register(vm.Hotkey, () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_bridge.IsConnected) { ShowToast("请先连接游戏"); return; }
                    captured.IsEnabled = !captured.IsEnabled;
                })));
            }
        }

        private bool ShouldFire()
        {
            if (!_state.OnlyWhenTargetOrPanelForeground) return true;
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out uint pid);
            return pid == (uint)Environment.ProcessId || (uint)_bridge.TargetPid == pid;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        private void BeginHotkeyCapture(FeatureVm vm, Action<string> commit)
        {
            _hotkeyTarget = vm;
            _capturingPanelKey = commit != null;
            HotkeyTitle.Text = vm == null ? "设置面板显隐热键" : "为「" + vm.Title + "」设置快捷键";
            HotkeyPreview.Text = "等待按键…";
            HotkeyLayer.Visibility = Visibility.Visible;
            _hotkeys.Suspended = true;
            Activate();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (HotkeyLayer.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                if (e.Key == Key.Escape)
                {
                    CloseHotkeyLayer();
                    return;
                }
                if (e.Key == Key.Back || e.Key == Key.Delete)
                {
                    CommitHotkey("");
                    return;
                }
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

                var combo = BuildCombo(Keyboard.Modifiers, key);
                HotkeyPreview.Text = combo;
                CommitHotkey(combo);
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && SearchHost.Visibility == Visibility.Visible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
            }
        }

        private static string BuildCombo(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(HotkeyService.KeyName(KeyInterop.VirtualKeyFromKey(key)));
            return string.Join("+", parts);
        }

        private void CommitHotkey(string combo)
        {
            string normalized = string.IsNullOrEmpty(combo) ? "" : HotkeyService.Normalize(combo);

            if (_hotkeyTarget == null)
            {
                _state.PanelToggleHotkey = normalized;
                _hotkeyTarget = null;
                CloseHotkeyLayer();
                RebuildHotkeys();
                ScheduleSave();
                ShowToast(string.IsNullOrEmpty(normalized) ? "显隐热键已清除" : "显隐热键已保存：" + normalized);
                return;
            }

            var duplicate = _all.FirstOrDefault(f => f != _hotkeyTarget && !string.IsNullOrEmpty(f.Hotkey)
                                && HotkeyService.Normalize(f.Hotkey) == normalized);
            if (duplicate != null && !string.IsNullOrEmpty(normalized))
            {
                ShowToast($"此快捷键已被「{duplicate.Title}」占用，是否覆盖？（已覆盖）");
                duplicate.Hotkey = "";
            }

            if (!string.IsNullOrEmpty(normalized) && HotkeyService.Normalize(_state.PanelToggleHotkey) == normalized)
            {
                ShowToast("该按键已用于面板显隐，请换一个");
                _hotkeyTarget.Hotkey = "";
                CloseHotkeyLayer();
                RebuildHotkeys();
                ScheduleSave();
                return;
            }

            _hotkeyTarget.Hotkey = normalized;
            string title = _hotkeyTarget.Title;
            _hotkeyTarget = null;
            CloseHotkeyLayer();
            RebuildHotkeys();
            ScheduleSave();
            ShowToast(string.IsNullOrEmpty(normalized) ? title + " · 快捷键已清除" : title + " · 快捷键已保存：" + normalized);
        }

        private void CloseHotkeyLayer()
        {
            HotkeyLayer.Visibility = Visibility.Collapsed;
            _hotkeys.Suspended = false;
            _hotkeyTarget = null;
            _capturingPanelKey = false;
        }

        private void TogglePanelVisibility()
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        // ---------- 窗口 ----------

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e) => _ = AutoConnectAsync();

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinButton.Foreground = Topmost
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0392B"))
                : (Brush)FindResource("SubText");
            ShowToast(Topmost ? "窗口已置顶" : "已取消窗口置顶");
        }

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _poll.Stop();
                _overlay.StopAll();
                SaveState();
                _hotkeys.Dispose();
                _bridge.Dispose();
            }
            catch { }
        }

        private void ShowToast(string text, double seconds = 2.4)
        {
            ToastText.Text = text;
            Toast.Opacity = 1;
            _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}
