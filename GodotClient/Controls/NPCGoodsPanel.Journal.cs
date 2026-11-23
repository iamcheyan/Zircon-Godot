using System;
using Library.SystemModels;

namespace ZirconClient.Controls;

public partial class NPCDialog
{
    internal NPCGoodsPanel JournalGoods => _goods;
}

public partial class NPCGoodsPanel
{
    internal CurrencyInfo JournalCurrency => _currency;
    internal NPCGood JournalGood(int index) => (uint)index < (uint)_goods.Count ? _goods[index] : null;

    internal void JournalSelectGood(int index)
    {
        _scroll.Value = index * _rowHeight;
        int row = index - FirstVisibleRowIndex;
        if ((uint)row < (uint)_rows.Count) _rows[row].TriggerClick();
    }

    internal void JournalBuy(Func<bool> current)
    {
        if (_buy.IsEnabled && current()) BuySelected(current);
    }
}
