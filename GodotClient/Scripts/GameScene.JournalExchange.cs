using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Controls;
using ServiceLine = ZirconClient.Controls.HandheldJournalService.Line;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private long _displayedTradeRevision;
    private bool _displayedTradeConfirm;
    private bool TradeLedgerChanged => _displayedTradeRevision != _tradeDialog.JournalRevision ||
        _displayedTradeConfirm != _tradeDialog.JournalConfirmEnabled;

    private bool JournalExchangeAvailable() => JournalWindowVisible(_tradeDialog) && _tradeDialog.JournalTrading && !_tradeDialog.JournalRequestVisible;

    internal bool TradePromptCurrent(TradeDialog trade, long revision) => trade == _tradeDialog && JournalExchangeAvailable()
        && trade.JournalRevision == revision && trade.JournalConfirmEnabled && !trade.JournalAwaitingAck
        && !_journalTradeCancelPending && !_observer && _net?.Connection?.Connected == true && _player?.Dead == false
        && DXItemCell.SelectedCell == null && _selectedCurrency == null;

    private void FillTradeLedger(List<ServiceLine> left, List<ServiceLine> right)
    {
        var trade = _tradeDialog;
        long revision = _displayedTradeRevision = trade.JournalRevision;
        bool unlocked = _displayedTradeConfirm = trade.JournalConfirmEnabled;
        bool Current() => _ledgerOwner == trade && JournalInputPane() == trade && TradePromptCurrent(trade, revision);
        const string stale = "交易内容或状态已变化，请重新核对。";
        bool ready = Current();
        long Balance() => Currencies.FirstOrDefault(c => c?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        long balance = Balance();
        left.Add(new("own-gold", $"金币：{trade.JournalGoldControl.Text}", $"可用 {balance:N0} · 选择金额", Act: ready && balance > 0 ? () =>
        {
            if (!Current() || Balance() != balance || !trade.JournalGoldControl.IsEnabled) return stale;
            trade.JournalGoldControl.TriggerClick();
            return "请在金额窗口中选择。";
        } : null));
        right.Add(new("other-gold", $"金币：{trade.JournalOtherGold}", "对方出价（只读）"));
        static void Offer(List<ServiceLine> rows, ClientUserItem item, int slot)
        {
            if (item?.Info == null) return;
            rows.Add(new($"offer:{slot}:{item.Index}", $"{item.Info.Local()} ×{item.Count:N0}", $"交易格 {slot + 1}",
                MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, DXItemCell.GetItemDrawIndex(item))));
        }
        for (int i = 0; i < trade.JournalOwnCells.Count; i++) Offer(left, trade.JournalOwnCells[i].Item, i);
        for (int i = 0; i < trade.JournalOtherItems.Count; i++) Offer(right, trade.JournalOtherItems[i], i);
        right.Add(new("partner-status", "对方确认状态", "客户端未提供此状态。"));
        left.Add(new("confirm", unlocked ? "确认当前交易" : "已提交确认", trade.JournalAwaitingAck ? "正在等待出价回执" : unlocked ? "请核对双方的物品及金币。" : "等待服务器回应", Act: ready ? () =>
        {
            if (!Current() || trade.JournalConfirm is not { IsEnabled: true, CanBePressed: true } confirm) return stale;
            confirm.TriggerClick();
            return "已提交确认";
        } : null));
        bool space = trade.JournalHasSpace;
        for (int i = 0; i < Inventory.Length; i++)
        {
            int slot = i;
            var item = Inventory[slot];
            if (item?.Info == null) continue;
            long count = item.Count;
            var source = JournalSourceCell(false, slot, item);
            bool eligible = source?.CanLinkToSpecialGrid(GridType.TradeUser) == true && space;
            left.Add(new($"add:{slot}:{item.Index}", $"背包 · {item.Info.Local()} ×{count:N0}",
                !eligible ? "当前不可添加" : count > 1 ? "切换原交易窗口选择数量" : "添加至我的出价",
                MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, DXItemCell.GetItemDrawIndex(item)), ready && eligible ? () =>
                {
                    if (!Current() || !trade.JournalHasSpace || !ReferenceEquals(Inventory[slot], item) || item.Count != count
                        || JournalSourceCell(false, slot, item) != source || !ReferenceEquals(source.Item, item)
                        || !source.CanLinkToSpecialGrid(GridType.TradeUser)) return stale;
                    if (count > 1)
                    {
                        // The original stack prompt captures a live cell, not the reviewed source.
                        // Hand off before starting that interaction; never submit a quantity on its behalf.
                        _ledgerLegacy = true;
                        _serviceBook.Hide();
                        WindowManager.Open(_inventoryDialog, _uiLayer);
                        SetJournalServiceFocus(_inventoryDialog, source);
                        return "请在原交易窗口添加并选择数量。";
                    }
                    return trade.TryRouteItem(source) ? "已请求添加物品" : stale;
                } : null));
        }
    }
}
