using System;
using System.Collections.Generic;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    internal void JournalGearEntries(int tab, int cat, List<JournalEntry> rows)
    {
        if (tab == 1)
        {
            JournalCharacterEntries(cat, rows);
            return;
        }
        if (tab != 0) return;
        if (cat == 5)
        {
            JournalBeltEntries(rows);
            return;
        }

        var grid = _inventoryDialog?.Grid;
        if (cat is 0 or 1)
        {
            for (int slot = 0; slot < Equipment.Length; slot++)
            {
                var worn = Equipment[slot];
                if (worn?.Info == null) continue;
                string position = slot < JournalSlotNames.Length ? JournalSlotNames[slot] : $"装备位 {slot + 1}";
                var equipped = JournalItemEntry($"equipped:{slot}:{worn.Index}", worn.Info.Local(), "已穿");
                if (grid?.UseLegacyFootprints == true)
                {
                    DXItemGrid.GetLegacyFootprint(worn, out int width, out int height);
                    equipped = equipped with { Width = width, Height = height };
                }
                rows.Add(equipped with { Detail = $"已穿戴 · {position}。A 更换装备，X 卸下；此物品不占背包容量。" });
            }
        }
        for (int slot = 0; slot < Inventory.Length; slot++)
        {
            var item = Inventory[slot];
            if (item?.Info == null || Array.Exists(Equipment, worn => worn != null &&
                    (ReferenceEquals(worn, item) || worn.Index > 0 && worn.Index == item.Index))
                || (cat != 0 && JournalInventoryCategory(item) != cat)) continue;
            var row = JournalItemEntry(JournalGearId(slot, item), item.Info.Local(), $"×{item.Count:N0}");
            if (grid?.UseLegacyFootprints == true)
            {
                // Size belongs to the PC inventory artwork, not to its current
                // placement. A full or not-yet-rebuilt hidden grid has no anchor
                // for late records; that must never turn a coat into a 1×1 icon.
                DXItemGrid.GetLegacyFootprint(item, out int width, out int height);
                row = row with { Width = width, Height = height };
            }
            rows.Add(row with { Detail = $"原始槽位 {slot + 1} · {row.Width}×{row.Height}" });
        }
    }

    internal IReadOnlyList<JournalAction> JournalGearActions(int tab, int cat, string id, bool secondary)
    {
        if (tab == 1) return JournalCharacterActions(cat, id, secondary);
        if (tab != 0) return Array.Empty<JournalAction>();
        if (cat == 5) return JournalBeltActions(id, secondary);
        if (TryJournalEquippedItem(id, out int equippedSlot, out var equipped))
            return secondary ? JournalItemActions(true, equippedSlot, equipped, false) : JournalEquipmentCandidates(equippedSlot);
        return TryJournalInventoryItem(id, out int slot, out var item)
            ? JournalItemActions(false, slot, item, secondary)
            : Array.Empty<JournalAction>();
    }

    internal int JournalInventoryColumns => Math.Max(1, _inventoryDialog?.Grid?.GridSize.X ?? 1);

    internal int JournalInventoryDisplayCell(string id) => TryJournalInventoryItem(id, out int slot, out _)
        ? _inventoryDialog?.Grid?.ResolveSlotCell(slot) ?? slot : -1;

    private bool TryJournalInventoryItem(string id, out int slot, out ClientUserItem item)
    {
        slot = -1;
        item = null;
        int separator = id?.IndexOf(':') ?? -1;
        if (separator <= 0 || !int.TryParse(id.AsSpan(0, separator), out slot) || (uint)slot >= Inventory.Length)
            return false;
        item = Inventory[slot];
        return item?.Info != null && int.TryParse(id.AsSpan(separator + 1), out int index) && item.Index == index;
    }

    private bool TryJournalEquippedItem(string id, out int slot, out ClientUserItem item)
    {
        slot = -1; item = null;
        if (id == null || !id.StartsWith("equipped:", StringComparison.Ordinal)) return false;
        string[] fields = id.Split(':');
        if (fields.Length != 3 || !int.TryParse(fields[1], out slot) || (uint)slot >= Equipment.Length
            || !int.TryParse(fields[2], out int index)) return false;
        item = Equipment[slot];
        return item?.Info != null && item.Index == index;
    }

    private static string JournalGearId(int slot, ClientUserItem item) => $"{slot}:{item.Index}";

    private static JournalEntry JournalItemEntry(string id, string name, string badge) =>
        new(id, name, string.Empty, badge);

    internal JournalEntry JournalGearPresentation(int tab, int cat, JournalEntry row, bool includeDetail)
    {
        ClientUserItem item = null;
        bool inInventory = tab == 0 && cat != 5;
        if (inInventory)
        {
            if (!TryJournalEquippedItem(row.Id, out _, out item) && !TryJournalInventoryItem(row.Id, out _, out item)) return row;
        }
        else if (tab == 0 && cat == 5 && row.Id.StartsWith("belt:", StringComparison.Ordinal)
                 && int.TryParse(row.Id.AsSpan(5), out int beltSlot))
            item = GetHandheldBeltItem(beltSlot);
        else if (tab == 1 && cat == 0)
        {
            int separator = row.Id.IndexOf(':');
            if (separator > 0 && int.TryParse(row.Id.AsSpan(0, separator), out int slot) && (uint)slot < Equipment.Length
                && int.TryParse(row.Id.AsSpan(separator + 1), out int index) && Equipment[slot]?.Index == index)
                item = Equipment[slot];
        }
        if (item?.Info == null) return row;

        int frame = DXItemCell.GetItemDrawIndex(item);
        var icon = inInventory && _inventoryDialog?.Grid?.UseLegacyFootprints == true
            ? MirSkin.GetTexture(LibraryFile.Inventory, frame) : null;
        icon ??= MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, frame)
            ?? MirSkin.GetTexture(LibraryFile.Inventory, frame);
        if (!includeDetail) return row with { Icon = icon };
        string detail = BuildItemHoverFull(item, applyColours: false);
        return row with { Icon = icon, Detail = string.IsNullOrEmpty(row.Detail) ? detail : row.Detail + "\n" + detail };
    }

    private static int JournalInventoryCategory(ClientUserItem item)
    {
        if ((item.Flags & UserItemFlags.QuestItem) != 0) return 4;
        return item.Info.ItemType switch
        {
            ItemType.Weapon or ItemType.Armour or ItemType.Torch or ItemType.Helmet or ItemType.Necklace
                or ItemType.Bracelet or ItemType.Ring or ItemType.Shoes or ItemType.Poison or ItemType.Amulet
                or ItemType.DarkStone or ItemType.Flower or ItemType.HorseArmour or ItemType.Emblem
                or ItemType.Shield or ItemType.Costume or ItemType.Hook or ItemType.Float or ItemType.Bait
                or ItemType.Finder or ItemType.Reel or ItemType.CompanionBag or ItemType.CompanionHead
                or ItemType.CompanionBack => 1,
            ItemType.Consumable or ItemType.Scroll or ItemType.Book or ItemType.CompanionFood
                or ItemType.Bundle or ItemType.LootBox or ItemType.System => 2,
            _ => 3,
        };
    }
}
