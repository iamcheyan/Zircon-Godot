using ClassicMagicFixer;
using Library;
using Library.SystemModels;
using MirDB;

// Classic three-class skill purity tool (phase 1 of CLASSIC_CORE_TRIAD_PIPELINE_SPEC).
//
// Usage:
//   ClassicMagicFixer dump  <RootDir>              # full MagicInfo dump, no writes
//   ClassicMagicFixer apply <RootDir> [--no-sync]  # purge to the 54 classic skills
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

    case "drops":
        return DropAligner.Run(root, !args.Contains("--no-sync", StringComparer.OrdinalIgnoreCase));

    case "restore":
        if (args.Length < 3) { Console.Error.WriteLine("restore needs <SourceRoot> <TargetRoot>"); return ExitFail; }
        return RestoreClassicItems.Run(Path.GetFullPath(args[2]), root);

    case "grant":
        return GrantClassicSkills.Run(root);

    default:
        Console.Error.WriteLine($"unknown mode: {mode}");
        return ExitFail;
}

internal partial class Program { }
