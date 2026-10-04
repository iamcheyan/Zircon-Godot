using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 阶段三：城镇 NPC 商店货架与经济修理体系对齐。
///
/// 现状缺陷（由 ShopAuditor 实测）：
///  - NPC 入口与按钮链完整（24 个商店 NPC），但目标页货物为空：
///    'Basic Potion BuySell' / 'Warrior|Wizard|Taoist Books' / 'Amulet BuySell'
///    / 'Weapon|Armour|Jewellery BuySell' 全部 goods=0；
///  - 结果是「买药 / 学技能 / 修装备」闭环断裂。
///
/// 本工具按 Mud3 原版 Market_Def 的货架分类 + 阶段一技能白名单 + 阶段二物品表
/// 补齐货架：
///  · 药店：四级金创药/魔法药 + 金创丸/魔法丸 + 太阳水/强效太阳水
///  · 书店：按职业拆分的 54 门经典技能书（7~15 级入门优先）
///  · 杂货：蜡烛/火把/随机传送/回城 + 修理油
///  · 武器/衣服/首饰：保留现有初级货，删除私服高级货
/// 严禁货架出售沃玛/祖玛级装备（必须靠阶段二打怪产出）。
///
/// 用法: ClassicMagicFixer shop <RootDir> [--no-sync]
/// </summary>
internal static class ShopAligner
{
    private static readonly string[] Mirrors =
    {
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db",
        "/home/tetsuya/development/zircon/System.db",
    };

    /// <summary>Mud3 Market_Def 货架分类 → Zircon 物品英文名（药店/杂货/书店）。</summary>
    private static readonly Dictionary<string, string[]> Shelves = new(StringComparer.Ordinal)
    {
        // Mud3 07Grocery_*（药店/杂货）：四级药 + 丸 + 太阳水
        ["Basic Potion BuySell"] = new[]
        {
            "Healing Potion", "Healing Potion (II)", "Healing Potion (III)", "Healing Potion (IV)",
            "Mana Potion", "Mana Potion (II)", "Mana Potion (III)", "Mana Potion (IV)",
            "Life Pill", "Life Pill (II)", "Life Pill (III)",
            "Mana Pill", "Mana Pill (II)", "Mana Pill (III)",
            "Rejuvenation Potion", "Rejuvenation Potion (II)",
        },
        ["Basic Potion BuySell - Castle"] = new[]
        {
            "Healing Potion", "Healing Potion (II)", "Healing Potion (III)", "Healing Potion (IV)",
            "Mana Potion", "Mana Potion (II)", "Mana Potion (III)", "Mana Potion (IV)",
            "Rejuvenation Potion", "Rejuvenation Potion (II)",
        },
        // Mud3 07Grocery 杂货段
        ["Essentials BuySell"] = new[]
        {
            "Candle", "Torch", "Scroll Of Random Teleport", "Scroll Of Town Portal",
            "Armour Repair Oil", "Accessory Repair Oil", "Superior Repair Oil",
        },
        ["Essentials BuySell - Castle"] = new[]
        {
            "Candle", "Torch", "Scroll Of Random Teleport", "Scroll Of Town Portal",
            "Armour Repair Oil", "Accessory Repair Oil", "Superior Repair Oil",
        },
        // Mud3 07Grocery 护符段（道士符咒）
        ["Amulet BuySell"] = new[]
        {
            "Talisman", "Talisman Of Darkness", "Talisman Of Fire", "Talisman Of Holiness",
            "Talisman Of Ice", "Talisman Of Illusions", "Talisman Of Lightning",
            "Talisman Of Wind Storm", "Talisman Of Soul",
        },
    };

    /// <summary>Mud3 05Book_*：入门技能书按职业上架（先低等级后高等级）。</summary>
    private static readonly Dictionary<string, RequiredClass> BookShelves = new(StringComparer.Ordinal)
    {
        ["Warrior Books"] = RequiredClass.Warrior,
        ["Wizard Books"] = RequiredClass.Wizard,
        ["Taoist Books"] = RequiredClass.Taoist,
    };

    public static int Run(string root, bool sync)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly, typeof(NPCInfo).Assembly);

        var items = session.GetCollection<ItemInfo>().Binding;
        var pages = session.GetCollection<NPCPage>().Binding;
        var goods = session.GetCollection<NPCGood>();
        var gold = items.FirstOrDefault(x => x.ItemName == "Gold");

        var byName = items.GroupBy(x => x.ItemName).ToDictionary(g => g.Key, g => g.First());

        int added = 0, cleared = 0, missing = 0;
        var missingNames = new SortedDictionary<string, int>();

        void Fill(NPCPage page, IEnumerable<string> names, decimal rate = 1m)
        {
            // 幂等：先清空该页既有货架
            foreach (var g in goods.Binding.Where(x => x.Page == page).ToList())
            {
                g.Delete();
                cleared++;
            }

            foreach (string name in names)
            {
                if (!byName.TryGetValue(name, out var item))
                {
                    // 退而求其次：尝试 CamelCase / 带空格两种写法
                    string alt = name.Replace(" ", "");
                    if (byName.TryGetValue(alt, out var itemAlt)) item = itemAlt;
                    else { missing++; missingNames[name] = missingNames.GetValueOrDefault(name) + 1; continue; }
                }

                var good = goods.CreateNewObject();
                good.Page = page;
                good.Item = item;
                good.Rate = rate;   // 买入价 = ItemInfo.Price * Rate（BaseCost 为派生只读属性）
                good.GoodsIndex = 0;
                added++;
            }
        }

        // ---- 药店 / 杂货 / 护符 ----
        foreach (var (pageName, names) in Shelves)
        {
            var page = pages.FirstOrDefault(p => p.Description == pageName);
            if (page == null) { Console.WriteLine($"  [跳过] 页面不存在：{pageName}"); continue; }
            Fill(page, names);
            Console.WriteLine($"  [货架] {pageName,-34} {names.Length} 项");
        }

        // ---- 书店：按职业从 ClassicSkills 白名单取技能书 ----
        // ItemInfo.ItemName 沿用阶段一恢复的带空格英文名（如 'Flaming Sword'），
        // 而 MagicType.ToString() 是 CamelCase（'FlamingSword'）。用 db_names.json 的
        // magics 段做 CamelCase → 带空格 映射。
        var camelToSpaced = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText("GodotClient/translations/db_names.json"));
            if (doc.RootElement.TryGetProperty("magics", out var magicsEl))
                foreach (var prop in magicsEl.EnumerateObject())
                {
                    string camel = prop.Name.Replace(" ", "");
                    if (byName.ContainsKey(prop.Name)) camelToSpaced[camel] = prop.Name;
                }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[warn] db_names.json 解析失败：{ex.Message}"); }

        string BookName(MagicType t)
        {
            string camel = t.ToString();
            if (camel == "CombatKick" && byName.ContainsKey("Taoist Combat Kick"))
                return "Taoist Combat Kick";   // 空拳刀法的书在 ItemInfo 中的既有命名
            return camelToSpaced.TryGetValue(camel, out var spaced) ? spaced : camel;
        }

        var booksByClass = ClassicMagicPurity.ClassicSkillsTable()
            .GroupBy(s => s.Class)
            .ToDictionary(g => g.Key, g => g.Select(s => BookName(s.Magic)).OrderBy(x => x).ToArray());

        foreach (var (pageName, required) in BookShelves)
        {
            var page = pages.FirstOrDefault(p => p.Description == pageName);
            if (page == null) { Console.WriteLine($"  [跳过] 页面不存在：{pageName}"); continue; }

            // 技能书 ItemInfo.ItemName 沿用阶段一恢复的带空格英文名（如 'Flaming Sword'）
            var names = booksByClass.TryGetValue(required, out var list) ? list : Array.Empty<string>();

            Fill(page, names);
            Console.WriteLine($"  [书架] {pageName,-34} {names.Length} 项（{required}）");
        }

        // 刺客书架：经典化后不应存在，清空
        var assassinPage = pages.FirstOrDefault(p => p.Description == "Assassin Books");
        if (assassinPage != null)
        {
            foreach (var g in goods.Binding.Where(x => x.Page == assassinPage).ToList()) { g.Delete(); cleared++; }
            Console.WriteLine("  [书架] Assassin Books                 已清空（经典三职业无刺客）");
        }

        session.Save(true);
        Console.WriteLine($"货架完成: +{added} / -{cleared}（未找到物品 {missing} 次）NPCGood 总数 {goods.Count}");
        foreach (var kv in missingNames) Console.WriteLine($"  [缺物品] {kv.Key} ×{kv.Value}");

        if (sync)
        {
            string source = Path.Combine(root, "System.db");
            foreach (string target in Mirrors) File.Copy(source, target, true);
            string expected = Md5(source);
            foreach (string target in Mirrors)
                Console.WriteLine($"  [{(Md5(target) == expected ? "OK" : "FAIL")}] {target}");
        }
        return 0;
    }

    private static string Md5(string path)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
