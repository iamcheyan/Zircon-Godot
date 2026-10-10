using System.Security.Cryptography;
using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 把 Mud3 脚本编译出的 NPC 对话 IR（tools/mud3_npc_compile.py 产出）落到 System.db：
/// 为每个活动 NPC 重建 NPCPage/NPCButton/NPCAction/NPCCheck/NPCGood/NPCType 并重挂入口页，
/// 然后清理不再可达的旧页，最后同步 4 处 System.db 镜像。只写 System.db，不改其它内容。
/// </summary>
internal static class NpcIrApply
{
    private const string PagePrefix = "IR";

    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private sealed class IrDoc
    {
        public List<IrNpc> npcs { get; set; } = new();
    }

    private sealed class IrNpc
    {
        public int index { get; set; }
        public string name { get; set; } = "";
        public string mapFile { get; set; } = "";
        public string? entry { get; set; }
        public List<IrPage> pages { get; set; } = new();
        public List<string> warnings { get; set; } = new();
    }

    private sealed class IrPage
    {
        public string key { get; set; } = "";
        public string type { get; set; } = "None";
        public string say { get; set; } = "";
        public List<IrButton> buttons { get; set; } = new();
        public List<IrCheck> checks { get; set; } = new();
        public List<IrAction> actions { get; set; } = new();
        public List<IrGood> goods { get; set; } = new();
        public List<string> types { get; set; } = new();
        public string? success { get; set; }
    }

    private sealed class IrButton
    {
        public int id { get; set; }
        public string dest { get; set; } = "";
    }

    private sealed class IrCheck
    {
        public string type { get; set; } = "";
        public string op { get; set; } = "Equal";
        public int i1 { get; set; }
        public int i2 { get; set; }
        public string? item { get; set; }
        public string? fail { get; set; }
    }

    private sealed class IrAction
    {
        public string type { get; set; } = "";
        public int i1 { get; set; }
        public int i2 { get; set; }
        public string? item { get; set; }
        public string? map { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public string? s1 { get; set; }
    }

    private sealed class IrGood
    {
        public string item { get; set; } = "";
        public int price { get; set; }
    }

    public static int Run(string root, string irPath, bool dryRun)
    {
        if (!File.Exists(irPath))
        {
            Console.Error.WriteLine($"[FATAL] IR 文件不存在: {irPath}");
            return 1;
        }

        var doc = JsonSerializer.Deserialize<IrDoc>(File.ReadAllText(irPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (doc == null || doc.npcs.Count == 0)
        {
            Console.Error.WriteLine("[FATAL] IR 解析失败或为空");
            return 1;
        }

        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly,
            typeof(MonsterInfo).Assembly, typeof(NPCInfo).Assembly);

        var pageCol = session.GetCollection<NPCPage>();
        var npcCol = session.GetCollection<NPCInfo>();
        var itemByName = session.GetCollection<ItemInfo>().Binding
            .GroupBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var mapByName = session.GetCollection<MapInfo>().Binding
            .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var npcByIndex = npcCol.Binding.ToDictionary(x => x.Index);

        int createdPages = 0, deletedPages = 0, missingItems = 0, missingMaps = 0, missingNpcs = 0;

        // ---- 清理上一轮导入生成的页（Description 以 IR 前缀开头）----
        foreach (var page in pageCol.Binding.Where(p => p.Description != null && p.Description.StartsWith(PagePrefix)).ToList())
        {
            page.Delete();
            deletedPages++;
        }

        foreach (var irNpc in doc.npcs)
        {
            if (!npcByIndex.TryGetValue(irNpc.index, out var npc))
            {
                missingNpcs++;
                continue;
            }
            if (irNpc.entry == null || irNpc.pages.Count == 0) continue;

            var map = new Dictionary<string, NPCPage>(StringComparer.Ordinal);
            var owner = npc.NPCName ?? irNpc.name;

            foreach (var irPage in irNpc.pages)
            {
                var page = pageCol.CreateNewObject();
                page.Description = $"{PagePrefix}{irNpc.index}|{irPage.key}";
                page.DialogType = ParseEnum<NPCDialogType>(irPage.type);
                page.Say = irPage.say;
                page.Currency = null;
                map[irPage.key] = page;
                createdPages++;
            }

            foreach (var irPage in irNpc.pages)
            {
                var page = map[irPage.key];
                if (irPage.success != null && map.TryGetValue(irPage.success, out var succ))
                    page.SuccessPage = succ;

                foreach (var c in irPage.checks)
                {
                    var check = page.Checks.AddNew();
                    check.CheckType = ParseEnum<NPCCheckType>(c.type);
                    check.Operator = ParseEnum<Operator>(c.op);
                    check.IntParameter1 = c.i1;
                    check.IntParameter2 = c.i2;
                    if (c.item != null)
                    {
                        if (itemByName.TryGetValue(c.item, out var ci)) check.ItemParameter1 = ci;
                        else missingItems++;
                    }
                    if (c.fail != null && map.TryGetValue(c.fail, out var fp)) check.FailPage = fp;
                }

                foreach (var a in irPage.actions)
                {
                    var act = page.Actions.AddNew();
                    act.ActionType = ParseEnum<NPCActionType>(a.type);
                    act.IntParameter1 = a.i1;
                    act.IntParameter2 = a.i2;
                    act.StringParameter1 = a.s1;
                    if (a.type == "Teleport")
                    {
                        if (a.map != null && mapByName.TryGetValue(a.map, out var mi))
                        {
                            act.MapParameter1 = mi;
                            act.IntParameter1 = a.x;
                            act.IntParameter2 = a.y;
                        }
                        else
                        {
                            missingMaps++;
                            act.ActionType = NPCActionType.Message;
                        }
                    }
                    else if (a.item != null)
                    {
                        if (itemByName.TryGetValue(a.item, out var ai)) act.ItemParameter1 = ai;
                        else missingItems++;
                    }
                }

                foreach (var b in irPage.buttons)
                {
                    if (b.id <= 0 || !map.TryGetValue(b.dest, out var dest)) continue;
                    var button = page.Buttons.AddNew();
                    button.ButtonID = b.id;
                    button.DestinationPage = dest;
                }

                foreach (var g in irPage.goods)
                {
                    if (!itemByName.TryGetValue(g.item, out var gi))
                    {
                        missingItems++;
                        continue;
                    }
                    var good = page.Goods.AddNew();
                    good.Item = gi;
                    good.GoodsIndex = npc.GoodsIndex;
                    // price > 0：脚本给的是绝对金币价；price == 0：沿用物品 DB 售价（Rate = 1）
                    good.Rate = g.price > 0 && gi.Price > 0 ? Math.Round((decimal)g.price / gi.Price, 4) : 1M;
                }

                foreach (var t in irPage.types)
                {
                    var nt = page.Types.AddNew();
                    nt.ItemType = ParseEnum<ItemType>(t);
                }
            }

            if (map.TryGetValue(irNpc.entry, out var entryPage))
                npc.EntryPage = entryPage;
        }

        // ---- 清理不再可达的旧页（保留被任何 NPC 入口链可达的页）----
        var reachable = new HashSet<NPCPage>();
        var stack = new Stack<NPCPage>();
        foreach (var n in npcCol.Binding)
            if (n.EntryPage != null) stack.Push(n.EntryPage);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p == null || !reachable.Add(p)) continue;
            if (p.SuccessPage != null) stack.Push(p.SuccessPage);
            foreach (var b in p.Buttons)
                if (b.DestinationPage != null)
                    stack.Push(b.DestinationPage);
            foreach (var c in p.Checks)
                if (c.FailPage != null)
                    stack.Push(c.FailPage);
        }

        var orphans = pageCol.Binding.Where(p => !reachable.Contains(p)).ToList();
        int orphanCount = orphans.Count;
        if (!dryRun)
            foreach (var p in orphans)
            {
                p.Delete();
                deletedPages++;
            }

        Console.WriteLine($"[NpcIrApply] npcs={doc.npcs.Count} createdPages={createdPages} "
                          + $"removedOldIrPages+orphans={deletedPages} orphansPruned={orphanCount} "
                          + $"missingNpcs={missingNpcs} missingItems={missingItems} missingMaps={missingMaps}");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] 未写入数据库");
            return 0;
        }

        session.Save(true);
        Console.WriteLine($"[NpcIrApply] saved. NPCPage={pageCol.Binding.Count}");
        return Sync(root) ? 0 : 1;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, true, out var v) ? v : default;

    private static bool Sync(string root)
    {
        string src = Path.Combine(root, "System.db");
        byte[] srcHash = SHA256.HashData(File.ReadAllBytes(src));
        Console.WriteLine($"[源 SHA256] {src}: {Convert.ToHexString(srcHash)}");
        foreach (var dest in Mirrors)
        {
            string absDest = Path.GetFullPath(dest);
            if (string.Equals(absDest, Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(absDest)!);
            File.Copy(src, absDest, overwrite: true);
            if (!srcHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(absDest))))
            {
                Console.Error.WriteLine($"[FATAL] 同步校验失败: {absDest}");
                return false;
            }
            Console.WriteLine($"[已同步] {absDest}");
        }
        return true;
    }
}
