using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 大地图 (移植自 Client/Scenes/Views/BigMapDialog.cs)。
/// 可拖动/缩放适配的 MiniMap 大图; 当前地图时以玩家为中心, Recenter 按钮复位；
/// 右键传送戒指、左键双击自动寻路按原版地图坐标发包。
/// </summary>
public partial class BigMapDialog : DXWindow
{
    public Rect2 Area;
    public DXImageControl Image;
    public DXControl Panel;
    public DXButton RecenterButton;

    public static float ScaleX, ScaleY;

    private int _mapWidth, _mapHeight;
    private int _mapIndex;
    private bool _isCurrentMap;
    private uint _playerObjectID;
    private int _playerCellX, _playerCellY;
    private readonly LegacyWindowFrame _frame;

    private readonly Dictionary<uint, DXMapInfoControl> _objectMarkers = new();
    private readonly List<Node> _staticMarkers = new();
    private readonly AutoPathRouteControl _routeLayer;

    private Func<MapInfo> _recenterMapProvider = () => null;
    private Action<MapInfo> _openBigMap = _ => { };

    private Vector2 _pressPos;
    private bool _pressValid;

    public BigMapDialog()
    {
        BackColour = Colors.Black;
        HasFooter = true;

        _frame = new LegacyWindowFrame { HasTitle = true, HasFooter = true };
        AddControl(_frame);
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(0, 3) };
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);

        Panel = new DXControl();
        Panel.Clip = true;
        AddControl(Panel);

        Image = new DXImageControl
        {
            LibraryFile = LibraryFile.MiniMap,
            Movable = true,
            IgnoreMoveBounds = true,
            Clip = true,
        };
        Panel.AddControl(Image);
        _routeLayer = new AutoPathRouteControl { ZIndex = 20 };
        Image.AddControl(_routeLayer);
        Image.MouseDoubleClick += (o, e) =>
        {
            var point = GetMapPoint();
            int x = point.X;
            int y = point.Y;
            GameScene.Game?.SendAutoPathWaypoint(_mapIndex, x, y);
        };
        Image.GuiInput += input =>
        {
            if (input is not InputEventMouseButton mouse) return;
            if (mouse.ButtonIndex == MouseButton.Right)
            {
                if (!mouse.Pressed) return;
                var point = GetMapPoint();
                GameScene.Game?.SendTeleportRing(point.X, point.Y, _mapIndex);
                CloseBigMapAfterTeleport();
            }
            else if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed)
                {
                    _pressPos = mouse.Position;
                    _pressValid = true;
                }
                else if (_pressValid)
                {
                    _pressValid = false;
                    // 拖动查看大地图后抬起不算点击
                    if (mouse.Position.DistanceTo(_pressPos) > 6f) return;
                    var point = GetMapPoint();
                    GameScene.Game?.SendTeleportRing(point.X, point.Y, _mapIndex);
                    CloseBigMapAfterTeleport();
                }
            }
        };
        RecenterButton = new DXButton
        {
            Type = DXButton.ButtonType.Default,
            Text = "回到中心",
            Size = new Vector2I(80, 24),
        };
        RecenterButton.MouseClick += (o, e) => Recenter();
        AddControl(RecenterButton);
    }

    /// <summary>GameScene 注入: 返回当前地图; 打开指定地图 (Recenter 点击时使用)</summary>
    public void SetRecenterMapProvider(Func<MapInfo> provider, Action<MapInfo> openBigMap)
    {
        _recenterMapProvider = provider;
        _openBigMap = openBigMap;
    }

    public override void _Ready()
    {
        base._Ready();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        // 原版 DXWindow.GetClientArea: x=9, y=37，底部包含
        // Interface 126/2/10 的 57px footer 和 6px 内边距。
        const int clientX = 9;
        const int clientY = 37;
        const int bottomPadding = 6;
        const int footerHeight = 57;
        Area = new Rect2(clientX, clientY,
            Math.Max(0, Size.X - clientX * 2),
            Math.Max(0, Size.Y - clientY - bottomPadding - footerHeight));
        _frame.Size = new Vector2I((int)Size.X, (int)Size.Y);
        var close = Controls.OfType<DXButton>().FirstOrDefault(x => x.Index == 15);
        if (close != null)
            close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        Panel.Location = (Vector2I)Area.Position;
        Panel.Size = Area.Size;
        RecenterButton.Location = new Vector2I(
            Math.Max(0, (int)(Size.X - 30 - 80)),
            Math.Max(0, (int)(Size.Y - 43)));
        Panel.QueueRedraw();
    }

    public void SetMap(MapInfo map, int mapWidth, int mapHeight, uint playerObjectID, bool isCurrentMap)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _mapIndex = map.Index;
        _playerObjectID = playerObjectID;
        _isCurrentMap = isCurrentMap;

        Text = map.PlayerDescription;
        Image.Index = map.MiniMap;

        foreach (var m in _objectMarkers.Values)
            m.Dispose();
        _objectMarkers.Clear();
        foreach (var marker in _staticMarkers)
        {
            if (IsInstanceValid(marker)) marker.QueueFree();
        }
        _staticMarkers.Clear();

        // 窗口尺寸适配贴图 (320,240)-(800,520)
        var img = Image.Size;
        var client = new Vector2I(
            (int)Math.Clamp(img.X, 320, 800),
            (int)Math.Clamp(img.Y, 240, 520));
        // Generic frame adds 18 horizontal and 100 vertical pixels around
        // the clamped client area.
        Size = new Vector2I(client.X + 18, (int)(client.Y + 100));
        UpdateLayout();

        ScaleX = Image.Size.X / (float)Math.Max(1, mapWidth);
        ScaleY = Image.Size.Y / (float)Math.Max(1, mapHeight);
        _routeLayer.Size = Image.Size;

        bool imageLargerThanPanel = img.X > client.X || img.Y > client.Y;
        Image.Movable = imageLargerThanPanel;
        Image.IgnoreMoveBounds = imageLargerThanPanel;

        RecenterButton.Enabled = !isCurrentMap;

        // 默认居中
        Image.Location = new Vector2I(-(int)(img.X - client.X) / 2, -(int)(img.Y - client.Y) / 2);
        if (isCurrentMap) Recenter();

        foreach (var npc in Globals.NPCInfoList?.Binding ?? Enumerable.Empty<NPCInfo>())
            UpdateStatic(npc);
        foreach (var mv in Globals.MovementInfoList?.Binding ?? Enumerable.Empty<MovementInfo>())
            UpdateStatic(mv);

        QueueRedraw();
    }

    /// <summary>玩家格坐标 (换图/移动时由 GameScene 更新)</summary>
    public void SetPlayerLocation(int cellX, int cellY)
    {
        _playerCellX = cellX;
        _playerCellY = cellY;
        if (_isCurrentMap) Recenter();
    }

    public void UpdateObject(uint objectID, int cellX, int cellY, ObjectRenderer.Kind kind)
    {
        if (objectID == _playerObjectID) { SetPlayerLocation(cellX, cellY); return; }

        if (!_objectMarkers.TryGetValue(objectID, out var dot))
        {
            dot = new DXMapInfoControl { Size = new Vector2I(3, 3) };
            Image.AddControl(dot);
            _objectMarkers[objectID] = dot;
        }
        dot.Visible = true;
        dot.BackColour = kind switch
        {
            ObjectRenderer.Kind.Monster => Colors.Red,
            ObjectRenderer.Kind.Item => new Color(0.0f, 0.0f, 0.55f),
            ObjectRenderer.Kind.Player => Colors.Cyan,
            _ => Colors.White,
        };
        dot.Location = new Vector2I((int)(ScaleX * cellX) - 1, (int)(ScaleY * cellY) - 1);
    }

    /// <summary>
    /// 传送后关闭大地图 (原版: 点一下地图跳走, 地图消失)。
    /// 走 WindowManager 才能同步 OpenWindows/Z 序；直改 Visible 会留下
    /// 「已隐藏但仍登记在最上层」的幽灵条目，让下一次 Esc 关错窗口。
    /// </summary>
    private void CloseBigMapAfterTeleport()
    {
        WindowManager.Close(this);
    }

    /// <summary>Image 局部坐标 → 地图 cell (与 BigMapDialog.GetMapPoint 同公式)</summary>
    private System.Drawing.Point GetMapPoint()
    {
        Vector2 point = Image.GetLocalMousePosition();
        int x = Mathf.Clamp(Mathf.RoundToInt(point.X / Math.Max(.001f, ScaleX)), 0, Math.Max(0, _mapWidth - 1));
        int y = Mathf.Clamp(Mathf.RoundToInt(point.Y / Math.Max(.001f, ScaleY)), 0, Math.Max(0, _mapHeight - 1));
        return new System.Drawing.Point(x, y);
    }

    /// <summary>玩家标记 (移动/换图时由 GameScene 更新)</summary>
    public void UpdatePlayer(int cellX, int cellY) => SetPlayerLocation(cellX, cellY);

    public void UpdateAutoPathRoutes(IReadOnlyList<AutoPathRoute> routes, int progressMap, int progressPoint)
    {
        _routeLayer.Size = Image.Size;
        _routeLayer.SetRoutes(routes, _mapIndex, progressMap, progressPoint, ScaleX, ScaleY);
    }

    public void RemoveObject(uint objectID)
    {
        if (_objectMarkers.Remove(objectID, out var dot))
            dot.Dispose();
    }

    private void UpdateStatic(NPCInfo npc)
    {
        if (npc.Region?.Map?.Index != _mapIndex) return;
        if (npc.Region.PointList == null) npc.Region.CreatePoints(_mapWidth);
        var c = RegionCenter(npc.Region.PointList);
        if (c == null) return;

        // 旧版 BigMapDialog.cs:368-372: 双击 NPC 图标 -> C.AutoPathStart{NPCIndex}。
        var marker = MapMarkerFactory.CreateNpcMarker(npc);
        var npcIndex = npc.Index;
        marker.MouseDoubleClick += (o, e) => GameScene.Game?.SendAutoPathStart(npcIndex);
        // 旧版 BigMapDialog 的 control.Hint = name：悬停标记显示 NPC 名。
        if (marker.TooltipText.Length == 0 && !string.IsNullOrWhiteSpace(npc.NPCName))
            marker.TooltipText = npc.Local();
        marker.Location = new Vector2I(
            (int)(ScaleX * c.Value.X) - (int)marker.Size.X / 2,
            (int)(ScaleY * c.Value.Y) - (int)marker.Size.Y / 2);
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
        MiniMapDialog.UpdateMapIcon(icon, mv.Icon);
        icon.Location = new Vector2I(
            (int)(ScaleX * c.Value.X) - (int)icon.Size.X / 2,
            (int)(ScaleY * c.Value.Y) - (int)icon.Size.Y / 2);
        Image.AddControl(icon);
        _staticMarkers.Add(icon);
    }

    private void Recenter()
    {
        if (!_isCurrentMap)
        {
            // 原版: Recenter 点击把 SelectedInfo 切回当前地图
            var current = _recenterMapProvider?.Invoke();
            if (current != null)
                _openBigMap(current);
            return;
        }

        var panel = Panel.Size;

        // 玩家像素坐标
        float px = ScaleX * _playerCellX;
        float py = ScaleY * _playerCellY;

        int targetX = (int)Math.Round(panel.X / 2f - px);
        int targetY = (int)Math.Round(panel.Y / 2f - py);

        // 图比面板小时整体居中, 否则钳位到面板范围内
        int minX = Math.Min(0, (int)panel.X - (int)Image.Size.X);
        int maxX = Math.Max(0, (int)panel.X - (int)Image.Size.X);
        int minY = Math.Min(0, (int)panel.Y - (int)Image.Size.Y);
        int maxY = Math.Max(0, (int)panel.Y - (int)Image.Size.Y);

        Image.Location = new Vector2I(
            Math.Max(minX, Math.Min(maxX, targetX)),
            Math.Max(minY, Math.Min(maxY, targetY)));
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
}
