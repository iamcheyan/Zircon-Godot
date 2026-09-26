using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 NPCGoodsDialog：227px 商品列表、7 行可视区域、滚动与购买。</summary>
public partial class NPCGoodsPanel : DXControl
{
    private readonly DXControl _list;
    private readonly DXVScrollBar _scroll;
    private readonly List<DXButton> _rows = new();
    private readonly List<NPCGood> _goods = new();
    private readonly DXButton _buy;
    private readonly DXButton _sell;
    private readonly DXCheckButton _guildFunds;
    private readonly LegacyWindowFrame _frame;
    private readonly List<CellLinkInfo> _sellLinks = new();
    private readonly List<CellLinkInfo> _pendingSellLinks = new();
    private readonly HashSet<ItemType> _sellableTypes = new();
    private bool _hasSellableTypes;
    private CurrencyInfo _currency;
    private int _selected = -1;

    // ── 旧版 EI 商店窗（窗口 id2）几何 ───────────────────────────────────────
    // 证据：store-window-render-evidence.json / store-window-content-verification-evidence.json
    //   · 根 = GameInter F1000、(0,0)、300x304（state0 BUY / state3 CRAFT 共用 F1000）
    //   · 素材 F1000 画布 512x512、alpha 可见区 (106,102)-(405,408) -> 背景锚点 -(106,102)
    //   · 购买列表 5 行，rowsY 40/86/132/178/224 -> 行距 46、首行 y=40
    //   · 控件（window-control-position-analysis.json，geometric_status=inside-window）：
    //       close   帧 1010/1011 @ (window.x+266, window.y+270) 28x26
    //       confirm 帧 1012/1013 @ (window.x+127, window.y+267) 48x20
    //   · 我方现代布局是 245x402 / 行距 43 / 首行 y=38 / 用 LegacyWindowFrame(Interface 素材)
    // 未在证据中的项（不臆测，仅取能塞进 300 宽且不与 close 重叠的值）：
    //   · 列表的 x 与宽度（证据只给 rowsY）；此处用 x=4、宽 250
    //   · 26 个槽位 +0x660 stride 0x24=36 的列分布（证据只给 stride，未给列 x）
    private bool _legacyEiLayout;
    private int _rowHeight = 43;   // 原版 NPCGoodsDialog 行距
    private int _rowInsetY = 1;    // 首行在列表内的 y 偏移
    private DXImageControl _legacyBackground;

    public NPCGoodsPanel()
    {
        Size = new Vector2I(245, 402);
        _frame = new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = true };
        AddControl(_frame);
        // legacy 背景（默认隐藏；ApplyLegacyEiLayout 时替换掉 _frame 的 Interface 外框）。
        _legacyBackground = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 1000,
            FixedSize = true,
            StretchImage = false,
            Size = new Vector2I(300, 307),
            Location = new Vector2I(-106, -102),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_legacyBackground);
        AddControl(new DXLabel { Text = "商品", FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(245, 18), IsControl = false });
        _list = new DXControl { Location = new Vector2I(9, 37), Size = new Vector2I(227, 302), Clip = true }; AddControl(_list);
        _scroll = new DXVScrollBar { Location = new Vector2I(217, 38), Size = new Vector2I(19, 301), VisibleSize = 302, Change = 43, HideWhenNoScroll = true };
        _scroll.UpButton.LibraryFile = LibraryFile.Interface; _scroll.UpButton.Index = 61;
        _scroll.DownButton.LibraryFile = LibraryFile.Interface; _scroll.DownButton.Index = 62;
        _scroll.PositionBar.LibraryFile = LibraryFile.Interface; _scroll.PositionBar.Index = 60;
        _scroll.ValueChanged += (o, e) => RefreshRows(); AddControl(_scroll);
        _buy = new DXButton { Text = "购买", Type = DXButton.ButtonType.Default, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(40, 359), Size = new Vector2I(80, 25), Enabled = false };
        _buy.MouseClick += (o, e) => BuySelected(); AddControl(_buy);
        _guildFunds = new DXCheckButton("使用行会资金:") { Location = new Vector2I(120, 363), Size = new Vector2I(110, 19), FontSize = 9, Enabled = false }; AddControl(_guildFunds);
        // 原版 BuySell 页的出售状态由独立 InventoryDialog.SellMode 负责；
        // 这里保留内部链接逻辑作为旧调用兼容，但不在商品面板绘制出售按钮。
        _sell = new DXButton
        {
            Text = "出售所选", FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1,
            Location = new Vector2I(150, 359), Size = new Vector2I(85, 25), Enabled = false, Visible = false
        };
        _sell.MouseClick += (o, e) => SellSelected();
        AddControl(_sell);
    }

    public void SetGoods(IEnumerable<NPCGood> goods, CurrencyInfo currency, IEnumerable<ItemType> sellableTypes = null)
    {
        ClearSaleVisuals(_sellLinks);
        _goods.Clear();
        if (goods != null) foreach (var good in goods) if (good?.Item != null) _goods.Add(good);
        _currency = currency;
        _sellableTypes.Clear();
        if (sellableTypes != null) _sellableTypes.UnionWith(sellableTypes);
        _hasSellableTypes = sellableTypes != null;
        _selected = -1;
        _sellLinks.Clear();
        // 已发送的 NPCSell 不能因 NPC 翻页而丢掉 pending；服务端的
        // ItemsChanged 可能在新页面响应之后到达，必须仍能解锁来源格。
        _buy.Enabled = false;
        _sell.Enabled = false;
        // 原版出售状态由独立 InventoryDialog.SellMode 绘制；商品面板只有购买区。
        _sell.Visible = false;
        _guildFunds.Checked = false;
        _guildFunds.Visible = _currency?.Type == CurrencyType.Gold;
        _guildFunds.Enabled = _guildFunds.Visible && GameScene.Game?.HasGuild == true;
        _scroll.Value = 0;
        // legacy（旧版 EI 商店窗）的根尺寸/列表/按钮几何由 ApplyLegacyEiLayout 固定，
        // 这里不能按现代公式重算，否则会覆盖掉 300x304 与行距 46。
        if (_legacyEiLayout)
        {
            _scroll.MaxValue = Math.Max(0, _goods.Count * _rowHeight - 2);
            RefreshRows();
            Visible = _goods.Count > 0 || _hasSellableTypes;
            return;
        }
        int clientHeight = Math.Clamp(_goods.Count * 43 - 1, 42, 299);
        Size = new Vector2I(245, clientHeight + 100);
        _frame.Size = Size;
        _list.Size = new Vector2I(227, clientHeight);
        _scroll.Position = new Vector2(217, 38);
        _scroll.Size = new Vector2I(19, clientHeight - 2);
        _scroll.VisibleSize = clientHeight;
        _buy.Position = new Vector2(30, Size.Y - 43);
        _guildFunds.Position = new Vector2(30, Size.Y - 20);
        _sell.Position = new Vector2(150, Size.Y - 43);
        _scroll.MaxValue = Math.Max(0, _goods.Count * 43 - 2);
        RefreshRows();
        // 原版 BuySell 页即使没有可购买商品，只要 Page.Types 非空，
        // 仍会打开背包出售模式；商品面板保留可见的出售提交入口。
        Visible = _goods.Count > 0 || _hasSellableTypes;
    }

    /// <summary>
    /// 旧版 EI 商店窗（窗口 id2）的购买态（state0 BUY）几何。
    /// 详见类顶部 _legacyEiLayout 附近的证据注释。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        _rowHeight = 46;
        _rowInsetY = 0;
        Size = new Vector2I(300, 304);
        _frame.Visible = false;
        _legacyBackground.Visible = true;
        // 列表：5 行、行距 46、首行 y=40（证据 rowsY 40/86/132/178/224）。
        // x 与宽度证据未给，取 4/250 以塞进 300 宽且不与 close(266) 重叠。
        _list.Location = new Vector2I(4, 40);
        _list.Size = new Vector2I(250, 230);
        _scroll.Location = new Vector2I(254, 40);
        _scroll.Size = new Vector2I(19, 228);
        _scroll.VisibleSize = 230;
        _scroll.Change = _rowHeight;
        // close 帧 1010/1011 @ (266,270) 28x26；confirm 帧 1012/1013 @ (127,267) 48x20。
        _buy.Location = new Vector2I(127, 267);
        _buy.Size = new Vector2I(48, 20);
        _buy.LibraryFile = LibraryFile.GameInter;
        _buy.Index = 1012;
        _buy.HoverIndex = 1013;
        _buy.PressedIndex = 1013;
        _guildFunds.Visible = false;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = _legacyEiLayout
            && Size == new Vector2I(300, 304)
            && _legacyBackground.Visible && !_frame.Visible
            && _legacyBackground.LibraryFile == LibraryFile.GameInter
            && _legacyBackground.Index == 1000
            && _legacyBackground.Location == new Vector2I(-106, -102)
            && _rowHeight == 46
            && _list.Location == new Vector2I(4, 40)
            && _buy.Location == new Vector2I(127, 267)
            && _buy.Size == new Vector2I(48, 20)
            && _buy.Index == 1012;
        details = $"size={Size} frame={_legacyBackground.Index}@{_legacyBackground.Location} rowHeight={_rowHeight} list={_list.Location}/{_list.Size} buy={_buy.Location}/{_buy.Size}#{_buy.Index}";
        return ok;
    }

    public bool TrySelectForSale(DXItemCell source)
    {
        // 原版 DXItemCell 的 NPC 买卖分支只在 GridType.Inventory 中切换
        // SelectedItems；伙伴背包右键仍走伙伴/普通物品处理，不会伪造 NPCSell。
        if (!Visible || !_hasSellableTypes || source?.Item == null || source.GridType != GridType.Inventory) return false;
        if (_hasSellableTypes && !_sellableTypes.Contains(source.Item.Info.ItemType)) return true;
        if (!source.Item.Info.CanSell || source.Item.Flags.HasFlag(UserItemFlags.Locked) ||
            source.Item.Flags.HasFlag(UserItemFlags.Worthless) || source.Item.Flags.HasFlag(UserItemFlags.Marriage)) return true;
        var existing = _sellLinks.FindIndex(x => x.GridType == source.GridType && x.Slot == source.Slot);
        if (existing >= 0)
        {
            _sellLinks.RemoveAt(existing);
            source.SaleSelected = false;
        }
        else
        {
            _sellLinks.Add(new CellLinkInfo { GridType = source.GridType, Slot = source.Slot, Count = source.Item.Count });
            // 原版通过 DXItemCell.SelectedChanged 显示出售选中边框；
            // Godot 的出售清单独立维护，因此使用独立 SaleSelected，不能
            // 改写表示普通拿起状态的全局 SelectedCell。
            source.SaleSelected = true;
        }
        _sell.Enabled = _sellLinks.Count > 0;
        return true;
    }

    public bool AuditSaleSelection(out string details)
    {
        var info = Globals.ItemInfoList?.Binding?.FirstOrDefault(x => x?.ItemType == ItemType.Weapon);
        if (info == null)
        {
            details = "no weapon ItemInfo in loaded database";
            return false;
        }
        info.ItemType = ItemType.Weapon;
        info.CanSell = true;
        info.StackSize = 10;
        var item = new ClientUserItem(info, 2);
        var cell = new DXItemCell
        {
            GridType = GridType.Inventory,
            Slot = 0,
            ItemGrid = new[] { item },
        };
        SetGoods(Array.Empty<NPCGood>(), null, new[] { ItemType.Weapon });
        bool first = TrySelectForSale(cell) && cell.SaleSelected;
        bool second = TrySelectForSale(cell) && !cell.SaleSelected;
        details = $"first={first} toggleOff={second} visible={Visible}";
        CancelUnsubmittedLinks();
        cell.QueueFree();
        return first && second;
    }

    public static bool CanAttemptPurchase(bool observer, int selected, int goodsCount)
        => !observer && selected >= 0 && selected < goodsCount;

    private void SellSelected()
    {
        if (_sellLinks.Count == 0) return;
        var links = _sellLinks.ToList();
        foreach (var link in links)
        {
            var source = (sourceGrid(link.GridType))
                .FirstOrDefault(c => c.GridType == link.GridType && c.Slot == link.Slot);
            if (source != null)
            {
                source.Locked = true;
                source.UpdateBorder();
            }
        }
        _pendingSellLinks.Clear();
        _pendingSellLinks.AddRange(links);
        GameScene.Game?.SendNPCSell(links);
        _sellLinks.Clear();
        _sell.Enabled = false;
    }

    private static DXItemCell[] sourceGrid(GridType grid) => grid switch
    {
        GridType.Inventory => GameScene.Game?.InventoryCells ?? Array.Empty<DXItemCell>(),
        GridType.CompanionInventory => GameScene.Game?.CompanionInventoryCells ?? Array.Empty<DXItemCell>(),
        _ => Array.Empty<DXItemCell>(),
    };

    public void ItemsChanged(IEnumerable<CellLinkInfo> links)
    {
        var keys = new HashSet<(GridType Grid, int Slot)>((links ?? Enumerable.Empty<CellLinkInfo>())
            .Where(x => x != null).Select(x => (x.GridType, x.Slot)));
        foreach (var link in _pendingSellLinks.Where(x => keys.Contains((x.GridType, x.Slot))).ToList())
        {
            FindSource(link)?.SaleSelected = false;
            GameScene.Game?.UnlockItemLink(link);
        }
        _pendingSellLinks.RemoveAll(x => keys.Contains((x.GridType, x.Slot)));
    }

    public List<CellLinkInfo> CancelUnsubmittedLinks()
    {
        var links = _sellLinks.ToList();
        ClearSaleVisuals(links);
        _sellLinks.Clear();
        _sell.Enabled = false;
        return links;
    }

    private static DXItemCell FindSource(CellLinkInfo link)
    {
        var cells = sourceGrid(link == null ? GridType.None : link.GridType);
        return cells.FirstOrDefault(c => c != null && c.Slot == link?.Slot);
    }

    private static void ClearSaleVisuals(IEnumerable<CellLinkInfo> links)
    {
        foreach (var link in links ?? Enumerable.Empty<CellLinkInfo>())
            FindSource(link)?.SaleSelected = false;
    }

    public List<CellLinkInfo> CancelAllLinks()
    {
        var links = CancelUnsubmittedLinks();
        ClearSaleVisuals(_pendingSellLinks);
        links.AddRange(_pendingSellLinks);
        _pendingSellLinks.Clear();
        return links;
    }

    private void RefreshRows()
    {
        foreach (var row in _rows) { _list.RemoveControl(row); row.QueueFree(); } _rows.Clear();
        int first = _scroll.Value / _rowHeight;
        for (int i = first; i < _goods.Count && i < first + 7; i++)
        {
            var good = _goods[i];
            long cost = good.CostFor(_currency, 1);
            bool selectedRow = i == _selected;
            var row = new DXButton
            {
                Text = string.Empty, FontSize = 9, TextColour = selectedRow ? new Color(1f, .85f, .3f) : Colors.White,
                BackColour = selectedRow ? new Color(.22f, .16f, .07f, .75f) : Colors.Transparent,
                Border = selectedRow, BorderColour = new Color(1f, .85f, .3f),
                LibraryFile = LibraryFile.Interface, Index = -1,
                Location = new Vector2I(1, (i - first) * _rowHeight + _rowInsetY), Size = new Vector2I(204, 40),
            };
            row.AddControl(new DXImageControl
            {
                LibraryFile = LibraryFile.StoreItem, Index = good.Item.Image, Location = new Vector2I(2, 2),
                Size = new Vector2I(36, 36), FixedSize = true, IsControl = false,
            });
            row.AddControl(new DXLabel
            {
                Text = good.Item.Local(), FontSize = 9, TextColour = row.TextColour,
                Location = new Vector2I(41, 3), Size = new Vector2I(145, 17), IsControl = false,
            });
            if (_currency?.DropItem != null)
                row.AddControl(new DXImageControl
                {
                    LibraryFile = LibraryFile.Ground, Index = _currency.DropItem.Image,
                    Location = new Vector2I(41, 22), Size = new Vector2I(16, 16), FixedSize = true, IsControl = false,
                });
            // 旧版 NPCGoodsPanel.UpdateCosts：余额不足时价格变红，否则黄色。
            long balance = GameScene.Game?.Currencies.FirstOrDefault(x =>
                x?.Info == _currency || x?.Info?.Type == _currency?.Type)?.Amount ?? 0;
            bool afford = cost <= balance;
            row.AddControl(new DXLabel
            {
                Text = $"{cost:#,##0}", FontSize = 9,
                TextColour = afford ? new Color(1f, .85f, .3f) : Colors.Red,
                Location = new Vector2I(60, 21), Size = new Vector2I(125, 17), IsControl = false,
            });
            // 旧版 UpdateColours 的 RequirementLabel：可装备/使用物品显示 Can use Item（青绿），
            // 否则 Cannot use Item（红）。
            if (good.Item.ItemType is ItemType.Consumable or ItemType.Scroll or ItemType.Weapon
                or ItemType.Armour or ItemType.Helmet or ItemType.Necklace or ItemType.Bracelet
                or ItemType.Ring or ItemType.Shoes or ItemType.Poison or ItemType.Amulet
                or ItemType.DarkStone or ItemType.Bundle or ItemType.Torch)
            {
                bool canUse = GameScene.Game?.CanUseItem(new ClientUserItem(good.Item, 1)) == true;
                row.AddControl(new DXLabel
                {
                    Text = canUse ? "Can use Item" : "Cannot use Item",
                    FontSize = 8,
                    TextColour = canUse ? Colors.Aquamarine : Colors.Red,
                    Location = new Vector2I(41, 33), Size = new Vector2I(160, 15), IsControl = false,
                });
            }
            int selected = i; row.MouseClick += (o, e) => { _selected = selected; _buy.Enabled = true; RefreshRows(); };
            // 旧版 NPCDialog 双击商品 -> C.NPCBuy (Godot DXControl.MouseDoubleClick 已修复触发)
            int doubleSelected = i; row.MouseDoubleClick += (o, e) => { _selected = doubleSelected; RefreshRows(); BuySelected(); };
            _list.AddControl(row); _rows.Add(row);
        }
    }

    private void BuySelected()
    {
        if (!CanAttemptPurchase(GameScene.Game?.IsObserver == true, _selected, _goods.Count)) return;
        var game = GameScene.Game;
        var good = _goods[_selected];
        if (game == null || game.IsObserver || good?.Item == null) return;

        var currency = _currency ?? Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold);
        long balance = game.Currencies.FirstOrDefault(x => x?.Info == currency || x?.Info?.Type == currency?.Type)?.Amount ?? 0;
        if (_guildFunds.Checked)
        {
            if (game.HasGuild == false) return;
            balance = game.GuildFunds;
        }

        long maxAmount = good.IsCurrencyGood ? long.MaxValue : Math.Max(1, good.Item.StackSize);
        maxAmount = good.MaxAmountFor(currency, balance, maxAmount);
        if (maxAmount <= 0)
        {
            // 原版：余额不足以购买时提示。
            GameScene.Game?.ReceiveChat($"你没有足够的{currency?.Name ?? "金币"}来购买'{good.Item.Local()}'。", MessageType.System);
            return;
        }

        if (!good.IsCurrencyGood && good.Item.Weight > 0)
        {
            int freeWeight = (game.PlayerStats?[Stat.BagWeight] ?? 0) - game.BagWeight;
            if (good.Item.ItemType is ItemType.Amulet or ItemType.Poison)
            {
                if (freeWeight < good.Item.Weight)
                {
                    GameScene.Game?.ReceiveChat($"你的负重不够，无法购买'{good.Item.Local()}'。", MessageType.System);
                    return;
                }
            }
            else
                maxAmount = Math.Min(maxAmount, Math.Max(0, freeWeight / good.Item.Weight));

            // 背包没有可用重量时不应打开一个 Amount=0 的确认框；原版超重时直接提示终止。
            if (maxAmount <= 0)
            {
                GameScene.Game?.ReceiveChat($"你的负重不够，无法购买'{good.Item.Local()}'。", MessageType.System);
                return;
            }
        }

        if (good.Item.StackSize > 1 || good.IsCurrencyGood)
        {
            long initial = good.IsCurrencyGood ? good.NormaliseCurrencyPurchaseAmount(currency, 1) : 1;
            var amount = new ItemAmountDialog(Lang.NPCGoodsPanelBuyLabel, maxAmount, Math.Min(maxAmount, Math.Max(1, initial)), count =>
            {
                game.SendNPCBuy(good.Index, count, _guildFunds.Checked);
                _guildFunds.Checked = false;
            });
            WindowManager.Open(amount, game.UILayer);
            return;
        }

        if (good.Item.Weight > 0 && (game.PlayerStats?[Stat.BagWeight] ?? 0) - game.BagWeight < good.Item.Weight)
        {
            GameScene.Game?.ReceiveChat($"你的负重不够，无法购买'{good.Item.Local()}'。", MessageType.System);
            return;
        }
        if (good.CostFor(currency, 1) > balance)
        {
            GameScene.Game?.ReceiveChat($"你没有足够的{currency?.Name ?? "金币"}来购买'{good.Item.Local()}'。", MessageType.System);
            return;
        }
        game.SendNPCBuy(good.Index, 1, _guildFunds.Checked);
        _guildFunds.Checked = false;
    }
}
