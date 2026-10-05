using ClassicMagicFixer;
using Library;
using Server.DBModels;
using Library.SystemModels;
using MirDB;

// Classic three-class skill purity tool (phase 1 of CLASSIC_CORE_TRIAD_PIPELINE_SPEC).
//
// Usage:
//   ClassicMagicFixer dump  <RootDir>              # full MagicInfo dump, no writes
//   ClassicMagicFixer apply <RootDir> [--no-sync]  # purge to the 54 classic skills
//   ClassicMagicFixer auditdeps <RootDir>          # validate runtime monster/skill data dependencies
//   ClassicMagicFixer restoredeps <TargetRoot> <SourceRoot> # restore retained runtime monster dependencies
//
// <RootDir> is the directory holding System.db. It is resolved to an absolute path
// and must already contain a non-empty System.db: a typo must fail loudly instead of
// silently creating a fresh empty database next to the build output.
const int ExitOk = 0;
const int ExitFail = 1;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ClassicMagicFixer <dump|apply> <RootDir> [--no-sync]");
    return ExitFail;
}

string mode = args[0];
string root = Path.GetFullPath(args[1]);
if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

string systemDb = Path.Combine(root, "System.db");
if (!File.Exists(systemDb))
{
    Console.Error.WriteLine($"[FATAL] System.db not found: {systemDb}");
    return ExitFail;
}

if (new FileInfo(systemDb).Length == 0)
{
    Console.Error.WriteLine($"[FATAL] System.db is empty: {systemDb}");
    return ExitFail;
}

bool sync = !args.Contains("--no-sync", StringComparer.OrdinalIgnoreCase);

switch (mode)
{
    case "dump":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly);

        var magics = session.GetCollection<MagicInfo>().Binding.OrderBy(x => x.Index).ToList();
        Console.WriteLine($"TOTAL {magics.Count}");
        Console.WriteLine(string.Join('\t',
            "Index", "Magic", "Name", "RequiredClass", "School", "Property", "Icon",
            "L1", "L2", "L3", "BaseCost", "LevelCost", "MinBase", "MaxBase", "MinLvl", "MaxLvl",
            "X1", "X2", "X3", "Delay"));
        foreach (var m in magics)
            Console.WriteLine(string.Join('\t',
                m.Index, (int)m.Magic, m.Name, (int)m.RequiredClass, (int)m.School, (int)m.Property, m.Icon,
                m.NeedLevel1, m.NeedLevel2, m.NeedLevel3, m.BaseCost, m.LevelCost,
                m.MinBasePower, m.MaxBasePower, m.MinLevelPower, m.MaxLevelPower,
                m.Experience1, m.Experience2, m.Experience3, m.Delay));
        return ExitOk;
    }

    case "apply":
        return ClassicMagicPurity.Run(root, sync);

    case "items":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly);
        Console.WriteLine("Index\tName\tClass\tType\tPrice\tShape\tEffect");
        foreach (var it in session.GetCollection<ItemInfo>().Binding.OrderBy(x => x.Index))
            Console.WriteLine($"{it.Index}\t{it.ItemName}\t{(int)it.RequiredClass}\t{(int)it.ItemType}\t{it.Price}\t{it.Shape}\t{(int)it.ItemEffect}");
        return ExitOk;
    }

    case "mons":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MonsterInfo).Assembly);
        foreach (var m in session.GetCollection<MonsterInfo>().Binding.OrderBy(x => x.Index))
            Console.WriteLine($"{m.Index}\t{m.MonsterName}");
        return ExitOk;
    }

    case "dropinspect":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(ItemInfo).Assembly, typeof(MonsterInfo).Assembly);
        var idx2name = session.GetCollection<MonsterInfo>().Binding.ToDictionary(m => m.Index, m => m.MonsterName);
        var iname = session.GetCollection<ItemInfo>().Binding.ToDictionary(i => i.Index, i => i.ItemName);
        foreach (var n in new[] { "Black Boar", "Ghoul Champion", "Uma King", "Zuma King" })
        {
            var m = idx2name.Values.FirstOrDefault(x => x == n);
            var row = session.GetCollection<MonsterInfo>().Binding.FirstOrDefault(x => x.MonsterName == n);
            if (row == null) { Console.WriteLine($"missing monster {n}"); continue; }
            Console.WriteLine($"=== {n} (idx {row.Index}) ===");
            foreach (var d in session.GetCollection<DropInfo>().Binding.Where(d => d.Monster?.Index == row.Index).OrderBy(d => d.Chance))
                Console.WriteLine($"  1/{d.Chance,4} qty {d.Amount,3} {iname.GetValueOrDefault(d.Item?.Index ?? 0, "???")}");
        }
        return ExitOk;
    }

    case "shop":
        return ShopAligner.Run(root, !args.Contains("--no-sync", StringComparer.OrdinalIgnoreCase));

    case "maplist":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(NPCInfo).Assembly);
        foreach (var m in session.GetCollection<MapInfo>().Binding.OrderBy(x => x.Index).Take(12))
            Console.WriteLine($"idx={m.Index,-4} file='{m.FileName}' desc='{m.Description}'");
        return ExitOk;
    }

    case "npcpos":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(NPCInfo).Assembly);
        foreach (var n in session.GetCollection<NPCInfo>().Binding
                 .Where(x => x.EntryPage != null && x.Region?.Map != null)
                 .OrderBy(x => x.Region.Map.Description).ThenBy(x => x.NPCName))
        {
            var pts = n.Region.PointRegion;
            Console.WriteLine($"{n.Region.Map.Description,-18} {n.NPCName,-22} region='{n.Region.ServerDescription}' points={pts.Length} first={(pts.Length>0? $"{pts[0].X},{pts[0].Y}":"-")}");
        }
        return ExitOk;
    }

    case "npcs":
        return ShopAuditor.Run(root);

    case "drops":
        return DropAligner.Run(root, !args.Contains("--no-sync", StringComparer.OrdinalIgnoreCase));

    case "restore":
        if (args.Length < 3) { Console.Error.WriteLine("restore needs <SourceRoot> <TargetRoot>"); return ExitFail; }
        return RestoreClassicItems.Run(Path.GetFullPath(args[2]), root);

    case "auditdeps":
        return RuntimeDataAudit.Run(root);

    case "restorenative":
    {
        string defaultBackup = "/home/tetsuya/development/zircon/Debug/ServerCore/Database/Backup/classic-purity-20261003-snapshot/ServerCore_Database_System.db";
        string backupPath = args.Length >= 3 ? Path.GetFullPath(args[2]) : defaultBackup;
        return RestoreNativeEntities.Run(root, backupPath);
    }

    case "setupguards":
        return SetupGuardsSystem.Run(root, args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase));

    case "guardlayout":
    {
        string? mapPath = null;
        int mp = Array.FindIndex(args, a => a.Equals("--map-path", StringComparison.OrdinalIgnoreCase));
        if (mp >= 0 && mp + 1 < args.Length) mapPath = args[mp + 1];
        return GuardLayoutRestore.Run(root, args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase), mapPath);
    }

    case "guardfix":
        return GuardAligner.Run(root, args.Contains("--dry-run"), !args.Contains("--no-sync"));

    case "guardaudit":
        return GuardAligner.Run(root, dryRun: true, sync: false);

    case "guarddump":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(GuardInfo).Assembly, typeof(MapInfo).Assembly, typeof(MonsterInfo).Assembly);
        var rows = session.GetCollection<GuardInfo>().Binding;
        Console.WriteLine($"TOTAL\t{rows.Count}");
        Console.WriteLine("Index\tMapFile\tMapDesc\tMonster\tX\tY\tDirection");
        foreach (var g in rows.OrderBy(g => g.Map?.FileName).ThenBy(g => g.Index))
            Console.WriteLine($"{g.Index}\t{g.Map?.FileName}\t{g.Map?.Description}\t{g.Monster?.MonsterName}\t{g.X}\t{g.Y}\t{g.Direction}");
        return ExitOk;
    }

    case "charpos":
    {
        var users = new Session(SessionMode.Users, root, root + "Backup/");
        users.Initialize(typeof(AccountInfo).Assembly, typeof(MagicInfo).Assembly, typeof(MapInfo).Assembly, typeof(ItemInfo).Assembly);
        foreach (var c in users.GetCollection<CharacterInfo>().Binding.Where(c => c.CharacterName == "TestHero"))
            Console.WriteLine($"TestHero map={c.CurrentMap?.FileName} loc=({c.CurrentLocation.X},{c.CurrentLocation.Y})");
        return ExitOk;
    }

    case "tp":
        if (args.Length < 6) { Console.Error.WriteLine("tp needs <RootDir> <char> <mapFile> <x> <y>"); return ExitFail; }
        return TeleportTool.Run(root, args[2], args[3], int.Parse(args[4]), int.Parse(args[5]));

    case "gm":
        if (args.Length < 3) { Console.Error.WriteLine("gm needs <RootDir> <account>"); return ExitFail; }
        return AccountGrantor.Run(root, args[2]);

    case "grant":
        return GrantClassicSkills.Run(root);

    case "syncbooks":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly);
        var magics = session.GetCollection<MagicInfo>().Binding;
        var items = session.GetCollection<ItemInfo>();
        var bookList = items.Binding.Where(i => i.ItemType == ItemType.Book).ToList();

        int created = 0;
        foreach (var m in magics)
        {
            var book = bookList.FirstOrDefault(b => b.Shape == m.Index || b.ItemName.Replace(" ", "").Equals(m.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            if (book == null)
            {
                book = items.CreateNewObject();
                book.ItemName = m.Name;
                book.ItemType = ItemType.Book;
                book.RequiredClass = m.RequiredClass;
                book.Price = 1000;
                book.Shape = m.Index;
                book.Image = 70;
                book.Weight = 1;
                book.CanDrop = true;
                book.CanSell = true;
                book.CanTrade = true;
                book.CanStore = true;
                created++;
                Console.WriteLine($"  [新建技能书] #{book.Index} {book.ItemName} for {m.Name} (Shape={m.Index})");
            }
            else
            {
                book.Shape = m.Index;
                book.RequiredClass = m.RequiredClass;
            }
        }
        session.Save(true);
        Console.WriteLine($"技能书补齐完成: 新建 {created} 本，当前总技能书 {items.Binding.Count(i => i.ItemType == ItemType.Book)}");

        return ShopAligner.Run(root, sync);
    }

    case "monsdetail":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MonsterInfo).Assembly);
        Console.WriteLine($"Total monsters in {root}: {session.GetCollection<MonsterInfo>().Binding.Count}");
        foreach (var m in session.GetCollection<MonsterInfo>().Binding.OrderBy(x => x.Index))
        {
            if (m.Flag != MonsterFlag.None || m.MonsterName.Contains("Skeleton", StringComparison.OrdinalIgnoreCase)
                || m.MonsterName.Contains("Shinsu", StringComparison.OrdinalIgnoreCase)
                || m.MonsterName.Contains("Larva", StringComparison.OrdinalIgnoreCase)
                || m.MonsterName.Contains("Bone", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Idx={m.Index,-4} Name='{m.MonsterName,-20}' Flag={m.Flag,-18} AI={m.AI,-3} Img={m.Image,-4} Lv={m.Level}");
            }
        }
        return ExitOk;
    }

    case "guards":
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(GuardInfo).Assembly, typeof(MapInfo).Assembly, typeof(MonsterInfo).Assembly);
        var guards = session.GetCollection<GuardInfo>().Binding;
        Console.WriteLine($"Total GuardInfo in {root}: {guards.Count}");
        foreach (var g in guards.Take(30))
        {
            Console.WriteLine($"Guard #{g.Index} Map='{g.Map?.Description}' ({g.Map?.FileName}) Monster='{g.Monster?.MonsterName}' ({g.X},{g.Y}) Dir={g.Direction}");
        }
        return ExitOk;
    }

    default:
        Console.Error.WriteLine($"unknown mode: {mode}");
        return ExitFail;
}

internal partial class Program { }
