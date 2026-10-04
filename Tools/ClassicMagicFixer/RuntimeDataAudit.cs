using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;

namespace ClassicMagicFixer;

/// <summary>
/// Read-only consistency checks for data-driven features that resolve monsters by MonsterFlag.
/// Keep these mappings aligned with the active lookups in Player magic and MonsterObject.GetMonster.
/// </summary>
internal static class RuntimeDataAudit
{
    private static readonly Dictionary<MagicType, MonsterFlag> MagicMonsterFlags = new()
    {
        [MagicType.SummonSkeleton] = MonsterFlag.Skeleton,
        [MagicType.SummonJinSkeleton] = MonsterFlag.JinSkeleton,
        [MagicType.SummonShinsu] = MonsterFlag.Shinsu,
    };

    private static readonly Dictionary<int, MonsterFlag[]> MonsterAiFlags = new()
    {
        [17] = [MonsterFlag.Larva],
        [43] = [MonsterFlag.BoneCaptain, MonsterFlag.BoneSoldier],
        [22] = [MonsterFlag.ZumaArcherMonster, MonsterFlag.ZumaFanaticMonster,
            MonsterFlag.ZumaGuardianMonster, MonsterFlag.ZumaKeeperMonster],
        [44] = [MonsterFlag.LesserWedgeMoth],
        [72] = [MonsterFlag.BanyoCaptain],
        [75] = [MonsterFlag.MatureEarwig],
        [76] = [MonsterFlag.GoldenArmouredBeetle],
        [80] = [MonsterFlag.FerociousFlameDemon, MonsterFlag.FlameDemon],
        [84] = [MonsterFlag.GoruArcher, MonsterFlag.GoruGeneral, MonsterFlag.GoruSpearman],
        [86] = [MonsterFlag.OYoungBeast, MonsterFlag.YumgonWitch, MonsterFlag.MaWarden,
            MonsterFlag.MaWarlord, MonsterFlag.JinhwanSpirit, MonsterFlag.JinhwanGuardian,
            MonsterFlag.OyoungGeneral, MonsterFlag.YumgonGeneral, MonsterFlag.DragonLord],
        [87] = [MonsterFlag.OYoungBeast, MonsterFlag.YumgonWitch, MonsterFlag.MaWarden,
            MonsterFlag.MaWarlord, MonsterFlag.JinhwanSpirit, MonsterFlag.JinhwanGuardian,
            MonsterFlag.OyoungGeneral, MonsterFlag.YumgonGeneral, MonsterFlag.DragonLord],
        [89] = [MonsterFlag.SamaSorcerer],
        [93] = [MonsterFlag.QuartzMiniTurtle, MonsterFlag.QuartzTurtleSub,
            MonsterFlag.QuartzBlueBat, MonsterFlag.QuartzPinkBat, MonsterFlag.QuartzBlueCrystal,
            MonsterFlag.QuartzRedHood],
        [98] = [MonsterFlag.Sacrifice],
    };

    public static int Run(string root)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MonsterInfo).Assembly, typeof(ItemInfo).Assembly);

        var monsters = session.GetCollection<MonsterInfo>().Binding;
        var magics = session.GetCollection<MagicInfo>().Binding;
        var items = session.GetCollection<ItemInfo>().Binding;
        var drops = session.GetCollection<DropInfo>().Binding;
        var respawns = session.GetCollection<RespawnInfo>().Binding;
        var flags = monsters.GroupBy(x => x.Flag).ToDictionary(x => x.Key, x => x.ToList());
        var failures = new List<string>();

        foreach (var (magic, flag) in MagicMonsterFlags)
        {
            if (!magics.Any(x => x.Magic == magic)) continue;
            if (!flags.ContainsKey(flag))
                failures.Add($"MagicInfo {magic} requires MonsterFlag.{flag}, but no monster has that flag.");
            else if (flags[flag].Count != 1)
                failures.Add($"MagicInfo {magic} requires one MonsterFlag.{flag}; found {flags[flag].Count}.");
        }

        foreach (var monster in monsters)
        {
            if (!MonsterAiFlags.TryGetValue(monster.AI, out var required)) continue;
            foreach (var flag in required)
                if (!flags.ContainsKey(flag))
                    failures.Add($"Monster #{monster.Index} {monster.MonsterName} (AI {monster.AI}) requires MonsterFlag.{flag}.");
        }

        var magicIndices = magics.Select(x => x.Index).ToHashSet();
        foreach (var book in items.Where(x => x.ItemType == ItemType.Book))
            if (!magicIndices.Contains(book.Shape))
                failures.Add($"Skill book #{book.Index} {book.ItemName} points to missing MagicInfo #{book.Shape}.");

        foreach (var drop in drops)
            if (drop.Monster == null || drop.Item == null)
                failures.Add($"DropInfo #{drop.Index} has a missing monster or item reference.");

        foreach (var respawn in respawns)
            if (respawn.Monster == null ||
                (respawn.Region?.Map == null && respawn.Monster.AI != -1))
                failures.Add($"RespawnInfo #{respawn.Index} has a missing monster, region, or map reference.");

        Console.WriteLine($"Runtime data: {monsters.Count} monsters, {magics.Count} magics, " +
            $"{items.Count(x => x.ItemType == ItemType.Book)} skill books, {drops.Count} drops, {respawns.Count} respawns.");

        foreach (string failure in failures)
            Console.Error.WriteLine("[FAIL] " + failure);

        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"Runtime data audit failed: {failures.Count} issue(s).");
            return 1;
        }

        Console.WriteLine("Runtime data audit passed: required flags and core item/monster references are present.");
        return 0;
    }
}
