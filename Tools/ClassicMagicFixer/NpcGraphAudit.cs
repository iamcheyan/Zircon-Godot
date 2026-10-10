using System.Text.Json;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 只读导出：把 System.db 中全部 NPC 的对话框图谱（NPCInfo → EntryPage →
/// Buttons/SuccessPage 可达闭包）导成 JSON，供离线审计与实机巡检清单使用。不写库。
/// </summary>
internal static class NpcGraphAudit
{
    public static int Run(string root, string outPath)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly, typeof(MonsterInfo).Assembly,
            typeof(NPCInfo).Assembly);

        var pages = session.GetCollection<NPCPage>().Binding;
        var npcs = session.GetCollection<NPCInfo>().Binding;
        var pageById = pages.ToDictionary(x => x.Index);

        var npcList = new List<Dictionary<string, object>>();
        var reachable = new HashSet<int>();

        foreach (var n in npcs.OrderBy(x => x.Index))
        {
            var entry = n.EntryPage;
            var region = n.Region;
            var pts = region?.PointRegion;
            var npcRec = new Dictionary<string, object>
            {
                ["index"] = n.Index,
                ["name"] = n.NPCName ?? "",
                ["active"] = region != null,
                ["mapFile"] = region?.Map?.FileName ?? "",
                ["mapDesc"] = region?.Map?.Description ?? "",
                ["mapIndex"] = region?.Map?.Index ?? -1,
                ["regionDesc"] = region?.Description ?? "",
                ["x"] = pts is { Length: > 0 } ? pts[0].X : -1,
                ["y"] = pts is { Length: > 0 } ? pts[0].Y : -1,
                ["regionAllPoints"] = pts == null
                    ? new List<Dictionary<string, object>>()
                    : pts.Select(p => new Dictionary<string, object> { ["x"] = p.X, ["y"] = p.Y }).ToList(),
                ["image"] = n.Image,
                ["face"] = n.FaceImage,
                ["category"] = n.Category.ToString(),
                ["goodsIndex"] = n.GoodsIndex,
                ["mapIcon"] = n.MapIcon.ToString(),
                ["entryPage"] = entry?.Index ?? -1,
                ["entryType"] = entry == null ? "NULL" : entry.DialogType.ToString(),
                ["entrySay"] = entry?.Say ?? "",
                ["requirements"] = n.Requirements.Select(r => new Dictionary<string, object>
                {
                    ["type"] = r.Requirement.ToString(),
                    ["i1"] = r.IntParameter1,
                    ["class"] = r.Class.ToString(),
                    ["days"] = r.DaysOfWeek.ToString(),
                    ["quest"] = r.QuestParameter?.QuestName ?? ""
                }).ToList()
            };
            npcList.Add(npcRec);

            if (entry != null) Walk(entry, reachable);
        }

        var itemList = session.GetCollection<ItemInfo>().Binding.OrderBy(x => x.Index)
            .Select(x => new Dictionary<string, object>
            {
                ["index"] = x.Index,
                ["name"] = x.ItemName ?? "",
                ["type"] = x.ItemType.ToString(),
                ["price"] = x.Price,
                ["stackSize"] = x.StackSize,
                ["shape"] = x.Shape
            }).ToList();
        var mapList = session.GetCollection<MapInfo>().Binding.OrderBy(x => x.Index)
            .Select(x => new Dictionary<string, object>
            {
                ["index"] = x.Index,
                ["fileName"] = x.FileName ?? "",
                ["description"] = x.Description ?? "",
                ["minLevel"] = x.MinimumLevel,
                ["maxLevel"] = x.MaximumLevel
            }).ToList();


        var pageList = new List<Dictionary<string, object>>();
        foreach (var idx in reachable.OrderBy(x => x))
        {
            if (!pageById.TryGetValue(idx, out var page)) continue;
            pageList.Add(DumpPage(page));
        }

        var doc = new Dictionary<string, object>
        {
            ["generatedFrom"] = root,
            ["npcCount"] = npcList.Count,
            ["pageTotal"] = pages.Count,
            ["reachablePageCount"] = pageList.Count,
            ["itemCount"] = itemList.Count,
            ["items"] = itemList,
            ["maps"] = mapList,
            ["npcs"] = npcList,
            ["pages"] = pageList
        };

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"[NpcGraphAudit] npcs={npcList.Count} active={npcList.Count(x => (bool)x["active"])} "
                          + $"pagesTotal={pages.Count} reachable={pageList.Count} -> {outPath}");
        return 0;
    }

    private static void Walk(NPCPage page, HashSet<int> seen)
    {
        var stack = new Stack<NPCPage>();
        stack.Push(page);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p == null || !seen.Add(p.Index)) continue;
            if (p.SuccessPage != null) stack.Push(p.SuccessPage);
            foreach (var b in p.Buttons)
                if (b.DestinationPage != null)
                    stack.Push(b.DestinationPage);
        }
    }

    private static Dictionary<string, object> DumpPage(NPCPage page)
    {
        return new Dictionary<string, object>
        {
            ["index"] = page.Index,
            ["desc"] = page.Description ?? "",
            ["type"] = page.DialogType.ToString(),
            ["say"] = page.Say ?? "",
            ["successPage"] = page.SuccessPage?.Index ?? -1,
            ["arguments"] = page.Arguments ?? "",
            ["currency"] = page.Currency?.Name ?? "",
            ["buttons"] = page.Buttons.OrderBy(b => b.ButtonID).Select(b => new Dictionary<string, object>
            {
                ["id"] = b.ButtonID,
                ["dest"] = b.DestinationPage?.Index ?? -1,
                ["destDesc"] = b.DestinationPage?.Description ?? "",
                ["destType"] = b.DestinationPage == null ? "NULL" : b.DestinationPage.DialogType.ToString(),
                ["destSay"] = b.DestinationPage?.Say ?? ""
            }).ToList(),
            ["actions"] = page.Actions.Select(a => new Dictionary<string, object>
            {
                ["type"] = a.ActionType.ToString(),
                ["s1"] = a.StringParameter1 ?? "",
                ["i1"] = a.IntParameter1,
                ["i2"] = a.IntParameter2,
                ["item"] = a.ItemParameter1?.ItemName ?? "",
                ["map"] = a.MapParameter1?.FileName ?? "",
                ["mapDesc"] = a.MapParameter1?.Description ?? "",
                ["stat"] = a.StatParameter1.ToString(),
                ["instance"] = a.InstanceParameter1?.Name ?? ""
            }).ToList(),
            ["checks"] = page.Checks.Select(c => new Dictionary<string, object>
            {
                ["type"] = c.CheckType.ToString(),
                ["op"] = c.Operator.ToString(),
                ["s1"] = c.StringParameter1 ?? "",
                ["i1"] = c.IntParameter1,
                ["i2"] = c.IntParameter2,
                ["stat"] = c.StatParameter1.ToString(),
                ["failPage"] = c.FailPage?.Index ?? -1,
                ["failSay"] = c.FailPage?.Say ?? "",
                ["item"] = c.ItemParameter1?.ItemName ?? ""
            }).ToList(),
            ["goods"] = page.Goods.OrderBy(g => g.Index).Select(g => new Dictionary<string, object>
            {
                ["index"] = g.Index,
                ["item"] = g.Item?.ItemName ?? "",
                ["itemIndex"] = g.Item?.Index ?? -1,
                ["itemType"] = g.Item?.ItemType.ToString() ?? "",
                ["goodsIndex"] = g.GoodsIndex,
                ["rate"] = (double)g.Rate,
                ["cost"] = g.Cost,
                ["isCurrencyGood"] = g.IsCurrencyGood
            }).ToList(),
            ["types"] = page.Types.Select(t => new Dictionary<string, object>
            {
                ["itemType"] = t.ItemType.ToString()
            }).ToList(),
            ["values"] = page.Values.Select(v => new Dictionary<string, object>
            {
                ["valueId"] = v.ValueID,
                ["valueType"] = v.ValueType.ToString(),
                ["dataCategory"] = v.DataCategory ?? "",
                ["dataType"] = v.DataType.ToString(),
                ["fieldType"] = v.FieldType.ToString()
            }).ToList()
        };
    }
}
