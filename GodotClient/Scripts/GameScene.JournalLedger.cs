using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;
using ServiceLine = ZirconClient.Controls.HandheldJournalService.Line;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private HandheldJournalService _serviceBook;
    private bool JournalNativeServiceVisible => _serviceBook?.Visible == true;
    private DXWindow _ledgerOwner;
    private int _ledgerPage, _ledgerStamp;
    private bool _ledgerLegacy;
    private ulong _ledgerNextScan;

    private void SyncServiceBook()
    {
        var root = JournalServiceRoot;
        int page = root != null && root == _npcDialog ? _npcDialog.PageShowCount : 0;
        bool changed = root != _ledgerOwner || page != _ledgerPage;
        if (changed) { _ledgerOwner = root; _ledgerPage = page; _ledgerLegacy = false; }
        if (root == null || _ledgerLegacy || HandheldTextEditing || _observer || _player?.Dead != false
            || _net?.Connection?.Connected != true || JournalInputPane() != root
            || (root == _tradeDialog && !JournalExchangeAvailable())
            || DXItemCell.SelectedCell != null || _selectedCurrency != null
            || (root != _tradeDialog && root != _storageDialog && (root != _npcDialog || !JournalMerchantAvailable())))
        {
            if (_serviceBook?.Visible == true) _serviceBook.Hide();
            return;
        }
        bool storage = root == _storageDialog, trade = root == _tradeDialog;
        if (_serviceBook == null)
        {
            var layer = new CanvasLayer { Layer = 98 };
            _serviceBook = new HandheldJournalService { Visible = false };
            AddChild(layer); layer.AddChild(_serviceBook);
        }
        bool opening = changed || !_serviceBook.Visible;
        if (opening)
        {
            SetJournalServiceFocus(null, null);
            _serviceBook.OpenBook(storage ? "仓库 · 存取簿" : trade ? $"交易 · {_tradeDialog.Text}" : "商店 · 货簿",
                storage ? "我的背包" : trade ? "我的出价" : "商店货品", storage ? "仓库存货" : trade ? "对方出价" : "我的物品");
        }
        ulong now = Godot.Time.GetTicksMsec();
        if (!opening && now < _ledgerNextScan) return;
        _ledgerNextScan = now + 180;
        var stamp = new HashCode();
        StampLedgerCells(ref stamp, InventoryCells);
        stamp.Add(InSafeZone);
        if (storage) { stamp.Add(StorageSize); StampLedgerCells(ref stamp, StorageCells); StampLedgerCells(ref stamp, PartsStorageCells); stamp.Add(_storageDialog.PartGrid.Visible); }
        else foreach (var currency in Currencies) stamp.Add(currency?.Amount);
        int value = stamp.ToHashCode();
        if (!opening && _ledgerStamp == value && !(trade && TradeLedgerChanged)) return;
        _ledgerStamp = value;
        RefreshServiceBook();
    }

    private static void StampLedgerCells(ref HashCode stamp, DXItemCell[] cells)
    {
        if (cells == null) return;
        foreach (var c in cells)
        {
            if (c?.Item == null) continue;
            stamp.Add(c.Slot); stamp.Add(c.Item.Index); stamp.Add(c.Item.Count); stamp.Add(c.Locked); stamp.Add(c.Item.Flags);
        }
    }

    private void RefreshServiceBook()
    {
        var cols = _serviceBook.Columns;
        string left = _serviceBook.CursorKey(0), right = _serviceBook.CursorKey(1);
        cols[0].Clear(); cols[1].Clear();
        if (_ledgerOwner == _tradeDialog) FillTradeLedger(cols[0],cols[1]);
        else if (_ledgerOwner == _storageDialog)
        {
            var grid = _storageDialog.PartGrid.Visible ? _storageDialog.PartGrid : _storageDialog.Grid;
            int capacity = grid == _storageDialog.Grid ? StorageSize : PartsStorage.Length;
            cols[1].Add(new("capacity", grid == _storageDialog.Grid ? "普通仓库" : "部件仓库", $"当前容量 {capacity} 格 · 转移整叠；指定槽位请使用原窗口"));
            FillStorageColumn(cols[0], _inventoryDialog.Grid, grid, true);
            FillStorageColumn(cols[1], grid, _inventoryDialog.Grid, false);
        }
        else FillShopLedger(cols[0],cols[1]);
        _serviceBook.RestoreSelection(0,left); _serviceBook.RestoreSelection(1,right); _serviceBook.QueueRedraw();
    }

    private void FillStorageColumn(List<ServiceLine> rows, DXItemGrid from, DXItemGrid to, bool deposit)
    {
        foreach (var cell in from.Cells)
        {
            if (cell?.Item?.Info == null || !cell.Enabled) continue;
            var item = cell.Item;
            int slot = cell.Slot; long count = item.Count; uint npc = _journalStorageNpc;
            if (deposit && JournalSourceCell(false, slot, item) != cell) continue;
            bool eligible = !cell.Locked && !cell.ReadOnly && cell.LinkedSourceSlot < 0 && (!deposit ||
                (to.GridType == GridType.PartsStorage
                ? DXItemCell.CanStoreInPartsStorage(InSafeZone,item.Flags.HasFlag(UserItemFlags.Marriage),item.Info.ItemEffect == ItemEffect.ItemPart)
                : DXItemCell.CanStoreInStorage(InSafeZone,item.Flags.HasFlag(UserItemFlags.Marriage),item.Info.ItemEffect == ItemEffect.ItemPart,item.Info.CanStore)));
            rows.Add(new($"{from.GridType}:{slot}:{item.Index}", $"{item.Info.Local()} ×{count}",
                $"第 {slot+1} 格 · " + (eligible ? deposit ? "存入仓库" : "取回背包" : "当前不可转移"),
                MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, DXItemCell.GetItemDrawIndex(item)), eligible ? () =>
                {
                    if (npc == 0 || npc != _journalStorageNpc || JournalInputPane() != _storageDialog || !IsInstanceValid(cell)
                        || cell.IsQueuedForDeletion() || !ReferenceEquals(cell.Item,item) || cell.Item.Count != count
                        || cell.ReadOnly || !cell.Enabled || cell.Locked || !from.IsAncestorOf(cell)
                        || (deposit ? !to.Visible : !from.Visible)
                        || DXItemCell.SelectedCell != null || _selectedCurrency != null) return "物品或服务状态已改变";
                    return cell.MoveItem(to) ? "已提交，等待服务器回应" : "无法转移：请检查容量、锁定及存取条件";
                } : null));
        }
    }

    private bool HandleServiceBookKey(Key code, bool pressed, bool echo)
    {
        if (_serviceBook?.Visible != true) return false;
        if (!pressed || (echo && code is not (Key.Up or Key.Down))) return true;
        switch (code)
        {
            case Key.Up: _serviceBook.Move(-1,-1); break;
            case Key.Down: _serviceBook.Move(-1,1); break;
            case Key.Left: _serviceBook.Move(0,0); break;
            case Key.Right: _serviceBook.Move(1,0); break;
            case Key.Q: _serviceBook.Move(-1,-HandheldJournalService.PageSize); break;
            case Key.E: _serviceBook.Move(-1,HandheldJournalService.PageSize); break;
            case Key.F16: _ledgerLegacy = true; _serviceBook.Hide(); break;
            case Key.Escape:
                if (!_serviceBook.Back())
                {
                    if (_ledgerOwner == _storageDialog) ClearJournalNpcStorageContext();
                    else if (_ledgerOwner == _tradeDialog) RequestJournalTradeCancellation();
                    else CloseNPCDialog();
                    SyncServiceBook();
                }
                break;
            case Key.Enter: case Key.KpEnter:
                if (_journalTradeCancelPending) break;
                RefreshServiceBook(); _serviceBook.Confirm(); SyncServiceBook();
                if (_serviceBook.Visible) RefreshServiceBook();
                break;
        }
        return true;
    }
}
