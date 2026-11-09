using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PBZTrainer.Services
{
    /// <summary>原版桥接的完整指令集（目录 / 物品 / 生成 / 传送 / 全收集 / 小地图 / 透视）。</summary>
    public sealed partial class BridgeClient
    {
        /// <summary>按功能 id 直接开关（例如助战预设）。</summary>
        public Task<JsonObject> FeatureAsync(string id, bool enabled, double value, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "feature",
                ["id"] = id,
                ["enabled"] = enabled,
                ["value"] = value
            }, ct, 20000);

        public Task<JsonObject> CapabilitiesAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "capabilities" }, ct, 20000);

        // ---------- 目录 ----------

        public Task<JsonObject> CatalogAsync(string type, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "catalog", ["type"] = type }, ct, 30000);

        public Task<JsonObject> InventoryAsync(CancellationToken ct = default) => CatalogAsync("inventory", ct);

        // ---------- 物品 ----------

        public Task<JsonObject> GrantAsync(IEnumerable<string> ids, int count, CancellationToken ct = default)
        {
            var arr = new JsonArray();
            foreach (var id in ids) arr.Add(id);
            return RequestAsync(new JsonObject { ["command"] = "grant_items", ["ids"] = arr, ["count"] = count }, ct, 30000);
        }

        public Task<JsonObject> InventoryChangeAsync(string id, string snapshot, bool delete, int value, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "inventory_change",
                ["id"] = id,
                ["snapshot"] = snapshot ?? "",
                ["delete"] = delete,
                ["value"] = value
            }, ct, 30000);

        // ---------- 生成 / 复活 ----------

        public Task<JsonObject> SpawnAsync(string id, int count, int distance, bool friendly, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "spawn",
                ["id"] = id,
                ["count"] = count,
                ["distance"] = distance,
                ["friendly"] = friendly
            }, ct, 30000);

        public Task<JsonObject> SpawnStatusAsync(string batch, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "spawn_status", ["batch"] = batch }, ct, 20000);

        public Task<JsonObject> ReviveAsync(string id, int count, int distance, bool friendly, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "revive_enemy",
                ["id"] = id,
                ["count"] = count,
                ["distance"] = distance,
                ["friendly"] = friendly
            }, ct, 30000);

        public Task<JsonObject> ReviveStatusAsync(string batch, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "revive_status", ["batch"] = batch }, ct, 20000);

        // ---------- 任务 / 成就 / 外观 / 模型 / 资源 / 招式 ----------

        public Task<JsonObject> QuestSetAsync(string id, bool completed, bool restart, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "quest_set",
                ["id"] = id,
                ["completed"] = completed,
                ["restart"] = restart
            }, ct, 30000);

        public Task<JsonObject> AchievementSetAsync(string id, bool completed, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "achievement_set",
                ["id"] = id,
                ["completed"] = completed
            }, ct, 25000);

        public Task<JsonObject> AppearanceUnlockAsync(string id, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "appearance_unlock", ["id"] = id }, ct, 25000);

        public Task<JsonObject> CharacterModelApplyAsync(string id, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "character_model_apply", ["id"] = id }, ct, 30000);

        public Task<JsonObject> CharacterModelRestoreAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "character_model_restore" }, ct, 30000);

        public Task<JsonObject> ResourceSetAsync(string id, double value, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "resource_set", ["id"] = id, ["value"] = value }, ct, 30000);

        public Task<JsonObject> AttributeSetAsync(string id, double value, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "attribute_set", ["id"] = id, ["value"] = value }, ct, 30000);

        public Task<JsonObject> BossMoveAsync(string id, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "boss_move", ["id"] = id }, ct, 30000);

        public Task<JsonObject> PlayerSkillCastAsync(string id, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_cast", ["id"] = id }, ct, 30000);

        // ---------- 技能连段 ----------

        public Task<JsonObject> SkillGroupBeginAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_group_begin" }, ct, 30000);

        public Task<JsonObject> SkillGroupCastAsync(string id, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_cast", ["id"] = id }, ct, 30000);

        public Task<JsonObject> SkillGroupStatusAsync(string chainId, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_group_status", ["chainId"] = chainId ?? "" }, ct, 20000);

        public Task<JsonObject> SkillGroupEndAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_group_end" }, ct, 30000);

        public Task<JsonObject> SkillGroupStopAsync(string chainId, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "player_skill_group_stop", ["chainId"] = chainId ?? "" }, ct, 20000);

        public Task<JsonObject> WeaponStateSetAsync(string id, string identity, double value, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "weapon_state_set",
                ["id"] = id,
                ["identity"] = identity ?? "",
                ["value"] = value
            }, ct, 30000);

        // ---------- 位置 / 传送 ----------

        public Task<JsonObject> PositionAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "position" }, ct, 15000);

        public Task<JsonObject> TeleportAsync(string world, double[] position, CancellationToken ct = default)
        {
            var arr = new JsonArray();
            if (position != null) foreach (var d in position) arr.Add(d);
            return RequestAsync(new JsonObject { ["command"] = "teleport", ["world"] = world ?? "", ["position"] = arr }, ct, 20000);
        }

        public Task<JsonObject> TravelAsync(string locationId, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "collection_locate", ["id"] = locationId, ["travel"] = true }, ct, 30000);

        // ---------- 全收集 ----------

        public Task<JsonObject> CompletionPlanAsync(string kind, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "completion.plan", ["kind"] = kind }, ct, 40000);

        public Task<JsonObject> CompletionBeginAsync(string kind, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "completion.begin", ["kind"] = kind }, ct, 40000);

        /// <summary>job 必须是数字（桥接用强类型解析，传字符串会报 type must be number）。</summary>
        public Task<JsonObject> CompletionStepAsync(JsonNode job, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "completion.step", ["job"] = job?.DeepClone() }, ct, 40000);

        public Task<JsonObject> CompletionCancelAsync(JsonNode job, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "completion.cancel", ["job"] = job?.DeepClone() }, ct, 20000);

        // ---------- 小地图 ----------

        public Task<JsonObject> MinimapConfigureAsync(bool enabled, bool clickEnabled, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "minimap.configure",
                ["enabled"] = enabled,
                ["clickEnabled"] = clickEnabled
            }, ct, 20000);

        public Task<JsonObject> MinimapFrameAsync(long stamp, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "minimap.frame", ["stamp"] = stamp }, ct, 15000);

        public Task<JsonObject> MinimapTravelAsync(string stamp, string targetId, CancellationToken ct = default)
            => RequestAsync(new JsonObject
            {
                ["command"] = "minimap.travel_target",
                ["stamp"] = stamp ?? "",
                ["target"] = targetId ?? ""
            }, ct, 30000);

        public Task<JsonObject> MinimapReturnAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "minimap.return" }, ct, 20000);

        // ---------- 透视 ----------

        public Task<JsonObject> EspConfigureAsync(JsonObject settings, bool enabled, CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "esp.configure", ["settings"] = settings, ["enabled"] = enabled }, ct, 20000);

        public Task<JsonObject> EspFrameAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "esp.frame" }, ct, 15000);

        public Task<JsonObject> EspStatusAsync(CancellationToken ct = default)
            => RequestAsync(new JsonObject { ["command"] = "esp.status" }, ct, 15000);
    }
}
