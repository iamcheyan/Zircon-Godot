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

    case "grant":
        return GrantClassicSkills.Run(root);

    default:
        Console.Error.WriteLine($"unknown mode: {mode}");
        return ExitFail;
}
