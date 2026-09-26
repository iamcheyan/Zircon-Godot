using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

// 地图渲染视图: 加载 .map + 渲染地形层，可滚动
public partial class MapView : Node2D
{
    private string _mapPath;
    public MirMap Map { get; private set; }
    public string MapFileName { get; private set; }
    public int BackgroundIndex { get; private set; }
    public int MissingLibraryCount { get; private set; }
    public int MissingTextureCount { get; private set; }
    public int EmptyImageEntryCount { get; private set; }
    private readonly HashSet<string> _missingTextureKeys = new();

    const int CellWidth = 48;
    const int CellHeight = 32;
    private static float WorldScale => GameScene.WorldScale;

    // 当前视野中心（玩家位置）
    public int CenterX = 0;
    public int CenterY = 0;
    public int ViewRangeX = 12;
    public int ViewRangeY = 15;
    // 连续跟随玩家时的像素级摄像机偏移。CenterX/CenterY 仍是地图格索引，
    // 但移动期间不能让背景随索引瞬间跳整格。
    public Vector2 CameraOffset { get; set; } = Vector2.Zero;

    private double _animationTime;
    private int _mapAnimation;
    private readonly Dictionary<int, MapTerrainRow> _terrainRows = new();
    private readonly Dictionary<int, MapTerrainRow> _frontTerrainRows = new();
    private readonly Dictionary<int, MapTerrainRow> _blendTerrainRows = new();
    private readonly Dictionary<int, MapTerrainRow> _blendFrontTerrainRows = new();

    private const int ManualHeightOffset = 34;

    public override void _Ready()
    {
        var projectDir = ProjectSettings.GlobalizePath("res://");
        _mapPath = Path.GetFullPath(Path.Combine(projectDir, "..", "Debug", "Client", "Map"));
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (Map == null) return;
        _animationTime += delta;
        if (_animationTime < 0.1) return;
        _animationTime = 0;
        _mapAnimation++;
        foreach (var row in _terrainRows.Values)
            row.QueueRedraw();
        foreach (var row in _frontTerrainRows.Values)
            row.QueueRedraw();
        foreach (var row in _blendTerrainRows.Values)
            row.QueueRedraw();
        foreach (var row in _blendFrontTerrainRows.Values)
            row.QueueRedraw();
        QueueRedraw();
    }

    public void LoadMap(string mapFileName, int backgroundIndex = 0)
    {
        MapFileName = mapFileName;
        BackgroundIndex = backgroundIndex;
        MissingLibraryCount = 0;
        MissingTextureCount = 0;
        EmptyImageEntryCount = 0;
        _missingTextureKeys.Clear();
        _debugLogged = false;
        _warned = false;
        _countLogged = false;
        string full = Path.Combine(_mapPath, mapFileName + ".map");
        Map = new MirMap(full);
        GD.Print($"[MapView] 加载 {mapFileName}: {Map.Width}x{Map.Height}");
        SyncTerrainRows();
    }

    private bool _debugLogged;

    public override void _Draw()
    {
        if (Map == null) return;
        if (!_debugLogged)
        {
            _debugLogged = true;
            GD.Print($"[MapView] 首帧诊断: viewport={GetViewport().GetVisibleRect().Size} " +
                     $"center=({CenterX},{CenterY}) range=({ViewRangeX},{ViewRangeY})");
        }

        // 原版地图层与对象层使用不同的 Y 基线：地图地面不加一格，
        // 中/前景对象加一格，并以贴图底边对齐。下方额外保留空间，
        // 否则高建筑会在视野边缘被截断。
        int sx = Math.Max(0, CenterX - ViewRangeX - 4);
        int sy = Math.Max(0, CenterY - ViewRangeY - 4);
        int ex = Math.Min(Map.Width, CenterX + ViewRangeX + 5);
        int ey = Math.Min(Map.Height, CenterY + ViewRangeY + 26);

        Vector2 viewport = GetViewport().GetVisibleRect().Size / WorldScale;
        float offsetX = (viewport.X - CellWidth) / 2f - ViewRangeX * CellWidth + CameraOffset.X;
        float offsetY = (viewport.Y - CellHeight) / 2f - ViewRangeY * CellHeight - ManualHeightOffset + CameraOffset.Y;

        int drawn = 0;
        DrawMapBackground(viewport);
        // 背景层：.map 只在偶数格保存一项，不能和中/前景混在同一基线。
        for (int x = sx; x < ex; x++)
        {
            for (int y = sy; y < ey; y++)
            {
                ref var cell = ref Map.Cells[x, y];
                // 原版 Floor.OnClearTexture 对 BackImage 无 >0 检查：index 0
                // 是合法地砖（如 Wood_Tiles5c[0]，D201 上 2390 格）。仅当库
                // 缺失/空帧/解码失败时才由 DrawCell 跳过。
                if (x % 2 != 0 || y % 2 != 0) continue;
                float px = (x - CenterX + ViewRangeX) * CellWidth + offsetX;
                float py = (y - CenterY + ViewRangeY) * CellHeight + offsetY;
                if (DrawCell(cell.BackFile, cell.BackImage, px, py, false, false, 0)) drawn++;
            }
        }

        if (drawn == 0 && !_warned)
        {
            _warned = true;
            GD.PrintErr("[MapView] 警告: 视野内没有可绘制的格子!");
        }
        else if (!_countLogged)
        {
            _countLogged = true;
            GD.Print($"[MapView] 首帧绘制: {drawn} 格, viewport={GetViewport().GetVisibleRect().Size}");
            GD.Print($"[MapView] 贴图诊断: missingLibraries={MissingLibraryCount}, missingTextures={MissingTextureCount}, emptyImageEntries={EmptyImageEntryCount}");
            if (_missingTextureKeys.Count > 0)
                GD.Print($"[MapView] 缺失贴图键: {string.Join(", ", _missingTextureKeys.OrderBy(x => x))}");
        }
    }

    private bool _warned;
    private bool _countLogged;

    private int AnimatedIndex(int baseIndex, int animationFrame, out bool blend)
    {
        blend = false;
        if (animationFrame > 1 && animationFrame < 255)
        {
            int count = animationFrame & 0x0F;
            blend = (animationFrame & 0x80) != 0;
            if (count > 0) baseIndex += _mapAnimation % count;
        }
        return baseIndex;
    }

    private void DrawMapBackground(Vector2 viewport)
    {
        if (BackgroundIndex <= 0) return;
        var lib = LibraryCache.Get(LibraryFile.Background);
        if (lib == null || BackgroundIndex >= lib.Images.Length)
        {
            MissingLibraryCount++;
            return;
        }
        var tex = lib.GetImageTexture(BackgroundIndex);
        if (tex == null)
        {
            MissingTextureCount++;
            return;
        }
        DrawTextureRect(tex, new Rect2(Vector2.Zero, viewport), false);
    }

    private void SyncTerrainRows()
    {
        if (Map == null) return;
        int first = Math.Max(0, CenterY - ViewRangeY - 4);
        int last = Math.Min(Map.Height - 1, CenterY + ViewRangeY + 25);

        foreach (var pair in _terrainRows)
            pair.Value.Visible = pair.Key >= first && pair.Key <= last;
        foreach (var pair in _frontTerrainRows)
            pair.Value.Visible = pair.Key >= first && pair.Key <= last;
        foreach (var pair in _blendTerrainRows)
            pair.Value.Visible = pair.Key >= first && pair.Key <= last;
        foreach (var pair in _blendFrontTerrainRows)
            pair.Value.Visible = pair.Key >= first && pair.Key <= last;

        for (int y = first; y <= last; y++)
        {
            if (!_terrainRows.TryGetValue(y, out var row))
            {
                row = new MapTerrainRow { OwnerView = this, Row = y, FrontLayer = false };
                _terrainRows[y] = row;
                AddChild(row);
            }
            row.Row = y;
            // 每行顺序：中层地面、前景树/悬崖、对象、对象特效。
            row.ZIndex = RenderOrder.TerrainMiddle(y);
            row.QueueRedraw();

            if (!_frontTerrainRows.TryGetValue(y, out var front))
            {
                front = new MapTerrainRow { OwnerView = this, Row = y, FrontLayer = true };
                _frontTerrainRows[y] = front;
                AddChild(front);
            }
            front.Row = y;
            front.ZIndex = RenderOrder.TerrainFront(y);
            front.QueueRedraw();

            if (!_blendTerrainRows.TryGetValue(y, out var blendRow))
            {
                blendRow = new MapTerrainRow { OwnerView = this, Row = y, FrontLayer = false, BlendOnly = true };
                _blendTerrainRows[y] = blendRow;
                AddChild(blendRow);
            }
            blendRow.Row = y;
            blendRow.ZIndex = RenderOrder.TerrainMiddle(y);
            blendRow.QueueRedraw();

            if (!_blendFrontTerrainRows.TryGetValue(y, out var blendFront))
            {
                blendFront = new MapTerrainRow { OwnerView = this, Row = y, FrontLayer = true, BlendOnly = true };
                _blendFrontTerrainRows[y] = blendFront;
                AddChild(blendFront);
            }
            blendFront.Row = y;
            blendFront.ZIndex = RenderOrder.TerrainFront(y);
            blendFront.QueueRedraw();
        }
    }

    // 由 MapTerrainRow 调用。每一行独立成为 CanvasItem，才能和角色
    // 使用同一套全局 Z 顺序；绘制规则仍然保持原版 y->x->中层->前景。
    public void DrawTerrainRow(CanvasItem canvas, int y, bool frontLayer = false, bool blendOnly = false)
    {
        if (Map == null || y < 0 || y >= Map.Height) return;

        Vector2 viewport = GetViewport().GetVisibleRect().Size / WorldScale;
        float offsetX = (viewport.X - CellWidth) / 2f - ViewRangeX * CellWidth + CameraOffset.X;
        float offsetY = (viewport.Y - CellHeight) / 2f - ViewRangeY * CellHeight - ManualHeightOffset + CameraOffset.Y;
        int firstX = Math.Max(0, CenterX - ViewRangeX - 4);
        int lastX = Math.Min(Map.Width - 1, CenterX + ViewRangeX + 4);

        for (int x = firstX; x <= lastX; x++)
        {
            ref var cell = ref Map.Cells[x, y];
            float px = (x - CenterX + ViewRangeX) * CellWidth + offsetX;
            float py = (y - CenterY + ViewRangeY + 1) * CellHeight + offsetY;

            if (!frontLayer && cell.MiddleImage > 0)
            {
                int index = AnimatedIndex(cell.MiddleImage - 1, cell.MiddleAnimationFrame,
                    out bool blend);
                // Old MapControl has an intentional middle-layer exception:
                // standard 48x32/96x64 cells always use Draw(Image), even
                // when MiddleAnimationBlend is set. Only non-cell-sized
                // middle art enters DrawBlend.
                bool cellSized = IsCellSized(cell.MiddleFile, index);
                bool drawBlend = blend && !cellSized;
                if (drawBlend == blendOnly)
                    DrawCell(canvas, cell.MiddleFile, index, px, py, true, drawBlend, -1);
            }

            if (frontLayer && cell.FrontImage > 0)
            {
                int index = AnimatedIndex(cell.FrontImage - 1, cell.FrontAnimationFrame,
                    out bool blend);
                if (blend == blendOnly)
                    DrawCell(canvas, cell.FrontFile, index, px, py, true, blend, CellHeight);
            }
        }
    }

    private bool IsCellSized(int fileByte, int imageIndex)
    {
        if (!Libraries.KROrder.TryGetValue(fileByte, out LibraryFile file)) return false;
        var lib = GetLibrary(file);
        if (lib == null || imageIndex < 0 || imageIndex >= lib.Images.Length) return false;
        var image = lib.Images[imageIndex];
        return image != null &&
            (image.Width == CellWidth || image.Width == CellWidth * 2) &&
            (image.Height == CellHeight || image.Height == CellHeight * 2);
    }

    // skipTilesc: Middle/Front 跳过 Tilesc；背景层允许 fileByte=0。
    // baselineHeight=0 表示背景层；baselineHeight<0 表示始终按贴图自身
    // 高度对齐（旧版 Middle）；否则标准尺寸使用指定基线、其它贴图按自身
    // 高度对齐（旧版 Front）。
    private bool DrawCell(int fileByte, int imageIndex, float px, float py,
        bool skipTilesc, bool blend, int baselineHeight)
        => DrawCell(this, fileByte, imageIndex, px, py, skipTilesc, blend, baselineHeight);

    private bool DrawCell(CanvasItem canvas, int fileByte, int imageIndex, float px, float py,
        bool skipTilesc, bool blend, int baselineHeight)
    {
        if (imageIndex < 0) return false;
        if (!Libraries.KROrder.TryGetValue(fileByte, out LibraryFile file))
        {
            MissingLibraryCount++;
            return false;
        }
        if (skipTilesc && file == LibraryFile.Tilesc) return false;

        var lib = GetLibrary(file);
        if (lib == null || imageIndex >= lib.Images.Length)
        {
            MissingLibraryCount++;
            return false;
        }
        if (lib.Images[imageIndex] == null)
        {
            // 原版 ZL 元数据允许用空条目表示“无图层”。例如 Housesc[0]
            // 是地图对象的空占位帧，不是资源损坏，不能计入 missingTextures。
            EmptyImageEntryCount++;
            return false;
        }

        var img = lib.Images[imageIndex];

        var texture = lib.GetImageTexture(imageIndex);
        if (texture == null)
        {
            MissingTextureCount++;
            _missingTextureKeys.Add($"{file}[{imageIndex}]:texture-null");
            return false;
        }

        bool cellSized = (img.Width == CellWidth || img.Width == CellWidth * 2) &&
            (img.Height == CellHeight || img.Height == CellHeight * 2);
        float y = baselineHeight == 0 ? py : py -
            (baselineHeight < 0 || !cellSized ? img.Height : baselineHeight);
        // 原版地图层一律 useOffSet=false（MapControl.Floor.OnClearTexture /
        // DrawObjects 的 Draw/DrawBlend 均传 false）：帧 OffSet 只用于角色/
        // 怪物等对象层。地图库常见 OffSet=(-24,-16)（半格），个别库（如
        // 部分 Dungeonsc）有极大 OffSet，加上会让墙体错位/飞出视野。
        Rect2 dest = new Rect2(px, y, img.Width, img.Height);
        Rect2 src = new Rect2(0, 0, img.Width, img.Height);
        // 原版 MapControl.DrawObjects 的 Middle/FrontAnimationBlend 调用
        // DrawBlend(..., Color.White, false, 0.5F, ...)：0.5F 落在 NORMAL
        // 混合的 blendRate 参数上并被忽略 → 顶点 Alpha = 1.0 全不透明
        // Screen Blend（由 BlendOnly 行的 LegacyScreenBlend 材质实现）。
        // 不能把 0.5 写进顶点 Alpha。
        canvas.DrawTextureRectRegion(texture, dest, src, Colors.White);
        return true;
    }

    public Vector2 CellToScreen(int cellX, int cellY, bool objectBaseline)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size / WorldScale;
        float offsetX = (viewport.X - CellWidth) / 2f - ViewRangeX * CellWidth + CameraOffset.X;
        float offsetY = (viewport.Y - CellHeight) / 2f - ViewRangeY * CellHeight - ManualHeightOffset + CameraOffset.Y;
        return new Vector2(
            (cellX - CenterX + ViewRangeX) * CellWidth + offsetX,
            (cellY - CenterY + ViewRangeY + (objectBaseline ? 1 : 0)) * CellHeight + offsetY);
    }

    /// <summary>
    /// 将视口鼠标位置转换为地图格。特效和 C.Magic 的 Location 使用地图坐标，
    /// 不能把 Godot 的屏幕坐标或玩家当前格直接当作落点。
    /// </summary>
    public System.Drawing.Point ScreenToCell(Vector2 viewportPosition)
    {
        Vector2 local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        Vector2 origin = CellToScreen(CenterX, CenterY, false);
        int cellX = CenterX + Mathf.FloorToInt((local.X - origin.X + CellWidth * 0.5f) / CellWidth);
        int cellY = CenterY + Mathf.FloorToInt((local.Y - origin.Y + CellHeight * 0.5f) / CellHeight);
        if (Map != null)
        {
            cellX = Mathf.Clamp(cellX, 0, Map.Width - 1);
            cellY = Mathf.Clamp(cellY, 0, Map.Height - 1);
        }
        return new System.Drawing.Point(cellX, cellY);
    }

    private ZlLibrary GetLibrary(LibraryFile file)
    {
        return LibraryCache.Get(file);
    }

    public void CenterOn(int x, int y)
    {
        CenterX = x;
        CenterY = y;
        SyncTerrainRows();
        QueueRedraw();
    }
}
