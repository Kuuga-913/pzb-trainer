using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Threading;
using PBZTrainer.Views;

namespace PBZTrainer.Services
{
    /// <summary>
    /// 养成货币（意识尘埃）与专属升级材料获取倍率服务。
    /// 解决游戏原生不计算 MindDustAdditiveMultiplier 属性的问题，
    /// 并在获取材料时根据设定倍率自动补偿差额。
    /// </summary>
    public sealed class MaterialMultiplierService : IDisposable
    {
        private readonly BridgeClient _bridge;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, double> _baseline = new(StringComparer.OrdinalIgnoreCase);
        private bool _enabled;
        private double _multiplier = 1.0;
        private bool _initialized;
        private bool _isBusy;

        /// <summary>
        /// 受倍率影响的专属目标物品与货币集合（意识尘埃 + 32项升级与打磨材料，不含铸魂材料与饰品碎片）。
        /// </summary>
        public static readonly HashSet<string> TargetIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // 基础养成货币
            "currency:dust",

            // 饰品打磨材料 (2项)
            "item:Ac_Material_1",  // 污秽残晶
            "item:Ac_Material_4",  // 提纯靛晶

            // 药水升级材料 (3项)
            "item:Medi_Material_1", // 学徒药师记忆
            "item:Medi_Material_2", // 熟练药师记忆
            "item:Medi_Material_3", // 大师药师记忆

            // 主武器升级材料 (12项)
            "item:Weapon_Material_01", // 铸造金属
            "item:Weapon_Material_02", // 抛光铸造金属
            "item:Weapon_Material_03", // 无瑕铸造金属
            "item:Weapon_Material_04", // 双生金属
            "item:Weapon_Material_05", // 抛光双生金属
            "item:Weapon_Material_06", // 无瑕双生金属
            "item:Weapon_Material_07", // 长板金属
            "item:Weapon_Material_08", // 抛光长板金属
            "item:Weapon_Material_09", // 无瑕长板金属
            "item:Weapon_Material_10", // 溶剂
            "item:Weapon_Material_11", // 浓缩溶剂
            "item:Weapon_Material_12", // 熔合万物之溶剂

            // 影之武装升级材料 (15项)
            "item:Weapon_Material_13", // 异铜锭
            "item:Weapon_Material_14", // 天外异铜
            "item:Weapon_Material_15", // 万锻天外异铜
            "item:Weapon_Material_16", // 魔火炭
            "item:Weapon_Material_17", // 热魔火炭
            "item:Weapon_Material_18", // 极热魔火炭
            "item:Weapon_Material_19", // 机关齿轮
            "item:Weapon_Material_20", // 精密机关齿轮
            "item:Weapon_Material_21", // 嬴式机关齿轮
            "item:Weapon_Material_22", // 古木
            "item:Weapon_Material_23", // 天樟古木
            "item:Weapon_Material_24", // 禁忌天樟古木
            "item:Weapon_Material_25", // 招魂香灰
            "item:Weapon_Material_26", // 迷幻香灰
            "item:Weapon_Material_27"  // 供奉圣母的香灰
        };

        public MaterialMultiplierService(BridgeClient bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += async (_, _) => await TickAsync();
        }

        public bool IsEnabled => _enabled;
        public double Multiplier => _multiplier;

        public void Start(double multiplier)
        {
            _multiplier = Math.Max(1.0, multiplier);
            _enabled = true;
            _initialized = false;
            _baseline.Clear();
            if (!_timer.IsEnabled)
                _timer.Start();
        }

        public void UpdateMultiplier(double multiplier)
        {
            _multiplier = Math.Max(1.0, multiplier);
        }

        public void Stop()
        {
            _enabled = false;
            _initialized = false;
            _baseline.Clear();
            _timer.Stop();
        }

        public void ResetBaseline()
        {
            _initialized = false;
            _baseline.Clear();
        }

        private async Task TickAsync()
        {
            if (!_enabled || !_bridge.IsConnected || !_bridge.Ready || !_bridge.CatalogReady)
            {
                _initialized = false;
                return;
            }

            if (_isBusy) return;
            _isBusy = true;

            try
            {
                var r = await _bridge.CatalogAsync("resources");
                if (r == null || !r.Flag("ok", false)) return;

                if (r["items"] is not JsonArray items) return;

                var current = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in items.OfType<JsonObject>())
                {
                    string id = node.Str("id");
                    if (string.IsNullOrEmpty(id) || !TargetIds.Contains(id)) continue;

                    double val = node.Num("value");
                    if (val <= 0 && node["owned"] != null)
                        val = node.Num("owned");
                    current[id] = val;
                }

                if (!_initialized)
                {
                    foreach (var kvp in current)
                    {
                        _baseline[kvp.Key] = kvp.Value;
                    }
                    _initialized = true;
                    return;
                }

                if (_multiplier <= 1.0)
                {
                    foreach (var kvp in current)
                    {
                        _baseline[kvp.Key] = kvp.Value;
                    }
                    return;
                }

                foreach (var kvp in current)
                {
                    string id = kvp.Key;
                    double currentVal = kvp.Value;

                    if (!_baseline.TryGetValue(id, out double baseVal))
                    {
                        _baseline[id] = currentVal;
                        continue;
                    }

                    if (currentVal > baseVal)
                    {
                        double delta = currentVal - baseVal;
                        long bonus = (long)Math.Round(delta * (_multiplier - 1.0));

                        if (bonus > 0)
                        {
                            if (string.Equals(id, "currency:dust", StringComparison.OrdinalIgnoreCase))
                            {
                                long targetVal = (long)currentVal + bonus;
                                _baseline[id] = targetVal;
                                try
                                {
                                    await _bridge.ResourceSetAsync("currency:dust", targetVal);
                                }
                                catch { }
                            }
                            else
                            {
                                _baseline[id] = currentVal + bonus;
                                try
                                {
                                    await _bridge.GrantAsync(new[] { id }, (int)bonus);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            _baseline[id] = currentVal;
                        }
                    }
                    else
                    {
                        _baseline[id] = currentVal;
                    }
                }
            }
            catch
            {
                // 忽略轮询临时异常，等待下一次周期
            }
            finally
            {
                _isBusy = false;
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _enabled = false;
            _initialized = false;
            _baseline.Clear();
        }
    }
}
