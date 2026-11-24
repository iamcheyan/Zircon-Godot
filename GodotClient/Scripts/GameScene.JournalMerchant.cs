using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Controls;
using ServiceLine = ZirconClient.Controls.HandheldJournalService.Line;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private bool JournalMerchantAvailable() => JournalWindowVisible(_npcDialog)
        && _npcDialog.HandheldPageValid && _npcDialog.CurrentPageDialogType == NPCDialogType.BuySell
        && _npcDialog.JournalGoods.IsVisibleInTree();

    private void FillShopLedger(List<ServiceLine> left, List<ServiceLine> right)
    {
        if (!JournalMerchantAvailable()) return;
        var dialog = _npcDialog;
        var goods = dialog.JournalGoods;
        var npc = _npcObjectId;
        int revision = dialog.PageShowCount;
        bool Current() => JournalMerchantAvailable() && JournalServiceRoot == dialog && _npcDialog == dialog
            && dialog.PageShowCount == revision && npc != 0 && _npcObjectId == npc && _objects.ContainsKey(npc)
            && !_observer && _player?.Dead == false && _net?.Connection?.Connected == true;
        const string changed = "交易内容已变更，请重新选择。";
        var currency = goods.JournalCurrency;
        string unit = currency?.Name ?? "金币";
        long balance = Currencies.FirstOrDefault(x => x?.Info?.Type == (currency?.Type ?? CurrencyType.Gold))?.Amount ?? 0;
        FillHandheldNpcReplies(left, dialog, revision);
        left.Add(new("balance", unit, $"持有 {balance:N0}"));
        for (int i = 0; i < goods.GoodCount; i++)
        {
            int index = i;
            var good = goods.JournalGood(index);
            bool selected = goods.SelectedIndex == index;
            left.Add(new($"good:{good.Index}", good.Item.Local(), $"{good.CostFor(currency, 1):N0} {unit} · {(selected ? "购买" : "选择")}",
                MirSkin.GetTexture(LibraryFile.StoreItem, good.Item.Image), () =>
                {
                    bool Valid() => Current() && goods.JournalGood(index) == good && goods.SelectedIndex == index;
                    if (!Current() || goods.JournalGood(index) != good || (goods.SelectedIndex == index) != selected) return changed;
                    if (selected) goods.JournalBuy(Valid);
                    else goods.JournalSelectGood(index);
                    return "";
                }));
        }

        var bag = _inventoryDialog;
        bool Selling() => Current() && _inventoryDialog == bag && JournalWindowVisible(bag) && bag.IsSellMode
            && DXItemCell.SelectedCell == null && _selectedCurrency == null;
        if (!JournalWindowVisible(bag) || !bag.IsSellMode)
        {
            right.Add(new("no-sale", "不可出售", "此商店未开启背包出售模式。"));
            return;
        }
        foreach (var cell in bag.Grid.Cells)
        {
            var item = cell?.Item;
            if (item?.Info == null) continue;
            int slot = cell.Slot;
            if (JournalSourceCell(false, slot, item) != cell) continue;
            long count = item.Count;
            bool picked = cell.SaleSelected;
            right.Add(new($"bag:{slot}", item.Info.Local(), $"×{count:N0} · {(picked ? "已选" : "选择出售")}",
                MirSkin.GetTexture(LibraryFile.StoreItem, item.Info.Image), () =>
                {
                    if (!Selling() || JournalSourceCell(false, slot, item) != cell
                        || cell.Item != item || item.Count != count || cell.SaleSelected != picked || cell.Locked || cell.ReadOnly) return changed;
                    bag.TrySelectForSale(cell);
                    return "";
                }));
        }
        if (bag.SelectedItems.Count == 0) return; // The native button means sell ALL when nothing is selected.
        var selection = bag.SelectedItems.Select(c => (Cell: c, Item: c.Item, Count: c.Item?.Count)).ToArray();
        string total = bag.GgLabel.Text;
        bool CanSubmit() => Selling() && bag.SellButton.IsEnabled && bag.SellButton.IsVisibleInTree()
            && bag.SelectedItems.Count == selection.Length && bag.GgLabel.Text == total
            && selection.All(s => s.Item != null && !s.Cell.Locked && s.Cell.SaleSelected && s.Cell.Item == s.Item
                && s.Item.Count == s.Count && bag.SelectedItems.Contains(s.Cell));
        right.Insert(0, new("sell", "出售所选", $"{selection.Length} 格 · {total} {unit}", Act: () =>
        {
            if (!CanSubmit()) return changed;
            WindowManager.Open(new ConfirmDialog($"以 {total} {unit} 出售所选物品？", "出售", () =>
            {
                if (CanSubmit()) bag.SellButton.TriggerClick();
            }), UILayer);
            return "";
        }));
    }
}
