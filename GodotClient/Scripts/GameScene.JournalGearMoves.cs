using System;
using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private static bool JournalSameItem(ClientUserItem left, ClientUserItem right) =>
        ReferenceEquals(left, right) || (left != null && right != null && left.Index == right.Index && left.Slot == right.Slot);

    private DXItemCell JournalSourceCell(bool equipped, int slot, ClientUserItem item)
    {
        var cells = equipped ? EquipmentCells : InventoryCells;
        var items = equipped ? Equipment : Inventory;
        int cellIndex = equipped ? slot : (_inventoryDialog?.Grid?.ResolveSlotCell(slot) ?? slot);
        if ((uint)slot >= (uint)items.Length || (uint)cellIndex >= (uint)cells.Length ||
            !JournalSameItem(items[slot], item)) return null;
        var cell = cells[cellIndex];
        return cell?.Slot == slot && JournalSameItem(cell.Item, item) && JournalReadyCell(cell) &&
               _player?.Dead == false && !IsObserver && _net?.Connection?.Connected == true ? cell : null;
    }

    private static bool JournalReadyCell(DXItemCell cell) => cell != null && cell.IsEnabled && !cell.Locked &&
        !cell.ReadOnly && cell.LinkedSourceSlot < 0 && cell.Item?.Flags.HasFlag(UserItemFlags.Locked) != true;

    private DXItemCell JournalFreeBagCell(int slot) => InventoryCells.FirstOrDefault(cell =>
        JournalReadyCell(cell) && cell.Slot == slot && (uint)slot < (uint)Inventory.Length &&
        Inventory[slot] == null && cell.Item == null);

    internal IReadOnlyList<JournalAction> JournalItemActions(bool equipped, int slot, ClientUserItem item, bool secondary)
    {
        if (item?.Info == null) return Array.Empty<JournalAction>();
        if (JournalSourceCell(equipped, slot, item) == null)
            return new[] { new JournalAction("物品已变化、已锁定或当前无法操作") };
        if (!equipped && secondary) return JournalBeltChoices(slot, item);
        var actions = new List<JournalAction>();
        if (equipped)
        {
            var empty = InventoryCells.FirstOrDefault(c => JournalReadyCell(c) && c.Item == null &&
                (uint)c.Slot < (uint)Inventory.Length && Inventory[c.Slot] == null);
            if (empty == null) return new[] { new JournalAction("背包没有可用空格") };
            int dest = empty.Slot;
            actions.Add(new JournalAction($"卸下至背包格 {dest + 1}", Children: () => new[] {
                new JournalAction($"确认卸下：{item.Info.Local()}", () => {
                    var source = JournalSourceCell(true, slot, item);
                    var target = JournalFreeBagCell(dest);
                    if (source == null || target == null || IsFishingActive || IsTamingActive ||
                        source.Item.Flags.HasFlag(UserItemFlags.Marriage)) return "装备或目标格已变化，无法卸下。";
                    source.MoveItem(target);
                    return source.Locked ? "已请求卸下" : "无法卸下装备";
                }) }));
            return actions;
        }

        var choices = new List<JournalAction>();
        foreach (var cell in EquipmentCells)
        {
            if (cell == null || !Functions.CorrectSlot(item.Info.ItemType, (EquipmentSlot)cell.Slot)) continue;
            int dest = cell.Slot;
            var old = cell.Item;
            choices.Add(new JournalAction($"比较：{JournalSlotNames[dest]} · {old?.Info?.Local() ?? "空"}",
                Children: () => {
                    var rows = JournalComparisonRows(item, old);
                    rows.Add(new JournalAction($"确认装备至{JournalSlotNames[dest]}", () => {
                        var source = JournalSourceCell(false, slot, item);
                        var target = EquipmentCells.ElementAtOrDefault(dest);
                        if (source == null || !JournalReadyCell(target) || target.Slot != dest ||
                            !JournalSameItem(target.Item, old) || !JournalSameItem(Equipment.ElementAtOrDefault(dest), old))
                            return "物品或装备槽已变化，操作已取消。";
                        return target.ToEquipment(source) ? "已请求装备" : "当前条件下无法装备";
                    }));
                    return rows;
                }));
        }
        if (choices.Count > 0) actions.Add(new JournalAction("选择装备槽并比较", Children: () => choices));
        if (item.Info.ItemType is ItemType.Consumable or ItemType.Scroll or ItemType.CompanionFood or ItemType.ItemPart or ItemType.Book or ItemType.System or ItemType.Bundle or ItemType.LootBox)
            actions.Add(new JournalAction("使用", () =>
            {
                var source = JournalSourceCell(false, slot, item);
                if (source == null || source.Locked || source.ReadOnly || IsObserver || _net?.Connection == null || !CanUseItem(item))
                    return "物品状态已变化，无法使用。";
                return source.UseItem() ? null : "当前条件下无法使用该物品。";
            }));
        actions.Add(new JournalAction("绑定到腰带", Children: () => JournalBeltChoices(slot, item)));
        if (item.Info.CanDrop && !item.Flags.HasFlag(UserItemFlags.Locked))
        {
            long displayedCount = item.Count;
            actions.Add(new JournalAction("丢弃", Children: () =>
            {
                var one = new JournalAction($"确认丢弃 1 个：{item.Info.Local()}", () => JournalDropItem(slot, item, 1));
                if (displayedCount <= 1) return new[] { one };
                return new[] { one, new JournalAction($"确认丢弃整组（{displayedCount} 个）：{item.Info.Local()}",
                    () => JournalDropItem(slot, item, displayedCount, requireExactCount: true)) };
            }));
        }
        return actions;
    }

    private static List<JournalAction> JournalComparisonRows(ClientUserItem candidate, ClientUserItem current)
    {
        var rows = new List<JournalAction> { new($"当前：{current?.Info?.Local() ?? "空"}"), new($"候选：{candidate.Info.Local()}") };
        foreach (Stat stat in Enum.GetValues<Stat>())
        {
            int before = (current?.Info?.Stats?[stat] ?? 0) + (current?.AddedStats?[stat] ?? 0);
            int after = (candidate.Info.Stats?[stat] ?? 0) + (candidate.AddedStats?[stat] ?? 0);
            if (before != after) rows.Add(new JournalAction($"{stat}: {before} → {after} ({after-before:+#;-#;0})"));
        }
        if (rows.Count == 2) rows.Add(new JournalAction("单件属性无差异"));
        rows.Add(new JournalAction("仅比较单件属性，不含套装效果"));
        return rows;
    }

    private IReadOnlyList<JournalAction> JournalBeltChoices(int sourceSlot, ClientUserItem item)
    {
        var rows = new List<JournalAction>(BeltLinks.Length);
        foreach (var link in BeltLinks)
        {
            if (link == null) continue;
            int dest = link.Slot, info = link.LinkInfoIndex, instance = link.LinkItemIndex;
            bool replace = info > 0 || instance > 0;
            rows.Add(new JournalAction($"腰带 {dest + 1} · {(replace ? "替换链接" : "空")}", Children: () => new[] {
                new JournalAction($"确认{(replace ? "替换" : "绑定")}：{item.Info.Local()}", () => {
                    var source = JournalSourceCell(false, sourceSlot, item);
                    var live = BeltLinks.ElementAtOrDefault(dest);
                    if (source == null || live == null || live.Slot != dest || live.LinkInfoIndex != info || live.LinkItemIndex != instance)
                        return "物品或腰带链接已变化，操作已取消。";
                    live.LinkInfoIndex = source.Item.Info.ShouldLinkInfo ? source.Item.Info.Index : -1;
                    live.LinkItemIndex = source.Item.Info.ShouldLinkInfo ? -1 : source.Item.Index;
                    SendBeltLinkChanged(dest, live.LinkInfoIndex, live.LinkItemIndex);
                    _beltDialog?.UpdateLinks();
                    return "腰带链接已更新";
                }) }));
        }
        return rows;
    }

    private string JournalDropItem(int slot, ClientUserItem item, long count, bool requireExactCount = false)
    {
        var source = JournalSourceCell(false, slot, item);
        if (source == null || source.GridType != GridType.Inventory || source.Locked || source.ReadOnly ||
            !CanBeginItemDrop(source) || count < 1 || count > source.Item.Count ||
            (requireExactCount && count != source.Item.Count) || !source.Item.Info.CanDrop || source.Item.Flags.HasFlag(UserItemFlags.Locked))
            return "物品状态已变化，无法丢弃。";
        source.Locked = true;
        source.UpdateBorder();
        SendItemDrop(new CellLinkInfo { GridType = GridType.Inventory, Slot = source.Slot, Count = count });
        return "已请求丢弃";
    }
}
