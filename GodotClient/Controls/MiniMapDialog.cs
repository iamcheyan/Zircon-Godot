using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 右上角小地图 (移植自 Client/Scenes/Views/MiniMapDialog.cs)。
/// MiniMap.Zl 贴图 + NPC/出口静态标记 + 怪物/物品/玩家动态标记;
/// 玩家居中跟随 (Image 平移), 右上三按钮悬停显示。
/// </summary>
public partial class MiniMapDialog : DXWindow
{
    public event Action LayoutChanged;
    public Rect2 Area;
    public DXImageControl Image;
    public DXImageControl TimeOfDayImage;
    public DXControl Panel;
    public DXButton SizeButton, TransparencyButton, BigMapButton;

    private static readonly Vector2I DefaultMiniMapSize = new(200, 200);
    private static readonly Vector2I LargeMiniMapSize = new(300, 300);
    private const float TransparentOpacity = 0.5F;
    private bool IsLarge, IsTransparent;
    private bool _legacyEiLayout;

    public static float ScaleX, ScaleY;

    // 动态标记: ObjectID -> 色点 (怪物/物品/其他玩家); NPC/出口标记在 SetMap 重建
    private readonly Dictionary<uint, DXMapInfoControl> _objectMarkers = new();
    private readonly List<Node> _staticMarkers = new();
    private readonly AutoPathRouteControl _routeLayer;

    private int _mapWidth, _mapHeight;
    private int _mapIndex;
    private uint _playerObjectID;
    private bool _hasPlayer;
    private int _originalMiniMapHeight;

    private Action _onBigMapRequest;

    public override void _Ready()
    {
        base._Ready();
        UpdateTitleLayout();
        UpdateButtonLocations();
        // 原版使用 DXWindow.ClientArea，并向四周扩大 6 像素；这会让地图
        // 与标题栏/边框的相对位置保持和旧客户端一致。
        Area = ClientArea;
        Area.Position -= new Vector2(6, 6);
        Area.Size += new Vector2(12, 12);
        Panel.Location = (Vector2I)Area.Position;
        Panel.Size = Area.Size;
        Resized += OnResized;
    }

    /// <summary>
    /// 旧版 EI 小地图。依据 map-ui-resource-evidence.json：
    /// - viewport.fixed_minimap_widget：固定屏幕矩形 (672,0)-(800,128) = 128x128，
    ///   evidence_level 为 primary-static-exact-rect（即贴着 800x600 屏幕右上角）。
    ///   原版**没有独立窗口**：无标题栏、无关闭按钮、不可缩放、不可自由拖动。
    /// - mode_switch：T 键在 256x256 与 128x128 两个 surface 之间切换。
    /// - border_evidence：边框是程序画的 1px 灰 0x646464，不是 GameInter 帧。
    /// 调用方负责把它放到 (vp.X-128, 0)。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        IsLarge = false;
        IsTransparent = false;
        Size = new Vector2I(128, 128);
        AllowResize = false;
        // 原版是 Ctrl+拖动重定位（0x43DEB0），普通拖动不移动。
        Movable = true;
        RequireCtrlToMove = true;
        HasTitle = false;
        HasFooter = false;
        ShowCloseButton = false;
        DrawChrome = false;
        DropShadow = false;
        // 1px 灰描边 0x646464 = RGB(100,100,100)
        Border = true;
        BorderColour = new Color(100f / 255f, 100f / 255f, 100f / 255f);
        // 原版只有 2 个切换按钮（owner+0x298 透明度、owner+0x2A8 尺寸）；
        // 第 3 个「大地图」按钮在 EI 里不存在，而且它用的 F137 在本机素材里
        // 是空白帧（WIL offset=0），本来就无图。
        if (BigMapButton != null) BigMapButton.Visible = false;
        // DXWindow 的标题标签在 _Ready 里就随 HasTitle 建好了，之后再改
        // HasTitle 不会回收它，必须显式隐藏；SetMap 里也要跳过写 Text。
        if (TitleLabel != null) TitleLabel.Visible = false;
        Text = string.Empty;
        // _Ready 里为现代边框把客户区四周扩了 6px（Area.Size += 12）。证据的
        // fixed_minimap_widget 是精确 128x128，所以 legacy 下必须收回这 12px，
        // 否则地图实际渲染成 140x140。
        Area = new Rect2(0, 0, 128, 128);
        Panel.Location = Vector2I.Zero;
        Panel.Size = new Vector2I(128, 128);
        UpdateButtonLocations();
        GD.Print($"[LegacyMiniMap] size={Size} loc={Location} legacy={_legacyEiLayout} "
            + $"chrome={DrawChrome} title={HasTitle} resize={AllowResize} movable={Movable} "
            + $"bigMapVisible={BigMapButton?.Visible}");
    }

    public MiniMapDialog()
    {
        BackColour = Colors.Black;
        HasFooter = false;
        ShowCloseButton = false;
        AllowResize = true;
        Size = DefaultMiniMapSize;

        Panel = new DXControl
        {
            // 原版 MiniMap 的 Panel 是客户区裁剪容器，地图贴图超出 200/300
            // 像素窗口时只能显示窗口内部分，不能把整张 MiniMap 溢出到场景。
            Clip = true,
        };
        AddControl(Panel);

        Image = new DXImageControl
        {
            LibraryFile = LibraryFile.MiniMap,
            Movable = true,
            IgnoreMoveBounds = true,
        };
        Image.Moving += (o, e) => ClipMap();
        Panel.AddControl(Image);
        // GM 专用: 点击小地图传送到对应格 (原版 TeleportRing, 服务端对 Admin/TempAdmin 直传)。
        // 用 GuiInput 自行判定按下/抬起距离, 避免拖动平移小地图时误触发。
        Image.GuiInput += OnImageInput;
        _routeLayer = new AutoPathRouteControl { ZIndex = 20 };
        Image.AddControl(_routeLayer);

        SizeButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 132,
            Visible = false,
        };
        SizeButton.MouseClick += (o, e) => ToggleSize();
        AddControl(SizeButton);

        TransparencyButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 130,
            Visible = false,
        };
        TransparencyButton.MouseClick += (o, e) => ToggleTransparency();
        AddControl(TransparencyButton);

        BigMapButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 137,
            Visible = false,
        };
        BigMapButton.MouseClick += (o, e) => _onBigMapRequest?.Invoke();
        AddControl(BigMapButton);

        TimeOfDayImage = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 0,
            IsControl = false,
        };
        AddControl(TimeOfDayImage);
        UpdateButtonLocations();
    }

    public void SetBigMapRequestHandler(Action handler) => _onBigMapRequest = handler;

    private Vector2 _pressPos;
    private bool _pressValid;

    /// <summary>GM 点击小地图传送 (仅 GM; 非 GM 完全忽略)。左键按下+抬起且未拖动 → 传送。</summary>
    private void OnImageInput(InputEvent input)
    {
        if (input is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left) return;
        if (!Globals.IsGM) return;

        if (mb.Pressed)
        {
            _pressPos = mb.Position;
            _pressValid = true;
        }
        else if (_pressValid)
        {
            _pressValid = false;
            // 拖动平移小地图后抬起不算点击 (阈值 6px)
            if (mb.Position.DistanceTo(_pressPos) > 6f) return;
            var point = GetMapPoint();
            GameScene.Game?.SendTeleportRing(point.X, point.Y, _mapIndex);
        }
    }

    /// <summary>Image 局部坐标 → 地图 cell (与 BigMapDialog.GetMapPoint 同公式)</summary>
    private System.Drawing.Point GetMapPoint()
    {
        Vector2 pos = Image.GetLocalMousePosition();
        int x = Mathf.Clamp(Mathf.RoundToInt(pos.X / Math.Max(.001f, ScaleX)), 0, Math.Max(0, _mapWidth - 1));
        int y = Mathf.Clamp(Mathf.RoundToInt(pos.Y / Math.Max(.001f, ScaleY)), 0, Math.Max(0, _mapHeight - 1));
        return new System.Drawing.Point(x, y);
    }

    // ---- 数据源 (GameScene 调用) ----

    /// <summary>换图: 重建静态标记, 计算缩放, 清空动态标记</summary>
    public void SetMap(MapInfo map, int mapWidth, int mapHeight, uint playerObjectID)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _mapIndex = map.Index;
        _playerObjectID = playerObjectID;
        _hasPlayer = false;

        // EI 旧版小地图没有标题栏（map-ui-resource-evidence.json
        // viewport.fixed_minimap_widget 的 rect 就是纯 128x128 地图），
        // 所以 legacy 下不写标题文字。
        if (!_legacyEiLayout) Text = map.Local();
        Image.Index = map.MiniMap;
        Image.Location = Vector2I.Zero;

        if (Image.Index <= 0)
        {
            if (_originalMiniMapHeight <= 0) _originalMiniMapHeight = (int)Size.Y;
            Size = new Vector2I((int)Size.X, 32);
            AllowResize = false;
        }
        else if (_originalMiniMapHeight > 0)
        {
            Size = new Vector2I((int)Size.X, _originalMiniMapHeight);
            _originalMiniMapHeight = 0;
            AllowResize = true;
        }

        foreach (var m in _objectMarkers.Values)
            m.Dispose();
        _objectMarkers.Clear();
        foreach (var marker in _staticMarkers)
        {
            if (IsInstanceValid(marker)) marker.QueueFree();
        }
        _staticMarkers.Clear();

        ScaleX = Image.Size.X / (float)Math.Max(1, mapWidth);
        ScaleY = Image.Size.Y / (float)Math.Max(1, mapHeight);
        _routeLayer.Size = Image.Size;

        foreach (var npc in Globals.NPCInfoList?.Binding ?? Enumerable.Empty<NPCInfo>())
            UpdateStatic(npc);
        foreach (var mv in Globals.MovementInfoList?.Binding ?? Enumerable.Empty<MovementInfo>())
            UpdateStatic(mv);

        QueueRedraw();
    }

    public bool AuditLayout(out string details)
    {
        bool valid = Size.X >= DefaultMiniMapSize.X
            && Size.Y >= DefaultMiniMapSize.Y
            && AllowResize
            && Panel != null
            && SizeButton != null
            && TransparencyButton != null
            && BigMapButton != null
            && SizeButton.Position.Y == (HasTitle ? TitleHeight : 0);
        details = $"size={Size} area={Area} panel={Panel?.Size} resize={AllowResize} buttons={SizeButton?.Size}@{SizeButton?.Position}/{TransparencyButton?.Size}@{TransparencyButton?.Position}/{BigMapButton?.Size}@{BigMapButton?.Position}";
        return valid;
    }

    private void UpdateStatic(NPCInfo npc)
    {
        if (npc.Region?.Map?.Index != _mapIndex) return;
        if (npc.Region.PointList == null) npc.Region.CreatePoints(_mapWidth);
        var c = RegionCenter(npc.Region.PointList);
        if (c == null) return;

        var marker = MapMarkerFactory.CreateNpcMarker(npc);
        marker.Location = new Vector2I(
            (int)(ScaleX * c.Value.X) - (int)marker.Size.X / 2,
            (int)(ScaleY * c.Value.Y) - (int)marker.Size.Y / 2);
        // 旧版 GameScene.GetNPCControl 的 control.Hint = name：悬停标记显示 NPC 名。
        if (marker.TooltipText.Length == 0 && !string.IsNullOrWhiteSpace(npc.NPCName))
            marker.TooltipText = npc.Local();
        Image.AddControl(marker);
        _staticMarkers.Add(marker);
    }

    private void UpdateStatic(MovementInfo mv)
    {
        if (mv.SourceRegion?.Map?.Index != _mapIndex) return;
        if (mv.DestinationRegion?.Map == null || mv.Icon == MapIcon.None) return;
        var instance = GameScene.Game?.CurrentInstanceInfo;
        if (instance != null)
        {
            bool sourceInInstance = instance.Maps?.Any(x => x.Map == mv.SourceRegion.Map) == true;
            bool destinationInInstance = instance.Maps?.Any(x => x.Map == mv.DestinationRegion.Map) == true;
            if ((!sourceInInstance || !destinationInInstance) && mv.NeedInstance == null)
                return;
        }
        if (mv.SourceRegion.PointList == null) mv.SourceRegion.CreatePoints(_mapWidth);
        var c = RegionCenter(mv.SourceRegion.PointList);
        if (c == null) return;

        var icon = new DXImageControl { LibraryFile = LibraryFile.MiniMapIcon };
        UpdateMapIcon(icon, mv.Icon);
        icon.Location = new Vector2I(
            (int)(ScaleX * c.Value.X) - (int)icon.Size.X / 2,
            (int)(ScaleY * c.Value.Y) - (int)icon.Size.Y / 2);
        Image.AddControl(icon);
        _staticMarkers.Add(icon);
    }

    /// <summary>出口/洞穴图标着色 (移植自原版 GameScene.UpdateMapIcon)</summary>
    public static void UpdateMapIcon(DXImageControl control, MapIcon icon)
    {
        switch (icon)
        {
            case MapIcon.Cave:
                control.Index = 1;
                control.ForeColour = Colors.Red;
                break;
            case MapIcon.Exit:
                control.Index = 1;
                control.ForeColour = Colors.Green;
                break;
            case MapIcon.Down:
                control.Index = 1;
                control.ForeColour = new Color(0.78f, 0.08f, 0.52f); // MediumVioletRed
                break;
            case MapIcon.Up:
                control.Index = 1;
                control.ForeColour = new Color(0.0f, 0.75f, 1.0f); // DeepSkyBlue
                break;
            case MapIcon.Province:
                control.Index = 7;
                break;
            case MapIcon.Building:
                control.Index = 6;
                break;
            default:
                control.Index = (int)icon;
                break;
        }
    }

    private static System.Drawing.Point? RegionCenter(List<System.Drawing.Point> points)
    {
        if (points == null || points.Count == 0) return null;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new System.Drawing.Point((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>动态物体标记 (怪物/物品/其他玩家)。返回 true 表示是玩家 (用于居中)</summary>
    public bool UpdateObject(uint objectID, int cellX, int cellY, ObjectRenderer.Kind kind)
    {
        if (objectID == _playerObjectID)
        {
            _hasPlayer = true;
            UpdatePlayerMarker(cellX, cellY);
            return true;
        }

        if (!_objectMarkers.TryGetValue(objectID, out var dot))
        {
            dot = new DXMapInfoControl { Size = new Vector2I(3, 3) };
            Image.AddControl(dot);
            _objectMarkers[objectID] = dot;
        }

        dot.Visible = true;
        // map-ui-resource-evidence.json render_evidence.marker_visual_semantics：
        // 原版标记是**程序画的描边矩形、没有精灵** —— NPC 黄 0xFFFF、玩家与
        // 邻近列表绿 0x64C864，都是 ±2px（即 4x4）。现代模式仍用彩色小点。
        if (_legacyEiLayout)
        {
            dot.Hollow = false;
            dot.Size = new Vector2I(4, 4);
            dot.BackColour = kind switch
            {
                ObjectRenderer.Kind.Monster => new Color(1f, 1f, 0f),          // 0xFFFF 黄
                ObjectRenderer.Kind.Item => new Color(0f, 0f, 0.55f),          // 保留物品色
                ObjectRenderer.Kind.Player => new Color(100f / 255f, 200f / 255f, 100f / 255f), // 0x64C864
                _ => new Color(100f / 255f, 200f / 255f, 100f / 255f),
            };
        }
        else
        {
            dot.BackColour = kind switch
            {
                ObjectRenderer.Kind.Monster => Colors.Red,
                ObjectRenderer.Kind.Item => new Color(0.0f, 0.0f, 0.55f), // DarkBlue
                ObjectRenderer.Kind.Player => Colors.Cyan,
                _ => Colors.White,
            };
        }
        dot.Location = new Vector2I(
            (int)(ScaleX * cellX) - (int)dot.Size.X / 2,
            (int)(ScaleY * cellY) - (int)dot.Size.Y / 2);
        return false;
    }

    /// <summary>玩家标记 (移动/换图时由 GameScene 更新)</summary>
    public void UpdatePlayer(int cellX, int cellY)
    {
        _hasPlayer = true;
        UpdatePlayerMarker(cellX, cellY);
    }

    public void UpdateAutoPathRoutes(IReadOnlyList<AutoPathRoute> routes, int currentMap, int progressMap, int progressPoint)
    {
        _routeLayer.Size = Image.Size;
        _routeLayer.SetRoutes(routes, _mapIndex, progressMap, progressPoint, ScaleX, ScaleY);
    }

    private void UpdatePlayerMarker(int cellX, int cellY)
    {
        if (!_objectMarkers.TryGetValue(_playerObjectID, out var dot))
        {
            dot = new DXMapInfoControl();
            Image.AddControl(dot);
            _objectMarkers[_playerObjectID] = dot;
        }

        dot.Visible = true;
        // 原版玩家标记是绿色 0x64C864 的 ±2px 描边矩形（4x4），不是空心点。
        if (_legacyEiLayout)
        {
            dot.Hollow = false;
            dot.BackColour = new Color(100f / 255f, 200f / 255f, 100f / 255f);
            dot.Size = new Vector2I(4, 4);
            dot.Location = new Vector2I((int)(ScaleX * cellX) - 2, (int)(ScaleY * cellY) - 2);
        }
        else
        {
            dot.Hollow = true;
            dot.BackColour = Colors.Lime;
            dot.Size = new Vector2I(5, 5);
            dot.Location = new Vector2I((int)(ScaleX * cellX) - 2, (int)(ScaleY * cellY) - 2);
        }

        // 玩家居中: 地图图平移
        Image.Location = new Vector2I(
            -dot.Location.X + (int)Area.Size.X / 2,
            -dot.Location.Y + (int)Area.Size.Y / 2);
        ClipMap();
    }

    public void RemoveObject(uint objectID)
    {
        if (_objectMarkers.Remove(objectID, out var dot))
            dot.Dispose();
    }

    public void ClearObjects()
    {
        foreach (var dot in _objectMarkers.Values)
            dot.Dispose();
        _objectMarkers.Clear();
        _hasPlayer = false;
    }

    private void ClipMap()
    {
        float imgW = Image.Size.X, imgH = Image.Size.Y;
        float panelW = Panel.Size.X, panelH = Panel.Size.Y;
        float x = Image.Location.X, y = Image.Location.Y;

        if (x + imgW < panelW) x = panelW - imgW;
        if (x > 0) x = 0;
        if (y + imgH < panelH) y = panelH - imgH;
        if (y > 0) y = 0;

        if (imgW < panelW) x = -((imgW - panelW) / 2);
        if (imgH < panelH) y = -((imgH - panelH) / 2);

        Image.Location = new Vector2I((int)x, (int)y);
    }

    /// <summary>EI 的 T 键入口：小地图 surface 128&lt;-&gt;256 切换。</summary>
    public void ToggleLegacySize() => ToggleSize();

    private void ToggleSize()
    {
        int right = Location.X + (int)Size.X;
        IsLarge = !IsLarge;
        if (_legacyEiLayout)
        {
            // map-ui-resource-evidence.json mode_switch：原版 T 键在
            // 256x256 与 128x128 两个 surface 之间切换（true_branch 256 /
            // false_branch 128，均为 primary-static-exact-call），
            // 且**不改变 AllowResize**（原版根本没有缩放）。
            Size = IsLarge ? new Vector2I(256, 256) : new Vector2I(128, 128);
            Location = new Vector2I(right - (int)Size.X, Location.Y);
            Area = new Rect2(0, 0, Size.X, Size.Y);
            Panel.Location = Vector2I.Zero;
            Panel.Size = Size;
            UpdateButtonLocations();
            LayoutChanged?.Invoke();
            return;
        }
        // 原版 MiniMapDialog: 大图模式 AllowResize=true (可拖边缩放), 小图固定尺寸。
        AllowResize = IsLarge;
        Size = IsLarge ? LargeMiniMapSize : DefaultMiniMapSize;
        Location = new Vector2I(right - (int)Size.X, Location.Y);
        UpdateButtonLocations();
        LayoutChanged?.Invoke();
    }

    private void OnResized()
    {
        // 通用边缘缩放后刷新客户区/面板/地图裁剪
        Area = ClientArea;
        Area.Position -= new Vector2(6, 6);
        Area.Size += new Vector2(12, 12);
        if (Panel != null)
        {
            Panel.Location = (Vector2I)Area.Position;
            Panel.Size = Area.Size;
        }
        UpdateTitleLayout();
        UpdateButtonLocations();
        ClipMap();
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// 小地图的标题栏由地图 Panel 向上扩展 6 像素覆盖到边框附近，不能沿用
    /// DXWindow 默认的 4 像素起点和固定标题尺寸，否则文字会贴近黑色标题栏
    /// 下沿，视觉上像是落到了地图内容区。标题区域始终完整覆盖黑色栏并居中。
    /// </summary>
    private void UpdateTitleLayout()
    {
        if (TitleLabel == null) return;

        const int horizontalPadding = 24;
        TitleLabel.Location = new Vector2I(horizontalPadding, 0);
        TitleLabel.Size = new Vector2(
            Math.Max(0, Size.X - horizontalPadding * 2),
            TitleHeight);
    }

    public override Vector2I GetAcceptableResize(Vector2 requested)
    {
        // 原版大图模式缩放范围 150~300
        int w = Mathf.Clamp((int)requested.X, 150, 300);
        int h = Mathf.Clamp((int)requested.Y, 150, 300);
        return new Vector2I(w, h);
    }

    private void ToggleTransparency()
    {
        IsTransparent = !IsTransparent;
        Opacity = IsTransparent ? TransparentOpacity : 1F;
        ApplyOpacityToMapLayers();
    }

    public void ToggleTransparencyForKeyBind() => ToggleTransparency();

    public void ResetTransparencyForKeyBind()
    {
        IsTransparent = false;
        Opacity = 1F;
        ApplyOpacityToMapLayers();
    }

    private void ApplyOpacityToMapLayers()
    {
        float opacity = Opacity;
        TransparencyButton.Index = IsTransparent ? 131 : 130;
        foreach (Node child in GetChildren())
        {
            if (child is DXControl control)
                control.Opacity = opacity;
        }
        foreach (DXMapInfoControl marker in _objectMarkers.Values)
            marker.Opacity = opacity;
        foreach (Node marker in _staticMarkers)
        {
            if (marker is DXControl control)
                control.Opacity = opacity;
        }
        Image.Opacity = opacity;
        Image.ImageOpacity = opacity;
        _routeLayer.Opacity = opacity;
    }

    public override void Process()
    {
        base.Process();
        bool hovered = Visible && GetGlobalRect().HasPoint(GetViewport().GetMousePosition());
        SizeButton.Visible = hovered;
        TransparencyButton.Visible = hovered;
        BigMapButton.Visible = hovered;
        TimeOfDayImage.Location = new Vector2I(3, (int)Size.Y - 29);
        TimeOfDayImage.Index = GameScene.Game?.TimeOfDay switch
        {
            TimeOfDay.Dawn => 215,
            TimeOfDay.Dusk => 217,
            TimeOfDay.Night => 218,
            TimeOfDay.Day => 216,
            _ => 0,
        };
        // 旧版 TimeOfDayImage.Hint = TimeOfDayLabel：悬停显示当前时段文字。
        TimeOfDayImage.TooltipText = GameScene.Game?.TimeOfDay switch
        {
            TimeOfDay.Dawn => Lang.MiniMapDawnLabel,
            TimeOfDay.Dusk => Lang.MiniMapDuskLabel,
            TimeOfDay.Night => Lang.MiniMapNightLabel,
            TimeOfDay.Day => Lang.MiniMapDayLabel,
            _ => string.Empty,
        };
    }

    public override void _Process(double delta) => Process();

    private void UpdateButtonLocations()
    {
        if (SizeButton == null || TransparencyButton == null || BigMapButton == null) return;

        const int rightPadding = 3;
        int top = HasTitle ? TitleHeight : 0;
        SizeButton.Location = new Vector2I((int)(Size.X - SizeButton.Size.X) - rightPadding, top);
        TransparencyButton.Location = new Vector2I(
            (int)(Size.X - TransparencyButton.Size.X) - rightPadding,
            SizeButton.Location.Y + (int)SizeButton.Size.Y);
        BigMapButton.Location = new Vector2I(
            (int)(Size.X - BigMapButton.Size.X) - rightPadding,
            TransparencyButton.Location.Y + (int)TransparencyButton.Size.Y);
    }
}
