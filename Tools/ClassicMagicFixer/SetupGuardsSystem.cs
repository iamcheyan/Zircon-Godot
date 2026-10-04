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

    public static int Run(string targetRoot)
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

        // 3. 在比奇城与盟重土城城墙部署 ArcherGuard
        var mapBichon = maps.FirstOrDefault(x => x.FileName == "0");
        var mapMongchon = maps.FirstOrDefault(x => x.FileName == "02");

        var archerSpawns = new List<(MapInfo Map, int X, int Y, MirDirection Dir)>();

        if (mapBichon != null)
        {
            // 比奇城四大城门两侧箭垛
            archerSpawns.Add((mapBichon, 141, 244, MirDirection.DownLeft));
            archerSpawns.Add((mapBichon, 150, 248, MirDirection.DownRight));
            archerSpawns.Add((mapBichon, 119, 228, MirDirection.Down));
            archerSpawns.Add((mapBichon, 125, 238, MirDirection.Down));
            archerSpawns.Add((mapBichon, 201, 179, MirDirection.UpRight));
            archerSpawns.Add((mapBichon, 209, 187, MirDirection.UpRight));
            archerSpawns.Add((mapBichon, 133, 213, MirDirection.UpLeft));
            archerSpawns.Add((mapBichon, 145, 206, MirDirection.UpRight));
        }

        if (mapMongchon != null)
        {
            // 盟重土城四大城门外侧箭楼
            archerSpawns.Add((mapMongchon, 272, 165, MirDirection.UpRight));
            archerSpawns.Add((mapMongchon, 279, 172, MirDirection.UpRight));
            archerSpawns.Add((mapMongchon, 242, 166, MirDirection.UpLeft));
            archerSpawns.Add((mapMongchon, 249, 163, MirDirection.UpLeft));
            archerSpawns.Add((mapMongchon, 287, 203, MirDirection.DownRight));
            archerSpawns.Add((mapMongchon, 291, 207, MirDirection.DownRight));
            archerSpawns.Add((mapMongchon, 225, 235, MirDirection.DownLeft));
            archerSpawns.Add((mapMongchon, 231, 241, MirDirection.DownLeft));
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

        session.Save(true);
        Console.WriteLine($"守卫体系更新完成: 新增 {addedMonsters} 个守卫怪物定义，部署 {addedArchers} 名弓箭手守卫，当前 GuardInfo 总数: {guardCollection.Count}");

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
