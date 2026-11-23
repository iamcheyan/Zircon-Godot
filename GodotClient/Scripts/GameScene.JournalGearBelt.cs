using System;
using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private const string JournalAutoPotionId = "belt:auto";

    private void JournalBeltEntries(List<JournalEntry> rows)
    {
        for (int slot = 0; slot < BeltLinks.Length; slot++)
        {
            string id = $"belt:{slot}";
            string label = $"腰带 {slot + 1}";
            var link = BeltLinks[slot];
            if (link == null || (link.LinkInfoIndex <= 0 && link.LinkItemIndex <= 0))
            {
                rows.Add(new(id, $"{label} · 空", "可从背包选择物品链接到此槽。"));
                continue;
            }

            var item = GetHandheldBeltItem(slot);
            if (item != null)
                rows.Add(JournalItemEntry(id, $"{label} · {item.Info.Local()}", $"×{GetHandheldBeltCount(slot)}"));
            else
            {
                var info = link.LinkInfoIndex > 0 ? Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == link.LinkInfoIndex) : null;
                rows.Add(new(id, $"{label} · {info?.Local() ?? "物品已耗尽"}", "没有可用物品；链接仍保留。", "×0"));
            }
        }
        rows.Add(new(JournalAutoPotionId, "自动喝药", "配置生命、魔法阈值和使用的药品。"));
    }

    private IReadOnlyList<JournalAction> JournalBeltActions(string id, bool secondary)
    {
        if (id == JournalAutoPotionId && !secondary && AutoPotionBox != null)
            return new JournalAction[] { new("配置自动喝药", () => { WindowManager.Open(AutoPotionBox, _uiLayer); return ""; }) };
        if (id == null || !id.StartsWith("belt:", StringComparison.Ordinal) || !int.TryParse(id.AsSpan(5), out int slot)
            || GetHandheldBeltLink(slot) is not { } link || (link.LinkInfoIndex <= 0 && link.LinkItemIndex <= 0))
            return Array.Empty<JournalAction>();

        int infoIndex = link.LinkInfoIndex, itemIndex = link.LinkItemIndex;
        bool StillLinked() => GetHandheldBeltLink(slot) is { } current && current.LinkInfoIndex == infoIndex && current.LinkItemIndex == itemIndex;
        var unlink = new JournalAction("解除链接", () =>
        {
            if (!StillLinked()) return "链接已更改";
            if (IsObserver || _net?.Connection?.Connected != true) return "当前无法解除链接";
            SendBeltLinkChanged(slot, -1, -1);
            link.LinkInfoIndex = link.LinkItemIndex = -1;
            if (_beltDialog?.Grid?.Cells?.ElementAtOrDefault(slot) is { } beltCell)
            {
                beltCell.QuickInfo = null;
                beltCell.QuickItem = null;
            }
            return "已解除链接";
        });
        if (secondary || GetHandheldBeltItem(slot) == null) return new[] { unlink };
        return new JournalAction[] { new("使用一个", () =>
        {
            if (!StillLinked()) return "链接已更改";
            if (IsObserver || _player?.Dead != false || _net?.Connection?.Connected != true) return "当前无法使用";
            var item = GetHandheldBeltItem(slot);
            if (item == null) return "物品已耗尽";
            int sourceSlot = Array.IndexOf(Inventory, item);
            var grid = _inventoryDialog?.Grid;
            var cell = sourceSlot >= 0
                ? grid?.Cells?.ElementAtOrDefault(grid.ResolveSlotCell(sourceSlot))
                : CompanionInventoryCells.FirstOrDefault(c => ReferenceEquals(c?.Item, item));
            if (!ReferenceEquals(cell?.Item, item) || cell.Locked) return "物品已移动或正在处理中";
            return cell.UseItem() ? "已请求使用" : "当前无法使用";
        }), unlink };
    }
}
