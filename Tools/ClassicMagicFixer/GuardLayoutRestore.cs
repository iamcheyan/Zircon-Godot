using Library;
using Library.SystemModels;
using MirDB;
using System.Security.Cryptography;

namespace ClassicMagicFixer;

/// <summary>
/// 按原版 Mir3 1.45 的 GuardList 还原全地图守卫摆放。
///
/// 位置真值来源：<c>/data/NAS/TMP/Mud3/Envir/GuardList.txt</c>（GB18030），
/// sha256 <c>cef315090639747b91b35943a5936e9038c7ec6f19e7e9db5015c66169149b68</c>，
/// 与坚果云「传奇3 1.45 数据 / 传奇 3 145 1.45原版资_Envir/GuardList.txt」逐字节相同。
/// 共 117 条，覆盖 0/01/02/1/2/4/5/74/8/9/D71601 十一张图。
///
/// 为什么不用英雄杀私服（EI3.0英雄杀服务端）的 32 条：那是私服裁剪版，
/// 比奇城只留 4 个守卫且不在城门大道上，与当前地图、NPC 布局不符。
///
/// 怪物种类遵循产品决策（2026-10-05）：位置一律取原版，但保留 Zircon 特色模型
/// ——道馆(map 1) 用 TownGuard 锦衣卫、盟重县(map 74) 用 ForestGuard、
/// 沙漠系(map 4/5) 用 SandGuard，其余原版「卫士/卫士1」统一用大刀守卫 Guard。
/// 弓箭守卫(ArcherGuard) 是 Zircon 自加项，本工具一律保留，只重建其它守卫。
/// </summary>
internal static class GuardLayoutRestore
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    /// <summary>原版 GuardList.txt 正文（GB18030 已解码为 UTF-8，117 条）。</summary>
    private const string OriginalGuardList = """
卫士    2        311,216     : 5
卫士    2        310,228     : 5
卫士    2        364,202     : 5
卫士    2        366,204     : 5
卫士1   01       426,283     : 5
卫士1   01       413,272     : 3
卫士1   01       431,240     : 7
卫士1   01       427,244     : 7
卫士1   01       462,241     : 1
卫士1   01       455,232     : 1
卫士1   01       441,325     : 4
卫士1   01       451,316     : 5
卫士1   01       427,320     : 3
卫士1   02       232,180     : 7
卫士1   02       228,184     : 7
卫士1   02       244,168     : 7
卫士1   02       247,165     : 7
卫士1   02       274,167     : 1
卫士1   02       277,170     : 1
卫士1   02       283,176     : 1
卫士1   02       287,180     : 1
卫士1   02       289,205     : 3
卫士1   02       286,208     : 3
卫士1   02       277,233     : 3
卫士1   02       274,236     : 3
卫士1   02       229,239     : 5
卫士1   02       227,237     : 5
卫士1   1        372,162     : 3
卫士1   1        375,164     : 5
卫士1   1        368,112     : 7
卫士1   1        372,108     : 7
卫士1   1        414,166     : 3
卫士1   1        417,163     : 3
卫士1   1        411,115     : 5
卫士1   1        414,118     : 5
卫士    0        457,386     : 5
卫士    0        454,383     : 5
卫士    0        471,371     : 5
卫士    0        467,367     : 5
卫士    0        495,387     : 5
卫士    0        499,391     : 5
卫士    0        511,369     : 1
卫士    0        515,373     : 1
卫士    0        497,344     : 1
卫士    0        492,339     : 1
卫士    0        511,325     : 1
卫士    0        515,329     : 1
卫士    0        466,325     : 1
卫士    0        461,320     : 1
卫士    0        451,342     : 5
卫士    0        448,339     : 5
卫士    0        406,343     : 5
卫士    0        389,328     : 7
卫士    0        387,330     : 7
卫士    0        412,426     : 7
卫士    0        403,443     : 5
卫士    0        396,436     : 5
卫士    0        393,449     : 5
卫士    0        391,447     : 5
卫士    0        507,448     : 3
卫士    0        503,452     : 3
卫士    0        483,410     : 5
卫士    D71601     24,53     : 9
卫士    74       285,268     : 7
卫士    74       275,278     : 7
卫士    74       353,309     : 3
卫士    74       346,316     : 3
沙漠战士  4        451,54      : 5
沙漠战士  4        457,60      : 5
沙漠战士  4        462,48      : 1
沙漠战士  4        456,46      : 1
沙漠战士  5        240,166     : 5
沙漠战士  5        237,163     : 5
沙漠战士  5        208,167     : 3
沙漠战士  5        204,171     : 3
沙漠战士  5        238,192     : 5
沙漠战士  5        242,196     : 5
沙漠战士  5        219,206     : 1
沙漠战士  5        197,184     : 1
沙漠战士  5        182,202     : 5
沙漠战士  5        205,224     : 5
沙漠战士  5        143,269     : 5
沙漠战士  5        138,265     : 5
沙漠战士  5        122,273     : 5
沙漠战士  5        129,280     : 5
沙漠战士  5        132,198     : 7
沙漠战士  5        138,192     : 7
沙漠战士  5        199,279     : 3
沙漠战士  5        207,271     : 3
卫士1   8        269,292     : 3
卫士1   8        260,302     : 3
卫士1   8        234,293     : 1
卫士1   8        230,288     : 1
卫士1   8        212,254     : 7
卫士1   8        232,231     : 5
卫士1   8        218,206     : 5
卫士1   8        223,200     : 7
卫士1   8        244,196     : 1
卫士1   8        262,204     : 3
卫士1   8        279,206     : 1
卫士1   8        281,225     : 3
卫士1   8        271,263     : 3
卫士1   8        263,269     : 1
昂克战士2  9        177,519     : 7
昂克战士2  9        174,522     : 7
昂克战士2  9        238,524     : 1
昂克战士2  9        242,527     : 1
昂克战士2  9        246,606     : 3
昂克战士2  9        249,602     : 3
昂克战士2  9        254,553     : 5
昂克战士2  9        258,556     : 5
昂克战士2  9        266,562     : 5
昂克战士2  9        250,548     : 5
昂克战士2  9        168,589     : 7
昂克战士2  9        163,539     : 0
昂克战士2  9        213,513     : 1
昂克战士2  9        209,643     : 4
""";

    private sealed record Entry(string Monster, string MapFile, int X, int Y, int Dir);

    public static int Run(string targetRoot, bool dryRun, string mapPathOverride)
    {
        targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot)) + Path.DirectorySeparatorChar;

        string mapPath = mapPathOverride ?? Path.GetFullPath(Path.Combine(targetRoot, "..", "Map"));
        if (!Directory.Exists(mapPath))
        {
            Console.Error.WriteLine($"[FATAL] MapPath not found: {mapPath} (use --map-path)");
            return 1;
        }

        var session = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        session.Initialize(typeof(GuardInfo).Assembly, typeof(MapInfo).Assembly, typeof(MonsterInfo).Assembly);

        var maps = session.GetCollection<MapInfo>().Binding;
        var monsters = session.GetCollection<MonsterInfo>().Binding;
        var guards = session.GetCollection<GuardInfo>();

        // 必需怪物定义
        var byName = new Dictionary<string, MonsterInfo>(StringComparer.Ordinal);
        foreach (string name in new[] { "Guard", "TownGuard", "ForestGuard", "SandGuard" })
        {
            var m = monsters.FirstOrDefault(x => x.MonsterName == name);
            if (m == null)
            {
                Console.Error.WriteLine($"[FATAL] MonsterInfo '{name}' missing; run setupguards first");
                return 1;
            }
            byName[name] = m;
        }

        var mapCache = new Dictionary<string, (int W, int H, byte[] Flags)>(StringComparer.Ordinal);

        // ---- 1. 解析原版清单 ----
        var wanted = new List<(MapInfo Map, MonsterInfo Monster, int X, int Y, MirDirection Dir, string Note)>();
        int skippedMap = 0, nudged = 0, skippedNoCell = 0;

        foreach (Entry e in Parse(OriginalGuardList))
        {
            var map = maps.FirstOrDefault(x => x.FileName == e.MapFile);
            if (map == null)
            {
                Console.WriteLine($"[跳过] 图 {e.MapFile} 未在 Zircon MapInfo 注册: {e.Monster} ({e.X},{e.Y})");
                skippedMap++;
                continue;
            }

            if (!mapCache.TryGetValue(map.FileName, out var grid))
            {
                string file = Path.Combine(mapPath, map.FileName + ".map");
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"[FATAL] map file missing: {file}");
                    return 1;
                }
                grid = LoadGrid(file);
                mapCache[map.FileName] = grid;
            }

            int x = e.X, y = e.Y;
            string note = "";
            if (!Walkable(grid, x, y))
            {
                var fix = NearestWalkable(grid, x, y, 4);
                if (fix == null)
                {
                    Console.WriteLine($"[跳过] {map.FileName} ({x},{y}) {e.Monster} 及附近无可行走格");
                    skippedNoCell++;
                    continue;
                }
                note = $"原版 ({x},{y}) 不可行走 → 就近移至 ({fix.Value.X},{fix.Value.Y})";
                Console.WriteLine($"[挪格] {map.FileName} ({x},{y}) -> ({fix.Value.X},{fix.Value.Y}) {e.Monster}");
                x = fix.Value.X; y = fix.Value.Y; nudged++;
            }

            if (e.Dir is < 0 or > 7)
            {
                Console.WriteLine($"[朝向] {map.FileName} ({x},{y}) 原版朝向 {e.Dir} 非法，改用 Down");
                note = (note + "; ").TrimStart(';', ' ') + $"原版朝向 {e.Dir} 非法，改用 Down";
            }

            var dir = e.Dir is >= 0 and <= 7 ? (MirDirection)e.Dir : MirDirection.Down;
            wanted.Add((map, byName[ResolveMonster(map.FileName)], x, y, dir, note));
        }

        // ---- 2. 删除受管地图上的既有非弓箭守卫 ----
        var covered = wanted.Select(w => w.Map).Distinct().ToHashSet();
        var toDelete = guards.Binding
            .Where(g => g.Map != null && covered.Contains(g.Map) && g.Monster?.MonsterName != "ArcherGuard")
            .ToList();
        foreach (var g in toDelete)
        {
            Console.WriteLine($"[删除] {g.Map.FileName} ({g.X},{g.Y}) {g.Monster?.MonsterName} #{g.Index}");
            if (!dryRun) g.Delete();
        }

        // ---- 3. 插入原版点位 ----
        int added = 0;
        foreach (var w in wanted)
        {
            Console.WriteLine($"[写入] {w.Map.FileName,-7} ({w.X,3},{w.Y,3}) {w.Monster.MonsterName,-11} {w.Dir}{(w.Note.Length > 0 ? "  // " + w.Note : "")}");
            if (dryRun) continue;
            var g = guards.CreateNewObject();
            g.Map = w.Map;
            g.Monster = w.Monster;
            g.X = w.X; g.Y = w.Y;
            g.Direction = w.Dir;
            added++;
        }

        Console.WriteLine();
        Console.WriteLine($"原版清单 {Parse(OriginalGuardList).Count()} 条: 目标 {wanted.Count}，未注册图跳过 {skippedMap}，无可行走格跳过 {skippedNoCell}，就近挪格 {nudged}");
        Console.WriteLine($"删除旧记录 {toDelete.Count}，写入 {wanted.Count}；收尾 GuardInfo 总数 {guards.Count}"
            + (dryRun ? "（dry-run，未写库）" : ""));

        if (dryRun) return 0;

        session.Save(true);
        return Sync(targetRoot) ? 0 : 1;
    }

    private static string ResolveMonster(string mapFile) => mapFile switch
    {
        "1" => "ForestGuard",   // 道馆：白日门带刀侍卫（原版点位 卫士1）
        "74" => "ForestGuard",  // 盟重县：保留 Zircon 森林护卫模型
        "4" or "5" => "SandGuard", // 绿洲 / 沙漠土城：原版即沙漠战士
        _ => "Guard"            // 比奇 / 毒蛇山谷 / 边境城市 / 银杏山谷 / 潘夜岛 / D71601
    };

    private static IEnumerable<Entry> Parse(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // 名字 图 x,y : 朝向
            string[] xy = parts[2].Trim(',').Split(',');
            int dir = int.Parse(line[(line.LastIndexOf(':') + 1)..].Trim());
            yield return new Entry(parts[0], parts[1], int.Parse(xy[0]), int.Parse(xy[1]), dir);
        }
    }

    private static (int W, int H, byte[] Flags) LoadGrid(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int w = b[23] << 8 | b[22];
        int h = b[25] << 8 | b[24];
        int offset = 28 + w * h / 4 * 3;
        var flags = new byte[w * h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                flags[x * h + y] = b[offset + (x * h + y) * 14];
        return (w, h, flags);
    }

    /// <summary>与 ServerLibrary/Models/Map.cs:82 同一判定：flag 同时含 0x01|0x02 才可站立。</summary>
    private static bool Walkable((int W, int H, byte[] Flags) g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.W || y >= g.H) return false;
        byte f = g.Flags[x * g.H + y];
        return (f & 0x02) == 2 && (f & 0x01) == 1;
    }

    private static (int X, int Y)? NearestWalkable((int W, int H, byte[] Flags) g, int x, int y, int radius)
    {
        (int X, int Y)? best = null;
        int bestD = int.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!Walkable(g, nx, ny)) continue;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = (nx, ny); }
            }
        return best;
    }

    private static bool Sync(string root)
    {
        string source = Path.Combine(root, "System.db");
        string expected = Md5(source);
        Console.WriteLine($"System.db 源文件 MD5: {expected}");

        foreach (string target in Mirrors)
            if (Path.GetFullPath(source) != Path.GetFullPath(target))
                File.Copy(source, target, true);

        bool ok = true;
        foreach (string target in Mirrors)
        {
            string actual = Md5(target);
            bool same = actual == expected;
            ok &= same;
            Console.WriteLine($"  [{(same ? "OK" : "FAIL")}] {target} {actual}");
        }
        return ok;
    }

    private static string Md5(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
