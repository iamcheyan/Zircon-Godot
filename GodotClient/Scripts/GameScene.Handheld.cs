using System;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    // ==========================================
    // 1. 右摇杆：人物方向操作 + 走跑缓冲（先走后跑）
    // ==========================================
    private double _rightStickPushStartMs;
    private double _nextRightStickMoveMs;
    private bool _isRightStickMoving;
    private MirDirection _lastRightStickDir;
    private const double HandheldWalkBufferMs = 380.0; // 推住 380ms 内为走(1格)，达到后转跑(2格)

    // ==========================================
    // 2. 左摇杆：目标焦点系统（选择目标 NPC / 怪物 / 掉落物）
    // ==========================================
    public ObjectRenderer _handheldFocusObject;
    private double _nextFocusScanMs;
    private HandheldFocusReticle _handheldFocusReticle;

    private void InitHandheldSystem()
    {
        _handheldFocusReticle = new HandheldFocusReticle(this);
        AddChild(_handheldFocusReticle);
        _handheldFocusReticle.ZIndex = 250;
        var pads = Input.GetConnectedJoypads();
        GD.Print($"[Handheld] 掌机输入与焦点系统已就绪：右摇杆先走后跑，左摇杆目标焦点。已连接手柄数={pads.Count}");
    }

    private void ProcessHandheldInput(double delta)
    {
        double now = Godot.Time.GetTicksMsec();
        ProcessHandheldMovement(now);
        ProcessHandheldFocus(now);
    }

    private void ProcessHandheldMovement(double now)
    {
        float rx = Input.GetJoyAxis(0, JoyAxis.RightX);
        float ry = Input.GetJoyAxis(0, JoyAxis.RightY);
        float rmag = Mathf.Sqrt(rx * rx + ry * ry);

        if (rmag < 0.25f)
        {
            _isRightStickMoving = false;
            _rightStickPushStartMs = 0;
            return;
        }

        if (!_isRightStickMoving)
        {
            _isRightStickMoving = true;
            _rightStickPushStartMs = now;
        }

        // 计算 8 方向 (Up=0, Right=90, Down=180, Left=270)
        double angle = Math.Atan2(rx, -ry) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;
        angle += 22.5;
        if (angle >= 360.0) angle -= 360.0;
        MirDirection dir = (MirDirection)(int)(angle / 45.0);

        // 走跑缓冲：推住时间 < HandheldWalkBufferMs 为走(1格)，达到后转跑(2格)
        double heldMs = now - _rightStickPushStartMs;
        bool isRun = heldMs >= HandheldWalkBufferMs;
        int steps = isRun ? GetRunSteps() : 1;

        if (now >= _nextRightStickMoveMs && CanPlayerMove())
        {
            var targetCell = Functions.Move(_playerLocation, dir, steps);
            if (!IsMovementCellBlocked(targetCell.X, targetCell.Y))
            {
                SendMouseMove(dir, steps, isRun && steps >= 2);
            }
            else if (steps > 1 && !IsMovementCellBlocked(Functions.Move(_playerLocation, dir, 1).X, Functions.Move(_playerLocation, dir, 1).Y))
            {
                // 前方 2 格受阻但 1 格可走
                SendMouseMove(dir, 1, false);
            }
            else if (CanPlayerTurn())
            {
                SendTurn(dir);
            }
            _nextRightStickMoveMs = now + (isRun ? 580.0 : 540.0);
            _lastRightStickDir = dir;
        }
    }

    private void ProcessHandheldFocus(double now)
    {
        // 校验现有焦点有效性
        if (_handheldFocusObject != null)
        {
            if (!IsInstanceValid(_handheldFocusObject)
                || (_handheldFocusObject.Type == ObjectRenderer.Kind.Monster && _handheldFocusObject.Dead)
                || !_objects.ContainsKey(_handheldFocusObject.ObjectID))
            {
                _handheldFocusObject = null;
                if (_combatController != null && _combatController.MouseObject == _handheldFocusObject)
                    _combatController.MouseObject = null;
            }
        }

        float lx = Input.GetJoyAxis(0, JoyAxis.LeftX);
        float ly = Input.GetJoyAxis(0, JoyAxis.LeftY);
        float lmag = Mathf.Sqrt(lx * lx + ly * ly);

        if (lmag < 0.30f || now < _nextFocusScanMs)
            return;

        ObjectRenderer bestTarget = null;
        float bestScore = -9999f;
        float bestDist = 0f;

        foreach (var ob in _objects.Values)
        {
            if (ob == null || !IsInstanceValid(ob)) continue;
            if (ob.Type is not (ObjectRenderer.Kind.NPC or ObjectRenderer.Kind.Monster or ObjectRenderer.Kind.Item or ObjectRenderer.Kind.Player))
                continue;
            if (ob.Type == ObjectRenderer.Kind.Monster && ob.Dead) continue;
            if (ReferenceEquals(ob, _player)) continue;

            int dx = ob.CellX - _playerLocation.X;
            int dy = ob.CellY - _playerLocation.Y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < 0.1f || dist > 24.0f) continue;

            // 摇杆向量 (lx, ly) 与 目标相对向量 (dx, dy) 夹角打分
            float objDirX = dx / dist;
            float objDirY = dy / dist;
            float stickDirX = lx / lmag;
            float stickDirY = ly / lmag;

            float dot = stickDirX * objDirX + stickDirY * objDirY;
            if (dot < 0.15f) continue; // 大致处于推杆锥形方向内

            // NPC 和活怪赋予优先权重
            float typeBonus = ob.Type == ObjectRenderer.Kind.NPC ? 1.8f : (ob.Type == ObjectRenderer.Kind.Monster ? 1.4f : 1.0f);
            float score = (dot * 3.0f - (dist / 24.0f)) * typeBonus;

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = ob;
                bestDist = dist;
            }
        }

        if (bestTarget != null && bestTarget != _handheldFocusObject)
        {
            _handheldFocusObject = bestTarget;
            GD.Print($"[HandheldFocus] 锁定目标: {bestTarget.DisplayName ?? bestTarget.Name} (Type={bestTarget.Type}, Cell=({bestTarget.CellX},{bestTarget.CellY}), Dist={bestDist:F1}, Score={bestScore:F2})");
            if (_combatController != null)
            {
                _combatController.MouseObject = bestTarget;
                if (bestTarget.Type == ObjectRenderer.Kind.Monster)
                    _combatController.TargetObject = bestTarget;
            }
            _nextFocusScanMs = now + 250.0;
        }
    }

    private bool HandleHandheldKeyAction(Key key)
    {
        // Physical A (Enter / Left Click): 交互当前焦点目标
        if (key == Key.Enter)
        {
            if (_handheldFocusObject != null && IsInstanceValid(_handheldFocusObject))
            {
                if (_handheldFocusObject.Type == ObjectRenderer.Kind.NPC)
                {
                    int dist = Math.Max(Math.Abs(_handheldFocusObject.CellX - _playerLocation.X),
                                        Math.Abs(_handheldFocusObject.CellY - _playerLocation.Y));
                    if (dist <= 2)
                    {
                        TrySendNpcCall(_handheldFocusObject.ObjectID);
                    }
                    else
                    {
                        SendAutoPathWaypoint(_playerMapIndex, _handheldFocusObject.CellX, _handheldFocusObject.CellY);
                        _pendingNpcClickObjectId = _handheldFocusObject.ObjectID;
                    }
                    return true;
                }
                else if (_handheldFocusObject.Type == ObjectRenderer.Kind.Monster)
                {
                    _combatController.TargetObject = _handheldFocusObject;
                    return true;
                }
                else if (_handheldFocusObject.Type == ObjectRenderer.Kind.Item)
                {
                    SendAutoPathWaypoint(_playerMapIndex, _handheldFocusObject.CellX, _handheldFocusObject.CellY);
                    return true;
                }
            }
        }
        // Physical B (Escape / Right Click): 关窗或清除焦点
        else if (key == Key.Escape)
        {
            if (WindowManager.CloseTop())
                return true;
            if (_handheldFocusObject != null)
            {
                GD.Print($"[HandheldFocus] 清除目标焦点: {_handheldFocusObject.DisplayName ?? _handheldFocusObject.Name}");
                _handheldFocusObject = null;
                if (_combatController != null && _combatController.MouseObject == _handheldFocusObject)
                    _combatController.MouseObject = null;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 掌机专用焦点指示器：在选中的目标周围绘制醒目的金色角标与名称
/// </summary>
public partial class HandheldFocusReticle : Node2D
{
    private readonly GameScene _scene;

    public HandheldFocusReticle(GameScene scene)
    {
        _scene = scene;
        ZIndex = 250;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        var target = _scene._handheldFocusObject;
        if (target == null || !IsInstanceValid(target)) return;

        Vector2 screenPos = target.Position;

        Color gold = new Color(1.0f, 0.84f, 0.0f, 0.95f);
        Color border = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        float halfW = 24f;
        float halfH = 32f;
        float bracketLen = 8f;
        Vector2 center = screenPos + new Vector2(0f, -halfH / 2f);

        // 四角金框
        // 左上
        DrawLine(center + new Vector2(-halfW, -halfH), center + new Vector2(-halfW + bracketLen, -halfH), gold, 2f);
        DrawLine(center + new Vector2(-halfW, -halfH), center + new Vector2(-halfW, -halfH + bracketLen), gold, 2f);

        // 右上
        DrawLine(center + new Vector2(halfW, -halfH), center + new Vector2(halfW - bracketLen, -halfH), gold, 2f);
        DrawLine(center + new Vector2(halfW, -halfH), center + new Vector2(halfW, -halfH + bracketLen), gold, 2f);

        // 左下
        DrawLine(center + new Vector2(-halfW, halfH), center + new Vector2(-halfW + bracketLen, halfH), gold, 2f);
        DrawLine(center + new Vector2(-halfW, halfH), center + new Vector2(-halfW, halfH - bracketLen), gold, 2f);

        // 右下
        DrawLine(center + new Vector2(halfW, halfH), center + new Vector2(halfW - bracketLen, halfH), gold, 2f);
        DrawLine(center + new Vector2(halfW, halfH), center + new Vector2(halfW, halfH - bracketLen), gold, 2f);

        // 头顶金色倒三角
        Vector2 arrowTip = center + new Vector2(0f, -halfH - 2f);
        DrawPolygon(new[]
        {
            arrowTip,
            arrowTip + new Vector2(-6f, -8f),
            arrowTip + new Vector2(6f, -8f),
        }, new[] { gold });

        // 脚底名称气泡
        string name = target.DisplayName ?? target.Name;
        if (!string.IsNullOrEmpty(name))
        {
            Vector2 textBaseline = center + new Vector2(0f, halfH + 16f);
            RenderPrimitives.DrawLabelWithBackground(this, name, textBaseline, Colors.White, 9f);
        }
    }
}
