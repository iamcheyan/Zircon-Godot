using System;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 仓库窗口 (移植自 Client/Scenes/Views/StorageDialog.cs):
/// Interface 121 底图, 10 列可滚动网格 (VisibleHeight=10, StorageSize 决定行数)。
/// 主仓库与碎片仓库均可切换，分别使用原版 Storage/PartsStorage 网格和滚动条。
/// </summary>
public partial class StorageDialog : DXWindow
{
    private DXImageControl _background;
    private DXButton _closeButton;
    public DXItemGrid Grid;
    public DXItemGrid PartGrid;
    public DXItemCell[] StorageCells => Grid?.Cells ?? Array.Empty<DXItemCell>();
    public DXVScrollBar ScrollBar;
    public DXVScrollBar PartScrollBar;
    public DXButton SortButton;
    private DXButton _storageTab;
    private DXButton _partsTab;
    private bool _partsVisible;
    private bool _legacyLayout;

    public StorageDialog()
    {
        // 原版 StorageDialog 直接使用 Interface 121 背景图。
        HasTitle = false;
        Movable = true;
        Text = Lang.StorageDialogTitle;
        Size = new Vector2I(410, 479);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 121,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        // 原版标题是背景图上的独立 DXLabel，不是 DXWindow 标题栏。
        AddControl(new DXLabel
        {
            Text = Lang.StorageDialogTitle,
            FontSize = 10,
            TextColour = new Color(1f, .85f, .3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(120, 8),
            Size = new Vector2I(170, 18),
            IsControl = false,
        });

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => { CancelLinks(); WindowManager.Close(this); };
        AddControl(_closeButton);

        SortButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 364,
            Location = new Vector2I((int)Size.X - 47, 41),
        };
        SortButton.MouseClick += SortStorage;
        AddControl(SortButton);

        _storageTab = CreateTab(Lang.StorageDialogTitle, 10, false);
        _partsTab = CreateTab(Lang.StorageUi116Label, 72, true);

        Grid = new DXItemGrid
        {
            GridSize = new Vector2I(10, 1),
            Location = new Vector2I(19, 72),
            GridType = GridType.Storage,
            ItemGrid = null, // GameScene 注入
            VisibleHeight = 10,
            Border = false,
            GridPadding = 1,
        };
        AddControl(Grid);

        PartGrid = new DXItemGrid
        {
            GridSize = new Vector2I(10, 10),
            Location = new Vector2I(19, 72),
            GridType = GridType.PartsStorage,
            VisibleHeight = 10,
            Border = false,
            GridPadding = 1,
            Visible = false,
        };
        AddControl(PartGrid);

        ScrollBar = new DXVScrollBar
        {
            // 原版位置 = Grid.Location.X + PartGrid.Size.Width；不能使用
            // 初始 1 行 Grid 的宽度，否则创建窗口时滚动条会落在 x=58。
            Location = new Vector2I(19 + (int)PartGrid.Size.X, 72),
            Size = new Vector2I(14, 349),
            VisibleSize = 10,
            Change = 1,
        };
        ScrollBar.ValueChanged += (o, e) => Grid.ScrollValue = ScrollBar.Value;
        AddControl(ScrollBar);

        PartScrollBar = new DXVScrollBar
        {
            Location = new Vector2I(19 + (int)PartGrid.Size.X, 72),
            Size = new Vector2I(14, 349),
            VisibleSize = 10,
            Change = 1,
            Visible = false,
        };
        PartScrollBar.ValueChanged += (o, e) => PartGrid.ScrollValue = PartScrollBar.Value;
        AddControl(PartScrollBar);

        BindWheel();
    }

    /// <summary>滚轮滚动仓库 (cells 在 RefreshStorage 重建后需重绑)</summary>
    private void BindWheel()
    {
        foreach (var cell in Grid.Cells)
        {
            cell.MouseWheel -= ScrollBar.DoMouseWheel;
            cell.MouseWheel += ScrollBar.DoMouseWheel;
        }
        foreach (var cell in PartGrid.Cells)
        {
            cell.MouseWheel -= PartScrollBar.DoMouseWheel;
            cell.MouseWheel += PartScrollBar.DoMouseWheel;
        }
    }

    /// <summary>StorageSize 包/登录后调用: 行数 = ceil(StorageSize/10)</summary>
    public void RefreshStorage()
    {
        var game = GameScene.Game;
        int size = game?.StorageSize ?? 100;
        Grid.GridSize = new Vector2I(10, Math.Max(10, (int)Math.Ceiling(size / 10f)));
        ScrollBar.MaxValue = Grid.GridSize.Y;
        PartGrid.GridSize = new Vector2I(10, Math.Max(10, (int)Math.Ceiling(Globals.StorageSize / 10f)));
        PartScrollBar.MaxValue = PartGrid.GridSize.Y;
        ApplyCapacity(size);
        Grid.ScrollValue = ScrollBar.Value;
        PartGrid.ScrollValue = PartScrollBar.Value;
        Grid.RefreshGrid();
        PartGrid.RefreshGrid();
        BindWheel();
        if (_legacyLayout)
            ApplyLegacyEiLayout();
    }

    private void ApplyCapacity(int storageSize)
    {
        foreach (var cell in Grid?.Cells ?? Array.Empty<DXItemCell>())
            cell.Enabled = cell.Slot < storageSize;
        foreach (var cell in PartGrid?.Cells ?? Array.Empty<DXItemCell>())
            cell.Enabled = true;
    }

    public bool AuditCapacity(int storageSize, out string details)
    {
        int size = Math.Max(1, storageSize);
        Grid.GridSize = new Vector2I(10, Math.Max(10, (int)Math.Ceiling(size / 10f)));
        ApplyCapacity(size);
        bool edgeEnabled = Grid.Cells.Length >= size && Grid.Cells[size - 1].Enabled;
        bool overflowDisabled = size < Grid.Cells.Length && !Grid.Cells[size].Enabled;
        details = $"capacity={size} edge={edgeEnabled} overflow={overflowDisabled}";
        return edgeEnabled && overflowDisabled;
    }

    public bool AuditCancelLinks(out string details)
    {
        var cell = Grid.Cells.FirstOrDefault();
        if (cell == null) { details = "no storage cell"; return false; }
        var items = new ClientUserItem[1];
        items[0] = new ClientUserItem { Count = 1 };
        cell.ItemGrid = items;
        cell.Slot = 0;
        cell.LinkedSourceGrid = GridType.Inventory;
        cell.LinkedSourceSlot = 4;
        cell.Selected = true;
        CancelLinks();
        bool cleared = items[0] == null && cell.LinkedSourceSlot < 0 && !cell.Selected && DXItemCell.SelectedCell != cell;
        details = $"cleared={cleared} sourceSlot={cell.LinkedSourceSlot} selected={cell.Selected}";
        return cleared;
    }

    private DXButton CreateTab(string text, int x, bool parts)
    {
        var tab = new DXButton
        {
            Text = text,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(58, 24),
            Location = new Vector2I(x, 61),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = parts ? DXButton.ButtonType.DeselectedTab : DXButton.ButtonType.SelectedTab,
        };
        tab.MouseClick += (o, e) => SelectTab(parts);
        AddControl(tab);
        return tab;
    }

    private void SelectTab(bool parts)
    {
        _partsVisible = parts;
        _storageTab.Type = parts ? DXButton.ButtonType.DeselectedTab : DXButton.ButtonType.SelectedTab;
        _partsTab.Type = parts ? DXButton.ButtonType.SelectedTab : DXButton.ButtonType.DeselectedTab;
        Grid.Visible = !parts;
        ScrollBar.Visible = !parts;
        PartGrid.Visible = parts;
        PartScrollBar.Visible = parts;
    }

    public bool AuditLayout(out string details)
    {
        bool geometry = Size == new Vector2I(410, 479)
            && Grid.Location == new Vector2(19, 72)
            && PartGrid.Location == new Vector2(19, 72)
            && ScrollBar.Location == new Vector2(390, 72)
            && PartScrollBar.Location == new Vector2(390, 72)
            && ScrollBar.VisibleSize == 10
            && PartScrollBar.VisibleSize == 10;
        SelectTab(false);
        bool storage = Grid.Visible && ScrollBar.Visible && !PartGrid.Visible && !PartScrollBar.Visible;
        SelectTab(true);
        bool parts = !Grid.Visible && !ScrollBar.Visible && PartGrid.Visible && PartScrollBar.Visible;
        SelectTab(false);
        details = $"size={Size} grid={Grid.Location}/{Grid.Size} scroll={ScrollBar.Location}/{ScrollBar.MaxValue} pages=storage:{storage},parts:{parts}";
        return geometry && storage && parts;
    }

    /// <summary>
    /// 旧版 EI 仓库分支：GameInter F1001 的 205×205 紧凑网格。
    /// 证据中的 12 个槽为 4×3，原点相对根窗口为 (22,43)，步长 38。
    /// 商店 F1000 与仓库状态分支不同，不能共用此方法。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyLayout = true;
        Size = new Vector2I(205, 205);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 1001;
        _background.FixedSize = true;
        _background.StretchImage = false;
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 1001);
        _background.Location = Vector2I.Zero;
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(177, 176);
        _closeButton.Size = new Vector2I(28, 26);
        Grid.GridSize = new Vector2I(4, 3);
        Grid.Location = new Vector2I(22, 43);
        Grid.VisibleHeight = 3;
        Grid.RefreshGrid();
        PartGrid.Visible = false;
        ScrollBar.Visible = false;
        PartScrollBar.Visible = false;
        _storageTab.Visible = false;
        _partsTab.Visible = false;
        SortButton.Visible = false;
        UpdateClientAreaForLegacySkin();
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        RefreshStorage();
        bool ok = Size == new Vector2I(205, 205)
            && _background.LibraryFile == LibraryFile.GameInter && _background.Index == 1001
            && Grid.GridSize == new Vector2I(4, 3)
            && Grid.Location == new Vector2(22, 43)
            && !PartGrid.Visible && !ScrollBar.Visible;
        details = $"size={Size} frame={_background.Index} grid={Grid.GridSize}@{Grid.Location} compact={!PartGrid.Visible && !ScrollBar.Visible}";
        return ok;
    }

    public void CancelLinks()
    {
        foreach (var cell in Grid?.Cells ?? Array.Empty<DXItemCell>())
            CancelLink(cell);
        foreach (var cell in PartGrid?.Cells ?? Array.Empty<DXItemCell>())
            CancelLink(cell);
    }

    private static void CancelLink(DXItemCell cell)
    {
        if (cell == null) return;
        if (cell.LinkedSourceSlot >= 0)
        {
            GameScene.Game?.UnlockItemLink(new CellLinkInfo { GridType = cell.LinkedSourceGrid, Slot = cell.LinkedSourceSlot });
            if (cell.ItemGrid != null && cell.Slot >= 0 && cell.Slot < cell.ItemGrid.Length)
                cell.ItemGrid[cell.Slot] = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
            cell.RefreshItem();
        }
        if (DXItemCell.SelectedCell == cell) DXItemCell.SelectedCell = null;
    }

    public override void Close()
    {
        CancelLinks();
        base.Close();
    }

    private void SortStorage(object sender, EventArgs e)
    {
        var confirm = new ConfirmDialog(Lang.StorageStorageLabel, Lang.StorageConfirmLabel, () =>
            GameScene.Game?.SendItemSort(_partsVisible ? GridType.PartsStorage : GridType.Storage));
        WindowManager.Open(confirm, GameScene.Game?.UILayer ?? GetParent());
    }
}
