using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 背包窗口 (移植自 Client/Scenes/Views/InventoryDialog.cs):
/// Interface 130 底图, 6x8 物品格 + 权重条 + 金币标签 + 排序/移除按钮。
/// </summary>
public partial class InventoryDialog : DXWindow
{
    public DXItemGrid Grid;
    public DXButton SortButton, TrashButton, SellButton, CloseButton;
    public DXControl WalletButton;
    public DXControl WeightBar;
    public DXLabel WeightLabel, GoldLabel, GgLabel;
    public readonly List<DXItemCell> SelectedItems = new();
    public readonly List<ItemType> SellableItemTypes = new();

    public InventoryMode InvMode { get; private set; } = InventoryMode.Normal;
    public bool IsSellMode => InvMode == InventoryMode.Sell;
    public bool IsRepairMode => InvMode == InventoryMode.Repair;
    public bool IsStorageMode => InvMode == InventoryMode.Storage;

    private bool _weightInit;
    private CurrencyInfo _primaryCurrency;
    private DXImageControl _background;
    private DXButton _legacyActionButton;
    private DXLabel _legacyModeLabel;
    private DXLabel _titleLabel, _goldTitle, _ggTitle;
    private DXImageControl _legacyScrollTrack;
    private DXVScrollBar _legacyScrollBar;
    private readonly List<CellLinkInfo> _pendingSellLinks = new();
    private bool _legacyEiLayout;

    public InventoryDialog()
    {
        // 原版 InventoryDialog 直接使用 Interface 130 背景图。
        HasTitle = false;
        Movable = true;
        Text = Lang.InventoryDialogTitle;
        Size = new Vector2I(264, 436);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 130,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        // 原版虽然没有 DXWindow 标题栏，但背景图内部仍有独立的标题文字层。
        // 不能只给 DXWindow.Text 赋值，否则 HasTitle=false 时不会绘制标题。
        _titleLabel = new DXLabel
        {
            Text = Lang.InventoryDialogTitle,
            FontSize = 10,
            TextColour = new Color(1f, .85f, .3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(52, 8),
            Size = new Vector2I(160, 18),
            IsControl = false,
        };
        AddControl(_titleLabel);

        CloseButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        CloseButton.Location = new Vector2I((int)Size.X - (int)CloseButton.Size.X - 3, 3);
        CloseButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(CloseButton);

        Grid = new DXItemGrid
        {
            GridSize = new Vector2I(6, 8),
            Location = new Vector2I(20, 39),
            GridPadding = 1,
            GridType = GridType.Inventory,
            ItemGrid = null, // GameScene 注入
        };
        AddControl(Grid);

        WeightBar = new DXControl
        {
            Location = new Vector2I(53, 355),
            Size = MirSkin.GetSize(LibraryFile.GameInter, 360),
        };
        WeightBar.BeforeDraw += DrawWeightFill;
        AddControl(WeightBar);

        WeightLabel = new DXLabel
        {
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Size = new Vector2I(200, 18),
            IsControl = false,
        };
        AddControl(WeightLabel);

        _goldTitle = new DXLabel
        {
            Text = Lang.InventoryDialogPrimaryCurrencyTitle,
            TextColour = new Color(0.85f, 0.68f, 0.2f),
            Location = new Vector2I(55, 381),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        AddControl(_goldTitle);

        GoldLabel = new DXLabel
        {
            Text = "0",
            TextColour = Colors.White,
            Location = new Vector2I(80, 381),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            Align = HorizontalAlignment.Right,
            VAlign = VerticalAlignment.Center,
        };
        GoldLabel.MouseClick += (o, e) => GameScene.Game?.SelectCurrency(
            GameScene.Game.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold));
        AddControl(GoldLabel);

        _ggTitle = new DXLabel
        {
            Text = "GG",
            TextColour = new Color(1f, 0.55f, 0.2f),
            Location = new Vector2I(55, 400),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        AddControl(_ggTitle);

        GgLabel = new DXLabel
        {
            Text = "0",
            TextColour = Colors.White,
            Location = new Vector2I(80, 400),
            AutoSize = false,
            Size = new Vector2I(97, 20),
            FontSize = 8,
            Align = HorizontalAlignment.Right,
            VAlign = VerticalAlignment.Center,
        };
        GgLabel.MouseClick += (o, e) => GameScene.Game?.SelectCurrency(
            GameScene.Game.Currencies.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold));
        AddControl(GgLabel);

        WalletButton = new DXControl
        {
            Location = new Vector2I(8, 380),
            Size = new Vector2I(45, 40),
            // 原版 WalletLabel 没有可见文字或按钮底图，只提供点击热区。
            BackColour = Colors.Transparent,
            Border = false,
        };
        WalletButton.MouseClick += (o, e) => GameScene.Game?.ToggleCurrencyWindow();
        AddControl(WalletButton);

        SortButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 364,
            Location = new Vector2I(180, 384),
        };
        SortButton.MouseClick += (o, e) => GameScene.Game?.SendItemSort(GridType.Inventory);
        AddControl(SortButton);

        TrashButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 358,
            Location = new Vector2I(218, 384),
        };
        TrashButton.MouseClick += (o, e) => TrashItem();
        AddControl(TrashButton);

        SellButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 354,
            Location = new Vector2I(218, 384),
            Visible = false,
            Enabled = false,
            TooltipText = "出售选中物品",
        };
        SellButton.MouseClick += (o, e) => SellSelected();
        AddControl(SellButton);
    }

    /// <summary>按最早 EI 客户端 GameInter F250 的原始坐标重排背包。</summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(284, 324);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 250;
        _background.Location = new Vector2I(-114, -94);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 250);
        _background.StretchImage = false;
        // EI F280 is a complete 16×424 vertical gauge. Six rows are visible;
        // the record array is placed into the separate six-column identity
        // table so multi-cell items reserve every occupied cell.
        Grid.UseLegacyFootprints = true;
        Grid.ItemLibraryFile = LibraryFile.Inventory;
        Grid.GridPadding = 0;
        Grid.GridSize = new Vector2I(6, 6);
        Grid.Location = new Vector2I(25, 41);
        Grid.Clip = true;

        _legacyScrollTrack ??= new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 280,
            FixedSize = true,
            StretchImage = false,
            Size = new Vector2I(16, 424),
            Location = new Vector2I(248, -165),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_legacyScrollTrack.GetParent() == null) AddControl(_legacyScrollTrack);

        _legacyScrollBar ??= new DXVScrollBar
        {
            Size = new Vector2I(16, 424),
            Location = new Vector2I(248, -165),
            Border = false,
            BackColour = Colors.Transparent,
            Change = 1,
        };
        if (_legacyScrollBar.GetParent() == null) AddControl(_legacyScrollBar);
        _legacyScrollBar.UpButton.DrawImage = false;
        _legacyScrollBar.DownButton.DrawImage = false;
        _legacyScrollBar.PositionBar.DrawImage = false;
        _legacyScrollBar.UpButton.FixedSize = true;
        _legacyScrollBar.DownButton.FixedSize = true;
        _legacyScrollBar.PositionBar.FixedSize = true;
        _legacyScrollBar.UpButton.Size = new Vector2I(16, 16);
        _legacyScrollBar.DownButton.Size = new Vector2I(16, 16);
        _legacyScrollBar.PositionBar.Size = new Vector2I(16, 34);
        _legacyScrollBar.MouseWheel += _legacyScrollBar.DoMouseWheel;
        _legacyScrollBar.ValueChanged -= LegacyScrollChanged;
        _legacyScrollBar.ValueChanged += LegacyScrollChanged;

        _titleLabel.Visible = false;
        _legacyModeLabel ??= new DXLabel
        {
            TextColour = new Color(1f, .85f, .55f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            FontSize = 9,
            AutoSize = false,
            IsControl = false,
        };
        if (_legacyModeLabel.GetParent() == null) AddControl(_legacyModeLabel);
        _legacyModeLabel.Text = InvMode switch
        {
            InventoryMode.Repair => "[修补]",
            InventoryMode.Sell => "[变卖]",
            InventoryMode.Storage => "[储存]",
            _ => "[包袱]",
        };
        _legacyModeLabel.Location = new Vector2I(38, 282);
        _legacyModeLabel.Size = new Vector2I(100, 18);

        _legacyActionButton ??= new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 264,
            HoverIndex = 265,
            PressedIndex = 265,
            Location = new Vector2I(176, 262),
            Size = new Vector2I(64, 20),
        };
        if (_legacyActionButton.GetParent() == null) AddControl(_legacyActionButton);
        _legacyActionButton.Location = new Vector2I(176, 262);
        _legacyActionButton.Size = new Vector2I(64, 20);
        CloseButton.LibraryFile = LibraryFile.GameInter;
        CloseButton.Index = 161;
        CloseButton.HoverIndex = 162;
        CloseButton.PressedIndex = 162;
        CloseButton.Location = new Vector2I(249, 288);
        CloseButton.Size = new Vector2I(28, 26);

        // EI 负重/记录区使用 F280 垂直 gauge；旧版包袱模式的文本仍
        // 单独绘制在根相对 (0x86,0x18)-(0xF0,0x26)。
        // 现代 F360 横向填充不能冒充该资源，因此保持隐藏。


        WeightBar.Visible = false;
        WeightLabel.Location = new Vector2I(0x86, 0x18);
        WeightLabel.Size = new Vector2I(0xF0 - 0x86, 0x26 - 0x18);
        WeightLabel.FontSize = 10;
        WeightLabel.Align = HorizontalAlignment.Left;
        WeightLabel.TextColour = new Color(0xA0 / 255f, 0xA0 / 255f, 0xA0 / 255f);
        _goldTitle.Visible = false;
        GoldLabel.Location = new Vector2I(0x41, 0x11A);
        GoldLabel.Size = new Vector2I(0x8E - 0x41, 0x12B - 0x11A);
        GoldLabel.FontSize = 10;
        GoldLabel.Align = HorizontalAlignment.Left;
        GoldLabel.TextColour = new Color(0x64 / 255f, 0xC8 / 255f, 0xF8 / 255f);
        _ggTitle.Visible = false;
        GgLabel.Visible = false;
        WalletButton.Visible = false;
        WalletButton.Location = new Vector2I(176, 262);
        WalletButton.Size = new Vector2I(64, 20);

        // 新版排序/丢弃/出售按钮没有旧版 F250 中的等价固定入口。
        SortButton.Visible = false;
        TrashButton.Visible = false;
        SellButton.Visible = false;
        UpdateClientAreaForLegacySkin();
    }
    /// <summary>
    /// 将服务器记录数组映射到 EI 的六列可视窗口。记录索引仍是
    /// ItemGrid 的槽位，格子位置由 footprint first-fit 计算，不按
    /// 46 条记录硬编码行数。
    /// </summary>
    public void ConfigureLegacyInventoryGrid()
    {
        if (!_legacyEiLayout || Grid == null) return;

        int rows = Grid.GetLegacyRequiredRows(6);
        Grid.GridSize = new Vector2I(6, rows);
        Grid.VisibleHeight = 6;
        Grid.ScrollValue = Math.Min(Grid.ScrollValue, Math.Max(0, rows - 6));

        if (_legacyScrollBar == null) return;
        _legacyScrollBar.MinValue = 0;
        _legacyScrollBar.VisibleSize = 6;
        _legacyScrollBar.MaxValue = rows;
        _legacyScrollBar.Value = Grid.ScrollValue;
    }

    private void LegacyScrollChanged(object sender, EventArgs e)
    {
        if (Grid == null || _legacyScrollBar == null) return;
        Grid.ScrollValue = _legacyScrollBar.Value;
    }

    private void TrashItem()
    {
        if (DXItemCell.SelectedCell == null) return;
        var cell = DXItemCell.SelectedCell;
        if (cell.Item == null) return;
        if (cell.GridType != GridType.Inventory) return;
        if (cell.Item.Flags.HasFlag(UserItemFlags.Locked) || cell.Item.Flags.HasFlag(UserItemFlags.Marriage)) return;

        cell.Locked = true;
        cell.UpdateBorder();
        DXItemCell.SelectedCell = null;
        GameScene.Game?.SendItemDelete(cell.GridType, cell.Slot);
    }

    public override void _Ready()
    {
        base._Ready();
        CenterWeightLabel();
        GameScene.Game?.RefreshInventoryWeights();
    }

    private void DrawWeightFill(object sender, EventArgs e)
    {
        var game = GameScene.Game;
        if (game == null) return;
        if (game.BagWeight <= 0) return;

        var stats = game.PlayerStats;
        if (stats == null || stats[Stat.BagWeight] <= 0) return;

        float percent = Math.Clamp(game.BagWeight / (float)stats[Stat.BagWeight], 0f, 1f);
        if (percent <= 0) return;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, 360);
        if (tex == null) return;
        var imgSize = tex.GetSize();
        WeightBar.DrawTextureRect(tex, new Rect2(0, 0, imgSize.X * percent, imgSize.Y), false);
    }

    public void CenterWeightLabel()
    {
        if (WeightLabel == null || WeightBar == null) return;
        if (_legacyEiLayout) return;
        var size = MirSkin.MeasureText(WeightLabel.Text, WeightLabel.FontSize);
        WeightLabel.Location = new Vector2I(
            WeightBar.Location.X + (int)((WeightBar.Size.X - size.X) / 2),
            WeightBar.Location.Y + (int)((WeightBar.Size.Y - size.Y) / 2));
    }

    /// <summary>GameScene 数据注入</summary>
    public void SetWeight(int bagWeight)
    {
        int capacity = GameScene.Game?.PlayerStats?[Stat.BagWeight] ?? 0;
        WeightLabel.Text = InvMode == InventoryMode.Normal
            ? (capacity > 0 ? $"负重:{bagWeight} / 总量:{capacity}" : $"负重:{bagWeight} / 总量:0")
            : string.Empty;
        CenterWeightLabel();
        WeightBar.QueueRedraw();
    }

    public void SetCurrency(long gold, long gg)
    {
        if (_legacyEiLayout)
        {
            GoldLabel.Text = gold.ToString();
            return;
        }

        if (!IsSellMode)
        {
            GoldLabel.Text = gold.ToString("N0");
            GgLabel.Text = gg.ToString("N0");
            return;
        }

        var current = GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info == _primaryCurrency);
        GoldLabel.Text = (current?.Amount ?? 0).ToString("N0");
        GgLabel.Text = SaleTotal().ToString("N0");
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(284, 324)
            && _background.Index == 250
            && Grid.GridSize == new Vector2I(6, 6)
            && Grid.Location == new Vector2I(25, 41)
            && CloseButton.Location == new Vector2I(249, 288)
            && _legacyActionButton?.Location == new Vector2I(176, 262);
        details = $"size={Size} background=F{_background.Index} grid={Grid.GridSize}@{Grid.Location} close={CloseButton.Location} action={_legacyActionButton?.Location}";
        return ok;
    }

    /// <summary>
    /// 旧版模式由服务端消息切换，而不是点击装饰性页签。保留一个明确的
    /// 业务入口，供 NPC 修补/变卖/储存回包直接切换旧版文字和操作状态。
    /// </summary>
    public void SetLegacyMode(InventoryMode mode)
    {
        InvMode = mode;
        if (_legacyModeLabel != null)
            _legacyModeLabel.Text = mode switch
            {
                InventoryMode.Repair => "[修补]",
                InventoryMode.Sell => "[变卖]",
                InventoryMode.Storage => "[储存]",
                _ => "[包袱]",
            };
        if (mode != InventoryMode.Sell)
        {
            ClearSaleSelection();
            SellButton.Visible = false;
        }
        WeightLabel.Text = mode == InventoryMode.Normal ? WeightLabel.Text : string.Empty;
        CenterWeightLabel();
    }

    /// <summary>原版 InventoryDialog.SellMode：背包负责多选物品和提交 NPCSell。</summary>
    public void SellMode(CurrencyInfo currency, IEnumerable<ItemType> sellableTypes)
    {
        _primaryCurrency = currency ?? Globals.CurrencyInfoList?.Binding.FirstOrDefault(x => x.Type == CurrencyType.Gold);
        SellableItemTypes.Clear();
        if (sellableTypes != null) SellableItemTypes.AddRange(sellableTypes);
        ClearSaleSelection();
        InvMode = InventoryMode.Sell;
        _titleLabel.Text = "背包 [出售]";
        _goldTitle.Text = _primaryCurrency?.Abbreviation ?? Lang.InventoryDialogPrimaryCurrencyTitle;
        _ggTitle.Text = Lang.InventoryDialogSecondaryCurrencyTitle;
        _ggTitle.TextColour = new Color(.4f, .65f, 1f);
        GgLabel.Text = "0";
        TrashButton.Visible = false;
        SellButton.Visible = true;
        // 原版按钮始终可用：有选中项时出售选中项，没有选中项时出售全部可售物品。
        SellButton.Enabled = true;
        if (_legacyModeLabel != null) _legacyModeLabel.Text = "[变卖]";
        SetCurrency(0, 0);
    }

    /// <summary>原版 InventoryDialog.NormalMode：离开 NPC 出售状态并恢复普通按钮。</summary>
    public void NormalMode()
    {
        ClearSaleSelection();
        SellableItemTypes.Clear();
        _primaryCurrency = null;
        InvMode = InventoryMode.Normal;
        _titleLabel.Text = Lang.InventoryDialogTitle;
        _goldTitle.Text = Lang.InventoryDialogPrimaryCurrencyTitle;
        _goldTitle.TextColour = new Color(.85f, .68f, .2f);
        _ggTitle.Text = "GG";
        _ggTitle.TextColour = new Color(1f, .55f, .2f);
        TrashButton.Visible = true;
        SellButton.Visible = false;
        SellButton.Enabled = false;
        if (_legacyModeLabel != null) _legacyModeLabel.Text = "[包袱]";
        SetCurrency(
            GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info?.Type == CurrencyType.Gold)?.Amount ?? 0,
            GameScene.Game?.Currencies?.FirstOrDefault(x => x.Info?.Type == CurrencyType.GameGold)?.Amount ?? 0);
    }

    /// <summary>出售模式下由背包格调用；返回 true 表示已消费这次点击。</summary>
    public bool TrySelectForSale(DXItemCell cell)
    {
        if (!IsSellMode || cell?.Item == null || cell.GridType != GridType.Inventory) return false;
        // 原版 DXItemCell 右键 SellMode：婚戒不可出售、静默返回；不可卖物品
        // 先给出系统提示（UnableToSellHereCannotSold）再拒绝选中。
        if (cell.Item.Flags.HasFlag(UserItemFlags.Marriage))
            return true;
        if (cell.Locked || cell.Item.Flags.HasFlag(UserItemFlags.Locked) ||
            cell.Item.Flags.HasFlag(UserItemFlags.Worthless) ||
            cell.Item.Info?.CanSell != true)
        {
            GameScene.Game?.ReceiveChat($"无法出售 {cell.Item.Info?.Local()}, 该物品不可出售。");
            return true;
        }
        // 原版 InventoryDialog.Cell_SelectedChanged：类型不在商店可售列表时
        // 提示 UnableToSellHere 并取消选中。
        if (SellableItemTypes.Count > 0 && !SellableItemTypes.Contains(cell.Item.Info.ItemType))
        {
            GameScene.Game?.ReceiveChat($"无法在 {cell.Item.Info?.Local()} 这里进行售卖。");
            return true;
        }

        if (SelectedItems.Contains(cell))
        {
            SelectedItems.Remove(cell);
            cell.SaleSelected = false;
        }
        else
        {
            SelectedItems.Add(cell);
            cell.SaleSelected = true;
        }

        DXItemCell.SelectedCell = null;
        GgLabel.Text = SaleTotal().ToString("N0");
        SellButton.Enabled = true;
        SellButton.TooltipText = SelectedItems.Count == 1 ? "出售" : "全部出售";
        return true;
    }

    private long SaleTotal()
    {
        decimal rate = _primaryCurrency?.ExchangeRate > 0M ? _primaryCurrency.ExchangeRate : 1M;
        return SelectedItems.Where(x => x?.Item != null)
            .Sum(x => (long)(x.Item.Price(x.Item.Count) / rate));
    }

    private void SellSelected()
    {
        if (!IsSellMode || GameScene.Game?.IsObserver == true) return;
        var candidates = SelectedItems.Count > 0
            ? SelectedItems
            : (Grid?.Cells ?? Array.Empty<DXItemCell>())
                .Where(x => x?.Item != null && (SellableItemTypes.Count == 0 || SellableItemTypes.Contains(x.Item.Info.ItemType)));
        var links = candidates.Where(x => x?.Item != null && !x.Locked &&
                !x.Item.Flags.HasFlag(UserItemFlags.Locked) &&
                !x.Item.Flags.HasFlag(UserItemFlags.Marriage) &&
                !x.Item.Flags.HasFlag(UserItemFlags.Worthless) &&
                x.Item.Info?.CanSell == true)
            .Select(x => new CellLinkInfo { GridType = GridType.Inventory, Slot = x.Slot, Count = x.Item.Count })
            .ToList();
        if (links.Count == 0) return;

        foreach (var link in links)
        {
            var cell = Grid?.Cells?.FirstOrDefault(x => x.Slot == link.Slot);
            if (cell == null) continue;
            cell.Locked = true;
            cell.UpdateBorder();
            _pendingSellLinks.Add(link);
        }
        foreach (var cell in SelectedItems) cell.SaleSelected = false;
        SelectedItems.Clear();
        SellButton.Enabled = true;
        GameScene.Game?.SendNPCSell(links);
    }

    public void ItemsChanged(IEnumerable<CellLinkInfo> links, bool success)
    {
        var changed = new HashSet<int>((links ?? Enumerable.Empty<CellLinkInfo>())
            .Where(x => x?.GridType == GridType.Inventory).Select(x => x.Slot));
        if (changed.Count == 0) return;
        foreach (var cell in Grid?.Cells ?? Array.Empty<DXItemCell>())
        {
            if (cell != null && changed.Contains(cell.Slot))
            {
                cell.SaleSelected = false;
                if (!success) cell.Locked = false;
                cell.UpdateBorder();
            }
        }
        _pendingSellLinks.RemoveAll(x => changed.Contains(x.Slot));
    }

    private void ClearSaleSelection()
    {
        foreach (var cell in SelectedItems) if (cell != null) cell.SaleSelected = false;
        SelectedItems.Clear();
        GgLabel.Text = "0";
        DXItemCell.SelectedCell = null;
    }
}
