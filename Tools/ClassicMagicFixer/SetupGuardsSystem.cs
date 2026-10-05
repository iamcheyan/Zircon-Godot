using System.Reflection;
using System.Security.Cryptography;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 全套城防守卫与地域特色侍卫系统安装与配置工具
/// 1. 注册 TownGuard (2), ForestGuard (3), SandGuard (4), ArcherGuard (5)
/// 2. 映射道馆(Map 1)至 TownGuard, 白日门(Map 74)至 ForestGuard, 绿洲(Map 4)至 SandGuard
/// 3. 比奇城与盟重土城城墙箭楼部署 ArcherGuard
/// </summary>
internal static class SetupGuardsSystem
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private record GuardDef(int Index, string Name, MonsterImage Image, int AI, int ViewRange, int Delay);

    private static readonly GuardDef[] NewGuards =
    [
        new(2, "TownGuard", MonsterImage.TownGuard, -1, 7, 1000),      // 道馆锦衣卫带刀侍卫
        new(3, "ForestGuard", MonsterImage.ForestGuard, -1, 7, 1000),  // 白日门森林道家护卫
        new(4, "SandGuard", MonsterImage.SandGuard, -1, 7, 1000),      // 绿洲/沙漠斗篷护卫
        new(5, "ArcherGuard", MonsterImage.ArcherGuard, -3, 15, 1500),  // 城防弓箭手守卫
    ];

    public static int Run(string targetRoot, bool dryRun)
    {
        static string NormalizeRoot(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

        targetRoot = NormalizeRoot(targetRoot);

        var session = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        session.Initialize(typeof(MonsterInfo).Assembly, typeof(MapInfo).Assembly, typeof(GuardInfo).Assembly);

        var monsters = session.GetCollection<MonsterInfo>();
        var stats = session.GetCollection<MonsterInfoStat>();
        var maps = session.GetCollection<MapInfo>().Binding;
        var guardCollection = session.GetCollection<GuardInfo>();

        var indexProperty = typeof(DBObject).GetProperty("Index", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        int addedMonsters = 0;
        foreach (var def in NewGuards)
        {
            var existing = monsters.Binding.FirstOrDefault(x => x.Index == def.Index || x.MonsterName == def.Name);
            if (existing == null)
            {
                existing = monsters.CreateNewObject();
                indexProperty?.SetValue(existing, def.Index);
                addedMonsters++;
                Console.WriteLine($"[新建守卫] #{def.Index} {def.Name} (Image={def.Image}, AI={def.AI})");
            }

            existing.MonsterName = def.Name;
            existing.Image = def.Image;
            existing.AI = def.AI;
            existing.Level = 250;
            existing.ViewRange = (byte)def.ViewRange;
            existing.AttackDelay = def.Delay;
            existing.MoveDelay = def.Delay;
            existing.CanPush = false;
            existing.CanTame = false;

            // 补充基础属性 (Health 50000, DC 5000, AC 500, MR 500, Accuracy 500)
            SetStat(stats, existing, Stat.Health, 50000);
            SetStat(stats, existing, Stat.MaxDC, 5000);
            SetStat(stats, existing, Stat.MinDC, 3000);
            SetStat(stats, existing, Stat.MaxAC, 500);
            SetStat(stats, existing, Stat.MaxMR, 500);
            SetStat(stats, existing, Stat.Accuracy, 500);
        }

        // 保持 monsters.Binding 升序
        var itemsField = typeof(System.Collections.ObjectModel.Collection<MonsterInfo>).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
        var innerList = itemsField?.GetValue(monsters.Binding) as List<MonsterInfo>;
        innerList?.Sort((a, b) => a.Index.CompareTo(b.Index));

        var townGuard = monsters.Binding.First(x => x.MonsterName == "TownGuard");
        var forestGuard = monsters.Binding.First(x => x.MonsterName == "ForestGuard");
        var sandGuard = monsters.Binding.First(x => x.MonsterName == "SandGuard");
        var archerGuard = monsters.Binding.First(x => x.MonsterName == "ArcherGuard");

        // 2. 切换现有地域守卫模型
        int updatedGuards = 0;
        foreach (var g in guardCollection.Binding)
        {
            if (g.Map == null) continue;

            if (g.Map.FileName == "1") // 道馆 -> TownGuard
            {
                if (g.Monster != townGuard)
                {
                    g.Monster = townGuard;
                    updatedGuards++;
                }
            }
            else if (g.Map.FileName == "74") // 白日门 -> ForestGuard
            {
                if (g.Monster != forestGuard)
                {
                    g.Monster = forestGuard;
                    updatedGuards++;
                }
            }
            else if (g.Map.FileName == "4") // 绿洲 -> SandGuard
            {
                if (g.Monster != sandGuard)
                {
                    g.Monster = sandGuard;
                    updatedGuards++;
                }
            }
        }

        Console.WriteLine($"[更新守卫] 已将 {updatedGuards} 处守卫切换为对应地域专属侍卫模型（道馆锦衣卫/白日门护卫/绿洲沙漠卫士）");

        // 3. 地图文件名来自 MUD3 Envir/MapInfo.txt：0=比奇城、5=沙漠土城。
        // 02 是银杏山谷，不能按旧地图编号误当盟重土城。
        var mapBichon = maps.FirstOrDefault(x => x.FileName == "0");
        var mapMongchon = maps.FirstOrDefault(x => x.FileName == "5");

        // 上次部署把比奇弓箭手放到了旧地图坐标区 (119..209,179..248)，
        // 把盟重弓箭手放进了银杏山谷。把这 16 条已有记录按当前 EI 地图落点迁移，
        // 让重新运行 setupguards 也能修复已经写入的错误记录。
        var oldBichonArchers = new Dictionary<(int X, int Y), (int X, int Y)>
        {
            [(141, 244)] = (447, 355), [(150, 248)] = (449, 353),
            [(119, 228)] = (451, 355), [(125, 238)] = (452, 357),
            [(201, 179)] = (458, 358), [(209, 187)] = (460, 359),
            [(133, 213)] = (459, 363), [(145, 206)] = (457, 367),
        };
        var oldMongchonArchers = new Dictionary<(int X, int Y), (int X, int Y)>
        {
            [(272, 165)] = (220, 156), [(279, 172)] = (224, 156),
            [(242, 166)] = (226, 160), [(249, 163)] = (220, 164),
            [(286, 203)] = (222, 124), [(291, 207)] = (225, 131),
            [(225, 235)] = (230, 128), [(231, 241)] = (234, 128),
        };
        int movedArchers = 0;
        foreach (var g in guardCollection.Binding.Where(g => g.Monster == archerGuard).ToList())
        {
            Dictionary<(int X, int Y), (int X, int Y)> source = g.Map?.FileName switch
            {
                "0" => oldBichonArchers,
                "02" => oldMongchonArchers,
                _ => null,
            };
            if (source == null || !source.TryGetValue((g.X, g.Y), out var destination)) continue;

            string oldMap = g.Map.FileName;
            int oldX = g.X, oldY = g.Y;
            if (oldMap == "02")
            {
                if (mapMongchon == null)
                {
                    Console.Error.WriteLine("[FATAL] MapInfo FileName=5 (沙漠土城) not found; refusing to move Mongchon archers");
                    return 1;
                }
                g.Map = mapMongchon;
            }
            g.X = destination.X;
            g.Y = destination.Y;
            movedArchers++;
            Console.WriteLine($"[迁移弓箭手] {oldMap} ({oldX},{oldY}) -> {g.Map?.FileName} ({g.X},{g.Y})");
        }

        // 这些 GuardInfo 是旧 350x350 地图中的比奇/道馆点位。
        // EI 替换地图后 NPC 已按新图校准；只保留 MUD3 GuardList.txt 能逐条
        // 对上的点位，避免旧点继续在新版地图的树林/城外刷出。
        var staleBichonGuardPoints = new HashSet<(int X, int Y)>
        {
            (122,230),(128,241),(135,215),(143,204),(144,246),(147,259),(153,206),
            (157,270),(164,223),(167,226),(168,259),(177,206),(178,235),(180,246),
            (182,211),(183,258),(203,181),(207,185),(195,245),
        };
        var staleLostParadiseGuardPoints = new HashSet<(int X, int Y)>
        {
            (184,201),(190,220),(194,208),(203,191),
        };
        int removedLegacyGuards = 0;
        foreach (var g in guardCollection.Binding.ToList())
        {
            bool stale = g.Map?.FileName switch
            {
                "0" => g.Monster?.MonsterName == "Guard" && staleBichonGuardPoints.Contains((g.X, g.Y)),
                "1" => g.Monster?.MonsterName == "TownGuard" && staleLostParadiseGuardPoints.Contains((g.X, g.Y)),
                _ => false,
            };
            if (!stale) continue;
            Console.WriteLine($"[清理旧地图守卫] {g.Map?.FileName} ({g.X},{g.Y}) {g.Monster?.MonsterName}，无 MUD3 GuardList 对应点位");
            g.Delete();
            removedLegacyGuards++;
        }

        var archerSpawns = new List<(MapInfo Map, int X, int Y, MirDirection Dir)>();

        if (mapBichon != null)
        {
            // 比奇城墙的 MUD3 原版卫士点位在 (451..457, 359..365)。
            // 弓箭手沿当前 EI 比奇城门/城墙一带部署，不再使用旧图的 100~200 区域。
            archerSpawns.Add((mapBichon, 447, 355, MirDirection.DownLeft));
            archerSpawns.Add((mapBichon, 449, 353, MirDirection.DownRight));
            archerSpawns.Add((mapBichon, 451, 355, MirDirection.Down));
            archerSpawns.Add((mapBichon, 452, 357, MirDirection.Down));
            archerSpawns.Add((mapBichon, 458, 358, MirDirection.UpRight));
            archerSpawns.Add((mapBichon, 460, 359, MirDirection.UpRight));
            archerSpawns.Add((mapBichon, 459, 363, MirDirection.UpLeft));
            archerSpawns.Add((mapBichon, 457, 367, MirDirection.UpRight));
        }

        if (mapMongchon != null)
        {
            // 当前资源中沙漠土城的城墙/塔楼与传送广场落在这些可站立格；
            // 它们已在 XFCE 实机视口核对，避开 02.map 的银杏村坐标。
            archerSpawns.Add((mapMongchon, 220, 156, MirDirection.UpRight));
            archerSpawns.Add((mapMongchon, 224, 156, MirDirection.UpRight));
            archerSpawns.Add((mapMongchon, 226, 160, MirDirection.UpLeft));
            archerSpawns.Add((mapMongchon, 220, 164, MirDirection.UpLeft));
            archerSpawns.Add((mapMongchon, 222, 124, MirDirection.DownRight));
            archerSpawns.Add((mapMongchon, 225, 131, MirDirection.DownRight));
            archerSpawns.Add((mapMongchon, 230, 128, MirDirection.DownLeft));
            archerSpawns.Add((mapMongchon, 234, 128, MirDirection.DownLeft));
        }

        int addedArchers = 0;
        foreach (var spawn in archerSpawns)
        {
            bool exists = guardCollection.Binding.Any(g => g.Map == spawn.Map && g.X == spawn.X && g.Y == spawn.Y);
            if (!exists)
            {
                var newGuard = guardCollection.CreateNewObject();
                newGuard.Map = spawn.Map;
                newGuard.Monster = archerGuard;
                newGuard.X = spawn.X;
                newGuard.Y = spawn.Y;
                newGuard.Direction = spawn.Dir;
                addedArchers++;
                Console.WriteLine($"[部署弓箭手] {spawn.Map.Description} ({spawn.X}, {spawn.Y}) Dir={spawn.Dir}");
            }
        }

        Console.WriteLine($"守卫体系更新完成: 新增 {addedMonsters} 个守卫怪物定义，部署 {addedArchers} 名弓箭手守卫，迁移 {movedArchers} 名旧弓箭手，清理 {removedLegacyGuards} 条旧图守卫，当前 GuardInfo 总数: {guardCollection.Count}");

        if (dryRun)
        {
            Console.WriteLine("[DRY-RUN] 未写入 System.db，也未同步镜像");
            return 0;
        }

        session.Save(true);
        Sync(targetRoot);
        return 0;
    }

    private static void SetStat(DBCollection<MonsterInfoStat> stats, MonsterInfo monster, Stat stat, int amount)
    {
        var existing = stats.Binding.FirstOrDefault(s => s.Monster == monster && s.Stat == stat);
        if (existing == null)
        {
            existing = stats.CreateNewObject();
            existing.Monster = monster;
            existing.Stat = stat;
        }
        existing.Amount = amount;
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
