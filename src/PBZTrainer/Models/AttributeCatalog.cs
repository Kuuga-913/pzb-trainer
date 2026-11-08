using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PBZTrainer.Views;

namespace PBZTrainer.Models
{
    /// <summary>内置的角色属性表（id / 名称 / 分类），用于属性页的列表。</summary>
    public static class AttributeCatalog
    {
        private static List<CatalogRow> _rows;

        public static List<CatalogRow> Rows()
        {
            if (_rows != null) return _rows;

            var list = new List<CatalogRow>();
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith("PBZAttributes.json", StringComparison.OrdinalIgnoreCase)) continue;
                    using var s = asm.GetManifestResourceStream(res);
                    using var r = new StreamReader(s, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(r.ReadToEnd().TrimStart('\uFEFF'));
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        string id = e.TryGetProperty("id", out var i) ? i.GetString() : null;
                        string name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string cat = e.TryGetProperty("category", out var c) ? c.GetString() : "";
                        if (string.IsNullOrEmpty(id)) continue;
                        list.Add(new CatalogRow { Id = id, Name = name ?? id, Category = cat ?? "" });
                    }
                    break;
                }
            }
            catch { }

            list.Sort((a, b) => string.Compare(a.Category, b.Category, StringComparison.Ordinal));
            _rows = list;
            return list;
        }
    }
}
