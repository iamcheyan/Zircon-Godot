using System.Security.Cryptography;
using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 按 Mud3 刷怪表编译出的计划重建 System.db 的刷怪数据：
///  - 计划覆盖的地图：删除该图现有 RespawnInfo（与不再被引用的 MapRegion），按 .gen 重建；
///  - 计划未覆盖的地图：保持原样；
///  - 每个 (地图,x,y,range) 生成一个 MapRegion（点集 = 该方块内的可走格，抽样上限 N），
///    其下每个怪物一条 RespawnInfo（Count/Delay 取自 .gen）。
/// </summary>
internal static class RespawnRebuilder
{
    private const int MaxPointsPerRegion = 150;

    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private sealed class Plan
    {
        public List<PlanRegion> regions { get; set; } = new();
        public List<string> replacedMaps { get; set; } = new();
    }

    private sealed class PlanRegion
    {
        public string mapFile { get; set; } = "";
        public int x { get; set; }
        public int y { get; set; }
        public int range { get; set; }
        public string genFile { get; set; } = "";
        public List<PlanRespawn> respawns { get; set; } = new();
    }

    private sealed class PlanRespawn
    {
        public int monsterIndex { get; set; }
        public string monsterName { get; set; } = "";
        public int count { get; set; }
        public int delay { get; set; }
        public string sourceName { get; set; } = "";
    }

    public static int Run(string root, string planPath, string mapDir, bool dryRun)
    {
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(planPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (plan == null || plan.regions.Count == 0)
        {
            Console.Error.WriteLine("[FATAL] 计划解析失败或为空");
            return 1;
        }

        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly,
            typeof(MonsterInfo).Assembly, typeof(NPCInfo).Assembly);

        var respawnCol = session.GetCollection<RespawnInfo>();
        var regionCol = session.GetCollection<MapRegion>();
        var monsterByIndex = session.GetCollection<MonsterInfo>().Binding.ToDictionary(x => x.Index);
        var mapByName = session.GetCollection<MapInfo>().Binding
            .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var replaceSet = plan.replacedMaps.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ---- 1. 删除被覆盖地图的旧刷怪（及其专属区域）----
        var oldRespawns = respawnCol.Binding.Where(r => r.Region?.Map != null
                                                        && replaceSet.Contains(r.Region.Map.FileName)).ToList();
        var touchedRegions = oldRespawns.Select(r => r.Region).Distinct().ToList();
        int removedRespawns = oldRespawns.Count;
        if (!dryRun)
            foreach (var r in oldRespawns)
                r.Delete();

        // 区域若已无任何引用（刷怪/NPC/安全区/移动/任务）则一并删除
        int removedRegions = 0;
        if (!dryRun)
            foreach (var region in touchedRegions)
            {
                if (region.Respawns.Count > 0) continue;
                if (region.NPCs.Count > 0 || region.SafeZones.Count > 0 || region.SourceMovements.Count > 0
                    || region.DestinationMovements.Count > 0 || region.QuestTasks.Count > 0) continue;
                region.Delete();
                removedRegions++;
            }

        // ---- 1b. 清理明显损坏的刷怪条目（计划未覆盖的图上也存在）----
        var broken = respawnCol.Binding.Where(r =>
            r.Monster == null || r.Region == null || r.Region.Map == null || r.Count <= 0 ||
            r.Region.PointRegion == null || r.Region.PointRegion.Length == 0).ToList();
        int removedBroken = broken.Count;
        if (!dryRun)
            foreach (var r in broken)
                r.Delete();

        // ---- 2. 建新区域与刷怪 ----
        var mapCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int createdRegions = 0, createdRespawns = 0, missingMapFiles = 0, emptyPointRegions = 0;

        foreach (var pr in plan.regions)
        {
            if (!mapByName.TryGetValue(pr.mapFile, out var mapInfo)) continue;

            if (!mapCache.TryGetValue(pr.mapFile, out var cells))
            {
                string path = Path.Combine(mapDir, pr.mapFile + ".map");
                if (File.Exists(path)) cells = File.ReadAllBytes(path);
                mapCache[pr.mapFile] = cells;
            }
            if (cells == null)
            {
                missingMapFiles++;
                continue;
            }

            var points = SamplePoints(cells, pr.x, pr.y, pr.range);
            if (points.Count == 0)
            {
                emptyPointRegions++;
                continue;
            }

            var region = regionCol.CreateNewObject();
            region.Map = mapInfo;
            region.Description = $"Mud3 {Path.GetFileNameWithoutExtension(pr.genFile)} {pr.x},{pr.y} r{pr.range}";
            region.PointRegion = points.ToArray();
            createdRegions++;

            foreach (var rp in pr.respawns)
            {
                if (!monsterByIndex.TryGetValue(rp.monsterIndex, out var monster)) continue;
                var respawn = respawnCol.CreateNewObject();
                respawn.Monster = monster;
                respawn.Region = region;
                respawn.Count = rp.count;
                respawn.Delay = rp.delay;
                respawn.EventSpawn = false;
                respawn.Announce = false;
                respawn.DropSet = 0;
                respawn.RespawnIndex = 0;
                createdRespawns++;
            }
        }

        Console.WriteLine($"[RespawnRebuilder] 覆盖地图={replaceSet.Count} 删除旧刷怪={removedRespawns} "
                          + $"删除损坏条目={removedBroken} "
                          + $"删除空区域={removedRegions} 新建区域={createdRegions} 新建刷怪={createdRespawns} "
                          + $"缺地图文件={missingMapFiles} 无可走点区域={emptyPointRegions}");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] 未写入数据库");
            return 0;
        }

        session.Save(true);
        Console.WriteLine($"[RespawnRebuilder] saved. RespawnInfo={respawnCol.Binding.Count} MapRegion={regionCol.Binding.Count}");
        return Sync(root) ? 0 : 1;
    }

    /// <summary>取 (x±range, y±range) 内的可走格（抽样上限 MaxPointsPerRegion）。</summary>
    private static List<System.Drawing.Point> SamplePoints(byte[] cells, int cx, int cy, int range)
    {
        int width = cells[23] << 8 | cells[22];
        int height = cells[25] << 8 | cells[24];
        int offset = 28 + width * height / 4 * 3;

        var all = new List<System.Drawing.Point>();
        int x0 = Math.Max(0, cx - range), x1 = Math.Min(width - 1, cx + range);
        int y0 = Math.Max(0, cy - range), y1 = Math.Min(height - 1, cy + range);

        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                int idx = offset + (x * height + y) * 14;
                if (idx + 1 >= cells.Length) continue;
                byte flag = cells[idx];
                if ((flag & 0x02) != 2 || (flag & 0x01) != 1) continue;
                all.Add(new System.Drawing.Point(x, y));
            }

        if (all.Count <= MaxPointsPerRegion) return all;

        int stride = (int)Math.Ceiling(all.Count / (double)MaxPointsPerRegion);
        var sampled = new List<System.Drawing.Point>();
        for (int i = 0; i < all.Count; i += stride) sampled.Add(all[i]);
        return sampled;
    }

    private static bool Sync(string root)
    {
        string src = Path.Combine(root, "System.db");
        byte[] srcHash = SHA256.HashData(File.ReadAllBytes(src));
        Console.WriteLine($"[源 SHA256] {src}: {Convert.ToHexString(srcHash)}");
        foreach (var dest in Mirrors)
        {
            string absDest = Path.GetFullPath(dest);
            if (string.Equals(absDest, Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(absDest)!);
            File.Copy(src, absDest, overwrite: true);
            if (!srcHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(absDest))))
            {
                Console.Error.WriteLine($"[FATAL] 同步校验失败: {absDest}");
                return false;
            }
            Console.WriteLine($"[已同步] {absDest}");
        }
        return true;
    }
}
