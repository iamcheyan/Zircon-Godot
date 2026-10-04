using System.Security.Cryptography;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 阶段二：经典怪物掉落对齐。
///
/// 1. 清空全部旧 DropInfo（含悬空外键、私服掉落、私服技能书等）；
/// 2. 以 Mud3 原版 Envir3/MonItems（UTF-8 解码于 /tmp/drops）重建：
///    - 概率 1/N 语义与 Zircon DropInfo.Chance 一致（MonsterObject.Drop: int.MaxValue / Chance）；
///    - 怪物按 canonical_identity.json 的 zircon_index 映射（名字英化后不可靠）；
///    - 物品通过三阶段解析器：① 引擎内 ItemInfo 物品名直查；② Mud3ItemAliases
///      （文档 + 网站百科生成的 Mud3-中文 → Zircon-英文 字典）；③ 去掉
///      「（秘籍）」后缀并按 ClassicSkills 白名单匹配技能书（ItemType.Book，
///      Shape = 重映射到当前 MagicInfo.Index）。
/// 3. 写库后由调用方负责四镜像同步，本类不自动同步。
///
/// 用法: ClassicMagicFixer drops <RootDir> [--no-sync]
/// </summary>
internal static class DropAligner
{
    private const string Mud3DropDir = "/tmp/drops";

    private static readonly string[] Mirrors =
    {
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db",
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/Debug/ServerCore/Database/System.db"
    };

    public static int Run(string root, bool sync)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly, typeof(MonsterInfo).Assembly);

        var items = session.GetCollection<ItemInfo>().Binding;
        var monsters = session.GetCollection<MonsterInfo>().Binding;
        var drops = session.GetCollection<DropInfo>();

        var gold = items.FirstOrDefault(x => x.ItemName == "Gold");

        // ---------- 1) 清空旧掉落 ----------
        int old = drops.Binding.Count, dangling = drops.Binding.Count(x => x.Monster == null || x.Item == null);
        foreach (var d in drops.Binding.ToList()) d.Delete();
        Console.WriteLine($"旧掉落 {old} 行全部清除（其中悬空外键 {dangling}）");

        if (!Directory.Exists(Mud3DropDir))
        {
            Console.Error.WriteLine($"[FATAL] Mud3 drop dir missing: {Mud3DropDir}");
            return 1;
        }

        var itemByName = items.GroupBy(x => x.ItemName).ToDictionary(g => g.Key, g => g.First());
        var monByIndex = monsters.ToDictionary(x => x.Index);

        // ② Mud3-中文 → Zircon 英文 名字典（来自文档生成）
        var alias = Program.Mud3ItemAliases;

        // ③ 技能书：Mud3 「<魔法中文名>（秘籍）」→ Zircon ItemInfo（英文名=EN magic 名字，去空格）
        var skillZhToMagicEn = new Dictionary<string, string>(StringComparer.Ordinal);
        var skillZhToMagicEnFallback = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in ClassicMagicPurity.ClassicSkillsTable())
        {
            string en = s.Magic.ToString();
            // db_names.json 'magics' 段是 EN -> { zh, ja }，反向索引把 EN 翻译值加入别名查找。
            // 已知特例：空拳刀法 → Combat Kick，但 item 名是 "Taoist Combat Kick"。
        }

        // 反查：取 ItemInfo 当前 Book 行（已被 restore 模式按 magic EN name 恢复过），
        // 再用 db_names.json magics 段获取 zh → EN 映射，反向填入。
        var enToZh = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var text = File.ReadAllText("GodotClient/translations/db_names.json");
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("magics", out var magicsEl))
            {
                foreach (var prop in magicsEl.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("zh", out var zhEl))
                        enToZh[prop.Name] = zhEl.GetString() ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] db_names.json 解析失败：{ex.Message}");
        }
        // 秘笈解析需要保留空格（如 'Flaming Sword'），否则 itemByName 找不到
        var zhToBookEn = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in enToZh)
        {
            // 别名表优先；然后按 ItemInfo.ItemName 实际名称选择（保留空格）
            string en = kv.Key;
            if (alias.ContainsKey(kv.Value)) continue;
            string spaced = itemByName.ContainsKey(en) ? en : en.Replace(" ", "");
            zhToBookEn[kv.Value] = spaced;
        }
        foreach (var (en, zh) in enToZh)
        {
            // 优先选择 ItemInfo.ItemName 中实际存在的英文名（含空格，如 'Flaming Sword'）
            // 保留 MagicType.ToString() 形式的 CamelCase 作为 fallback
            string spaced = itemByName.ContainsKey(en) ? en : en.Replace(" ", "");
            skillZhToMagicEn[zh] = spaced;
        }
        // Combat Kick 特例
        skillZhToMagicEn["空拳刀法"] = "TaoistCombatKick";

        // ---------- 2) Mud3 MonItems 重建 ----------
        int created = 0, unmappedItems = 0, unmappedMons = 0;
        var unmappedNames = new SortedDictionary<string, int>();

        string Norm(string s) => s.Replace("（", "(").Replace("）", ")").Replace("：", ":").Replace(" ", "");

        ItemInfo? ResolveItem(string rawName, SortedDictionary<string, int> misses)
        {
            if (rawName == "金币") return gold;
            string nn = Norm(rawName);
            string query = nn.EndsWith("(秘籍)") ? nn[..^4] : nn;
            string? en = alias.TryGetValue(query, out var a) ? a
                       : zhToBookEn.TryGetValue(query, out var b) ? b
                       : skillZhToMagicEn.TryGetValue(query, out var c) ? c
                       : itemByName.ContainsKey(rawName) ? rawName
                       : null;
            if (en == null) { misses[rawName] = misses.GetValueOrDefault(rawName) + 1; return null; }
            return itemByName.TryGetValue(en, out var it) ? it : null;
        }

        foreach (string file in Directory.GetFiles(Mud3DropDir, "*.txt"))
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            string monName = fileName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (monName.Length == 0) monName = fileName;

            if (!Program.CanonicalMonsterIndex.TryGetValue(monName, out int zirconIndex) ||
                !monByIndex.TryGetValue(zirconIndex, out var monster))
            {
                unmappedMons++;
                continue;
            }

            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '%' or '+' or '[') continue;

                string[] parts = line.Replace('\t', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !parts[0].Contains('/')) continue;
                if (!int.TryParse(parts[0].Split('/')[1], out int den) || den <= 0) continue;

                string itemName = parts[1];
                int amount = parts.Length > 2 && int.TryParse(parts[2], out int a) ? a : 1;

                var item = ResolveItem(itemName, unmappedNames);
                if (item == null) { unmappedItems++; continue; }

                var drop = drops.CreateNewObject();
                drop.Monster = monster;
                drop.Item = item;
                drop.Chance = den;
                drop.Amount = Math.Max(1, amount);
                created++;
            }
        }

        session.Save(true);
        Console.WriteLine($"掉落重建: +{created} 行（未映射物品引用 {unmappedItems} 次 / 无 Mud3 表的怪物文件 {unmappedMons} 个）");
        Console.WriteLine($"DropInfo 总数: {drops.Count}");
        foreach (var kv in unmappedNames)
            Console.WriteLine($"  [未映射物品] {kv.Key} ×{kv.Value}");

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