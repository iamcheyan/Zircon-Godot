using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 GameStoreDialog 的分页商品布局：左侧分类、右侧两列商品、底部翻页。</summary>
public partial class GameStoreDialog : DXWindow
{
    private readonly List<StoreInfo> _items = new();
    private readonly List<DXControl> _rows = new();
    private readonly List<DXButton> _categoryButtons = new();
    private readonly List<GameStoreTopItemRow> _topRows = new();
    private readonly HashSet<int> _favourites = new();
    private DXControl _list;
    private DXControl _categoryContent;
    private DXVScrollBar _categoryScroll;
    private DXLabel _page;
    private DXLabel _currency;
    private DXLabel _topItems;
    private DXControl _topPanel;
    private DXTextInput _search;
    private DXButton _sort;
    private GameStoreSortMenu _sortMenu;
    private DXButton _previousButton;
    private DXButton _nextButton;
    private int _sortMode;
    private int _pageIndex;
    private GameStoreCategory _category = GameStoreCategory.All;
    private ItemType? _itemTypeFilter;
    private int? _storeIndexFilter;
    private string _storeFilter;
    private bool _requiresStoreFilter;
    private bool _useHuntGold;

    public Vector2I ItemListGeometry => _list == null ? Vector2I.Zero : new Vector2I((int)_list.Position.X, (int)_list.Position.Y);
    public Vector2I TopItemsGeometry => _topPanel == null ? Vector2I.Zero : new Vector2I((int)_topPanel.Size.X, (int)_topPanel.Size.Y);
    public int TopItemRowCount => _topRows.Count;

    public GameStoreDialog()
    {
        HasTitle = false; HasFooter = false; Movable = true; Size = new Vector2I(800, 515);
        AddControl(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 310, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this); AddControl(close);
        AddControl(new DXLabel { Text = Lang.GameStoreMarketLabel, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(800, 18), IsControl = false });
        AddControl(new DXLabel { Text = Lang.GameStoreUi414Label, FontSize = 11, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(20, 20), IsControl = false });
        var categoryPanel = new DXControl { Location = new Vector2I(10, 38), Size = new Vector2I(170, 305), Clip = true };
        _categoryContent = new DXControl { Size = new Vector2I(168, 305) };
        categoryPanel.AddControl(_categoryContent);
        AddControl(categoryPanel);
        _categoryScroll = new DXVScrollBar { Location = new Vector2I(181, 38), Size = new Vector2I(14, 305), VisibleSize = 305, Change = 20, HideWhenNoScroll = true };
        _categoryScroll.ValueChanged += (o, e) => _categoryContent.Location = new Vector2I(0, -_categoryScroll.Value);
        AddControl(_categoryScroll);
        AddControl(new DXLabel { Text = Lang.GameStoreUi415Label, FontSize = 9, TextColour = new Color(1f, .85f, .3f), Align = HorizontalAlignment.Center, AutoSize = false, Location = new Vector2I(10, 354), Size = new Vector2I(172, 20), IsControl = false });
        _currency = new DXLabel { Text = Lang.GameStoreGoldLabel, FontSize = 10, TextColour = Colors.White, Align = HorizontalAlignment.Center, AutoSize = false, Location = new Vector2I(14, 375), Size = new Vector2I(164, 18), IsControl = false };
        AddControl(_currency);
        var recharge = new DXButton { Text = Lang.GameStoreUi417Label, Type = DXButton.ButtonType.Default, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(10, 410), Size = new Vector2I(172, 27) };
        recharge.MouseClick += (o, e) => GameScene.Game?.OpenRechargePage();
        AddControl(recharge);
        var currency = new DXButton { Text = Lang.GameStoreUi418Label, Type = DXButton.ButtonType.Default, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(10, 438), Size = new Vector2I(172, 27) };
        currency.MouseClick += (o, e) => { _useHuntGold = !_useHuntGold; BuildCategoryTree(); Refresh(); };
        AddControl(currency);

        AddControl(new DXLabel { Text = Lang.GameStoreDialogSortByLabel, FontSize = 9, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(225, 44), IsControl = false });
        _sort = new DXButton { Text = Lang.GameStoreDialogSortNameLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(270, 39), Size = new Vector2I(108, 25) };
        _sort.MouseClick += (o, e) =>
        {
            if (_sortMenu == null)
            {
                _sortMenu = new GameStoreSortMenu(value =>
                {
                    _sortMode = (int)value;
                    _sort.Text = SortName();
                    _sortMenu.Visible = false;
                    _pageIndex = 0;
                    Refresh();
                })
                {
                    Location = new Vector2I(270, 64),
                };
                AddControl(_sortMenu);
            }
            _sortMenu.Visible = !_sortMenu.Visible;
            _sortMenu.BringToFront();
        };
        AddControl(_sort);
        _search = new DXTextInput { Location = new Vector2I(385, 39), Size = new Vector2I(132, 20) };
        _search.TextChanged += (t) => GD.Print($"[GameStore] 搜索框输入: '{t}'");
        AddControl(_search);
        var search = new DXButton { Text = Lang.GameStoreDialogSearchButtonLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(530, 38), Size = new Vector2I(68, 25) };
        search.MouseClick += (o, e) => { _pageIndex = 0; Refresh(); };
        AddControl(search);

        _list = new DXControl { Location = new Vector2I(199, 67), Size = new Vector2I(409, 432), Clip = true };
        AddControl(_list);
        AddControl(new DXLabel { Text = Lang.GameStoreUi419Label, FontSize = 11, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(614, 37), IsControl = false });
        _topPanel = new DXControl { Location = new Vector2I(614, 65), Size = new Vector2I(174, 425), Clip = true };
        AddControl(_topPanel);
        _previousButton = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4840, Location = new Vector2I(321, 477) };
        _previousButton.MouseClick += (o, e) => { if (_pageIndex > 0) { _pageIndex--; Refresh(); } }; AddControl(_previousButton);
        _page = new DXLabel { Text = "1 / 1", FontSize = 10, TextColour = Colors.White, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Size = new Vector2I(106, 20), Location = new Vector2I(349, 473), IsControl = false }; AddControl(_page);
        _nextButton = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4845, Location = new Vector2I(464, 477) };
        _nextButton.MouseClick += (o, e) => { if (_pageIndex + 1 < PageCount) { _pageIndex++; Refresh(); } }; AddControl(_nextButton);
        _nextButton.MouseClick += (o, e) => GD.Print("[GameStore] 下一页点击"); // dbeditor 验收辅助
        Refresh();
    }

    private int PageCount => Math.Max(1, (_items.Count + 9) / 10);

    private void Refresh()
    {
        _items.Clear();
        if (Globals.StoreInfoList != null)
        {
            _items.AddRange(Globals.StoreInfoList.Binding.Where(x => x?.Item != null && EffectivePrice(x) > 0)
                .Where(MatchesCategory)
                .Where(x => string.IsNullOrWhiteSpace(_search?.Text) || x.Item.ItemName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => SortKey(x)));
        }
        // [dbeditor 验收诊断] 全量列表日志（含未翻页行）
        foreach (var it in _items)
            GD.Print($"[GameStore] 列表: {it.Index} {it.Item.ItemName} | 显示价: {(it.Available ? $"{EffectivePrice(it):#,##0}" : "Unavailable")}");
        // [dbeditor 验收钩子] DBEDITOR_VERIFY_ITEM=<名称子串> 时自动跳到含该商品的页（便于无头截图验证价格）
        string verify = System.Environment.GetEnvironmentVariable("DBEDITOR_VERIFY_ITEM");
        if (!string.IsNullOrWhiteSpace(verify))
        {
            int vi = _items.FindIndex(x => x.Item.ItemName.Contains(verify, StringComparison.OrdinalIgnoreCase));
            if (vi >= 0 && vi / 10 != _pageIndex)
            {
                GD.Print($"[GameStore] 验收钩子: '{verify}' 在第 {vi / 10 + 1} 页, 自动翻页");
                _pageIndex = vi / 10;
                _search.Text = "";
            }
        }
        foreach (var row in _rows) { _list.RemoveControl(row); row.QueueFree(); } _rows.Clear();
        _pageIndex = Math.Min(_pageIndex, PageCount - 1);
        for (int i = 0; i < 10; i++)
        {
            int index = _pageIndex * 10 + i;
            if (index >= _items.Count) break;
            var item = _items[index];
            var row = CreateRow(item, i);
            _list.AddControl(row); _rows.Add(row);
        }
        _page.Text = $"{_pageIndex + 1} / {PageCount}";
        _previousButton.Enabled = _pageIndex > 0;
        _nextButton.Enabled = _pageIndex < PageCount - 1;
        long amount = _useHuntGold
            ? GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.HuntGold)?.Amount ?? 0
            : GameScene.Game?.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold)?.Amount ?? 0;
        _currency.Text = $"{(_useHuntGold ? Lang.GameStoreGoldLabel2 : Lang.LootBoxGoldLabel)}: {amount:#,##0}";
    }

    private bool MatchesCategory(StoreInfo info)
    {
        if (_storeIndexFilter.HasValue) return info.Index == _storeIndexFilter.Value;
        if (_itemTypeFilter.HasValue) return info.Item.ItemType == _itemTypeFilter.Value;
        if (!string.IsNullOrWhiteSpace(_storeFilter))
            return (info.Filter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => string.Equals(x.Trim(), _storeFilter, StringComparison.OrdinalIgnoreCase));
        if (_requiresStoreFilter)
            return !string.IsNullOrWhiteSpace(info.Filter);
        return _category switch
        {
            GameStoreCategory.Favourites => _favourites.Contains(info.Index),
            GameStoreCategory.NewItems => Globals.StoreInfoList?.Binding.Where(x => x?.Item != null).OrderByDescending(x => x.Index).Take(10).Contains(info) == true,
            GameStoreCategory.Equipment => IsEquipment(info.Item.ItemType),
            GameStoreCategory.Consumables => IsConsumable(info.Item.ItemType),
            GameStoreCategory.Cosmetics => IsCosmetic(info.Item.ItemType),
            GameStoreCategory.Other => !IsEquipment(info.Item.ItemType) && !IsConsumable(info.Item.ItemType) && !IsCosmetic(info.Item.ItemType),
            _ => true,
        };
    }

    private string SortKey(StoreInfo info)
    {
        return _sortMode switch
        {
            (int)MarketPlaceStoreSort.HighestPrice => $"{int.MaxValue - EffectivePrice(info):D10}",
            (int)MarketPlaceStoreSort.LowestPrice => $"{EffectivePrice(info):D10}",
            (int)MarketPlaceStoreSort.Favourite => $"{(_favourites.Contains(info.Index) ? 0 : 1)}:{info.Item.ItemName}",
            _ => info.Item.ItemName,
        };
    }

    private string SortName() => _sortMode switch
    {
        (int)MarketPlaceStoreSort.HighestPrice => Lang.GameStoreHighestPriceLabel,
        (int)MarketPlaceStoreSort.LowestPrice => Lang.ConsignmentLowestPriceLabel,
        (int)MarketPlaceStoreSort.Favourite => Lang.GameStoreDialogSortFavouritesLabel,
        _ => Lang.GameStoreDialogSortNameLabel,
    };

    private void BuildCategoryTree()
    {
        foreach (var button in _categoryButtons)
        {
            _categoryContent.RemoveControl(button);
            button.QueueFree();
        }
        _categoryButtons.Clear();

        int y = 0;
        void Add(string text, Action action, int indent = 0)
        {
            var button = new DXButton
            {
                Text = new string(' ', indent * 2) + text,
                FontSize = 9,
                TextColour = Colors.White,
                LibraryFile = LibraryFile.Interface,
                Index = -1,
                Type = DXButton.ButtonType.SmallButton,
                Location = new Vector2I(0, y),
                Size = new Vector2I(168, 20),
            };
            button.MouseClick += (o, e) => { action(); _pageIndex = 0; Refresh(); };
            _categoryContent.AddControl(button);
            _categoryButtons.Add(button);
            y += 21;
        }

        if (_favourites.Count > 0) Add(Lang.GameStoreDialogSortFavouritesLabel, () => SetFilter(GameStoreCategory.Favourites));
        var filters = Globals.StoreInfoList?.Binding.Where(x => x?.Item != null)
            .SelectMany(x => (x.Filter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        foreach (var filter in filters) Add($"{filter}", () => SetFilter(GameStoreCategory.All, null, filter, true), 1);

        if (Globals.StoreInfoList?.Binding.Any(x => x?.Item != null) == true) Add(Lang.GameStoreDialogNewItemsLabel, () => SetFilter(GameStoreCategory.NewItems));

        Add(Lang.GameStoreAllLabel, () => SetFilter(GameStoreCategory.All));
        Add(Lang.GameStoreDialogEquipmentLabel, () => SetFilter(GameStoreCategory.Equipment));
        AddTypeFilters(type => IsEquipment(type), y, Add);
        Add(Lang.GameStoreDialogConsumablesLabel, () => SetFilter(GameStoreCategory.Consumables));
        AddTypeFilters(type => IsConsumable(type), y, Add);
        Add(Lang.GameStoreUi425Label, () => SetFilter(GameStoreCategory.Cosmetics));
        AddTypeFilters(type => IsCosmetic(type), y, Add);
        Add(Lang.GameStoreDialogOtherLabel, () => SetFilter(GameStoreCategory.Other));

        _categoryContent.Size = new Vector2I(168, Math.Max(305, y));
        _categoryScroll.MaxValue = Math.Max(305, y);
        _categoryScroll.Value = Math.Min(_categoryScroll.Value, Math.Max(0, y - 305));
    }

    private void AddTypeFilters(Func<ItemType, bool> matches, int ignored, Action<string, Action, int> add)
    {
        var types = Globals.StoreInfoList?.Binding.Where(x => x?.Item != null && matches(x.Item.ItemType))
            .Select(x => x.Item.ItemType).Distinct().OrderBy(x => x.ToString()).ToList() ?? new List<ItemType>();
        foreach (var type in types)
        {
            var selected = type;
            add(type.ToString(), () => SetFilter(GameStoreCategory.All, selected), 1);
        }
    }

    private void SetFilter(GameStoreCategory category, ItemType? itemType = null, string storeFilter = null, bool requiresStoreFilter = false)
    {
        _category = category;
        _storeIndexFilter = null;
        _itemTypeFilter = itemType;
        _storeFilter = storeFilter;
        _requiresStoreFilter = requiresStoreFilter;
    }

    private int EffectivePrice(StoreInfo info) => _useHuntGold && info.HuntGoldPrice > 0 ? info.HuntGoldPrice : info.Price;

    private static ClientUserItem CreateStoreItem(StoreInfo info)
    {
        var item = new ClientUserItem(info.Item, 1);
        if (info.Duration > 0)
        {
            item.Flags |= UserItemFlags.Expirable;
            item.ExpireTime = TimeSpan.FromSeconds(info.Duration);
        }
        return item;
    }

    private static bool IsEquipment(ItemType type) => type is ItemType.Weapon or ItemType.Armour or ItemType.Torch or ItemType.Helmet or ItemType.Necklace or ItemType.Bracelet or ItemType.Ring or ItemType.Shoes or ItemType.Amulet or ItemType.HorseArmour or ItemType.ItemPart or ItemType.Emblem or ItemType.Shield;
    private static bool IsConsumable(ItemType type) => type is ItemType.Consumable or ItemType.Poison or ItemType.Meat or ItemType.Book or ItemType.Scroll or ItemType.DarkStone or ItemType.RefineSpecial or ItemType.Flower or ItemType.CompanionFood or ItemType.Bait or ItemType.Currency or ItemType.Bundle or ItemType.LootBox;
    private static bool IsCosmetic(ItemType type) => type is ItemType.Costume or ItemType.CompanionHead or ItemType.CompanionBack;

    private DXControl CreateRow(StoreInfo info, int rowIndex)
    {
        var row = new DXControl { Location = new Vector2I((rowIndex % 2) * 202, (rowIndex / 2) * 80), Size = new Vector2I(200, 78), Border = false };
        var hover = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 4872,
            FixedSize = true,
            Size = new Vector2I(200, 78),
            Visible = false,
            IsControl = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddControl(hover);
        row.MouseEnter += (o, e) => hover.Visible = info != null;
        row.MouseLeave += (o, e) => hover.Visible = false;
        var itemGrid = new[] { CreateStoreItem(info) };
        var cell = new DXItemCell { Location = new Vector2I(19, 18), Size = new Vector2I(36, 36), ItemGrid = itemGrid, Slot = 0, ReadOnly = true, GridType = GridType.None, Border = false };
        row.AddControl(cell);
        // [dbeditor 验收诊断] 商城行渲染日志：物品名 + 实际显示价格（dbeditor 同步验收用）
        GD.Print($"[GameStore] 行渲染: {info.Item.ItemName} | 显示价: {(info.Available ? $"{EffectivePrice(info):#,##0}" : "Unavailable")}");
        row.AddControl(new DXLabel { Text = info.Item.Local(), FontSize = 9, TextColour = Colors.White, Location = new Vector2I(65, 8), Size = new Vector2I(128, 17), IsControl = false });
        row.AddControl(new DXLabel { Text = info.Available ? $"{EffectivePrice(info):#,##0}" : "Unavailable", FontSize = 9, TextColour = info.Available ? new Color(1f, .55f, .1f) : new Color(.55f, .55f, .55f), Align = HorizontalAlignment.Center, Location = new Vector2I(7, 59), Size = new Vector2I(58, 16), IsControl = false });
        int quantityValue = 1;
        var quantity = new DXButton { Text = "1", Type = DXButton.ButtonType.SmallButton, FontSize = 8, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(72, 30), Size = new Vector2I(117, 20) };
        row.AddControl(quantity);
        var buy = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4835, Location = new Vector2I(83, 51), CanBePressed = info.Available };
        buy.MouseClick += (o, e) =>
        {
            if (!info.Available) return;
            int count = quantityValue;
            long total = (long)EffectivePrice(info) * count;
            string currency = _useHuntGold ? Lang.GameStoreGoldLabel2 : Lang.LootBoxGoldLabel;
            var confirm = new ConfirmDialog(string.Format(Lang.GameStoreTotalLabel, info.Item.ItemName, count, EffectivePrice(info), currency, total), Lang.GameStoreDialogPurchaseConfirmCaption, () => GameScene.Game?.SendGameStoreBuy(info.Index, count, _useHuntGold));
            WindowManager.Open(confirm, GameScene.Game?.UILayer ?? GetParent());
        };
        row.AddControl(buy);
        var gift = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 4830, Location = new Vector2I(116, 51), CanBePressed = info.Available };
        gift.MouseClick += (o, e) =>
        {
            if (!CanAttemptGift(GameScene.Game?.IsObserver == true, info.Available, quantityValue)) return;
            int count = quantityValue;
            var dialog = new GameStoreGiftDialog(info.Item.ItemName, recipient => GameScene.Game?.SendGameStoreGift(info.Index, count, _useHuntGold, recipient));
            WindowManager.Open(dialog, GameScene.Game?.UILayer ?? GetParent());
        };
        row.AddControl(gift);
        var favourite = new DXButton { LibraryFile = LibraryFile.GameInter, Index = _favourites.Contains(info.Index) ? 4857 : 4855, Location = new Vector2I(151, 51) };
        // 旧版 RefreshFavourite 的 Hint：收藏/取消收藏悬停提示。
        favourite.TooltipText = _favourites.Contains(info.Index) ? Lang.GameStoreCancelLabel : Lang.GameStoreDialogSortFavouritesLabel;
        // 原版只发送切换请求，图标/收藏集合由
        // S.GameStoreFavouriteChanged 回包确认；不能乐观修改，否则服务端
        // 拒绝请求时客户端会永久显示错误收藏状态。
        favourite.MouseClick += (o, e) => GameScene.Game?.SendGameStoreFavourite(info.Index);
        row.AddControl(favourite);

        // 原版 QuantityBox 是 DXComboBox，打开后显示 1–10 的列表项，
        // 不是点击一次就改变数值的循环按钮。
        var quantityMenu = new DXControl
        {
            Location = new Vector2I(72, 50),
            Size = new Vector2I(117, 190),
            Border = true,
            BorderColour = new Color(.8f, .6f, .2f),
            BackColour = new Color(0.02f, 0.015f, 0.02f, .98f),
            Clip = true,
            Visible = false,
        };
        for (int value = 1; value <= 10; value++)
        {
            int selected = value;
            var option = new DXButton
            {
                Text = value.ToString(),
                Type = DXButton.ButtonType.DeselectedTab,
                FontSize = 8,
                Size = new Vector2I(115, 18),
                Location = new Vector2I(1, 1 + (value - 1) * 18),
                Index = -1,
                LibraryFile = LibraryFile.Interface,
            };
            option.MouseClick += (o, e) =>
            {
                quantityValue = selected;
                quantity.Text = selected.ToString();
                quantityMenu.Visible = false;
            };
            quantityMenu.AddControl(option);
        }
        quantity.MouseClick += (o, e) =>
        {
            quantityMenu.Visible = !quantityMenu.Visible;
            quantityMenu.BringToFront();
        };
        row.AddControl(quantityMenu);
        return row;
    }

    public void SetFavourites(IEnumerable<int> values)
    {
        _favourites.Clear();
        if (values != null) _favourites.UnionWith(values);
        BuildCategoryTree();
        Refresh();
    }

    public void SetFavourite(int index, bool value)
    {
        if (value) _favourites.Add(index); else _favourites.Remove(index);
        BuildCategoryTree();
        Refresh();
    }

    public void SetTopItems(IEnumerable<int> indexes)
    {
        // 热销包可能重复到达，先清空包括“暂无热销商品”占位标签在内的
        // 全部旧子控件，避免刷新一次叠一层。
        foreach (var child in _topPanel.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _topPanel.RemoveControl(control);
            else _topPanel.RemoveChild(node);
            node.QueueFree();
        }
        _topRows.Clear();
        var infos = new List<StoreInfo>();
        if (indexes != null && Globals.StoreInfoList != null)
            foreach (int index in indexes.Take(5))
            {
                var info = Globals.StoreInfoList.Binding.FirstOrDefault(x => x?.Index == index);
                if (info?.Item != null) infos.Add(info);
            }
        if (infos.Count == 0)
        {
            _topPanel.AddControl(new DXLabel { Text = Lang.GameStoreNoneLabel, FontSize = 10, TextColour = Colors.White, Size = new Vector2I(174, 50), Align = HorizontalAlignment.Center, IsControl = false });
            return;
        }
        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var row = new GameStoreTopItemRow(info, i + 1)
            {
                Location = new Vector2I(0, 5 + i * 87),
                Size = new Vector2I(174, i == 4 ? 73 : 78),
            };
            row.MouseClick += (o, e) => SelectTopItem(info);
            row.ItemCell.MouseClick += (o, e) => SelectTopItem(info);
            _topPanel.AddControl(row);
            _topRows.Add(row);
        }
    }

    private void SelectTopItem(StoreInfo info)
    {
        if (info?.Item == null) return;
        _category = GameStoreCategory.All;
        _storeIndexFilter = info.Index;
        _itemTypeFilter = null;
        _storeFilter = null;
        _requiresStoreFilter = false;
        _search.Text = string.Empty;
        _pageIndex = 0;
        Refresh();
    }

    public static bool CanAttemptGift(bool observer, bool available, int count)
        => !observer && available && count >= 1 && count <= 10;

    public bool AuditLayout()
    {
        return Size == new Vector2I(800, 515)
            && _list.Position == new Vector2I(199, 67)
            && _list.Size == new Vector2I(409, 432)
            && _previousButton.Position == new Vector2I(321, 477)
            && _nextButton.Position == new Vector2I(464, 477)
            && _topPanel.Position == new Vector2I(614, 65)
            && _topPanel.Size == new Vector2I(174, 425)
            && _topRows.Count <= 5
            && _topRows.Select((row, index) => row.Position == new Vector2I(0, 5 + index * 87)
                && row.ItemCell.Position == new Vector2I(19, 26)).All(x => x);
    }
}

/// <summary>商城排序下拉框，等价于原版 GameStoreDialog.SortBox 的四个列表项。</summary>
public sealed partial class GameStoreSortMenu : DXControl
{
    private readonly List<DXButton> _items = new();
    private readonly Action<MarketPlaceStoreSort> _select;

    public GameStoreSortMenu(Action<MarketPlaceStoreSort> select)
    {
        _select = select;
        Size = new Vector2I(108, 80);
        Border = true;
        BorderColour = new Color(.8f, .6f, .2f);
        BackColour = new Color(0.02f, 0.015f, 0.02f, .98f);
        Clip = true;
        IsControl = true;
        AddItem(MarketPlaceStoreSort.Alphabetical, Lang.GameStoreDialogSortNameLabel);
        AddItem(MarketPlaceStoreSort.HighestPrice, Lang.GameStoreHighestPriceLabel);
        AddItem(MarketPlaceStoreSort.LowestPrice, Lang.ConsignmentLowestPriceLabel);
        AddItem(MarketPlaceStoreSort.Favourite, Lang.GameStoreDialogSortFavouritesLabel);
    }

    private void AddItem(MarketPlaceStoreSort value, string text)
    {
        var item = new DXButton
        {
            Text = text,
            Type = DXButton.ButtonType.DeselectedTab,
            FontSize = 9,
            Size = new Vector2I(106, 19),
            Location = new Vector2I(1, 1 + _items.Count * 19),
            Index = -1,
            LibraryFile = LibraryFile.Interface,
        };
        item.MouseClick += (o, e) => _select(value);
        AddControl(item);
        _items.Add(item);
    }
}

/// <summary>原版 GameStoreTopItemControl：名次、物品格和商品名均保持原版锚点。</summary>
public sealed partial class GameStoreTopItemRow : DXControl
{
    public DXItemCell ItemCell { get; }

    public GameStoreTopItemRow(StoreInfo info, int rank)
    {
        ItemCell = new DXItemCell
        {
            Location = new Vector2I(19, 26),
            Size = new Vector2I(36, 36),
            ItemGrid = new[] { CreateStoreItem(info) },
            Slot = 0,
            ReadOnly = true,
            GridType = GridType.None,
            Border = false,
            ShowCountLabel = false,
        };
        AddControl(new DXLabel
        {
            Text = RankText(rank),
            FontSize = 9,
            TextColour = new Color(1f, .85f, .3f),
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Size = new Vector2I(174, 20),
            Location = new Vector2I(0, 1),
            IsControl = false,
        });
        AddControl(ItemCell);
        AddControl(new DXLabel
        {
            Text = info.Item.Local(),
            FontSize = 9,
            TextColour = Colors.White,
            Location = new Vector2I(65, 30),
            Size = new Vector2I(100, 20),
            IsControl = false,
        });
    }

    private static string RankText(int rank) => rank switch
    {
        1 => Lang.GameStoreUi433Label,
        2 => Lang.GameStoreUi434Label,
        3 => Lang.GameStoreUi435Label,
        4 => Lang.GameStoreUi436Label,
        _ => Lang.GameStoreUi437Label,
    };

    private static ClientUserItem CreateStoreItem(StoreInfo info)
    {
        var item = new ClientUserItem(info.Item, 1);
        if (info.Duration > 0)
        {
            item.Flags |= UserItemFlags.Expirable;
            item.ExpireTime = TimeSpan.FromSeconds(info.Duration);
        }
        return item;
    }
}
