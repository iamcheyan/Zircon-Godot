using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>只读审计：NPC / NPCPage / NPCGood 现状。</summary>
internal static class ShopAuditor
{
    public static int Run(string root)
    {
        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly, typeof(MonsterInfo).Assembly,
            typeof(NPCInfo).Assembly);

        var itemName = session.GetCollection<ItemInfo>().Binding.ToDictionary(x => x.Index, x => x.ItemName);
        var pages = session.GetCollection<NPCPage>().Binding;
        var npcs = session.GetCollection<NPCInfo>().Binding;
        var goods = session.GetCollection<NPCGood>().Binding;

        Console.WriteLine($"NPCInfo={npcs.Count} NPCPage={pages.Count} NPCGood={goods.Count}");

        Console.WriteLine("=== 带入口的 NPC ===");
        foreach (var n in npcs.Where(x => x.EntryPage != null).OrderBy(x => x.Index))
            Console.WriteLine($"  {n.NPCName,-24} map={n.Region?.Map?.Description,-16} region='{n.Region?.ServerDescription}' entry='{n.EntryPage.Description}'");

        Console.WriteLine("=== 入口页的按钮链 ===");
        foreach (var n in npcs.Where(x => x.EntryPage != null).OrderBy(x => x.Index).Take(8))
        {
            var ep = n.EntryPage;
            Console.WriteLine($"{n.NPCName} -> '{ep.Description}' buttons={ep.Buttons.Count} goods={ep.Goods.Count}");
            foreach (var b in ep.Buttons)
                Console.WriteLine($"    btn id={b.ButtonID} -> page='{b.DestinationPage?.Description}'");
            foreach (var a in ep.Actions)
                Console.WriteLine($"    act {a.ActionType} page='{a.Page?.Description}'");
        }

        foreach (var page in pages.OrderBy(x => x.Index))
        {
            var pageGoods = goods.Where(g => g.Page == page).ToList();

            // NPCPage 自身不持有 NPC 反向引用；通过 NPCInfo.EntryPage/SuccessPage 间接归属
            var owner = npcs.FirstOrDefault(n => n.EntryPage == page);
            string ownerText = owner != null
                ? $"{owner.NPCName} map={owner.Region?.Map?.Description ?? "?"} region='{owner.Region?.ServerDescription ?? "?"}'"
                : "(无入口 NPC)";

            Console.WriteLine($"--- page '{page.Description}' owner={ownerText} dialog={page.DialogType} goods={pageGoods.Count} currency={page.Currency?.Name ?? "Gold"}");
            foreach (var g in pageGoods.OrderBy(g => g.Index))
            {
                string iname = g.Item != null ? itemName.GetValueOrDefault(g.Item.Index, "???") : "NULL!";
                Console.WriteLine($"    {iname,-40} rate={g.Rate} baseCost={g.BaseCost} idx={g.GoodsIndex}");
            }
        }
        return 0;
    }
}
