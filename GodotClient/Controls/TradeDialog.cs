using System;
using System.Collections.Generic;
using Godot;
using System.Linq;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 TradeDialog(Interface 125)：双方 5×2 物品区、金币、确认状态。</summary>
public partial class TradeDialog : DXWindow
{
    private readonly DXImageControl _background;
    private readonly DXButton _closeButton;
    private readonly DXItemGrid _playerGrid;
    private readonly DXItemGrid _userGrid;
    private readonly ClientUserItem[] _playerItems = new ClientUserItem[10];
    private readonly Dictionary<(GridType Grid, int Slot), ClientUserItem> _pendingSources = new();
    private readonly DXLabel _userGold;
    private readonly DXLabel _playerGold;
    private DXButton _confirm;

    public TradeDialog()
    {
        HasTitle = false; HasFooter = false; Movable = true; Size = new Vector2I(428, 244);
        _background = new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 125, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_background);
        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => CloseTrade(); AddControl(_closeButton);
        AddControl(new DXLabel { Text = Lang.TradeUi349Label, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(428, 18), IsControl = false });
        AddControl(new DXLabel { Text = Lang.TradeDialogUserLabel, FontSize = 11, TextColour = new Color(1f, .85f, .3f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(186, 20), Location = new Vector2I(15, 38), IsControl = false });
        AddControl(new DXLabel { Text = Lang.TradeDialogPlayerLabel, FontSize = 11, TextColour = new Color(1f, .85f, .3f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(186, 20), Location = new Vector2I(226, 38), IsControl = false });
        _userGrid = new DXItemGrid { GridSize = new Vector2I(5, 2), Location = new Vector2I(15, 73), GridType = GridType.TradeUser, Linked = true, GridPadding = 1, Border = false }; AddControl(_userGrid);
        foreach (var cell in _userGrid.Cells)
            cell.LinkChanged += linked =>
            {
                if (linked?.LinkedSourceSlot >= 0)
                {
                    var source = GetSourceCell(new CellLinkInfo { GridType = linked.LinkedSourceGrid, Slot = linked.LinkedSourceSlot });
                    if (source?.Item != null)
                        _pendingSources[(linked.LinkedSourceGrid, linked.LinkedSourceSlot)] = source.Item;
                    GameScene.Game?.SendTradeItem(new CellLinkInfo { GridType = linked.LinkedSourceGrid, Slot = linked.LinkedSourceSlot, Count = linked.Item?.Count ?? 1 });
                }
            };
        _playerGrid = new DXItemGrid { GridSize = new Vector2I(5, 2), Location = new Vector2I(226, 73), GridType = GridType.TradePlayer, ItemGrid = _playerItems, ReadOnly = true, GridPadding = 1, Border = false }; AddControl(_playerGrid);
        AddControl(new DXLabel { Text = Lang.TradeDialogGoldLabel, FontSize = 8, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(11, 168), Size = new Vector2I(58, 16), IsControl = false });
        AddControl(new DXLabel { Text = Lang.TradeDialogGoldLabel, FontSize = 8, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(222, 168), Size = new Vector2I(58, 16), IsControl = false });
        _userGold = AddValue("0", 75, 168); _playerGold = AddValue("0", 286, 168);
        _userGold.IsControl = true;
        _userGold.MouseClick += (o, e) =>
        {
            if (GameScene.Game?.IsObserver == true) return;
            long available = GameScene.Game?.Currencies?.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
            if (!CanOfferGold(available)) return;
            var dialog = new ItemAmountDialog(Lang.TradeGoldLabel, available, 1, amount =>
            {
                long current = GameScene.Game?.Currencies?.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
                if (CanOfferGold(current) && amount <= current)
                    GameScene.Game?.SendTradeGold(amount);
            });
            WindowManager.Open(dialog, GameScene.Game?.UILayer ?? GetParent());
        };
        _confirm = new DXButton { Text = Lang.TradeConfirmLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(126, 203), Size = new Vector2I(80, 25) };
        _confirm.MouseClick += (o, e) =>
        {
            if (GameScene.Game?.IsObserver == true) return;
            _confirm.Enabled = false;
            GameScene.Game?.SendTradeConfirm();
        }; AddControl(_confirm);
    }

    private DXLabel AddValue(string text, int x, int y)
    {
        var label = new DXLabel { Text = text, FontSize = 10, TextColour = Colors.White, DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Right, AutoSize = false, Size = new Vector2I(130, 16), Location = new Vector2I(x, y), IsControl = false }; AddControl(label); return label;
    }
    public void OpenTrade(string name) { Text = name; _confirm.Enabled = true; WindowManager.Open(this, GameScene.Game?.UILayer ?? GetParent()); }
    public void ShowRequest(string name)
    {
        var panel = new DXControl { Location = new Vector2I(12, 36), Size = new Vector2I(300, 72), BackColour = new Color(.05f, .03f, .02f, .98f), Border = true, BorderColour = new Color(1f, .75f, .25f) };
        panel.AddControl(new DXLabel { Text = $"{name ?? Lang.GroupUnknownLabel} 请求交易", Location = new Vector2I(8, 7), Size = new Vector2I(280, 20), IsControl = false });
        var yes = new DXButton { Text = Lang.GroupAcceptLabel, Location = new Vector2I(60, 38), Size = new Vector2I(70, 24), Index = -1 };
        yes.MouseClick += (o, e) => { GameScene.Game?.SendTradeRequestResponse(true); RemoveControl(panel); panel.QueueFree(); };
        var no = new DXButton { Text = Lang.GroupDeclineLabel, Location = new Vector2I(170, 38), Size = new Vector2I(70, 24), Index = -1 };
        no.MouseClick += (o, e) => { GameScene.Game?.SendTradeRequestResponse(false); RemoveControl(panel); panel.QueueFree(); };
        panel.AddControl(yes); panel.AddControl(no); AddControl(panel);
    }
    public void SetOtherItem(ClientUserItem item)
    {
        if (item == null) return;
        int slot = Array.FindIndex(_playerItems, x => x == null);
        if (slot < 0) return;
        _playerItems[slot] = item;
        _playerGrid.RefreshGrid();
    }
    /// <summary>原版 S.TradeAddGold：刷新本地玩家提交的金币。</summary>
    public void SetPlayerGold(long gold) => _userGold.Text = gold.ToString("#,##0");

    /// <summary>原版 S.TradeGoldAdded：刷新交易对方的金币。</summary>
    public void SetOtherGold(long gold) => _playerGold.Text = gold.ToString("#,##0");

    public bool AuditGoldRouting(out string details)
    {
        SetPlayerGold(1234);
        SetOtherGold(5678);
        details = $"player={_userGold.Text} other={_playerGold.Text}";
        bool valid = _userGold.Text == "1,234" && _playerGold.Text == "5,678";
        _userGold.Text = Lang.TradeGoldLabel2;
        _playerGold.Text = Lang.TradeGoldLabel2;
        return valid;
    }
    public static bool CanOfferGold(long amount) => amount > 0;

    /// <summary>旧版 EI F1050：484×330，双方各 5×6 格。</summary>
    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(484, 330);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 1050;
        _background.FixedSize = true;
        _background.StretchImage = false;
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 1050);
        // 与 InventoryDialog/CharacterDialog/GroupDialog/QuestDialog/MagicDialog/ConfigDialog
        // 相同的约定：把该帧 alpha 可见区原点对齐到窗口 (0,0)。
        // 素材实测 F1050 画布 512x512、alpha bbox (14,91)-(497,421)，故取 -(14,91)。
        _background.Location = new Vector2I(-14, -91);
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(456, 304);
        _closeButton.Size = new Vector2I(28, 26);
        // 证据给的是**首格**左上角（trade-window-render-evidence.json：
        // 左 (x+0x15,y+0x30)..(x+0xC9,y+0x108) = (21,48)-(201,264)，右 (253,48)-(397,264)，
        // cell 36x36、stride 36、5 列 x 6 行）。
        // 我方 DXItemGrid 的格子按 GridPadding 内缩，故首格 = 网格原点 + padding(1,1)，
        // 网格原点应为 (20,47) / (252,47)。原值 (14,91)/(246,91) 使首格落在 (15,92)/(247,92)，
        // 相对证据偏 (-6,+44)。
        _userGrid.GridSize = new Vector2I(5, 6);
        _userGrid.Location = new Vector2I(20, 47);
        _playerGrid.GridSize = new Vector2I(5, 6);
        _playerGrid.Location = new Vector2I(252, 47);
        _userGrid.RefreshGrid();
        _playerGrid.RefreshGrid();
        UpdateClientAreaForLegacySkin();
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(484, 330)
            && _background.LibraryFile == LibraryFile.GameInter && _background.Index == 1050
            && _userGrid.GridSize == new Vector2I(5, 6)
            && _playerGrid.GridSize == new Vector2I(5, 6)
            && _userGrid.Location == new Vector2(20, 47)
            && _playerGrid.Location == new Vector2(252, 47);
        // details 里同时给出首格（= 原点 + padding），便于与证据的 (21,48)/(253,48) 直接比对。
        details = $"size={Size} frame={_background.Index} bg={_background.Location} userGrid={_userGrid.GridSize}@{_userGrid.Location} playerGrid={_playerGrid.GridSize}@{_playerGrid.Location}";
        return ok;
    }
    public void Unlock() => _confirm.Enabled = true;
    public void ClearTrade()
    {
        for (int i = 0; i < _playerItems.Length; i++) _playerItems[i] = null;
        foreach (var cell in _userGrid.Cells ?? System.Array.Empty<DXItemCell>())
        {
            if (cell.LinkedSourceSlot >= 0)
            {
                var link = (cell.LinkedSourceGrid, cell.LinkedSourceSlot);
                var source = GetSourceCell(new CellLinkInfo { GridType = link.Item1, Slot = link.Item2 });
                if (!_pendingSources.TryGetValue(link, out var expected) || ShouldUnlockTradeSource(source?.Item, expected))
                    source?.UnlockForTrade();
                _pendingSources.Remove(link);
            }
            cell.Item = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
        }
        _playerGrid.RefreshGrid();
        _userGold.Text = "0";
        _playerGold.Text = "0";
        _confirm.Enabled = true;
        _pendingSources.Clear();
        WindowManager.Close(this);
    }

    public void ApplyTradeAddItem(Library.Network.ServerPackets.TradeAddItem packet)
    {
        var link = packet?.Cell;
        if (link == null) return;
        var source = GetSourceCell(link);
        var key = (link.GridType, link.Slot);
        _pendingSources.TryGetValue(key, out var expectedSource);
        var target = _userGrid.Cells?.FirstOrDefault(c => c.LinkedSourceGrid == link.GridType && c.LinkedSourceSlot == link.Slot);
        if (!packet.Success)
        {
            if (target != null)
            {
                target.Item = null;
                target.LinkedSourceGrid = GridType.None;
                target.LinkedSourceSlot = -1;
                target.LinkChanged?.Invoke(target);
            }
            if (expectedSource == null || ShouldUnlockTradeSource(source?.Item, expectedSource))
                source?.UnlockForTrade();
            _pendingSources.Remove(key);
            return;
        }

        if (expectedSource != null && !ShouldUnlockTradeSource(source?.Item, expectedSource))
        {
            _pendingSources.Remove(key);
            return;
        }
        if (source == null || target == null)
        {
            // 成功回包可能晚于窗口清理/重复回包；来源不能因找不到展示格而永久保持交易锁。
            if (expectedSource == null || ShouldUnlockTradeSource(source?.Item, expectedSource))
                source?.UnlockForTrade();
            _pendingSources.Remove(key);
            return;
        }
        DXItemCell.SetCellItem(target, new ClientUserItem(source.Item, Math.Clamp(link.Count, 1, source.Item.Count)));
        target.RefreshItem();
        source.Locked = true;
        source.UpdateBorder();
    }

    private static DXItemCell GetSourceCell(CellLinkInfo link)
    {
        var game = GameScene.Game;
        if (game == null || link == null) return null;
        var cells = link.GridType switch
        {
            GridType.Inventory => game.InventoryCells,
            GridType.Equipment => game.EquipmentCells,
            GridType.Storage => game.StorageCells,
            GridType.PartsStorage => game.PartsStorageCells,
            GridType.CompanionInventory => game.CompanionInventoryCells,
            GridType.CompanionEquipment => game.CompanionEquipmentCells,
            _ => System.Array.Empty<DXItemCell>(),
        };
        return link.Slot >= 0 && link.Slot < cells.Length ? cells[link.Slot] : null;
    }

    public bool TryRouteItem(DXItemCell source)
    {
        if (GameScene.Game?.IsObserver == true) return false;
        var target = _userGrid.Cells?.FirstOrDefault(c => c.Item == null && c.LinkedSourceSlot < 0);
        if (target == null || source?.Item == null || source.GridType is not (GridType.Inventory or GridType.Storage or GridType.PartsStorage or GridType.Equipment) ||
            source.Item.Flags.HasFlag(UserItemFlags.Marriage)) return false;
        source.MoveItem(target);
        return true;
    }
    public static bool ShouldUnlockTradeSource(ClientUserItem current, ClientUserItem expected)
        => ReferenceEquals(current, expected);
    // 原版 OnIsVisibleChanged：关闭窗口时清除链接，且仅非观察者且交易中才发 TradeClose。
    private void CloseTrade()
    {
        if (GameScene.Game?.IsObserver != true) GameScene.Game?.SendTradeClose();
        WindowManager.Close(this);
    }
}
