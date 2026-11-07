using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PBZTrainer.Models
{
    public enum FeatureKind
    {
        Toggle,
        ToggleValue,
        Apply,
        ApplyValue
    }

    /// <summary>一条修改器功能定义，字段与原版 PBZFeatures.json 一一对应。</summary>
    public sealed class FeatureInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Page { get; set; } = "";
        public string Binding { get; set; } = "";
        public string Description { get; set; } = "";
        public FeatureKind Kind { get; set; } = FeatureKind.Toggle;
        public string DefaultValue { get; set; } = "1";

        /// <summary>可输入数值（数值型开关 / 应用型）。</summary>
        public bool HasValue => Kind == FeatureKind.ToggleValue || Kind == FeatureKind.ApplyValue;

        /// <summary>点击后执行一次，而不是保持开关状态。</summary>
        public bool IsAction => Kind == FeatureKind.Apply || Kind == FeatureKind.ApplyValue;

        public bool HasToggle => !IsAction;

        /// <summary>收集类功能：需要 collection_step 轮询。</summary>
        public bool IsCollection => Id == "all_memory_flowers" || Id == "area_bells" || Id == "all_maps";

        public string ActionText => IsAction ? "应用" : "设置快捷键";
    }

    public static class FeatureCatalog
    {
        public static List<FeatureInfo> Load()
        {
            string json = ReadEmbedded();
            var list = new List<FeatureInfo>();
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var f = new FeatureInfo();
                if (e.TryGetProperty("id", out var v)) f.Id = v.GetString() ?? "";
                if (e.TryGetProperty("title", out v)) f.Title = v.GetString() ?? "";
                if (e.TryGetProperty("page", out v)) f.Page = v.GetString() ?? "";
                if (e.TryGetProperty("binding", out v)) f.Binding = v.GetString() ?? "";
                if (e.TryGetProperty("description", out v)) f.Description = v.GetString() ?? "";
                if (e.TryGetProperty("kind", out v))
                {
                    f.Kind = (v.GetString() ?? "toggle") switch
                    {
                        "toggleValue" => FeatureKind.ToggleValue,
                        "apply" => FeatureKind.Apply,
                        "applyValue" => FeatureKind.ApplyValue,
                        _ => FeatureKind.Toggle
                    };
                }
                if (e.TryGetProperty("value", out v))
                {
                    f.DefaultValue = v.ValueKind switch
                    {
                        JsonValueKind.Number => v.GetDouble().ToString("0.####", CultureInfo.InvariantCulture),
                        JsonValueKind.String => v.GetString(),
                        _ => "1"
                    };
                }
                if (string.IsNullOrEmpty(f.Description)) f.Description = Describe(f);
                list.Add(f);
            }
            return list;
        }

        private static string Describe(FeatureInfo f)
        {
            return f.Kind switch
            {
                FeatureKind.ToggleValue => "可调数值 · 修改后自动重新应用",
                FeatureKind.Apply => "点击执行一次",
                FeatureKind.ApplyValue => "填入数值后执行一次",
                _ => ""
            };
        }

        private static string ReadEmbedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("PBZFeatures.json", StringComparison.OrdinalIgnoreCase))
                {
                    using var s = asm.GetManifestResourceStream(name);
                    using var r = new StreamReader(s, Encoding.UTF8);
                    return r.ReadToEnd().TrimStart('\uFEFF');
                }
            }
            throw new FileNotFoundException("内置功能清单缺失");
        }
    }
}
