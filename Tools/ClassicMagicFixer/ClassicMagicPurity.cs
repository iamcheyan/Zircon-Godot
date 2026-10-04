using System.Security.Cryptography;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 阶段一：经典三职业技能纯净化。
///
/// 背景：2026-10-03 的 classic-purity 清洗把 MagicInfo 从 174 行裁到 59 行，
/// 判据是 db_names.json 的**英文别名**（zh）匹配。引擎行的 Name 与 17173
/// 技能名并不一致（例如 zh 别名把「雷电术」指到 Lightning Ball，而经典值
/// 其实是 Thunder Bolt），因此那次清洗误删了 10 个经典技能，又让 25 个
/// 经典技能从未被收录。
///
/// 本工具按 17173 权威清单（mir3-website/data/skills.json）+ 原版客户端
/// 技能手册（ClientData/Magic.exp.txt 的日文标准名与 1/2/3 级需求等级）
/// 重新锁定技能集合，并校验每一行都存在对应�� [MagicType] 引擎实现。
/// </summary>
internal static class ClassicMagicPurity
{
    /// <summary>一条经典技能定义。Name 保持引擎英文名（MagicInfo.Name 是 [IsIdentity] 主键，不可改）。</summary>
    internal sealed record ClassicSkill(
        RequiredClass Class,
        string NameZh,
        MagicType Magic,
        int Icon,
        int[] NeedLevels,
        int[] Experiences,
        int Delay,
        MagicSchool School,
        MagicProperty Property,
        int BaseCost,
        int LevelCost,
        int MinBasePower,
        int MaxBasePower,
        int MinLevelPower,
        int MaxLevelPower);

    private static readonly string[] Mirrors =
    {
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db",
        "/home/tetsuya/development/zircon/System.db",
    };

    // 17173 经典技能 → Zircon 引擎行映射（54 个，全部经 [MagicType] 实现验证）
    // NameZh 仅作审计对照；MagicInfo.Name 保持英文（[IsIdentity] 主键）
    private static readonly ClassicSkill[] ClassicSkills =
    [
        new(RequiredClass.Warrior, "基本剑术", MagicType.Swordsmanship, 4, new[] {7, 9, 11}, new[] {100, 200, 300}, 0, MagicSchool.Passive, MagicProperty.Passive, 0, 0, 0, 0, 10, 10),
        new(RequiredClass.Warrior, "攻杀剑术", MagicType.Slaying, 12, new[] {14, 16, 18}, new[] {300, 400, 500}, 0, MagicSchool.Passive, MagicProperty.Passive, 0, 0, 8, 8, 7, 7),
        new(RequiredClass.Warrior, "刺杀剑术", MagicType.Thrusting, 22, new[] {19, 21, 23}, new[] {400, 500, 600}, 0, MagicSchool.Toggle, MagicProperty.Toggle, 0, 0, 50, 50, 50, 50),
        new(RequiredClass.Warrior, "半月弯刀", MagicType.HalfMoon, 48, new[] {24, 26, 28}, new[] {600, 700, 800}, 0, MagicSchool.Toggle, MagicProperty.Toggle, 3, 0, 40, 40, 50, 50),
        new(RequiredClass.Warrior, "野蛮冲撞", MagicType.ShoulderDash, 52, new[] {27, 29, 31}, new[] {700, 800, 900}, 4000, MagicSchool.Active, MagicProperty.Active, 0, 20, 2, 3, 3, 3),
        new(RequiredClass.Warrior, "烈火剑法", MagicType.FlamingSword, 50, new[] {32, 34, 36}, new[] {1000, 1100, 1200}, 7000, MagicSchool.Active, MagicProperty.Charge, 7, 2, 160, 160, 100, 100),
        new(RequiredClass.Warrior, "翔空剑法", MagicType.DragonRise, 68, new[] {35, 37, 39}, new[] {1000, 1100, 1200}, 7000, MagicSchool.Active, MagicProperty.Charge, 8, 2, 120, 120, 100, 100),
        new(RequiredClass.Warrior, "莲月剑法", MagicType.BladeStorm, 66, new[] {38, 40, 42}, new[] {1000, 1100, 1200}, 7000, MagicSchool.Active, MagicProperty.Charge, 9, 3, 240, 240, 100, 100),
        new(RequiredClass.Warrior, "十方斩", MagicType.DestructiveSurge, 204, new[] {40, 43, 46}, new[] {2000, 3000, 6000}, 0, MagicSchool.Toggle, MagicProperty.Toggle, 7, 0, 70, 70, 30, 30),
        new(RequiredClass.Warrior, "乾坤大挪移", MagicType.Interchange, 212, new[] {42, 45, 48}, new[] {4000, 6000, 12000}, 5000, MagicSchool.Active, MagicProperty.Active, 10, 40, 0, 0, 0, 0),
        new(RequiredClass.Warrior, "铁布衫", MagicType.Defiance, 202, new[] {44, 47, 50}, new[] {6000, 9000, 18000}, 0, MagicSchool.Active, MagicProperty.Active, 40, 80, 30, 30, 90, 90),
        new(RequiredClass.Warrior, "斗转星移", MagicType.Beckon, 214, new[] {46, 49, 52}, new[] {8000, 12000, 24000}, 5000, MagicSchool.Active, MagicProperty.Active, 20, 40, 0, 0, 0, 0),
        new(RequiredClass.Warrior, "破血狂杀", MagicType.Might, 210, new[] {48, 51, 54}, new[] {10000, 15000, 30000}, 0, MagicSchool.Active, MagicProperty.Active, 50, 100, 30, 30, 90, 90),
        new(RequiredClass.Wizard, "火球术", MagicType.FireBall, 0, new[] {7, 9, 11}, new[] {100, 200, 300}, 0, MagicSchool.Fire, MagicProperty.Active, 1, 3, 0, 4, 6, 10),
        new(RequiredClass.Wizard, "霹雳掌", MagicType.LightningBall, 80, new[] {8, 10, 12}, new[] {100, 200, 300}, 0, MagicSchool.Lightning, MagicProperty.Active, 1, 4, 0, 4, 6, 10),
        new(RequiredClass.Wizard, "冰月神掌", MagicType.IceBolt, 76, new[] {9, 11, 13}, new[] {100, 200, 300}, 0, MagicSchool.Ice, MagicProperty.Active, 1, 4, 0, 4, 4, 8),
        new(RequiredClass.Wizard, "风掌", MagicType.GustBlast, 132, new[] {10, 12, 14}, new[] {100, 200, 300}, 0, MagicSchool.Wind, MagicProperty.Active, 1, 3, 0, 4, 5, 9),
        new(RequiredClass.Wizard, "抗拒火环", MagicType.Repulsion, 14, new[] {12, 14, 16}, new[] {200, 300, 400}, 0, MagicSchool.Wind, MagicProperty.Active, 1, 8, 2, 3, 3, 3),
        new(RequiredClass.Wizard, "诱惑之光", MagicType.ElectricShock, 38, new[] {13, 15, 17}, new[] {200, 300, 400}, 0, MagicSchool.Lightning, MagicProperty.Active, 3, 3, 0, 0, 0, 0),
        new(RequiredClass.Wizard, "瞬息移动", MagicType.Teleportation, 40, new[] {14, 16, 18}, new[] {300, 400, 500}, 7000, MagicSchool.Phantom, MagicProperty.Active, 10, 10, 0, 0, 0, 0),
        new(RequiredClass.Wizard, "大火球", MagicType.AdamantineFireBall, 8, new[] {15, 17, 19}, new[] {400, 500, 600}, 0, MagicSchool.Fire, MagicProperty.Active, 6, 6, 7, 11, 15, 19),
        new(RequiredClass.Wizard, "雷电术", MagicType.ThunderBolt, 20, new[] {16, 18, 20}, new[] {400, 500, 600}, 0, MagicSchool.Lightning, MagicProperty.Active, 6, 7, 7, 11, 15, 19),
        new(RequiredClass.Wizard, "冰月震天", MagicType.IceBlades, 78, new[] {17, 19, 21}, new[] {400, 500, 600}, 0, MagicSchool.Ice, MagicProperty.Active, 6, 7, 7, 11, 13, 17),
        new(RequiredClass.Wizard, "击风", MagicType.Cyclone, 146, new[] {18, 20, 22}, new[] {400, 500, 600}, 0, MagicSchool.Wind, MagicProperty.Active, 6, 6, 7, 11, 14, 18),
        new(RequiredClass.Wizard, "疾光电影", MagicType.LightningBeam, 18, new[] {21, 23, 25}, new[] {500, 600, 700}, 0, MagicSchool.Lightning, MagicProperty.Active, 15, 12, 12, 16, 14, 14),
        new(RequiredClass.Wizard, "冰沙掌", MagicType.FrozenEarth, 104, new[] {22, 24, 26}, new[] {500, 600, 700}, 0, MagicSchool.Ice, MagicProperty.Active, 15, 12, 12, 16, 12, 16),
        new(RequiredClass.Wizard, "火墙", MagicType.FireWall, 42, new[] {24, 26, 28}, new[] {600, 700, 800}, 0, MagicSchool.Fire, MagicProperty.Active, 30, 22, 1, 6, 2, 9),
        new(RequiredClass.Wizard, "圣言术", MagicType.ExpelUndead, 62, new[] {26, 28, 30}, new[] {700, 800, 900}, 0, MagicSchool.Phantom, MagicProperty.Active, 30, 30, 0, 0, 0, 0),
        new(RequiredClass.Wizard, "异形换位", MagicType.GeoManipulation, 206, new[] {27, 29, 31}, new[] {800, 900, 1000}, 5000, MagicSchool.Phantom, MagicProperty.Active, 20, 25, 0, 0, 0, 0),
        new(RequiredClass.Wizard, "魔法盾", MagicType.MagicShield, 60, new[] {29, 31, 33}, new[] {900, 1000, 1100}, 0, MagicSchool.Phantom, MagicProperty.Active, 30, 20, 0, 0, 0, 0),
        new(RequiredClass.Wizard, "爆裂火焰", MagicType.FireStorm, 44, new[] {32, 34, 36}, new[] {1000, 1100, 1200}, 0, MagicSchool.Fire, MagicProperty.Active, 20, 15, 14, 18, 14, 18),
        new(RequiredClass.Wizard, "地狱雷光", MagicType.LightningWave, 46, new[] {33, 35, 37}, new[] {1000, 1100, 1200}, 0, MagicSchool.Lightning, MagicProperty.Active, 20, 17, 14, 18, 14, 18),
        new(RequiredClass.Wizard, "冰咆哮", MagicType.IceStorm, 64, new[] {34, 36, 38}, new[] {1000, 1100, 1200}, 0, MagicSchool.Ice, MagicProperty.Active, 20, 19, 14, 18, 12, 16),
        new(RequiredClass.Wizard, "龙卷风", MagicType.DragonTornado, 142, new[] {35, 37, 39}, new[] {1000, 1100, 1200}, 0, MagicSchool.Wind, MagicProperty.Active, 20, 18, 14, 18, 13, 17),
        new(RequiredClass.Wizard, "凝血离魂", MagicType.Renounce, 222, new[] {46, 48, 50}, new[] {8000, 12000, 24000}, 0, MagicSchool.Phantom, MagicProperty.Active, 10, 60, 0, 0, 0, 0),
        new(RequiredClass.Taoist, "治愈术", MagicType.Heal, 2, new[] {7, 9, 11}, new[] {100, 200, 300}, 0, MagicSchool.Holy, MagicProperty.Active, 2, 7, 0, 0, 11, 15),
        new(RequiredClass.Taoist, "精神力战法", MagicType.SpiritSword, 6, new[] {8, 10, 12}, new[] {100, 200, 300}, 0, MagicSchool.Physical, MagicProperty.Passive, 0, 0, 0, 0, 9, 9),
        new(RequiredClass.Taoist, "施毒术", MagicType.PoisonDust, 10, new[] {12, 14, 16}, new[] {200, 300, 400}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 10, 15, 25, 25, 55),
        new(RequiredClass.Taoist, "灵魂火符", MagicType.ExplosiveTalisman, 24, new[] {13, 15, 17}, new[] {300, 400, 500}, 0, MagicSchool.Dark, MagicProperty.Active, 3, 6, 3, 3, 6, 10),
        new(RequiredClass.Taoist, "月魂断玉", MagicType.EvilSlayer, 72, new[] {14, 16, 18}, new[] {300, 400, 500}, 0, MagicSchool.Holy, MagicProperty.Active, 3, 6, 3, 3, 6, 10),
        new(RequiredClass.Taoist, "召唤骷髅", MagicType.SummonSkeleton, 32, new[] {17, 19, 21}, new[] {400, 500, 600}, 0, MagicSchool.Phantom, MagicProperty.Active, 10, 15, 0, 0, 0, 0),
        new(RequiredClass.Taoist, "隐身术", MagicType.Invisibility, 34, new[] {20, 22, 24}, new[] {500, 600, 700}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 5, 5, 10, 5, 15),
        new(RequiredClass.Taoist, "幽灵盾", MagicType.MagicResistance, 26, new[] {21, 23, 25}, new[] {500, 600, 700}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 10, 30, 50, 40, 120),
        new(RequiredClass.Taoist, "集体隐身术", MagicType.MassInvisibility, 36, new[] {23, 25, 27}, new[] {600, 700, 800}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 10, 5, 10, 5, 15),
        new(RequiredClass.Taoist, "月魂灵波", MagicType.GreaterEvilSlayer, 74, new[] {24, 26, 28}, new[] {600, 700, 800}, 0, MagicSchool.Holy, MagicProperty.Active, 4, 8, 7, 7, 8, 14),
        new(RequiredClass.Taoist, "神圣战甲术", MagicType.Resilience, 28, new[] {25, 27, 29}, new[] {700, 800, 900}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 10, 30, 50, 40, 120),
        new(RequiredClass.Taoist, "困魔咒", MagicType.TrapOctagon, 30, new[] {27, 29, 31}, new[] {800, 900, 1000}, 0, MagicSchool.Dark, MagicProperty.Active, 10, 15, 10, 20, 10, 20),
        new(RequiredClass.Taoist, "空拳刀法", MagicType.CombatKick, 70, new[] {28, 30, 32}, new[] {900, 1000, 1100}, 0, MagicSchool.Physical, MagicProperty.Active, 10, 20, 2, 3, 3, 3),
        new(RequiredClass.Taoist, "召唤神兽", MagicType.SummonShinsu, 58, new[] {30, 32, 34}, new[] {900, 1000, 1100}, 0, MagicSchool.Phantom, MagicProperty.Active, 15, 15, 0, 0, 0, 0),
        new(RequiredClass.Taoist, "群体治愈术", MagicType.MassHeal, 56, new[] {31, 33, 35}, new[] {1000, 1200, 1400}, 0, MagicSchool.Holy, MagicProperty.Active, 20, 10, 8, 8, 16, 24),
        new(RequiredClass.Taoist, "超强召唤骷髅", MagicType.SummonJinSkeleton, 208, new[] {33, 35, 37}, new[] {1000, 1100, 1200}, 0, MagicSchool.Phantom, MagicProperty.Active, 25, 20, 0, 0, 0, 0),
        new(RequiredClass.Taoist, "猛虎强势", MagicType.BloodLust, 186, new[] {34, 36, 38}, new[] {1000, 1100, 1200}, 0, MagicSchool.Dark, MagicProperty.Active, 5, 10, 30, 50, 40, 120),
        new(RequiredClass.Taoist, "回生术", MagicType.Resurrection, 152, new[] {35, 37, 39}, new[] {500, 600, 700}, 0, MagicSchool.Holy, MagicProperty.Active, 100, 100, 10, 20, 15, 30),
        new(RequiredClass.Taoist, "移花接玉", MagicType.ReflectDamage, 250, new[] {48, 51, 54}, new[] {10000, 15000, 30000}, 120000, MagicSchool.Active, MagicProperty.Active, 50, 100, 30, 30, 90, 90),
    ];

    // 引擎中不存在 [MagicType] 实现的经典技能：不写入 MagicInfo，
    // 避免产生「扣蓝但无效果」的哑技能。待实现清单见 Unimplemented。
    private static readonly (RequiredClass Class, string NameZh, string Reason)[] Unimplemented =
    {
        (RequiredClass.Wizard, "地狱火", "经典为法师火系；引擎仅有刺客系 MagicType.HellFire(411)，职业不符"),
        (RequiredClass.Wizard, "魄冰刺", "引擎无任何对应 MagicType"),
        (RequiredClass.Wizard, "怒神霹雳", "引擎无任何对应 MagicType"),
        (RequiredClass.Wizard, "焰天火雨", "引擎无任何对应 MagicType（近似为 MeteorShower 流星火雨，元素/名称均不符）"),
        (RequiredClass.Taoist, "云寂术", "引擎无任何对应 MagicType"),
        (RequiredClass.Taoist, "妙影无踪", "引擎无任何对应 MagicType"),
        (RequiredClass.Taoist, "阴阳法环", "引擎无任何对应 MagicType"),
    };

    internal static System.Collections.Generic.IReadOnlyList<ClassicSkill> ClassicSkillsTable() => ClassicSkills;

    public static int Run(string root, bool sync)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly);

        var collection = session.GetCollection<MagicInfo>();
        var magics = collection.Binding;
        Console.WriteLine($"清洗前 MagicInfo: {magics.Count} 行");

        int created = 0, updated = 0;
        var survivors = new List<MagicInfo>(ClassicSkills.Length);

        foreach (var skill in ClassicSkills)
        {
            var info = magics.FirstOrDefault(x => x.Magic == skill.Magic);

            if (info == null)
            {
                info = collection.CreateNewObject();
                info.Name = skill.Magic.ToString();
                info.Magic = skill.Magic;
                created++;
                Console.WriteLine($"  [新建] {skill.NameZh,-8} {skill.Magic,-22} icon={skill.Icon,4}");
            }
            else
            {
                updated++;
            }

            info.RequiredClass = skill.Class;
            info.Icon = skill.Icon;
            info.NeedLevel1 = skill.NeedLevels[0];
            info.NeedLevel2 = skill.NeedLevels[1];
            info.NeedLevel3 = skill.NeedLevels[2];
            info.Experience1 = skill.Experiences[0];
            info.Experience2 = skill.Experiences[1];
            info.Experience3 = skill.Experiences[2];
            info.Delay = skill.Delay;
            info.School = skill.School;
            info.Property = skill.Property;
            info.BaseCost = skill.BaseCost;
            info.LevelCost = skill.LevelCost;
            info.MinBasePower = skill.MinBasePower;
            info.MaxBasePower = skill.MaxBasePower;
            info.MinLevelPower = skill.MinLevelPower;
            info.MaxLevelPower = skill.MaxLevelPower;

            survivors.Add(info);
        }

        var keep = survivors.ToHashSet();
        int purged = 0;
        foreach (var info in magics.ToList())
        {
            if (keep.Contains(info)) continue;

            purged++;
            info.Delete();
        }

        session.Save(true);
        Console.WriteLine($"清洗后 MagicInfo: {collection.Count} 行（新建 {created} / 复用 {updated} / 删除 {purged}）");

        foreach (var (class_, nameZh, reason) in Unimplemented)
            Console.WriteLine($"  [待实现] {class_,-7} {nameZh,-8} {reason}");

        if (sync) Sync(root);
        return 0;
    }

    private static void Sync(string root)
    {
        string source = Path.Combine(root, "System.db");
        foreach (string target in Mirrors)
        {
            File.Copy(source, target, true);
            Console.WriteLine($"  -> 已覆盖 {target}");
        }

        string expected = Md5(source);
        Console.WriteLine($"System.db MD5: {expected}");
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
