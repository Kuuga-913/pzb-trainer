using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PBZTrainer.Models;

namespace PBZTrainer.Services
{
    /// <summary>
    /// 状态持久化：完全沿用原版 trainer_state.json 的字段结构
    /// （SelectedPage / AutoSave / OnlyWhenTargetOrPanelForeground / PanelToggleHotkey / Features{Enabled,Value,Hotkey}）。
    /// </summary>
    /// <summary>单个怪物的一键生成热键绑定（每个怪物各自一套参数）。</summary>
    public sealed class SpawnBinding
    {
        public string Hotkey { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; } = 1;
        public int Distance { get; set; } = 800;
        public bool Friendly { get; set; }
    }

    public sealed class StateStore
    {
        public string Directory { get; }
        public string StatePath => Path.Combine(Directory, "trainer_state.json");
        public string PrefsPath => Path.Combine(Directory, "preferences.json");

        public string SelectedPage { get; set; } = "基础功能";
        public bool AutoSave { get; set; } = true;
        public bool OnlyWhenTargetOrPanelForeground { get; set; } = true;
        /// <summary>启动时是否套用上次的开关与数值（默认开）。</summary>
        public bool RestoreSettings { get; set; } = true;
        public string PanelToggleHotkey { get; set; } = "F1";

        /// <summary>每个怪物各自的一键生成热键：怪物 id → 绑定（可同时绑多个）。</summary>
        public Dictionary<string, SpawnBinding> SpawnBindings { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>旧版单条绑定，仅用于迁移到 SpawnBindings。</summary>
        public string SpawnHotkey { get; set; } = "";
        public string SpawnHotkeyId { get; set; } = "";
        public string SpawnHotkeyName { get; set; } = "";
        public int SpawnHotkeyCount { get; set; } = 1;
        public int SpawnHotkeyDistance { get; set; } = 6;
        public bool SpawnHotkeyFriendly { get; set; }

        public string Theme { get; set; } = "雾紫晴岚";
        public List<string> Favorites { get; } = new();
        public bool WriteFailed { get; private set; }

        public StateStore(string dir)
        {
            Directory = dir;
            System.IO.Directory.CreateDirectory(dir);
        }

        public void Load(IEnumerable<FeatureVm> items)
        {
            var map = new Dictionary<string, FeatureVm>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items) map[it.Id] = it;

            if (File.Exists(StatePath))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(StatePath)) as JsonObject;
                    if (root != null)
                    {
                        SelectedPage = Str(root["SelectedPage"]) ?? SelectedPage;
                        AutoSave = Bool(root["AutoSave"], AutoSave);
                        OnlyWhenTargetOrPanelForeground = Bool(root["OnlyWhenTargetOrPanelForeground"], OnlyWhenTargetOrPanelForeground);
                        PanelToggleHotkey = Str(root["PanelToggleHotkey"]) ?? PanelToggleHotkey;
                        SpawnHotkey = Str(root["SpawnHotkey"]) ?? "";
                        SpawnHotkeyId = Str(root["SpawnHotkeyId"]) ?? "";
                        SpawnHotkeyName = Str(root["SpawnHotkeyName"]) ?? "";
                        SpawnHotkeyCount = Int(root["SpawnHotkeyCount"], 1);
                        SpawnHotkeyDistance = Int(root["SpawnHotkeyDistance"], 6);
                        SpawnHotkeyFriendly = Bool(root["SpawnHotkeyFriendly"], false);

                        SpawnBindings.Clear();
                        if (root["SpawnBindings"] is JsonObject binds)
                        {
                            foreach (var kv in binds)
                            {
                                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is not JsonObject o) continue;
                                SpawnBindings[kv.Key] = new SpawnBinding
                                {
                                    Hotkey = Str(o["Hotkey"]) ?? "",
                                    Name = Str(o["Name"]) ?? "",
                                    Count = Int(o["Count"], 1),
                                    Distance = Int(o["Distance"], 800),
                                    Friendly = Bool(o["Friendly"], false)
                                };
                            }
                        }

                        // 旧版单条绑定迁移成一条记录
                        if (!string.IsNullOrEmpty(SpawnHotkey) && !string.IsNullOrEmpty(SpawnHotkeyId)
                            && !SpawnBindings.ContainsKey(SpawnHotkeyId))
                        {
                            SpawnBindings[SpawnHotkeyId] = new SpawnBinding
                            {
                                Hotkey = SpawnHotkey,
                                Name = SpawnHotkeyName,
                                Count = SpawnHotkeyCount <= 0 ? 1 : SpawnHotkeyCount,
                                Distance = SpawnHotkeyDistance <= 0 ? 800 : SpawnHotkeyDistance,
                                Friendly = SpawnHotkeyFriendly
                            };
                        }
                        RestoreSettings = Bool(root["RestoreSettings"], true);
                        if (RestoreSettings && root["Features"] is JsonObject feats)
                        {
                            foreach (var kv in feats)
                            {
                                if (!map.TryGetValue(kv.Key, out var vm)) continue;
                                if (kv.Value is not JsonObject o) continue;
                                vm.IsEnabled = Bool(o["Enabled"], false);
                                string val = Str(o["Value"]);
                                if (!string.IsNullOrEmpty(val)) vm.ValueText = val;
                                vm.Hotkey = Str(o["Hotkey"]) ?? "";
                            }
                        }
                    }
                }
                catch
                {
                    // 读取失败时保留默认值，并在保存时备份原文件
                }
            }

            if (File.Exists(PrefsPath))
            {
                try
                {
                    var p = JsonNode.Parse(File.ReadAllText(PrefsPath)) as JsonObject;
                    if (p != null)
                    {
                        Theme = Str(p["Theme"]) ?? Theme;
                        if (p["Favorites"] is JsonArray arr)
                        {
                            Favorites.Clear();
                            foreach (var n in arr) if (Str(n) is string s) Favorites.Add(s);
                        }
                    }
                }
                catch { }
            }

            foreach (var f in Favorites)
                if (map.TryGetValue(f, out var vm)) vm.IsFavorite = true;
        }

        public void Save(IEnumerable<FeatureVm> items)
        {
            try
            {
                var feats = new JsonObject();
                foreach (var vm in items)
                {
                    feats[vm.Id] = new JsonObject
                    {
                        ["Enabled"] = vm.IsEnabled,
                        ["Value"] = vm.ValueText ?? "",
                        ["Hotkey"] = vm.Hotkey ?? ""
                    };
                }

                var spawnBinds = new JsonObject();
                foreach (var kv in SpawnBindings)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrEmpty(kv.Value?.Hotkey)) continue;
                    spawnBinds[kv.Key] = new JsonObject
                    {
                        ["Hotkey"] = kv.Value.Hotkey,
                        ["Name"] = kv.Value.Name ?? "",
                        ["Count"] = kv.Value.Count,
                        ["Distance"] = kv.Value.Distance,
                        ["Friendly"] = kv.Value.Friendly
                    };
                }

                var root = new JsonObject
                {
                    ["SelectedPage"] = SelectedPage,
                    ["AutoSave"] = AutoSave,
                    ["OnlyWhenTargetOrPanelForeground"] = OnlyWhenTargetOrPanelForeground,
                    ["PanelToggleHotkey"] = PanelToggleHotkey,
                    ["SpawnBindings"] = spawnBinds,
                    ["RestoreSettings"] = RestoreSettings,
                    ["Features"] = feats
                };

                var opt = new JsonSerializerOptions { WriteIndented = true };
                WriteAtomic(StatePath, root.ToJsonString(opt));

                var favs = new JsonArray();
                foreach (var vm in items) if (vm.IsFavorite) favs.Add(vm.Id);
                var prefs = new JsonObject
                {
                    ["Theme"] = Theme,
                    ["Favorites"] = favs
                };
                WriteAtomic(PrefsPath, prefs.ToJsonString(opt));
                WriteFailed = false;
            }
            catch
            {
                WriteFailed = true;
            }
        }

        private static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                File.WriteAllText(path, text);
                try { File.Delete(tmp); } catch { }
            }
        }

        private static string Str(JsonNode n)
        {
            if (n == null) return null;
            try { return n.GetValue<string>(); } catch { return null; }
        }

        private static int Int(JsonNode n, int def)
        {
            if (n == null) return def;
            try { return n.GetValue<int>(); } catch { }
            try { return (int)n.GetValue<double>(); } catch { return def; }
        }

        private static bool Bool(JsonNode n, bool def)
        {
            if (n == null) return def;
            try { return n.GetValue<bool>(); } catch { return def; }
        }
    }
}
