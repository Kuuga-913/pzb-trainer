using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PBZTrainer.Models
{
    /// <summary>静态目录（items / enemies / collections …）的 id → 中文名映射，用于把回执里的 id 显示成人话。</summary>
    public static class CatalogNames
    {
        private static Dictionary<string, string> _map;
        private static readonly object Gate = new();

        public static string Of(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            Ensure();
            if (_map.TryGetValue(id, out var name)) return name;
            int colon = id.IndexOf(':');
            if (colon > 0 && _map.TryGetValue(id.Substring(colon + 1), out name)) return name;
            return id;
        }

        private static void Ensure()
        {
            if (_map != null) return;
            lock (Gate)
            {
                if (_map != null) return;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var res in asm.GetManifestResourceNames())
                    {
                        if (!res.EndsWith("PBZCatalog.json", StringComparison.OrdinalIgnoreCase)) continue;
                        using var s = asm.GetManifestResourceStream(res);
                        using var r = new StreamReader(s, Encoding.UTF8);
                        using var doc = JsonDocument.Parse(r.ReadToEnd().TrimStart('\uFEFF'));
                        foreach (var group in doc.RootElement.EnumerateObject())
                        {
                            if (group.Value.ValueKind != JsonValueKind.Array) continue;
                            foreach (var e in group.Value.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Object) continue;
                                if (!e.TryGetProperty("id", out var idEl)) continue;
                                if (!e.TryGetProperty("name", out var nameEl)) continue;
                                string id = idEl.GetString();
                                string name = nameEl.GetString();
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && !map.ContainsKey(id))
                                    map[id] = name;
                            }
                        }
                    }
                }
                catch { }
                _map = map;
            }
        }
    }
}
