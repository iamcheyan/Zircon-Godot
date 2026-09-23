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
    private int _slotCount = -1;
    /// <summary>Optional occupied grid capacity when the final row is partial.</summary>
    public int SlotCount
    {
        get => _slotCount < 0 ? GridSize.X * GridSize.Y : _slotCount;
        set
        {
            int next = value < 0 ? -1 : value;
            if (_slotCount == next) return;
            _slotCount = next;
            UpdateSize();
        }
    }

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
    public event EventHandler<MouseWheelEventArgs> GridMouseWheel;
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

    public DXItemCell[] Cells;

    public DXItemCell this[int slot] => Cells[slot];

    private float Step => DXItemCell.CellWidth - 1 + (GridPadding * 2);

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

        int count = Math.Min(GridSize.X * GridSize.Y, SlotCount);
        Cells = new DXItemCell[count];

        for (int slot = 0; slot < count; slot++)
        {
            int x = slot % GridSize.X;
            int y = slot / GridSize.X;
            var cell = new DXItemCell
            {
                Location = new Vector2I(
                    (int)(x * Step + GridPadding),
                    (int)(y * Step + GridPadding)),
                Slot = slot,
                HostGrid = this,
                ItemGrid = _itemGrid,
                GridType = GridType,
                ReadOnly = ReadOnly,
            };
            cell.MouseWheel += ForwardMouseWheel;
            AddControl(cell);
            Cells[slot] = cell;
        }

        UpdateGridDisplay();
    }

    private void ForwardMouseWheel(object sender, MouseWheelEventArgs e)
    {
        GridMouseWheel?.Invoke(this, e);
    }

    /// <summary>滚动后重新摆放格子 (隐藏行外格子)</summary>
    public void UpdateGridDisplay()
    {
        if (Cells == null) return;

        for (int slot = 0; slot < Cells.Length; slot++)
        {
            var cell = Cells[slot];
            if (cell == null) continue;
            int y = slot / GridSize.X;
            int x = slot % GridSize.X;

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

    /// <summary>重刷所有格子显示 (数组批量变更后调用)</summary>
    public void RefreshGrid()
    {
        if (Cells == null) return;
        foreach (var cell in Cells)
        {
            cell?.RefreshItem();
        }
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
