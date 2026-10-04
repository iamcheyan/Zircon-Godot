using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 阶段二/三前置：恢复被 dbeditor 工作区回写覆盖掉的消耗品/技能书/护身符/肉食。
/// 来源：10-03 清洗后快照（含全部 326 件纯净物品），逐条复制标量字段，
/// 不复制 ItemStats 与 Drops（避免重新引入指向已删怪物的掉落规则）。
/// 用法: RestoreClassicItems <SourceRoot> <TargetRoot>
/// </summary>
internal static class RestoreClassicItems
{
    public static int Run(string sourceRoot, string targetRoot)
    {
        var source = new Session(SessionMode.System, sourceRoot, sourceRoot + "Backup/");
        source.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly);
        var target = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        target.Initialize(typeof(MagicInfo).Assembly, typeof(ItemInfo).Assembly);

        var srcItems = source.GetCollection<ItemInfo>().Binding;
        var dstItems = target.GetCollection<ItemInfo>();
        var existing = dstItems.Binding.Select(x => x.ItemName).ToHashSet();

        // 白名单判据（与 /tmp/restore_items.json 相同的规则，内联以保证工具自洽）
        var classicMagics = ClassicMagicPurity.ClassicSkillsTable()
            .Select(s => s.Magic.ToString().Replace(" ", "")).ToHashSet();
        static string Norm(string s) => s.Replace(" ", "");

        int restored = 0;
        foreach (var src in srcItems)
        {
            string name = src.ItemName;
            if (string.IsNullOrEmpty(name) || existing.Contains(name)) continue;
            if (src.RequiredClass == RequiredClass.Assassin) continue;

            bool isBook = src.ItemType == ItemType.Book &&
                (classicMagics.Contains(Norm(name)) || name == "Taoist Combat Kick");
            bool isPotion = src.ItemType == ItemType.Consumable &&
                (name.StartsWith("Healing Potion") || name.StartsWith("Mana Potion") ||
                 name.StartsWith("Life Pill") || name.StartsWith("Mana Pill") ||
                 name == "Rejuvenation Potion (II)" || name.Contains("Repair Oil") ||
                 name == "Pill Of Reincarnation");
            bool isTalisman = src.ItemType == ItemType.Amulet && name.StartsWith("Talisman");
            bool isMeat = src.ItemType == ItemType.Meat;
            bool isLight = src.ItemType == ItemType.Torch &&
                (name == "Bright Candle" || name == "Bright Torch");

            if (!(isBook || isPotion || isTalisman || isMeat || isLight)) continue;

            var dst = dstItems.CreateNewObject();
            dst.ItemName = src.ItemName;
            // 书籍 Shape 指向 MagicInfo.Index；若源库与目标库的魔法索引不同，则重映射
            if (isBook)
            {
                var magicInfo = target.GetCollection<MagicInfo>().Binding
                    .FirstOrDefault(m => Norm(m.Name) == Norm(src.ItemName));
                if (magicInfo != null && magicInfo.School != MagicSchool.None)
                    dst.Shape = magicInfo.Index;
                else if (name == "Taoist Combat Kick")
                {
                    var kick = target.GetCollection<MagicInfo>().Binding
                        .FirstOrDefault(m => Norm(m.Name) == "CombatKick");
                    dst.Shape = kick?.Index ?? src.Shape;
                }
                else
                    dst.Shape = src.Shape;
            }
            dst.ItemType = src.ItemType;
            dst.RequiredClass = src.RequiredClass;
            dst.RequiredGender = src.RequiredGender;
            dst.RequiredType = src.RequiredType;
            dst.RequiredAmount = src.RequiredAmount;
            dst.ItemEffect = src.ItemEffect;
            dst.ExteriorEffect = src.ExteriorEffect;
            dst.Weight = src.Weight;
            dst.StartItem = src.StartItem;
            dst.CanStore = src.CanStore;
            dst.CanTrade = src.CanTrade;
            dst.CanDeathDrop = src.CanDeathDrop;
            dst.CanAutoPot = src.CanAutoPot;
            dst.BuffIcon = src.BuffIcon;
            dst.Description = src.Description;
            dst.Image = src.Image;
            dst.Durability = src.Durability;
            dst.Price = src.Price;
            dst.StackSize = src.StackSize;
            dst.PartCount = src.PartCount;
            dst.SellRate = src.SellRate;
            dst.CanRepair = src.CanRepair;
            dst.CanSell = src.CanSell;
            dst.CanDrop = src.CanDrop;

            restored++;
            Console.WriteLine($"  [恢复] #{dst.Index,-4} {name,-32} type={src.ItemType} price={src.Price}");
        }

        target.Save(true);
        Console.WriteLine($"恢复完成: +{restored} 件，当前 ItemInfo 总数 {dstItems.Count}");
        return 0;
    }
}
