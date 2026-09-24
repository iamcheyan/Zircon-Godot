using System;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 物品网格 (移植自 Client/Controls/DXItemGrid.cs):
/// 按 GridSize 创建 DXItemCell 数组, 支持滚动 (ScrollValue/VisibleHeight,
/// Storage/PartsStorage 用)。格子直接绑定同一个底层 ItemGrid 数组。
/// </summary>
public partial class DXItemGrid : DXControl
{
    private Vector2I _gridSize;
    public Vector2I GridSize
    {
        get => _gridSize;
        set
        {
            if (_gridSize == value) return;
            _gridSize = value;
            UpdateSize();
        }
    }

    private float _gridPadding = 1f;
    public float GridPadding
    {
        get => _gridPadding;
        set
        {
            if (_gridPadding == value) return;
            _gridPadding = value;
            UpdateSize();
        }
    }

    private int _visibleHeight = int.MaxValue;
    public int VisibleHeight
    {
        get => _visibleHeight;
        set
        {
            if (_visibleHeight == value) return;
            _visibleHeight = value;
            UpdateSize();
        }
    }

    private int _scrollValue;
    public int ScrollValue
    {
        get => _scrollValue;
        set
        {
            int v = Math.Max(0, Math.Min(value, Math.Max(0, GridSize.Y - VisibleHeight)));
            if (_scrollValue == v) return;
            _scrollValue = v;
            UpdateGridDisplay();
        }
    }

    private GridType _gridType;
    public GridType GridType
    {
        get => _gridType;
        set
        {
            if (_gridType == value) return;
            _gridType = value;
            if (Cells == null) return;
            foreach (var cell in Cells)
            {
                if (cell == null) continue;
                cell.GridType = value;
                cell.RefreshItem();
            }
        }
    }
    private ClientUserItem[] _itemGrid;
    public ClientUserItem[] ItemGrid
    {
        get => _itemGrid;
        set
        {
            _itemGrid = value;
            // 格子在网格构造时就已经创建；旧版 DXItemGrid 的 ItemGrid
            // 变更会让所有 DXItemCell 继续指向同一数组，不能只更新网格自身。
            if (Cells == null) return;
            RebuildLegacyFootprints();
            foreach (var cell in Cells)
            {
                if (cell == null) continue;
                cell.ItemGrid = value;
                cell.RefreshItem();
            }
        }
    }
    public bool Linked;
    /// <summary>原版腰带网格的固定槽位分隔线。</summary>
    public bool ShowCellDividers;
    public bool AllowLink;
    private bool _readOnly;
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value) return;
            _readOnly = value;
            if (Cells == null) return;
            foreach (var cell in Cells)
                if (cell != null) cell.ReadOnly = value;
        }
    }

    /// <summary>
    /// EI F280 inventory uses a six-column cell identity table separate from
    /// the 46 item records. The modern protocol only carries the record slot,
    /// so the legacy view reconstructs first-fit placement from item frames.
    /// </summary>
    public bool UseLegacyFootprints { get; set; }
    /// <summary>网格内图标使用的图库；EI legacy 背包切到 Inventory.wil。</summary>
    public LibraryFile ItemLibraryFile { get; set; } = LibraryFile.StoreItem;

    private int[] _legacyCellAnchors = Array.Empty<int>();


    public int ResolveOperationSlot(int cellIndex)
    {
        if (!UseLegacyFootprints || ItemGrid == null || cellIndex < 0) return cellIndex;
        if (cellIndex < _legacyCellAnchors.Length && _legacyCellAnchors[cellIndex] >= 0)
            return _legacyCellAnchors[cellIndex];
        if (cellIndex < ItemGrid.Length && ItemGrid[cellIndex] == null)
            return cellIndex;
        for (int slot = 0; slot < ItemGrid.Length; slot++)
            if (ItemGrid[slot] == null) return slot;
        return cellIndex;
    }

    public ClientUserItem GetItemForCell(int cellIndex)
    {
        if (!UseLegacyFootprints || ItemGrid == null || cellIndex < 0)
            return cellIndex >= 0 && ItemGrid != null && cellIndex < ItemGrid.Length ? ItemGrid[cellIndex] : null;
        int slot = cellIndex < _legacyCellAnchors.Length ? _legacyCellAnchors[cellIndex] : -1;
        return slot >= 0 && slot < ItemGrid.Length ? ItemGrid[slot] : null;
    }

    public bool IsLegacyFootprintPlaceholder(int cellIndex)
        => UseLegacyFootprints && cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length
            && _legacyCellAnchors[cellIndex] >= 0 && _legacyCellAnchors[cellIndex] != cellIndex;

    /// <summary>Returns the rows required by first-fit footprint placement.</summary>
    public int GetLegacyRequiredRows(int minimumRows = 6)
    {
        if (!UseLegacyFootprints || ItemGrid == null) return Math.Max(1, minimumRows);
        int rows = Math.Max(minimumRows, ItemGrid.Length);
        var occupied = new bool[GridSize.X, rows];
        int maxRow = minimumRows;
        for (int slot = 0; slot < ItemGrid.Length; slot++)
        {
            var item = ItemGrid[slot];
            if (item?.Info == null) continue;
            GetLegacyFootprint(item, out int width, out int height);
            if (!TryPlace(occupied, width, height, out int x, out int y)) continue;
            MarkPlacement(occupied, x, y, width, height);
            maxRow = Math.Max(maxRow, y + height);
        }
        return maxRow;
    }

    private void RebuildLegacyFootprints()
    {
        if (!UseLegacyFootprints || Cells == null)
        {
            _legacyCellAnchors = Array.Empty<int>();
            return;
        }

        _legacyCellAnchors = new int[Cells.Length];
        Array.Fill(_legacyCellAnchors, -1);
        if (ItemGrid == null || GridSize.X <= 0 || GridSize.Y <= 0) return;

        var occupied = new bool[GridSize.X, GridSize.Y];
        for (int slot = 0; slot < ItemGrid.Length; slot++)
        {
            var item = ItemGrid[slot];
            if (item?.Info == null) continue;
            GetLegacyFootprint(item, out int width, out int height);
            if (!TryPlace(occupied, width, height, out int x, out int y)) continue;
            MarkPlacement(occupied, x, y, width, height);
            for (int row = y; row < y + height; row++)
                for (int col = x; col < x + width; col++)
                    _legacyCellAnchors[row * GridSize.X + col] = slot;
        }
    }

    private static bool TryPlace(bool[,] occupied, int width, int height, out int x, out int y)
    {
        int columns = occupied.GetLength(0);
        int rows = occupied.GetLength(1);
        width = Math.Min(width, columns);
        for (y = 0; y + height <= rows; y++)
        {
            for (x = 0; x + width <= columns; x++)
            {
                bool free = true;
                for (int row = y; row < y + height && free; row++)
                    for (int col = x; col < x + width; col++)
                        if (occupied[col, row]) { free = false; break; }
                if (free) return true;
            }
        }
        x = y = -1;
        return false;
    }

    private static void MarkPlacement(bool[,] occupied, int x, int y, int width, int height)
    {
        for (int row = y; row < y + height; row++)
            for (int col = x; col < x + width; col++)
                occupied[col, row] = true;
    }

    private static void GetLegacyFootprint(ClientUserItem item, out int width, out int height)
    {
        width = height = 1;
        if (item?.Info == null) return;
        // EI 背包物品记录的 frame WORD 由 Inventory.wil selector 绘制；
        // 现代 StoreItem.Zl 不能作为旧版 footprint 尺寸的同号替代。
        var texture = MirSkin.GetTexture(LibraryFile.Inventory, item.Info.Image);
        if (texture == null) return;
        Vector2 size = texture.GetSize();
        width = Math.Max(1, ((int)size.X + DXItemCell.CellWidth - 1) / DXItemCell.CellWidth);
        height = Math.Max(1, ((int)size.Y + DXItemCell.CellHeight - 1) / DXItemCell.CellHeight);
    }

    public int LegacyFootprintAnchor(int cellIndex)
        => cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length ? _legacyCellAnchors[cellIndex] : -1;

    public DXItemCell[] Cells;

    public DXItemCell this[int slot] => Cells[slot];

    private float Step => UseLegacyFootprints ? DXItemCell.CellWidth : DXItemCell.CellWidth - 1 + (GridPadding * 2);

    private void UpdateSize()
    {
        Size = new Vector2(
            GridSize.X * Step + 1,
            Math.Min(GridSize.Y, VisibleHeight) * Step + 1);
        CreateGrid();
        QueueRedraw();
    }

    public void CreateGrid()
    {
        if (Cells != null)
        {
            foreach (var cell in Cells)
            {
                if (cell != null) cell.Dispose();
            }
            Cells = null;
        }

        int count = GridSize.X * GridSize.Y;
        Cells = new DXItemCell[count];

        for (int y = 0; y < GridSize.Y; y++)
        {
            for (int x = 0; x < GridSize.X; x++)
            {
                int slot = y * GridSize.X + x;
                var cell = new DXItemCell
                {
                    Location = new Vector2I(
                        (int)(x * Step + GridPadding),
                        (int)(y * Step + GridPadding)),
                    GridIndex = slot,
                    Slot = slot,
                    ItemLibraryFile = this.ItemLibraryFile,
                    ItemGrid = _itemGrid,
                    GridType = GridType,
                    ReadOnly = ReadOnly,
                };
                AddControl(cell);
                Cells[slot] = cell;
            }
        }
        RebuildLegacyFootprints();
        UpdateGridDisplay();
    }

    /// <summary>滚动后重新摆放格子 (隐藏行外格子)</summary>
    public void UpdateGridDisplay()
    {
        if (Cells == null) return;

        for (int y = 0; y < GridSize.Y; y++)
        {
            for (int x = 0; x < GridSize.X; x++)
            {
                var cell = Cells[y * GridSize.X + x];
                if (cell == null) continue;

                if (y < ScrollValue || y >= ScrollValue + VisibleHeight)
                {
                    cell.Visible = false;
                    continue;
                }

                cell.Visible = true;
                cell.Location = new Vector2I(
                    (int)(x * Step + GridPadding),
                    (int)((y - ScrollValue) * Step + GridPadding));
            }
        }
    }

    /// <summary>重刷所有格子显示 (数组批量变更后调用)</summary>
    public void RefreshGrid()
    {
        if (Cells == null) return;
        RebuildLegacyFootprints();
        foreach (var cell in Cells)
            cell?.RefreshItem();

    }
    public override void _Draw()
    {
        base._Draw();
        if (!ShowCellDividers || Cells == null || GridSize.X <= 1) return;

        float step = DXItemCell.CellWidth - 1 + (GridPadding * 2);
        var colour = new Color(0.39f, 0.325f, 0.196f, 0.95f);
        for (int x = 1; x < GridSize.X; x++)
        {
            float lineX = x * step;
            DrawLine(new Vector2(lineX, 1), new Vector2(lineX, Size.Y - 2), colour, 1f);
        }
    }
}
