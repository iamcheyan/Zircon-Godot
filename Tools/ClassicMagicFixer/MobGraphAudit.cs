using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 只读导出：全部刷怪信息（RespawnInfo → 怪物/地图/区域点集/数量/间隔）+ 怪物表，
/// 供离线与 Mud3 Mon_Def/*.gen 对照审计。不写库。
/// </summary>
internal static class MobGraphAudit
{
    public static int Run(string root, string outPath)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly,
            typeof(MonsterInfo).Assembly, typeof(NPCInfo).Assembly);

        var respawns = session.GetCollection<RespawnInfo>().Binding;
        var monsters = session.GetCollection<MonsterInfo>().Binding;
        var maps = session.GetCollection<MapInfo>().Binding;

        var mapList = maps.OrderBy(x => x.Index).Select(x => new Dictionary<string, object>
        {
            ["index"] = x.Index,
            ["fileName"] = x.FileName ?? "",
            ["description"] = x.Description ?? "",
            ["dungeon"] = x.Dungeon?.Name ?? "",
            ["instance"] = x.Instance?.Name ?? "",
            ["spawnMultiplier"] = x.Dungeon == null ? 1.0 : (double)x.Dungeon.SpawnMultiplier
        }).ToList();

        var monsterList = monsters.OrderBy(x => x.Index).Select(x => new Dictionary<string, object>
        {
            ["index"] = x.Index,
            ["name"] = x.MonsterName ?? "",
            ["level"] = x.Level,
            ["ai"] = x.AI,
            ["image"] = x.Image.ToString(),
            ["isBoss"] = x.IsBoss,
            ["viewRange"] = x.ViewRange
        }).ToList();

        var respawnList = respawns.OrderBy(x => x.Index).Select(r =>
        {
            var region = r.Region;
            var pts = region?.PointRegion;
            return new Dictionary<string, object>
            {
                ["index"] = r.Index,
                ["monster"] = r.Monster?.MonsterName ?? "",
                ["monsterIndex"] = r.Monster?.Index ?? -1,
                ["isBoss"] = r.Monster?.IsBoss ?? false,
                ["mapFile"] = region?.Map?.FileName ?? "",
                ["mapDesc"] = region?.Map?.Description ?? "",
                ["regionDesc"] = region?.Description ?? "",
                ["pointCount"] = pts?.Length ?? 0,
                ["points"] = pts == null
                    ? new List<Dictionary<string, object>>()
                    : pts.Take(400).Select(p => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y }).ToList(),
                ["count"] = r.Count,
                ["delay"] = r.Delay,
                ["eventSpawn"] = r.EventSpawn,
                ["announce"] = r.Announce,
                ["dropSet"] = r.DropSet,
                ["respawnIndex"] = r.RespawnIndex
            };
        }).ToList();

        var doc = new Dictionary<string, object>
        {
            ["generatedFrom"] = root,
            ["respawnCount"] = respawnList.Count,
            ["monsterCount"] = monsterList.Count,
            ["mapCount"] = mapList.Count,
            ["respawns"] = respawnList,
            ["monsters"] = monsterList,
            ["maps"] = mapList
        };

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"[MobGraphAudit] respawns={respawnList.Count} monsters={monsterList.Count} "
                          + $"maps={mapList.Count} -> {outPath}");
        return 0;
    }
}
