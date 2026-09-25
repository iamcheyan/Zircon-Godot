using Library;
using Library.SystemModels;
using MirDB;

// Read-only dump of MagicInfo Power fields for warrior passive skills.
// Usage: MagicInfoDump <RootDir> [--icons] (RootDir must contain System.db)
string root = args.Length > 0 ? args[0] : "/tmp/magicdb/";
bool dumpIcons = args.Contains("--icons", StringComparer.OrdinalIgnoreCase);

var session = new Session(SessionMode.System, root: root, backup: root + "Backup/");
session.Initialize(typeof(MagicInfo).Assembly);

var magics = session.GetCollection<MagicInfo>().Binding;

if (dumpIcons)
{
    Console.WriteLine($"{"Magic",-32}{"Name",-28}{"Class",-9}{"School",-12}{"Icon",6}{"NeedL1",7}");
    Console.WriteLine(new string('-', 96));
    foreach (var magic in magics.OrderBy(x => (int)x.Magic))
        Console.WriteLine($"{magic.Magic,-32}{magic.Name ?? "",-28}{magic.Class,-9}{magic.School,-12}{magic.Icon,6}{magic.NeedLevel1,7}");
    return;
}

var targets = new HashSet<MagicType>
{
    MagicType.Swordsmanship, MagicType.PotionMastery, MagicType.Slaying,
    MagicType.AugmentDefiance, MagicType.Defiance,
    MagicType.DefensiveMastery, MagicType.PhysicalImmunity, MagicType.MagicImmunity,
    MagicType.AdvancedPotionMastery,
};

Console.WriteLine($"{"MagicType",-22}{"Name",-26}{"Class",-8}{"School",-12}{"NeedL1",7}{"MinBase",8}{"MaxBase",8}{"MinLvl",7}{"MaxLvl",7}  Property");
Console.WriteLine(new string('-', 110));

foreach (var m in magics.OrderBy(x => (int)x.Magic))
{
    if (!targets.Contains(m.Magic)) continue;
    Console.WriteLine($"{m.Magic,-22}{(m.Name ?? ""),-26}{m.Class,-8}{m.School,-12}{m.NeedLevel1,7}{m.MinBasePower,8}{m.MaxBasePower,8}{m.MinLevelPower,7}{m.MaxLevelPower,7}  {m.Property}");
}

Console.WriteLine();
Console.WriteLine("=== All warrior-class magics with Power (for context) ===");
foreach (var m in magics.OrderBy(x => (int)x.Magic))
{
    if (m.Class != MirClass.Warrior) continue;
    Console.WriteLine($"{(int)m.Magic,-6}{m.Magic,-22}{(m.Name ?? ""),-26}{m.MinBasePower,8}{m.MaxBasePower,8}{m.MinLevelPower,7}{m.MaxLevelPower,7}");
}