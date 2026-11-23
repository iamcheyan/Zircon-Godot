using System.Collections.Generic;
using System.Linq;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

public partial class TradeDialog
{
    internal long JournalRevision { get; private set; }
    internal bool JournalTrading { get; private set; }
    internal bool JournalRequestVisible => _requestPanel?.IsVisibleInTree() == true;
    internal bool JournalConfirmEnabled => _confirm.Enabled;
    internal DXButton JournalConfirm => _legacyEiLayout ? _legacyAccept : _confirm;
    internal DXLabel JournalGoldControl => _userGold;
    internal string JournalOtherGold => _playerGold.Text;
    internal IReadOnlyList<ClientUserItem> JournalOtherItems => _playerItems;
    internal IReadOnlyList<DXItemCell> JournalOwnCells => _userGrid.Cells;
    internal bool JournalHasSpace => _userGrid.Cells.Any(c => c.Item == null && c.LinkedSourceSlot < 0);
    private readonly HashSet<(GridType, int)> _awaitingTradeAck = new();
    private bool _awaitingGoldAck;
    internal bool JournalAwaitingAck => _awaitingGoldAck || _awaitingTradeAck.Count != 0;

    private void BindTradeSources()
    {
        foreach (var cell in _userGrid.Cells)
        {
            cell.LinkChanged -= TradeSourceChanged;
            cell.LinkChanged += TradeSourceChanged;
        }
    }

    private void TradeSourceChanged(DXItemCell linked)
    {
        JournalRevision++;
        if (linked == null || linked.LinkedSourceSlot < 0) return;
        var link = new CellLinkInfo { GridType = linked.LinkedSourceGrid, Slot = linked.LinkedSourceSlot, Count = linked.Item?.Count ?? 1 };
        var source = GetSourceCell(link);
        if (source?.Item == null) return;
        _pendingSources[(link.GridType, link.Slot)] = source.Item;
        _awaitingTradeAck.Add((link.GridType, link.Slot));
        GameScene.Game?.SendTradeItem(link);
    }
}
