using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.Network;

namespace ZirconClient.Scripts;

/// <summary>
/// 战斗交互 (移植自 Client/Scenes/Views/MapControl.cs ProcessInput 战斗部分)。
/// 独立节点, 挂在 GameScene 下。不修改 GameScene/MapView。
///
/// 职责:
///   1. 鼠标悬停 -> 高亮最近可点物体 (MouseObject)，由 GameScene/各主体渲染器绘制原版轮廓
///   2. 左键点怪物 -> 选中为 TargetObject (服务端无包, 纯客户端状态)；
///      Shuriken 点击分支 (超距提示/冷却取消/投掷后清目标) 就地处理
///   3. 顶部自动攻击分支: 选中目标相邻 (距离=1) 且冷却到 -> 平砍 (C.Attack)
///      —— 原版 ProcessInput 顺序，先于任何鼠标分支
///   4. Shift + 左键且未选中目标 -> 朝鼠标方向原地攻击；已选中则走正常流程
///   5. 底部追击: 选中目标 >1 格 -> 按 MoveTime(600ms) 节拍 C.Move 接近；
///      目标死亡保留选中，被阻挡时原地转向
///   6. 右键 -> 取消选中 (RightClickDeTarget)
///
/// 攻击冷却: 使用原版 max(800, AttackDelay - Stats[AttackSpeed]*ASpeedRate)
/// 且超重/Neutralize 翻倍的本地预测，服务端回包仍是最终校验。
/// </summary>
public partial class CombatController : Node2D
{
    // 与 MapView 一致的渲染常量 (用于命中测试的坐标换算)
    private const float CellWidth = 48f;
    private const float CellHeight = 32f;
    private const double AttackIntervalMs = 800.0;

    private readonly MapView _mapView;
    private readonly Func<IReadOnlyDictionary<uint, ObjectRenderer>> _getObjects;
    private readonly Func<System.Drawing.Point> _getPlayerCell;
    private readonly Action<MirDirection, MirAction, MagicType> _sendAttack;
    private readonly Action<MirDirection, int> _sendMove;
    private readonly Func<double> _getAttackInterval;
    private readonly Func<ObjectRenderer, bool> _canRangeAttack;
    private readonly Action<MirDirection, uint> _sendRangeAttack;
    private readonly Func<int, int, bool> _cellBlocked;
    private readonly Func<bool> _rightClickDeTarget;
    private readonly Func<bool> _mouseOverUi;
    private readonly Func<bool> _canUseCombatInput;
    // 原版 Shuriken 超距提示（MapControl.OnMouseDown 683-692 的 ReceiveChat
    // "Unable to throw Shuriken, Your target is too far." + Stop()）。
    private readonly Action _notifyRangeAttackTooFar;
    // 纯形状判定（不含坐骑）：超距提示分支在任何坐骑状态下都先触发。
    private readonly Func<bool> _isShurikenShape;
    // 骑马状态：顶部自动攻击与 TryAttack 的 Horse == None 门控。
    private readonly Func<bool> _isMounted;
    // ElementalHurricane buff：原版 ProcessInput 顶部攻击/移动分支门控。
    private readonly Func<bool> _isHurricane;
    // 原版 AutoRun 分支先于鼠标分支与底部追击；开启时由 MouseWalker 持续 Run。
    private readonly Func<bool> _isAutoRun;
    // 原版 MagicAction 入队期间 ProcessInput 整体 return，暂停攻击/追击。
    private readonly Func<bool> _isMagicPending;
    // 追击被阻挡时的原地转向（原版 AttemptAction(Standing)）。
    private readonly Action<MirDirection> _sendTurn;
    private readonly Action _clearMagicLock;
    private readonly Action _stopPlayerAction;
    // 预测坐标会在发包时先切到下一格；攻击必须等视觉移动完成，不能只看逻辑格距。
    private readonly Func<bool> _isPlayerMoving;
    // 目标追击沿用原版的“第一段走、后续可跑”节奏。
    private readonly Func<int> _getRunSteps;
    // 目标追击不依赖右键是否按住；左键点怪后，第一段走完即可进入跑步。
    private readonly Func<int> _getTargetRunSteps;
    private bool _targetMoveStarted;

    public bool Enabled = true;

    // 选中目标 (null=无)。原版 MapObject.TargetObject。
    public ObjectRenderer TargetObject;
    // 鼠标悬停物体 (目标高亮与点击攻击共用)
    public ObjectRenderer MouseObject;

    public static ObjectRenderer SelectLatestHit(IEnumerable<ObjectRenderer> candidates)
        => candidates?.Where(x => x != null)
            .OrderByDescending(x => x.HitOrder)
            .FirstOrDefault();

    public static bool CanAttackObject(ObjectRenderer target)
        => target != null && !target.Dead
            && target.Type is ObjectRenderer.Kind.Monster or ObjectRenderer.Kind.Player
            && (target.Type != ObjectRenderer.Kind.Monster || (target.MonsterInfo?.AI ?? -1) >= 0);

    /// <summary>
    /// 原版地图输入在玩家当前格优先处理脚下拾取；战斗控制器先收到
    /// Godot _Input，因此必须把同格的普通左键留给 GameScene._UnhandledInput。
    /// Shift 原地攻击仍由战斗分支接管。
    /// </summary>
    public static bool ShouldDeferForMapPickup(System.Drawing.Point mouseCell,
        System.Drawing.Point playerCell, bool shiftPressed)
        => !shiftPressed && mouseCell == playerCell;

    /// <summary>
    /// 原版 CheckCursor 环扫描边界语义：下/右越界 → continue（跳过该行/列），
    /// 上/左越界 → break（起始值已越界时整个环不再扫描）。
    /// 返回 1=continue、-1=break、0=正常扫描。
    /// </summary>
    public static int RingEdgeMode(int coord, int limit)
    {
        if (coord >= limit) return 1;
        if (coord < 0) return -1;
        return 0;
    }

    /// <summary>
    /// 原版 MapControl.ProcessInput 底部接近分支：
    /// 目标是玩家或宠物所有者且未按 Shift → 只选中，不接近不攻击。
    /// </summary>
    public static bool ShouldSelectOnly(bool isPlayer, bool hasPetOwner, bool shift)
        => (isPlayer || hasPetOwner) && !shift;

    public enum ShurikenClickResult
    {
        /// <summary>非飞镖武器/骑马在范围内 → 落入普通近战选中流程。</summary>
        Melee,
        /// <summary>超 MagicRange（任何坐骑状态）→ 提示 + Stop() 清目标。</summary>
        HintAndClear,
        /// <summary>冷却中 → Stop() 清目标，不发。</summary>
        ClearOnly,
        /// <summary>可投 → RangeAttack + Stop() 清目标。</summary>
        ThrowAndClear,
    }

    /// <summary>
    /// 原版 MapControl.OnMouseDown Shuriken 分支（683-739）的纯判定。
    /// 分支顺序与原文一致：超距提示先于坐骑检查；坐骑在范围内落入近战。
    /// </summary>
    public static ShurikenClickResult ShurikenClick(bool isShurikenShape, bool canThrow,
        bool outOfMagicRange, bool onCooldown)
    {
        if (!isShurikenShape) return ShurikenClickResult.Melee;
        if (outOfMagicRange) return ShurikenClickResult.HintAndClear;
        if (!canThrow) return ShurikenClickResult.Melee;  // 骑马：走普通近战
        return onCooldown ? ShurikenClickResult.ClearOnly : ShurikenClickResult.ThrowAndClear;
    }

    private double _nextAttackMs;

    /// <summary>
    /// 原版 CConnection.Process(ObjectRemove) 会清掉所有指向该对象的
    /// Target/Mouse 引用。切图和迟到的 ObjectRemove 都必须经过同一入口，
    /// 否则自动攻击会继续读取已经 QueueFree 的节点。
    /// </summary>
    public void RemoveObjectReference(uint objectId)
    {
        if (TargetObject?.ObjectID == objectId)
        {
            TargetObject = null;
            _targetMoveStarted = false;
        }
        if (MouseObject?.ObjectID == objectId) MouseObject = null;
        _nextAttackMs = 0;
        QueueRedraw();
    }

    public CombatController(MapView mapView,
        Func<IReadOnlyDictionary<uint, ObjectRenderer>> getObjects,
        Func<System.Drawing.Point> getPlayerCell,
        Action<MirDirection, MirAction, MagicType> sendAttack,
        Action<MirDirection, int> sendMove,
        Func<double> getAttackInterval = null,
        Func<ObjectRenderer, bool> canRangeAttack = null,
        Action<MirDirection, uint> sendRangeAttack = null,
        Func<int, int, bool> cellBlocked = null,
        Func<bool> rightClickDeTarget = null,
        Func<bool> mouseOverUi = null,
        Func<bool> canUseCombatInput = null,
        Action notifyRangeAttackTooFar = null,
        Func<bool> isShurikenShape = null,
        Func<bool> isMounted = null,
        Func<bool> isHurricane = null,
        Func<bool> isAutoRun = null,
        Func<bool> isMagicPending = null,
        Action<MirDirection> sendTurn = null,
        Action clearMagicLock = null,
        Func<bool> isPlayerMoving = null,
        Func<int> getRunSteps = null,
        Func<int> getTargetRunSteps = null,
        Action stopPlayerAction = null)
    {
        _mapView = mapView;
        _getObjects = getObjects;
        _getPlayerCell = getPlayerCell;
        _sendAttack = sendAttack;
        _sendMove = sendMove;
        _getAttackInterval = getAttackInterval;
        _canRangeAttack = canRangeAttack;
        _sendRangeAttack = sendRangeAttack;
        _cellBlocked = cellBlocked;
        _rightClickDeTarget = rightClickDeTarget;
        _mouseOverUi = mouseOverUi;
        _canUseCombatInput = canUseCombatInput;
        _notifyRangeAttackTooFar = notifyRangeAttackTooFar;
        _isShurikenShape = isShurikenShape;
        _isMounted = isMounted;
        _isHurricane = isHurricane;
        _isAutoRun = isAutoRun;
        _isMagicPending = isMagicPending;
        _sendTurn = sendTurn;
        _clearMagicLock = clearMagicLock;
        _stopPlayerAction = stopPlayerAction;
        _isPlayerMoving = isPlayerMoving;
        _getRunSteps = getRunSteps;
        _getTargetRunSteps = getTargetRunSteps;
        SetProcessAlways();
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _mapView?.Map == null) return;

        // 更新鼠标悬停物体
        MouseObject = PickObjectAtMouse();
        QueueRedraw();  // 重画悬停/选中高亮

        if (_canUseCombatInput?.Invoke() == false)
            return;
        // 原版 MagicAction 入队期间 ProcessInput 整体 return（等待动作
        // 边界），攻击与追击都暂停，由 GameScene._Process 在走完后释放。
        if (_isMagicPending?.Invoke() == true)
            return;

        bool shift = Input.IsKeyPressed(Key.Shift);
        bool leftHeld = Input.IsMouseButtonPressed(MouseButton.Left);
        bool rightHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        var playerCell = _getPlayerCell();
        double now = Godot.Time.GetTicksMsec();

        // 原版 ProcessInput 顶部自动攻击分支（MapControl.cs:875-895）：
        // 在任何鼠标分支之前、不受鼠标按键影响。目标必须相邻（Chebyshev
        // == 1，同一格不砍——拾取优先）、冷却到、未骑马、无元素飓风。
        if (_isHurricane?.Invoke() != true
            && TargetObject != null && IsInstanceValid(TargetObject) && !TargetObject.Dead
            && ((TargetObject.Type == ObjectRenderer.Kind.Monster
                    && string.IsNullOrWhiteSpace(TargetObject.PetOwner))
                || shift)
            && Functions.Distance(new System.Drawing.Point(TargetObject.CellX, TargetObject.CellY),
                playerCell) == 1
            && _isPlayerMoving?.Invoke() != true
            && now >= _nextAttackMs && _isMounted?.Invoke() != true)
        {
            _sendAttack(Functions.DirectionFromPoint(playerCell,
                new System.Drawing.Point(TargetObject.CellX, TargetObject.CellY)),
                MirAction.Attack, MagicType.None);
            _nextAttackMs = now + GetAttackInterval();
            return;
        }

        // 原版 AutoRun 分支（896-901）先于鼠标分支；开启时由 MouseWalker
        // 持续 Run，底部追击不再运行。
        if (_isAutoRun?.Invoke() == true)
            return;

        // 原版鼠标分支 case Left（904-913）：Shift 且未选中目标 →
        // 朝鼠标方向攻击后无条件返回（骑马/飓风/冷却在 TryAttack 内门控）。
        if (shift && TargetObject == null && leftHeld)
        {
            TryAttack(DirectionToMouse(playerCell));
            return;
        }

        if (rightHeld)
            return;

        // 原版 case Left（926-927）：悬停是活着的非物品（怪物/玩家/宠物）
        // 时 break 落到底部追击；其余情况（拾取/采矿/普通行走）由
        // GameScene._UnhandledInput / MouseWalker 处理，这里不接管。
        if (leftHeld && !IsHoverLiveNonItem())
            return;

        // 底部追击分支（MapControl.cs:1058-1129）。
        if (TargetObject == null || !IsInstanceValid(TargetObject))
        {
            TargetObject = null;
            _targetMoveStarted = false;
            return;
        }
        // D15：目标死亡保留选中（尸体高亮），等 ObjectRemove / 自身死亡 /
        // 切图 / 右键 DeTarget 才清除——与原版一致，绝不在这里主动清空。
        if (TargetObject.Dead)
            return;
        // 防御性跳过 AI<0 的怪物（原版 CanAttack 拒绝后不可能成为目标）。
        if (TargetObject.Type == ObjectRenderer.Kind.Monster
            && (TargetObject.MonsterInfo?.AI ?? -1) < 0)
            return;
        // 玩家/宠物目标：未按 Shift 只选中不追击（原版 1060-1061）。
        if ((TargetObject.Type == ObjectRenderer.Kind.Player
                || !string.IsNullOrWhiteSpace(TargetObject.PetOwner)) && !shift)
            return;
        // 相邻交给顶部自动攻击分支。
        if (Functions.Distance(new System.Drawing.Point(TargetObject.CellX, TargetObject.CellY),
                playerCell) <= 1)
            return;

        MirDirection dir = Functions.DirectionFromPoint(playerCell,
            new System.Drawing.Point(TargetObject.CellX, TargetObject.CellY));

        // 原版 1063-1071：直行被挡或飓风 → DirectionBest；若没有更好的
        // 方向则原地转向目标（AttemptAction Standing），不发移动。
        if (!CanStep(playerCell, dir) || _isHurricane?.Invoke() == true)
        {
            var best = BestApproachDirection(playerCell, TargetObject);
            if (best == dir)
            {
                _sendTurn?.Invoke(dir);
                return;
            }
            dir = best;
        }
        if (_isHurricane?.Invoke() == true)
            return;

        // 原版 AttemptAction(Moving) 的 NextActionTime 门控 ≈ 600ms/次
        // （walk 帧表 Delays 之和）。之前 120ms 的追击节奏相对原版是
        // 5 倍 C.Move 发包量：服务端限速但客户端每次都会重启走动画、
        // 松开鼠标后 DelayedAction 队列还会幽灵走位，必须按 MoveTime 节拍。
        int distance = _targetMoveStarted
            ? Math.Max(1, _getTargetRunSteps?.Invoke() ?? _getRunSteps?.Invoke() ?? 1)
            : 1;
        // 追击只能停在目标相邻格，不能把两格跑步请求直接送进目标所在格。
        int targetDistance = Functions.Distance(new System.Drawing.Point(TargetObject.CellX, TargetObject.CellY), playerCell);
        distance = Math.Min(distance, Math.Max(1, targetDistance - 1));
        // A multi-cell request is only valid when every intermediate cell is clear.
        // If the run segment is blocked, fall back to the original one-cell chase.
        if (distance > 1 && !CanMoveDistance(playerCell, dir, distance))
            distance = 1;
        _sendMove?.Invoke(dir, distance);
        _targetMoveStarted = true;
        _nextAttackMs = now + Globals.MoveTime.TotalMilliseconds;
    }

    private bool IsHoverLiveNonItem()
        => MouseObject != null
            && MouseObject.Type != ObjectRenderer.Kind.Item
            && !MouseObject.Dead;

    public override void _Draw()
    {
        // 原版高亮属于目标主体的 DrawBody/DrawBlend，而不是一个固定
        // 48x32 的格子框；由 GameScene 将目标颜色传给 ObjectRenderer。
    }

    // 选中/攻击用鼠标点击 (不和 GameScene._Input 的键盘处理冲突: 不同输入类型)
    public override void _Input(InputEvent @event)
    {
        if (!Enabled || _mapView?.Map == null) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            // CombatController receives _Input before Control._GuiInput. The
            // legacy client dispatches UI clicks before MapControl, so a click
            // on an inventory/shop button must never select or attack a map
            // target. Automatic target processing in _Process intentionally
            // remains independent of this click-only guard.
            if (_mouseOverUi?.Invoke() == true) return;
            // 采集/钓鱼/驯服状态由 GameScene 的地图分支负责取消；
            // CombatController 先收到 _Input，必须先禁止攻击抢占这次点击。
            if (_canUseCombatInput?.Invoke() == false) return;

            if (mb.ButtonIndex == MouseButton.Left)
            {
                bool shiftHeld = mb.ShiftPressed || Input.IsKeyPressed(Key.Shift);
                ObjectRenderer hit = PickObjectAtMouse();
                // 同格拾取优先：非 Shift 时把普通左键让给 GameScene._UnhandledInput。
                // Shift 点击脚下格不拾取（原版 case Left 的 Shift 分支先返回）。
                if (ShouldDeferForMapPickup(MouseCell(), _getPlayerCell(), shiftHeld))
                {
                    // 脚下拾取优先，但这次点击仍代表用户放弃当前攻击目标。
                    TargetObject = null;
                    _targetMoveStarted = false;
                    _stopPlayerAction?.Invoke();
                    _clearMagicLock?.Invoke();
                    return;
                }

                // 原版 OnMouseDown（683-739）：CanAttack 通过 → 选中并走
                // Shuriken 分支；未通过 → 取消选中。Shift 点击不在这里
                // 特判：选中/清空与普通点击一致，持续攻击由 _Process 的
                // Shift 分支（TargetObject == null 时朝鼠标）驱动。
                bool attackable = CanAttackObject(hit);
                if (!attackable)
                {
                    TargetObject = null;
                    _targetMoveStarted = false;
                    _stopPlayerAction?.Invoke();
                    _clearMagicLock?.Invoke();
                    QueueRedraw();
                    return;
                }
                if (TargetObject != hit)
                {
                    _targetMoveStarted = false;
                    // 主动切换目标必须先结束上一目标的攻击动作；否则移动
                    // 动画会排在攻击动作后面，人物会保持攻击姿势滑向新目标。
                    _stopPlayerAction?.Invoke();
                }
                TargetObject = hit;
                GD.Print($"[Combat] 选中目标: {hit.DisplayName} ObjectID={hit.ObjectID}");

                var pCell = _getPlayerCell();
                var hitCell = new System.Drawing.Point(hit.CellX, hit.CellY);
                var result = ShurikenClick(_isShurikenShape?.Invoke() == true,
                    _canRangeAttack?.Invoke(hit) == true,
                    !Functions.InRange(hitCell, pCell, Globals.MagicRange),
                    Godot.Time.GetTicksMsec() < _nextAttackMs);
                if (result == ShurikenClickResult.HintAndClear)
                {
                    _notifyRangeAttackTooFar?.Invoke();
                    TargetObject = null;
                    _targetMoveStarted = false;
                    _stopPlayerAction?.Invoke();
                    QueueRedraw();
                    return;
                }
                if (result == ShurikenClickResult.ClearOnly)
                {
                    TargetObject = null;
                    _targetMoveStarted = false;
                    _stopPlayerAction?.Invoke();
                    QueueRedraw();
                    return;
                }
                if (result == ShurikenClickResult.ThrowAndClear)
                {
                    _sendRangeAttack?.Invoke(Functions.DirectionFromPoint(pCell, hitCell),
                        hit.ObjectID);
                    _nextAttackMs = Godot.Time.GetTicksMsec() + GetAttackInterval();
                    TargetObject = null;
                    QueueRedraw();
                    return;
                }
                // 普通近战选中：不立即移动/攻击。追击与相邻攻击由 _Process
                // 每帧驱动（原版 OnMouseDown 选中后同样等下一帧 ProcessInput，
                // 顶部攻击分支/底部追击接管）。
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                // 原版只有启用 RightClickDeTarget 时才取消怪物目标；
                // 右键查看玩家装备不能意外清掉普通目标。
                if ((_rightClickDeTarget?.Invoke() ?? true)
                    && TargetObject?.Type == ObjectRenderer.Kind.Monster)
                {
                    TargetObject = null;
                    _targetMoveStarted = false;
                    _stopPlayerAction?.Invoke();
                    _clearMagicLock?.Invoke();
                }
            }
        }
    }

    private void TryAttack(MirDirection direction)
    {
        // 原版 Shift 分支（915-916）：Horse == None && !hurricane 才攻击。
        if (_isHurricane?.Invoke() == true) return;
        if (_isMounted?.Invoke() == true) return;
        double now = Godot.Time.GetTicksMsec();
        if (now < _nextAttackMs) return;
        GD.Print($"[Combat] ATTACK direction={direction} target={TargetObject?.ObjectID ?? 0}");
        _sendAttack(direction, MirAction.Attack, MagicType.None);
        _nextAttackMs = now + GetAttackInterval();
    }

    /// <summary>攻击间隔完全交给 GameScene 的原版公式（800 地板 +
    /// AS 减免 + 超重/Neutralize 翻倍）；这里只做 800ms 兜底。</summary>
    private double GetAttackInterval()
        => _getAttackInterval?.Invoke() ?? AttackIntervalMs;

    private MirDirection BestApproachDirection(System.Drawing.Point from, ObjectRenderer target)
    {
        var targetCell = new System.Drawing.Point(target.CellX, target.CellY);
        var direct = Functions.DirectionFromPoint(from, targetCell);
        if (CanStep(from, direct)) return direct;

        double angle = Math.Atan2(targetCell.X - from.X, -(targetCell.Y - from.Y)) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;
        var best = (MirDirection)(int)(angle / 45.0);
        if (best == direct) best = Functions.ShiftDirection(direct, 1);
        var next = Functions.ShiftDirection(direct, -(int)best + (int)direct);
        if (CanStep(from, best)) return best;
        if (CanStep(from, next)) return next;
        return direct;
    }

    private bool CanStep(System.Drawing.Point from, MirDirection direction)
    {
        var next = Functions.Move(from, direction, 1);
        if (_mapView?.Map == null || next.X < 0 || next.Y < 0
            || next.X >= _mapView.Map.Width || next.Y >= _mapView.Map.Height)
            return false;
        return !_mapView.Map.Cells[next.X, next.Y].Flag
            && !(_cellBlocked?.Invoke(next.X, next.Y) ?? false);
    }

    private bool CanMoveDistance(System.Drawing.Point from, MirDirection direction, int distance)
    {
        var current = from;
        for (int i = 0; i < distance; i++)
        {
            if (!CanStep(current, direction)) return false;
            current = Functions.Move(current, direction, 1);
        }
        return true;
    }

    /// <summary>鼠标位置下方最近的可点物体 (怪物/NPC/物品), 1 格内才算命中。</summary>
    private ObjectRenderer PickObjectAtMouse()
    {
        // All map objects and this controller are children of the scaled
        // GameScene. Convert the viewport mouse point back into this node's
        // logical (48x32) coordinate space before comparing it with an object.
        // Comparing raw viewport pixels with GetGlobalTransformWithCanvas()
        // made the hit box drift when the 2x world scale/canvas transform was
        // applied.
        Vector2 mouseLocal = GetGlobalTransformWithCanvas().AffineInverse()
            * GetViewport().GetMousePosition();

        var mouseCell = _mapView.ScreenToCell(GetViewport().GetMousePosition());
        return PickObjectAt(mouseCell, mouseLocal);
    }

    /// <summary>
    /// 供在线交互审计使用：以指定逻辑格及其格中心执行同一套 CheckCursor
    /// 扫描，不直接注入 MouseObject，确保测试覆盖坐标转换和命中优先级。
    /// </summary>
    public ObjectRenderer PickObjectAtCellForAudit(System.Drawing.Point cell)
    {
        if (_mapView?.Map == null) return null;
        var candidate = SelectLatestHit(_getObjects().Values
            .Where(x => x != null && x.CellX == cell.X && x.CellY == cell.Y));
        Vector2 local = candidate?.Position ?? _mapView.CellToScreen(cell.X, cell.Y, true);
        return PickObjectAt(cell, local);
    }

    private ObjectRenderer PickObjectAt(System.Drawing.Point mouseCell, Vector2 mouseLocal)
    {

        // 原版 CheckCursor 按 d=0..3 的格子顺序扫描，而不是把所有对象
        // 收集后按距离排序。活着的对象立即返回；死亡对象/宠物和掉落物
        // 分别保留为后备命中，完全没有活对象时才使用它们。
        var objects = _getObjects();
        // 原版按每个 Cell.Objects 的逆序扫描，即最新加入/移动到该格的
        // 对象优先；全局字典顺序不能表达这个局部顺序。
        var orderedObjects = objects.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.HitOrder)
            .ToArray();

        // 物品名称标签位于物品图标上方，可能落在另一个逻辑格内；先做
        // 标签命中，避免点击标签时被地图格扫描转换成普通移动。
        var labelItem = orderedObjects.FirstOrDefault(ob =>
            ob.Type == ObjectRenderer.Kind.Item
            && ob.IsGroundItemLabelHit(mouseLocal - ob.Position));
        if (labelItem != null) return labelItem;

        ObjectRenderer deadObject = null;
        ObjectRenderer itemObject = null;
        for (int d = 0; d < 4; d++)
        {
            for (int y = mouseCell.Y - d; y <= mouseCell.Y + d; y++)
            {
                // 原版 CheckCursor 边界：下/右越界跳过该行/列，上/左越界
                // 直接 break（起始值已越界时整个环不再扫描）。Godot 之前
                // 用 continue 会在鼠标靠近地图上/左边缘时多命中原版不会
                // 命中的对象，破坏边缘格的优先级一致性。
                int ymode = RingEdgeMode(y, _mapView.Map.Height);
                if (ymode == 1) continue;
                if (ymode == -1) break;
                for (int x = mouseCell.X - d; x <= mouseCell.X + d; x++)
                {
                    int xmode = RingEdgeMode(x, _mapView.Map.Width);
                    if (xmode == 1) continue;
                    if (xmode == -1) break;
                    ObjectRenderer cellSelect = null;
                    foreach (var ob in orderedObjects)
                    {
                        // 原版 CheckCursor 只排除本地玩家；其它玩家必须保留在
                        // 命中链路中，否则 Ctrl+右键观察/组队等操作永远拿不到目标。
                        if (ob == null || ob.CellX != x || ob.CellY != y)
                            continue;

                        // NPC 名称和高体型 NPC 的可视区域可能明显高于普通
                        // 2.25 格身体。名称点击仍归属于同一 NPC，不改变
                        // 原版同格/逆序优先级，只扩大 NPC 的垂直命中范围。
                        float maxY = ob.Type switch
                        {
                            ObjectRenderer.Kind.Item => 0.9f,
                            ObjectRenderer.Kind.NPC => 4.5f,
                            _ => 2.25f,
                        };
                        float maxX = ob.Type == ObjectRenderer.Kind.NPC ? 1.5f : 1.0f;
                        Vector2 objectLocal = ob.Position;
                        float dx = Math.Abs(mouseLocal.X - objectLocal.X) / CellWidth;
                        float dy = Math.Abs(mouseLocal.Y - objectLocal.Y) / CellHeight;
                        bool mouseOver = dx <= maxX && dy <= maxY;

                        bool deadOrPet = ob.Dead || ob.Type == ObjectRenderer.Kind.Monster && !string.IsNullOrWhiteSpace(ob.PetOwner);
                        if (deadOrPet)
                        {
                            deadObject ??= ob;
                            continue;
                        }
                        if (ob.Type == ObjectRenderer.Kind.Item)
                        {
                            itemObject ??= ob;
                            continue;
                        }
                        if (x == mouseCell.X && y == mouseCell.Y && !mouseOver)
                            cellSelect ??= ob;
                        else
                            return ob;
                    }
                    if (cellSelect != null) return cellSelect;
                }
            }
        }
        return deadObject ?? itemObject;
    }

    /// <summary>玩家格 -> 鼠标方向 (8 方向)。</summary>
    public System.Drawing.Point MouseCell()
    {
        if (_mapView?.Map == null) return _getPlayerCell();
        return _mapView.ScreenToCell(GetViewport().GetMousePosition());
    }

    /// <summary>玩家格到鼠标格的原版八方向。</summary>
    public MirDirection DirectionToMouse(System.Drawing.Point pCell)
    {
        return Functions.DirectionFromPoint(pCell, MouseCell());
    }

    private void SetProcessAlways() => ProcessMode = ProcessModeEnum.Always;
}
