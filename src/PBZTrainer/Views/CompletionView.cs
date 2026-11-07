using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PBZTrainer.Services;

namespace PBZTrainer.Views
{
    /// <summary>一键全收集：九类收集 / 所有任务 / BOSS 战绩 / 所有地图探索。</summary>
    public sealed class CompletionView : Grid
    {
        private sealed class Item
        {
            public string Kind, Title, Desc;
            public TextBlock PlanText, ProgressText;
            public Button Run, Stop;
        }

        private readonly BridgeClient _bridge;
        private readonly TextBox _log = Ui.LogBox(120);
        private readonly List<Item> _items = new();
        private JsonNode _activeJob;
        private string _activeKind;
        private readonly List<string> _addedNames = new();
        private readonly List<string> _recordNames = new();
        private readonly List<string> _failedNames = new();
        private int _statChecked, _statAdded, _statRecords, _statFailures;
        private bool _started;
        private bool _stoppedByUser;

        private static readonly (string Kind, string Title, string Desc)[] Kinds =
        {
            ("collection", "九类收集 · 一键补齐",
                "主武器、影之武装、饰品、药瓶、铃铛、挑战、琥珀、黑石、记忆之花。只补未收集项目，已有物品不重复添加，逐项回读确认。"),
            ("quests", "所有任务 · 一键完成",
                "按任务原生接取与完成流程执行。会改变任务和剧情进度；章节切换时停止剩余操作。"),
            ("bosses", "BOSS 战绩 · 全部难度",
                "补齐当前周目全部难度的首领击败记录；保留已有最快用时及历史次数。"),
            ("exploration", "所有地图探索 · 全域补齐",
                "处理全域地图目录中的收集和挑战记录；逐项核对，未确认的场景拾取点会列为未完成。")
        };

        public CompletionView(BridgeClient bridge)
        {
            _bridge = bridge;
            Margin = new Thickness(22, 4, 22, 0);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = Ui.Col(3, Ui.Text("一键全收集", 17, "Text", true),
                Ui.Sub("只补缺失项，逐项回读；执行前会再次确认。会修改当前存档，建议先备份。"));
            header.Margin = new Thickness(0, 0, 0, 10);
            Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            var list = new StackPanel();
            foreach (var (kind, title, desc) in Kinds) list.Children.Add(BuildCard(kind, title, desc));
            body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 10, 0) });
            var logPanel = Ui.Panel(Ui.Col(6, Ui.Sub("执行日志"), _log));
            Grid.SetColumn(logPanel, 1);
            body.Children.Add(logPanel);
            Grid.SetRow(body, 1);
            Children.Add(body);

            var footer = Ui.Sub("提示：全收集会真实写入存档进度，关闭修改器不会撤销。");
            footer.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(footer, 2);
            Children.Add(footer);

            Loaded += (_, __) => { if (!_loaded) { _loaded = true; _ = RefreshPlansAsync(); } };
        }

        private bool _loaded;

        private UIElement BuildCard(string kind, string title, string desc)
        {
            var planText = Ui.Sub("尚未读取计划");
            var progress = Ui.Sub("");
            var run = Ui.Button("读取并执行", () => _ = RunAsync(kind), "AccentBtn", 30, 108, 12);
            var stop = Ui.Button("停止", () => _ = StopAsync(kind), "Ghost", 30, 68, 12);
            stop.IsEnabled = false;

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Ui.Col(2, Ui.Text(title, 14.5, "Text", true), Ui.Sub(desc));
            head.Children.Add(t);
            var buttons = Ui.Row(8, run, stop);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(buttons, 1);
            head.Children.Add(buttons);

            var stack = Ui.Col(4, head, planText, progress);
            var panel = Ui.Panel(stack, new Thickness(16, 13, 16, 13));
            panel.Margin = new Thickness(0, 0, 0, 10);

            _items.Add(new Item { Kind = kind, Title = title, Desc = desc, PlanText = planText, ProgressText = progress, Run = run, Stop = stop });
            return panel;
        }

        private async Task RefreshPlansAsync()
        {
            if (!_bridge.IsConnected) { Ui.Log(_log, "请先连接游戏"); return; }
            foreach (var it in _items)
            {
                try
                {
                    var r = await _bridge.CompletionPlanAsync(it.Kind);
                    if (!r.Flag("ok"))
                    {
                        it.PlanText.Text = "计划读取失败：" + (r.Str("message") ?? "");
                        continue;
                    }
                    int total = (int)r["total"].Num();
                    var groups = r["groups"] as JsonObject;
                    string g = groups == null ? "" : string.Join(" · ", groups.Select(kv => $"{kv.Key} {kv.Value.Num():0}"));
                    it.PlanText.Text = $"待处理 {total} 项" + (string.IsNullOrEmpty(g) ? "" : "　" + g);
                }
                catch (Exception ex)
                {
                    it.PlanText.Text = "计划读取异常：" + ex.Message;
                }
            }
        }

        private async Task RunAsync(string kind)
        {
            var it = _items.First(x => x.Kind == kind);
            var owner = Window.GetWindow(this);
            var answer = owner != null
                ? MessageBox.Show(owner,
                    $"确认执行「{it.Title}」？\n\n会真实修改当前存档进度，关闭修改器不会撤销，建议先自行备份存档。",
                    "一键全收集", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                : MessageBox.Show(
                    $"确认执行「{it.Title}」？\n\n会真实修改当前存档进度，关闭修改器不会撤销，建议先自行备份存档。",
                    "一键全收集", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) { Ui.Log(_log, "已取消：" + it.Title); return; }

            it.Run.IsEnabled = false;
            it.Stop.IsEnabled = true;
            it.ProgressText.Foreground = Ui.Brush("Jade");
            it.ProgressText.Text = "正在开始…";
            try
            {
                Ui.Log(_log, "开始：" + it.Title);

                // 上一次没跑完的任务先停掉，否则 begin 会被拒绝
                if (_activeJob != null)
                {
                    try { await _bridge.CompletionCancelAsync(_activeJob); } catch { }
                    _activeJob = null;
                }

                var begin = await _bridge.CompletionBeginAsync(kind);
                if (!begin.Flag("ok") && begin["job"] == null)
                {
                    string why = begin.Str("message") ?? "未知原因";
                    it.ProgressText.Foreground = Ui.Brush("Warn");
                    it.ProgressText.Text = "无法开始：" + why;
                    Ui.Log(_log, "无法开始：" + why);
                    return;
                }

                var job = begin["job"];
                if (job == null)
                {
                    it.ProgressText.Foreground = Ui.Brush("Warn");
                    it.ProgressText.Text = "游戏未返回任务标识";
                    Ui.Log(_log, "游戏未返回任务标识（job），已停止");
                    return;
                }

                _activeJob = job;
                _activeKind = kind;
                _started = true;
                _stoppedByUser = false;
                _addedNames.Clear(); _recordNames.Clear(); _failedNames.Clear();
                int total = (int)begin["total"].Num();
                int processed = (int)begin["processed"].Num();
                it.ProgressText.Text = $"已检查 {processed}/{total}";

                var result = begin;
                for (int i = 0; i < 4000; i++)
                {
                    if (!_bridge.IsConnected)
                    {
                        it.ProgressText.Foreground = Ui.Brush("Warn");
                        it.ProgressText.Text = "连接已断开，已停止";
                        Ui.Log(_log, "连接已断开，已停止");
                        return;
                    }

                    await Task.Delay(120);
                    result = await _bridge.CompletionStepAsync(_activeJob);
                    if (result == null) { Ui.Log(_log, "未收到结果，已停止"); return; }

                    if (!result.Flag("ok") && result["processed"] == null)
                    {
                        string why = result.Str("message") ?? "未确认";
                        it.ProgressText.Foreground = Ui.Brush("Warn");
                        it.ProgressText.Text = "已停止：" + why;
                        Ui.Log(_log, "已停止：" + why);
                        return;
                    }

                    processed = (int)result["processed"].Num(processed);
                    total = (int)result["total"].Num(total);
                    int added = (int)result["added"].Num();
                    int records = (int)result["records"].Num();
                    int done = (int)result["done"].Num();
                    int failures = result["failures"] is JsonArray fa ? fa.Count : 0;

                    _statChecked = processed; _statAdded = added; _statRecords = records; _statFailures = failures;
                    CollectStep(result);

                    it.ProgressText.Text = $"已检查 {processed}/{total}　补齐 {added}　仅记录 {records}　未完成 {failures}";
                    if (processed % 20 == 0 || added > 0)
                        Ui.Log(_log, $"进度 {processed}/{total} · 本次补齐 {added} · 仅记录 {records} · 未完成 {failures}");

                    if (!result.Flag("pending")) break;
                }

                bool ok = result.Flag("done");
                it.ProgressText.Foreground = Ui.Brush(ok ? "Jade" : "Warn");
                it.ProgressText.Text = it.ProgressText.Text + (ok ? "　已完成" : "　已结束");
                Ui.Log(_log, ok ? "全部条目已回读确认，已有物品未重复添加。" : (result.Str("message") ?? "操作结束，请核对游戏中的已完成部分。"));
                ShowSummary(it, _stoppedByUser ? "已停止（已完成部分保留，可再次执行继续）"
                                                : (ok ? "已完成" : "已结束（可再次执行继续补齐）"));
            }
            catch (Exception ex)
            {
                it.ProgressText.Foreground = Ui.Brush("Warn");
                it.ProgressText.Text = "执行异常：" + ex.Message;
                Ui.Log(_log, "执行异常：" + ex.Message);
                if (_started) ShowSummary(it, "异常中止");
            }
            finally
            {
                it.Run.IsEnabled = true;
                it.Stop.IsEnabled = false;
                _activeJob = null;
                _activeKind = null;
                _started = false;
                _stoppedByUser = false;
                _ = RefreshPlansAsync();
            }
        }

        private void CollectStep(JsonObject result)
        {
            if (result == null) return;

            void Take(JsonArray arr)
            {
                if (arr == null) return;
                foreach (var node in arr.OfType<JsonObject>())
                {
                    string id = node.Str("id") ?? "";
                    string msg = node.Str("message") ?? "";
                    string name = string.IsNullOrEmpty(id) ? "" : PBZTrainer.Models.CatalogNames.Of(id);
                    bool ok = node.Flag("ok");
                    bool isAdded = node["added"] != null && node.Flag("added");
                    bool isRecord = node["records"] != null && node.Flag("records");
                    string line = string.IsNullOrEmpty(name) ? msg : name;

                    if (!ok)
                    {
                        if (!string.IsNullOrEmpty(msg)) line += "　—　" + msg;
                        if (!_failedNames.Contains(line)) _failedNames.Add(line);
                    }
                    else if (isRecord)
                    {
                        if (!_recordNames.Contains(line)) _recordNames.Add(line);
                    }
                    else
                    {
                        if (!isAdded && !(msg.Contains("已添加") || msg.Contains("补齐"))) return;
                        if (!_addedNames.Contains(line)) _addedNames.Add(line);
                    }
                }
            }

            Take(result["results"] as JsonArray);
            Take(result["failures"] as JsonArray);
            if (result["failures"] is JsonArray fa)
            {
                foreach (var node in fa.OfType<JsonObject>())
                {
                    string id = node.Str("id") ?? "";
                    string msg = node.Str("message") ?? "";
                    string name = string.IsNullOrEmpty(id) ? msg : PBZTrainer.Models.CatalogNames.Of(id);
                    string line = string.IsNullOrEmpty(msg) ? name : (string.IsNullOrEmpty(name) ? msg : name + "　—　" + msg);
                    if (!string.IsNullOrEmpty(line) && !_failedNames.Contains(line)) _failedNames.Add(line);
                }
            }
        }

        private void ShowSummary(Item it, string state)
        {
            string ListOf(List<string> items, int max = 12)
            {
                if (items.Count == 0) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var x in items.Take(max)) sb.AppendLine("· " + x);
                if (items.Count > max) sb.AppendLine($"…… 等 {items.Count} 项");
                return sb.ToString();
            }

            var text = new System.Text.StringBuilder();
            text.AppendLine($"状态：{state}");
            text.AppendLine($"检查 {_statChecked} 项 ｜ 本次补齐 {_statAdded} ｜ 仅记录 {_statRecords} ｜ 未完成 {_statFailures}");
            text.AppendLine();

            if (_addedNames.Count > 0)
            {
                text.AppendLine($"本次补齐（{Math.Max(_addedNames.Count, _statAdded)}）：");
                text.Append(ListOf(_addedNames));
                text.AppendLine();
            }
            if (_recordNames.Count > 0)
            {
                text.AppendLine($"仅更新记录（{_recordNames.Count}）：");
                text.Append(ListOf(_recordNames, 6));
                text.AppendLine();
            }
            if (_failedNames.Count > 0)
            {
                text.AppendLine($"未完成（{_failedNames.Count}）：");
                text.Append(ListOf(_failedNames, 10));
            }
            if (_addedNames.Count == 0 && _failedNames.Count == 0)
                text.AppendLine("本次没有需要补齐的项目（都已拥有）。");

            Ui.Log(_log, "—— 结果 ——");
            foreach (var line in text.ToString().Split('\n'))
                if (line.Trim().Length > 0) Ui.Log(_log, line.TrimEnd());

            var owner = Window.GetWindow(this);
            var body = text.ToString();
            if (body.Length > 1800) body = body.Substring(0, 1800) + "……（完整明细见右侧日志）";
            if (owner != null) MessageBox.Show(owner, body, it.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            else MessageBox.Show(body, it.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task StopAsync(string kind)
        {
            if (_activeJob == null) return;
            try
            {
                _stoppedByUser = true;
                await _bridge.CompletionCancelAsync(_activeJob);
                Ui.Log(_log, "已请求停止，已完成部分保留");
            }
            catch (Exception ex) { Ui.Log(_log, "停止失败：" + ex.Message); }
        }
    }
}
