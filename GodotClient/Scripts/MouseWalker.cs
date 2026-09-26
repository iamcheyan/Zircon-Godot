using System;
using Godot;
using Library;

namespace ZirconClient.Scripts;

/// <summary>
/// 鼠标按住走路/跑步 (移植自 Client/Scenes/Views/MapControl.cs ProcessInput + MouseDirection)。
/// 独立节点, 挂在 GameScene 下, 与 MapView 同世界坐标系。不修改 MapView。
///
/// 原版行为:
///   左键按住 -> 朝鼠标方向走 (Distance=1)
///   右键按住 -> 朝鼠标方向跑 (Distance=2, 含负重/骑马可更多; 此处先固定 2)
///   Shift+左键 -> 原地攻击 (此处只处理移动, 攻击交给战斗组件)
///
/// 方向算法 (原版 MouseDirection 22.5° 划分):
///   玩家恒在视野中心 (MapView 居中渲染)。鼠标相对玩家的角度按 22.5° 分成 8 份。
///   注意 CellWidth=48 != CellHeight=32, 必须先归一化到"格"单位再算角度, 否则对角线方向会偏。
/// </summary>
public partial class MouseWalker : Node2D
{
    // 与 MapView 完全一致的常量 (渲染不变量, 复制而非引用, 避免修改 MapView)
    private const float CellWidth = 48f;
    private const float CellHeight = 32f;
    private static float WorldScale => GameScene.WorldScale;
    private const int ManualHeightOffset = 34;

    private readonly MapView _mapView;
    private readonly Action<MirDirection, int, bool> _sendMove;  // (方向, 距离, 是否跑步) -> C.Move
    private readonly Action<MirDirection> _sendTurn;
    private readonly Func<bool> _blockLeftWalk;  // 返回 true 时左键不走路 (鼠标下有可点物体)
    private readonly Func<bool> _mouseOverUi;  // 返回 true 时鼠标在游戏 UI 上, 屏蔽任何移动/转向
    private readonly Func<int> _getRunSteps;  // 返回当前可跑步数 (1=走, 2=负重允许跑, 3=骑马); null=默认2
    private readonly Func<int, int, bool> _cellBlocked;  // 地形之外的动态阻挡物
    private readonly Func<bool> _movementAllowed;
    private readonly Func<bool> _turnAllowed;
    private readonly Func<bool> _blockLeftMouse;
    private readonly Func<bool> _blockRightMouse;
    // 原版 ServerTime 门控: 返回 true = 正在等服务端回包, 本帧不发移动/不判阻挡。
    private readonly Func<bool> _awaitingServer;
    // 原版 CanMove 用 User.CurrentLocation(权威格子)做起点; Godot 相机基准 CenterX/Y
    // 由回包驱动且经 CallDeferred 延迟一帧, 与真实玩家位置有窗口期不同步, 会误判阻挡
    // (空气墙)。改用权威 _playerLocation 做起点判定。null 时回退到 CenterX/Y。
    private readonly Func<System.Drawing.Point> _playerCell;

    // 原版 Globals.MoveTime = 600ms。一段移动完成后才允许下一段；
    // 跑步不是把动画播得更快，而是在相同 6 帧/600ms 内移动 2 格，
    // 因而实际速度是走路的两倍。
    private const double WalkIntervalMs = 600.0;
    private const double RunIntervalMs = 600.0;
    private double _nextSendMs;
    private bool _suspendUntilInputRelease;
    public bool Enabled = true;  // GameScene 可在登录前关掉
    public bool AutoRun;  // D 键切换; 开启时左键也跑步

    /// <summary>
    /// 技能开始时中断当前鼠标移动意图。必须先松开鼠标，再重新按下，
    /// 才能开始下一段移动；否则施法结束后会沿用旧的按键状态继续乱走。
    /// </summary>
    public void SuspendUntilInputRelease() => _suspendUntilInputRelease = true;

    public void AddMoveDelay(TimeSpan slow)
    {
        if (slow <= TimeSpan.Zero) return;
        _nextSendMs = Math.Max(_nextSendMs, Godot.Time.GetTicksMsec() + slow.TotalMilliseconds);
    }

    public MouseWalker(MapView mapView, Action<MirDirection, int, bool> sendMove,
        Func<bool> blockLeftWalk = null, Func<int> getRunSteps = null,
        Action<MirDirection> sendTurn = null, Func<bool> mouseOverUi = null,
        Func<int, int, bool> cellBlocked = null, Func<bool> movementAllowed = null,
        Func<bool> turnAllowed = null, Func<bool> blockLeftMouse = null,
        Func<bool> blockRightMouse = null, Func<bool> awaitingServer = null,
        Func<System.Drawing.Point> playerCell = null)
    {
        _mapView = mapView;
        _sendMove = sendMove;
        _blockLeftWalk = blockLeftWalk;
        _getRunSteps = getRunSteps;
        _sendTurn = sendTurn;
        _mouseOverUi = mouseOverUi;
        _cellBlocked = cellBlocked;
        _movementAllowed = movementAllowed;
        _turnAllowed = turnAllowed;
        _blockLeftMouse = blockLeftMouse;
        _blockRightMouse = blockRightMouse;
        _awaitingServer = awaitingServer;
        _playerCell = playerCell;
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _mapView?.Map == null) return;

        // 父节点 (GameScene) 是 Control 且 Scale=WorldScale; 鼠标全局位置已是 GameScene 局部像素。
        // 转回世界坐标: 除以 WorldScale。
        // 用父节点的 GetGlobalMousePosition 以兼容嵌套。
        Control parent = GetParent() as Control;
        if (parent == null) return;

        Vector2 mouseScreen = parent.GetGlobalMousePosition();
        Vector2 mouseWorld = mouseScreen / WorldScale;

        bool leftDown = Input.IsMouseButtonPressed(MouseButton.Left);
        bool rightDown = Input.IsMouseButtonPressed(MouseButton.Right);
        bool autoRun = AutoRun;
        if (_suspendUntilInputRelease)
        {
            if (!leftDown && !rightDown && !autoRun)
                _suspendUntilInputRelease = false;
            else
                return;
        }
        // 鼠标在游戏 UI (背包/人物/商店等窗口或主面板) 上: 点击是操作界面, 不是移动角色。
        // 原版等价物是 MapControl.ProcessInput 的 MouseControl == this 判断。
        // 但原版 AutoRun 在 MouseControl 判断之前执行，不能被 UI 悬停截断。
        if (!autoRun && _mouseOverUi != null && _mouseOverUi())
            return;

        // Shift 按住 = 原地攻击, 不走
        bool shiftDown = Input.IsKeyPressed(Key.Shift);
        if (!autoRun && shiftDown) return;

        // MapControl.ProcessInput handles Alt+left before its ordinary walk
        // branch (harvest/fishing/taming). MouseWalker runs independently, so
        // it must not enqueue a same-frame movement request for that input.
        // Alt+right keeps the legacy run behavior because the original right
        // button branch has no Alt-special action.
        if (!autoRun && leftDown && Input.IsKeyPressed(Key.Alt)) return;

        // 原版 AutoRun 不依赖鼠标按钮；开启后即使松开鼠标也会继续调用 Run。
        if (!leftDown && !rightDown && !autoRun) return;

        if (!autoRun && leftDown && _blockLeftMouse?.Invoke() == true) return;
        if (!autoRun && rightDown && _blockRightMouse?.Invoke() == true) return;

        // 左键但鼠标下有可点物体 (怪物/NPC/物品) -> 让 CombatController 处理选中, 不走路
        if (!autoRun && leftDown && _blockLeftWalk != null && _blockLeftWalk())
            return;

        double now = Godot.Time.GetTicksMsec();
        int steps = _getRunSteps?.Invoke() ?? 2;
        bool run = rightDown || autoRun;
        int distance = run ? steps : 1;
        double interval = run ? RunIntervalMs : WalkIntervalMs;
        if (now < _nextSendMs) return;
        // 原版 AttemptAction: if (Now < ServerTime) return; 一次只发一个移动, 等回包。
        if (_awaitingServer?.Invoke() == true) return;

        MirDirection target = ComputeDirection(mouseWorld);
        // 原版右键在玩家附近只转身，不会向脚下移动。
        bool canMove = _movementAllowed?.Invoke() ?? true;
        bool canTurn = _turnAllowed?.Invoke() ?? canMove;
        if (rightDown && (IsMouseWithinCells(mouseWorld, 2) || !canMove))
        {
            if (canTurn) _sendTurn?.Invoke(target);
            _nextSendMs = now + WalkIntervalMs;
            return;
        }
        if (!canMove) return;

        // 撞墙绕路: 正方向走不通时找相邻可行方向 (复刻原版 MouseDirectionBest)
        MirDirection dir = target;
        if (!CanMove(target, distance))
        {
            // 原版 Run 在遇到阻挡时用 MouseDirectionBest(direction, 1)，
            // 即使原本请求的是两格/三格，也只寻找下一格的替代方向。
            MirDirection best = BestWalkDirection(target, mouseWorld, 1);
            if (best == target && !CanMove(target, 1))
            {
                _sendTurn?.Invoke(target);
                _nextSendMs = now + WalkIntervalMs;
                return;
            }
            dir = best;
            distance = 1;
        }
        _sendMove(dir, distance, run && distance >= 2);
        _nextSendMs = now + interval;
    }

    /// <summary>
    /// 鼠标世界坐标 -> 8 方向之一。复刻原版 MouseDirection 的 22.5° 划分。
    /// 玩家恒居中: 使用地图格中心计算角度，避免人物贴图 baseline 偏移。
    /// </summary>
    private MirDirection ComputeDirection(Vector2 mouseWorld)
    {
        // 原版角度中心是地图格中心，而不是人物贴图的 baseline。
        Vector2 playerWorld = _mapView.CellToScreen(_mapView.CenterX, _mapView.CenterY, false)
            + new Vector2(CellWidth / 2f, CellHeight / 2f);
        float dx = mouseWorld.X - playerWorld.X;
        float dy = mouseWorld.Y - playerWorld.Y;
        int cellX = (int)Math.Floor((dx + CellWidth / 2f) / CellWidth);
        int cellY = (int)Math.Floor((dy + CellHeight / 2f) / CellHeight);

        // 原版近距离先按地图格坐标取方向。
        if (Math.Max(Math.Abs(cellX), Math.Abs(cellY)) <= 2)
        {
            if (cellX == 0 && cellY == 0) return _lastDir;
            _lastDir = Functions.DirectionFromPoint(
                new System.Drawing.Point(0, 0), new System.Drawing.Point(cellX, cellY));
            return _lastDir;
        }

        // 原版远距离用实际像素角度；48x32 的比例不能归一化，否则边界
        // 会与旧客户端的 22.5 度分界不同。
        double angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;  // [-180, 180], 正上=0
        if (angle < 0) angle += 360.0;

        angle += 22.5;
        if (angle >= 360.0) angle -= 360.0;
        int idx = (int)(angle / 45.0);
        _lastDir = (MirDirection)idx;
        return _lastDir;
    }

    /// <summary>该格是否可行走 (边界 + MapCell.Flag)。复刻原版 CanMove。</summary>
    private bool CanMove(int x, int y)
    {
        var map = _mapView.Map;
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
        // 原版 Cell.Blocking() 同时检查地形 Flag 和格子上的动态 MapObject。
        // 之前这里只检查地形，遇到怪物/NPC/其他玩家时会错误地继续发移动。
        return !map.Cells[x, y].Flag && !(_cellBlocked?.Invoke(x, y) ?? false);
    }

    /// <summary>
    /// 玩家朝 dir 走 distance 格, 途经任一格阻挡则不可行。
    /// 复刻原版 CanMove(direction, distance); 起点用权威 _playerLocation(原版用 User.CurrentLocation)。
    /// </summary>
    private bool CanMove(MirDirection dir, int distance)
    {
        // 原版用 User.CurrentLocation(权威格); Godot CenterX/Y 由回包驱动且
        // 经 CallDeferred 延迟, 与真实位置有窗口期不同步 → 误判空气墙。
        var cell = _playerCell?.Invoke() ?? new System.Drawing.Point(_mapView.CenterX, _mapView.CenterY);
        int px = cell.X, py = cell.Y;
        for (int i = 1; i <= distance; i++)
        {
            var p = Functions.Move(new System.Drawing.Point(px, py), dir, i);
            if (!CanMove(p.X, p.Y)) return false;
        }
        return true;
    }

    /// <summary>
    /// 正方向走不通时, 按 22.5° 偏角找最近可行相邻方向。
    /// 复刻原版 MouseDirectionBest: 先试 dir, 不行试 ShiftDirection(dir, ±1), 再 ±2。
    /// 全都不行就原地转身 (返回 dir, 发 Move 会被服务端拒, 但转身方向要发)。
    /// </summary>
    private MirDirection BestWalkDirection(MirDirection target, Vector2 mouseWorld, int distance)
    {
        if (CanMove(target, distance)) return target;

        Vector2 playerWorld = _mapView.CellToScreen(_mapView.CenterX, _mapView.CenterY, false)
            + new Vector2(CellWidth / 2f, CellHeight / 2f);
        double angle = Math.Atan2(mouseWorld.X - playerWorld.X,
            -(mouseWorld.Y - playerWorld.Y)) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;
        MirDirection best = (MirDirection)(int)(angle / 45.0);
        if (best == target) best = Functions.ShiftDirection(target, 1);
        MirDirection next = Functions.ShiftDirection(target, -(int)best + (int)target);

        if (CanMove(best, distance)) return best;
        if (CanMove(next, distance)) return next;
        return target;
    }

    private bool IsMouseWithinCells(Vector2 mouseWorld, int range)
    {
        Vector2 playerWorld = _mapView.CellToScreen(_mapView.CenterX, _mapView.CenterY, false)
            + new Vector2(CellWidth / 2f, CellHeight / 2f);
        int x = (int)Math.Floor((mouseWorld.X - playerWorld.X + CellWidth / 2f) / CellWidth);
        int y = (int)Math.Floor((mouseWorld.Y - playerWorld.Y + CellHeight / 2f) / CellHeight);
        return Math.Max(Math.Abs(x), Math.Abs(y)) <= range;
    }

    private MirDirection _lastDir = MirDirection.Down;
}
