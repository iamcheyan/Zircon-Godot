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
            RefreshLegacyHighlight();
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

    // EI 占用表模型（bag+0x2C4）：每个格子一个标记——0xFFFF=空，否则存槽号，
    // 原点格额外 +0x3E8(1000) 标记「这是该 footprint 的左上角，画图标」。
    // 对应这里：_legacyCellAnchors[cell]=槽号（所有被覆盖格），
    // _legacyCellOrigins[cell]=是否原点格。物品槽号与格子号通常不相等，
    // 所以绝不能用 anchor==cellIndex 判原点。
    private int[] _legacyCellAnchors = Array.Empty<int>();
    private bool[] _legacyCellOrigins = Array.Empty<bool>();
    private int[] _legacySlotOriginCell = Array.Empty<int>();

    // 整块 footprint 的高亮层（悬停/选中时画在占用区域外面一圈，而不是单格）。
    private DXControl _legacyHighlight;
    private int _legacyHoverCell = -1;
    private int _legacyHighlightDrawnFor = int.MinValue;


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

    /// <summary>
    /// 服务端槽号 → 显示该记录的格索引。
    ///
    /// 非 legacy footprint 模式下槽号即格索引；legacy 模式下物品按 first-fit
    /// 摆放，槽号 6 的记录可能显示在格 0，两者**不相等**。任何按服务端槽号
    /// 去索引 <c>Cells[]</c> 的代码都必须先经过这里，否则会操作到另一格
    /// （实测：丢弃物品后锁住的是显示格，回包却解锁了 <c>cells[槽号]</c>，
    /// 真正被锁的格永久卡死、无法再移动或装备）。
    /// 返回 -1 表示该槽号当前没有可见格。
    /// </summary>
    public int ResolveSlotCell(int slot)
    {
        if (!UseLegacyFootprints) return slot;
        if (slot < 0 || slot >= _legacySlotOriginCell.Length) return -1;
        return _legacySlotOriginCell[slot];
    }

    public ClientUserItem GetItemForCell(int cellIndex)
    {
        if (!UseLegacyFootprints || ItemGrid == null || cellIndex < 0)
            return cellIndex >= 0 && ItemGrid != null && cellIndex < ItemGrid.Length ? ItemGrid[cellIndex] : null;
        int slot = cellIndex < _legacyCellAnchors.Length ? _legacyCellAnchors[cellIndex] : -1;
        return slot >= 0 && slot < ItemGrid.Length ? ItemGrid[slot] : null;
    }

    /// <summary>覆盖某格的那条记录的 footprint 尺寸（以格为单位；非 footprint 返回 1×1）。</summary>
    public void GetLegacyFootprintSize(int cellIndex, out int width, out int height)
    {
        width = height = 1;
        if (!UseLegacyFootprints || ItemGrid == null) return;
        if (cellIndex < 0 || cellIndex >= _legacyCellAnchors.Length) return;
        int slot = _legacyCellAnchors[cellIndex];
        if (slot < 0 || slot >= ItemGrid.Length) return;
        var item = ItemGrid[slot];
        if (item?.Info == null) return;
        GetLegacyFootprint(item, out width, out height);
    }

    public bool IsLegacyFootprintPlaceholder(int cellIndex)
        => UseLegacyFootprints && cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length
            && _legacyCellAnchors[cellIndex] >= 0
            && (cellIndex >= _legacyCellOrigins.Length || !_legacyCellOrigins[cellIndex]);

    /// <summary>
    /// first-fit footprint 摆放所需行数。
    ///
    /// 注意：背包数组是**按最大 slot 扩容**的（`EnsureInventoryCapacity`），
    /// 所以 `ItemGrid.Length` 反映的是「最高槽位 + 1」而不是行数。此前用
    /// `rows = max(minimumRows, ItemGrid.Length)` 会把稀疏 slot 造成的空洞
    /// （实测 1,13,14,19,25,...,115,186 → 数组长 187）直接当成 187 行，
    /// 网格被撑到 187×6=1112 格，而 F250 背景只有 6 行可视 —— 物品排布
    /// 整体错乱、滚动范围失真。改为：从可视行数起步，**放不下就翻倍扩容**，
    /// 行数完全由实际摆放结果决定。
    /// </summary>
    public int GetLegacyRequiredRows(int minimumRows = 6)
    {
        if (!UseLegacyFootprints || ItemGrid == null) return Math.Max(1, minimumRows);

        int columns = Math.Max(1, GridSize.X);
        int rows = Math.Max(1, minimumRows);
        int maxRow = minimumRows;

        // 物品数即最坏情况所需格数（每件至少占 1 格），据此给初始容量，避免反复翻倍。
        int itemCount = 0;
        for (int i = 0; i < ItemGrid.Length; i++)
            if (ItemGrid[i]?.Info != null) itemCount++;
        rows = Math.Max(rows, (int)Math.Ceiling(itemCount / (double)columns));

        while (true)
        {
            var occupied = new bool[columns, rows];
            maxRow = minimumRows;
            bool placedAll = true;
            for (int slot = 0; slot < ItemGrid.Length; slot++)
            {
                var item = ItemGrid[slot];
                if (item?.Info == null) continue;
                GetLegacyFootprint(item, out int width, out int height);
                if (!TryPlace(occupied, width, height, out _, out int y))
                {
                    placedAll = false;
                    break;
                }
                MarkPlacement(occupied, 0, y, width, height);
                maxRow = Math.Max(maxRow, y + height);
            }
            if (placedAll) break;

            int grown = rows * 2;
            // 兜底：再放不下说明 footprint 非法（超大物品），此时保持当前 rows，
            // 避免无限扩张。
            if (grown > rows && grown <= 4096) { rows = grown; continue; }
            break;
        }
        return maxRow;
    }

    private void RebuildLegacyFootprints()
    {
        if (!UseLegacyFootprints || Cells == null)
        {
            _legacyCellAnchors = Array.Empty<int>();
            _legacyCellOrigins = Array.Empty<bool>();
            _legacySlotOriginCell = Array.Empty<int>();
            return;
        }

        _legacyCellAnchors = new int[Cells.Length];
        Array.Fill(_legacyCellAnchors, -1);
        _legacyCellOrigins = new bool[Cells.Length];
        _legacySlotOriginCell = new int[Math.Max(Cells.Length, ItemGrid?.Length ?? 0)];
        Array.Fill(_legacySlotOriginCell, -1);
        if (ItemGrid == null || GridSize.X <= 0 || GridSize.Y <= 0) return;

        var occupied = new bool[GridSize.X, GridSize.Y];
        for (int slot = 0; slot < ItemGrid.Length; slot++)
        {
            var item = ItemGrid[slot];
            if (item?.Info == null) continue;
            GetLegacyFootprint(item, out int width, out int height);
            if (!TryPlace(occupied, width, height, out int x, out int y)) continue;
            MarkPlacement(occupied, x, y, width, height);
            // 原点格：整条记录只在这里画一次图标（其它覆盖格画图标切片/不重复）。
            int originCell = y * GridSize.X + x;
            _legacyCellOrigins[originCell] = true;
            if (slot < _legacySlotOriginCell.Length) _legacySlotOriginCell[slot] = originCell;
            for (int row = y; row < y + height; row++)
                for (int col = x; col < x + width; col++)
                    _legacyCellAnchors[row * GridSize.X + col] = slot;
        }
    }

    /// <summary>某格所属记录的放置信息（槽号、原点格坐标、footprint 格数）。</summary>
    public bool GetLegacyPlacement(int cellIndex, out int slot, out int originCol,
        out int originRow, out int width, out int height)
    {
        slot = -1; originCol = originRow = 0; width = height = 1;
        if (!UseLegacyFootprints || GridSize.X <= 0) return false;
        if (cellIndex < 0 || cellIndex >= _legacyCellAnchors.Length) return false;
        slot = _legacyCellAnchors[cellIndex];
        if (slot < 0 || slot >= _legacySlotOriginCell.Length) { slot = -1; return false; }
        int originCell = _legacySlotOriginCell[slot];
        if (originCell < 0) { slot = -1; return false; }
        originCol = originCell % GridSize.X;
        originRow = originCell / GridSize.X;
        GetLegacyFootprintSize(cellIndex, out width, out height);
        return true;
    }

    /// <summary>该格是否为某 footprint 的原点格（EI 标记 &gt;= 0x3E8 的那种）。</summary>
    public bool IsLegacyOriginCell(int cellIndex)
        => UseLegacyFootprints && cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length
            && _legacyCellAnchors[cellIndex] >= 0
            && cellIndex < _legacyCellOrigins.Length && _legacyCellOrigins[cellIndex];

    /// <summary>该格是否被某条记录占用（原点或占位都算）。</summary>
    public bool IsLegacyCovered(int cellIndex)
        => UseLegacyFootprints && cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length
            && _legacyCellAnchors[cellIndex] >= 0;

    /// <summary>格子把鼠标进/出转给网格，以便整块 footprint 高亮。</summary>
    public void NotifyLegacyHover(int cellIndex, bool entered)
    {
        if (!UseLegacyFootprints) return;
        if (entered) _legacyHoverCell = cellIndex;
        else if (_legacyHoverCell == cellIndex) _legacyHoverCell = -1;
        RefreshLegacyHighlight();
    }

    private void EnsureLegacyHighlight()
    {
        if (_legacyHighlight != null) return;
        _legacyHighlight = new DXControl { PassThrough = true, IsControl = false, ZIndex = 100 };
        AddControl(_legacyHighlight);
    }

    /// <summary>
    /// 把整块高亮框对准当前悬停/选中物品的 footprint（EI：选中/悬停高亮覆盖整个
    /// 占用区，而不是单格）。越出视口的行不画。
    /// </summary>
    public void RefreshLegacyHighlight()
    {
        if (!UseLegacyFootprints)
        {
            if (_legacyHighlight != null) _legacyHighlight.Visible = false;
            return;
        }
        EnsureLegacyHighlight();

        int cell = _legacyHoverCell;
        var selected = DXItemCell.SelectedCell;
        if (selected != null && ReferenceEquals(selected.HostGrid, this)) cell = selected.GridIndex;
        if (cell < 0 || !IsLegacyCovered(cell)
            || !GetLegacyPlacement(cell, out _, out int oc, out int orow, out int w, out int h))
        {
            _legacyHighlight.Visible = false;
            return;
        }
        if (orow + h <= ScrollValue || orow >= ScrollValue + VisibleHeight)
        {
            _legacyHighlight.Visible = false;
            return;
        }

        _legacyHighlight.Location = new Vector2I((int)(oc * Step + GridPadding),
            (int)((orow - ScrollValue) * Step + GridPadding));
        _legacyHighlight.Size = new Vector2I((int)(w * Step), (int)(h * Step));
        _legacyHighlight.Border = true;
        _legacyHighlight.BorderColour = Colors.Lime;
        _legacyHighlight.BackColour = new Color(1f, 0.6f, 0.2f, 0.12f);
        _legacyHighlight.Visible = true;
        _legacyHighlight.QueueRedraw();
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

        // 优先从 LegacyEI 原版 Inventory.wil 读取经典多格大图标（衣服 2x3, 武器 1x3/1x4 等）；
        // 缺图或索引超 1440 的后期/杂物道具，平滑回退至 StoreItem。
        var texture = MirSkin.GetTexture(LibraryFile.Inventory, item.Info.Image);
        if (texture == null)
        {
            texture = MirSkin.GetTexture(LibraryFile.StoreItem, item.Info.Image);
        }
        if (texture == null) return;

        Vector2 size = texture.GetSize();
        width = Math.Max(1, ((int)size.X + DXItemCell.CellWidth - 1) / DXItemCell.CellWidth);
        height = Math.Max(1, ((int)size.Y + DXItemCell.CellHeight - 1) / DXItemCell.CellHeight);
    }

    public int LegacyFootprintAnchor(int cellIndex)
        => cellIndex >= 0 && cellIndex < _legacyCellAnchors.Length ? _legacyCellAnchors[cellIndex] : -1;

    public DXItemCell[] Cells;

    public DXItemCell this[int slot] => Cells[slot];

    /// <summary>
    /// legacy 显式步距（&lt;=0 表示不启用，走原有公式）。
    ///
    /// 为什么不复用 <see cref="UseLegacyFootprints"/>：那个开关除了步距还会切换
    /// 格子图库与锚点映射（见 GetCellIndex / GetLegacyAnchor / GetVisibleRows），
    /// 只想改步距时会连带把语义带偏（它绑的是 Inventory 的 36px 素材）。
    ///
    /// 证据：
    ///  - 交易 F1050：cell 36x36、stride **36**（trade-window-render-evidence.json，
    ///    左栏首格 (21,48)..(201,264) 共 5 列，跨度 180 = 5x36）
    ///  - 仓库 state2：stride **0x26 = 38**（store-state-graph.json states[2].grid_rects，
    ///    cols 22/60/98/136、rows 43/81/119，间距 38）
    /// </summary>
    public int LegacyCellStep { get; set; }

    private float Step => LegacyCellStep > 0
        ? LegacyCellStep
        : UseLegacyFootprints ? DXItemCell.CellWidth : DXItemCell.CellWidth - 1 + (GridPadding * 2);

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
                    // 对齐 WinForms Client/Controls/DXItemGrid.CreateGrid()：
                    // Item/Slot/IsLegacyFootprintPlaceholder 都靠 HostGrid 才能
                    // 走 first-fit footprint，缺了它多格占用与占位格判定全部失效。
                    HostGrid = this,
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
        RefreshLegacyHighlight();
    }

    /// <summary>
    /// 内容变化后重建占用表（锚点/原点）。物品移动时 ItemGrid 引用和行数都可能不变，
    /// 只靠 GridSize 变化触发重建会留下**旧锚点**，表现为大件被截成单格、高亮变小。
    /// </summary>
    public void RefreshLegacyFootprints()
    {
        if (Cells == null) return;
        RebuildLegacyFootprints();
        RefreshLegacyHighlight();
    }

    /// <summary>重刷所有格子显示 (数组批量变更后调用)</summary>
    public void RefreshGrid()
    {
        if (Cells == null) return;
        RebuildLegacyFootprints();
        foreach (var cell in Cells)
            cell?.RefreshItem();
        RefreshLegacyHighlight();
    }
    public override void _Draw()
    {
        base._Draw();
        if (!ShowCellDividers || Cells == null || GridSize.X <= 1) return;

        // 与 Step 保持一致（含 legacy 显式步距），否则分隔线会与格子错位。
        float step = Step;
        var colour = new Color(0.39f, 0.325f, 0.196f, 0.95f);
        for (int x = 1; x < GridSize.X; x++)
        {
            float lineX = x * step;
            DrawLine(new Vector2(lineX, 1), new Vector2(lineX, Size.Y - 2), colour, 1f);
        }
    }
}
