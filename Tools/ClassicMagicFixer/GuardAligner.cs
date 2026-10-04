using System.Drawing;
using System.Security.Cryptography;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 城防守卫坐标对齐与去重修复（GuardInfo）。
///
/// 实测缺陷（2026-10-04 守卫地图坐标审计）：
///  1. 银杏山谷(02) 有 14 条与既有记录完全同坐标的重复大刀守卫
///     （#114-#127 逐条复制 #96-#109，方向也相同）→ 同格双怪叠放；
///  2. 银杏山谷(02) 弓箭守卫 #158 落在不可站立格 (287,203)，
///     服务端 Map.Load 判定该格 flag 不含 (0x02|0x01) → Spawn 返回 false，
///     日志输出 "Failed to spawn Guard Map:银杏山谷"，该守卫永远不出现在地图上。
///
/// 本工具按服务端 Map.cs:82 同一格判定规则扫描全部 GuardInfo：
///  · 不可站立 → 挪到最近的可站立且未被其它守卫占用的格子；
///  · 同地图同格同怪物的重复记录 → 只保留 Index 最小的一条。
///
/// 用法: ClassicMagicFixer guardfix <RootDir> [--dry-run] [--no-sync]
/// </summary>
internal static class GuardAligner
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private sealed record MapCells(byte[] Bytes, int Width, int Height, int Offset)
    {
        public bool Valid(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            byte flag = Bytes[Offset + (x * Height + y) * 14];
            return (flag & 0x02) == 2 && (flag & 0x01) == 1;
        }
    }

    public static int Run(string root, bool dryRun, bool sync)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string mapDir = Path.GetFullPath(Path.Combine(root, "..", "Map"));

        if (!Directory.Exists(mapDir))
        {
            Console.Error.WriteLine($"[FATAL] map dir not found: {mapDir}");
            return 1;
        }

        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(GuardInfo).Assembly, typeof(MapInfo).Assembly, typeof(MonsterInfo).Assembly);

        var guards = session.GetCollection<GuardInfo>().Binding;
        var maps = session.GetCollection<MapInfo>().Binding;

        var mapFiles = maps.Where(m => m.FileName != null)
            .GroupBy(m => m.FileName)
            .ToDictionary(g => g.Key, g => g.First());

        var cellCache = new Dictionary<string, MapCells>();

        MapCells Cells(string fileName)
        {
            if (cellCache.TryGetValue(fileName, out var cached)) return cached;

            string path = Path.Combine(mapDir, fileName + ".map");
            if (!File.Exists(path)) return null;

            byte[] bytes = File.ReadAllBytes(path);
            int width = bytes[23] << 8 | bytes[22];
            int height = bytes[25] << 8 | bytes[24];
            int offset = 28 + width * height / 4 * 3;
            var cells = new MapCells(bytes, width, height, offset);
            cellCache[fileName] = cells;
            return cells;
        }

        foreach (var g in guards.ToList())
        {
            if (g.Map?.FileName == null) continue;
            var cells = Cells(g.Map.FileName);
            if (cells == null)
            {
                Console.WriteLine($"[缺图] {g.Map.FileName} 不存在，守卫 #{g.Index} ({g.X},{g.Y}) 无法校验");
                continue;
            }
            if (cells.Valid(g.X, g.Y)) continue;

            var taken = guards.Where(o => o != g && o.Map == g.Map)
                .Select(o => new Point(o.X, o.Y)).ToHashSet();

            Point? replacement = Nearest(cells, new Point(g.X, g.Y), taken);
            if (replacement == null)
            {
                Console.WriteLine($"[不可修复] #{g.Index} {g.Map.FileName} ({g.X},{g.Y}) 附近无可站立格");
                continue;
            }

            Console.WriteLine($"[挪格] #{g.Index} {g.Map.Description} [{g.Map.FileName}] {g.Monster?.MonsterName} " +
                              $"({g.X},{g.Y}) -> ({replacement.Value.X},{replacement.Value.Y})  原格不可站立");
            if (!dryRun)
            {
                g.X = replacement.Value.X;
                g.Y = replacement.Value.Y;
            }
        }

        if (!dryRun)
        {
            var groups = guards
                .GroupBy(g => new { Map = g.Map?.Index ?? -1, g.X, g.Y, Monster = g.Monster?.Index ?? -1 })
                .Where(x => x.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(g => g.Index).ToList();
                var keep = ordered[0];
                foreach (var dupe in ordered.Skip(1))
                {
                    Console.WriteLine($"[去重] 删除 #{dupe.Index} {dupe.Map?.FileName} ({dupe.X},{dupe.Y}) " +
                                      $"{dupe.Monster?.MonsterName} —— 与 #{keep.Index} 完全重复");
                    dupe.Delete();
                }
            }
        }
        else
        {
            var dupes = guards.GroupBy(g => new { Map = g.Map?.Index ?? -1, g.X, g.Y, Monster = g.Monster?.Index ?? -1 })
                .Where(x => x.Count() > 1)
                .Select(x => x.OrderBy(g => g.Index).Skip(1).Select(g => g.Index).ToList())
                .ToList();
            int total = dupes.Sum(d => d.Count);
            Console.WriteLine($"[去重预检] 将删除 {total} 条重复记录: {string.Join(", ", dupes.SelectMany(d => d))}");
        }

        int before = guards.Count;
        if (!dryRun)
        {
            session.Save(true);
            Console.WriteLine($"GuardInfo: {before} -> {guards.Count}");
        }

        if (sync && !dryRun)
            Sync(root);

        return 0;
    }

    private static Point? Nearest(MapCells cells, Point from, HashSet<Point> taken)
    {
        for (int radius = 1; radius <= 6; radius++)
        {
            Point? best = null;
            int bestDist = int.MaxValue;

            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;

                int x = from.X + dx, y = from.Y + dy;
                if (!cells.Valid(x, y)) continue;
                if (taken.Contains(new Point(x, y))) continue;

                int dist = Math.Abs(dx) + Math.Abs(dy);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = new Point(x, y);
                }
            }

            if (best != null) return best;
        }

        return null;
    }

    private static void Sync(string root)
    {
        string source = Path.Combine(root, "System.db");
        string expected = Md5(source);
        Console.WriteLine($"System.db 源文件 MD5: {expected}");

        foreach (string target in Mirrors)
        {
            if (Path.GetFullPath(source) != Path.GetFullPath(target))
            {
                File.Copy(source, target, true);
                Console.WriteLine($"  -> 已同步覆盖 {target}");
            }
        }

        foreach (string target in Mirrors)
        {
            string actual = Md5(target);
            Console.WriteLine($"  [{(actual == expected ? "OK" : "FAIL")}] {target} {actual}");
        }
    }

    private static string Md5(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
