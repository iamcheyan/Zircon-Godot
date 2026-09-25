using System;
using System.Collections.Generic;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

// 地图对象基类 (怪物/NPC/物品共用): 动画帧推进 + 动作队列 + 移动插值 + 屏幕定位
// M7 对齐原版 Client/Models/MapObject.cs 的 UpdateFrame/SetFrame/DoNextAction:
//   * 帧推进 = Frame.GetFrame 语义: doubleSpeed 双倍速 / Reversed 倒放 / StaticSpeed 固定速
//   * 动作队列: 一次性动作播完或被 Standing/Dead 打断 -> DoNextAction 弹下一个动作
//   * 移动 = 权威格不变 + 8 向像素偏移 (OffsetX/OffsetY), 动画播完落格
//   * FrameIndexChanged 虚钩子 + SetScale (原版 5172/5314 行)
// 帧号公式与 PlayerRenderer 一致: DrawFrame = FrameIndex + StartIndex + OffSet * dir
public partial class MapObjectNode : Node2D
{
    public uint ObjectID;
    public MirDirection Direction;
    public MirAnimation Animation = MirAnimation.Standing;
    public int FrameIndex;
    public double FrameStartMs;
    protected Frame _currentFrame;

    // 服务端权威格子坐标 + 移动期间像素偏移 (权威格不变, 偏移回拉, 播完落格)
    public int CellX, CellY;
    public float OffsetX, OffsetY;

    // M5 战斗: 血量 (0=未知不显示血条)
    public int Health;
    public int MaxHealth;
    public bool ShowHealthBar;
    public bool Dead;

    // ---- M7: 动作队列 (原版 ActionQueue) ----
    public readonly Queue<MirAnimation> ActionQueue = new();

    // 打断标记: Standing/Dead 立即打断当前动画; 其他动作播完再切 (原版 SetFrame 的 Interupt)
    private bool _interupt = true;

    // ---- 施法动画结束事件 (原版 SetAction 用 OLD 动作跑 release switch 的移植) ----
    // 原版任何一次离开 Spell 动作的切换 (播完 DoNextAction / Standing/Dead 打断)
    // 都会执行该技能的 release 分支。Godot 端在 SetAnimation 顶部对"当前播放中的
    // 施法实例"触发 SpellAnimEnded; 实例号让 GameScene 能把释放特效匹配到具体施法。
    public event Action<int, MagicType> SpellAnimEnded;
    private int _spellInstanceCounter;
    private int _pendingSpellInstance;
    private MagicType _pendingSpellType = MagicType.None;
    private int _lastSpellInstance;
    private MagicType _lastSpellType = MagicType.None;
    public int LastSpellInstance => _lastSpellInstance;

    // 施法开始: 分配实例号。PlaySpell 调用后紧跟 SetAnimation, 该实例在
    // SetAnimation 顶部成为"播放中", 动画结束时以同一实例号触发 SpellAnimEnded。
    public int BeginSpell(MagicType magic)
    {
        _spellInstanceCounter++;
        _pendingSpellInstance = _spellInstanceCounter;
        _pendingSpellType = magic;
        return _pendingSpellInstance;
    }

    // 缩放百分比 (原版 SetScale, -50..50)
    private int _scalePercent;

    // 移动插值 (原版 MovingOffSet): 起点格 + 终点格 + 动画时长
    public System.Drawing.Point MoveFrom;
    public double MoveStartMs;
    private int _targetX, _targetY;
    public int MoveDistance { get; private set; }
    public double SpellReleaseDelayMs
    {
        get
        {
            if (_currentFrame == null || _currentFrame.FrameCount <= 1) return 0;
            int releaseFrame = Math.Min(3, _currentFrame.FrameCount - 1);
            double delay = 0;
            for (int i = 0; i < releaseFrame && i < _currentFrame.Delays.Length; i++)
                delay += _currentFrame.Delays[i].TotalMilliseconds;
            return delay;
        }
    }
    private readonly Queue<(System.Drawing.Point To, MirDirection Direction, int Distance)> _moveQueue = new();

    private Dictionary<MirAnimation, Frame> _frameTable = new(FrameSet.DefaultMonster);
    public virtual Dictionary<MirAnimation, Frame> FrameTable => _frameTable;

    // 立即切换动画 (原版 SetAnimation + SetFrame 的 Interupt 规则)
    public virtual void SetAnimation(MirAnimation anim)
    {
        // 离开上一个施法动作 → 释放 (原版 SetAction 的 OLD 动作 release switch)
        if (_lastSpellInstance != 0)
        {
            SpellAnimEnded?.Invoke(_lastSpellInstance, _lastSpellType);
            _lastSpellInstance = 0;
        }
        // BeginSpell 预留的施法实例在动画真正开始时成为"播放中"
        if (_pendingSpellInstance != 0)
        {
            _lastSpellInstance = _pendingSpellInstance;
            _lastSpellType = _pendingSpellType;
            _pendingSpellInstance = 0;
        }
        Animation = anim;
        _currentFrame = FrameTable?.TryGetValue(anim, out var f) == true && f != null
            ? f
            : FrameSet.DefaultMonster.GetValueOrDefault(MirAnimation.Standing);
        FrameStartMs = Godot.Time.GetTicksMsec(); // 从当前时刻起播, 保证从第 0 帧开始
        FrameIndex = 0;
        // 原版 SetFrame: Standing/Dead 立即打断 (Interupt=true), 其他动作播完再切
        _interupt = anim is MirAnimation.Standing or MirAnimation.Dead;
        QueueRedraw();
    }

    // 原版 SetFrame: 设定当前动作 (含打断规则)
    public virtual void SetFrame(MirAnimation anim) => SetAnimation(anim);

    // 动作入队: 当前动作播完/被打断后执行 (原版 ActionQueue.Add)
    public void QueueAction(MirAnimation anim) => ActionQueue.Enqueue(anim);

    // 原版 DoNextAction: 队列空 -> Standing (Die 后保持 Dead); 否则弹队首
    public virtual void DoNextAction()
    {
        if (_moveQueue.Count > 0 && !Dead)
        {
            var move = _moveQueue.Dequeue();
            StartMove(move.To, move.Direction, move.Distance);
            return;
        }
        if (ActionQueue.Count == 0)
        {
            SetAnimation(Dead ? MirAnimation.Dead : MirAnimation.Standing);
            return;
        }
        SetAnimation(ActionQueue.Dequeue());
    }

    // 帧号变化钩子 (原版 FrameIndexChanged): 攻击/受击/死亡帧事件, 子类可 override
    public virtual void FrameIndexChanged() { }

    // 原版 SetScale: sizePercent -50..50, 以格中心为锚点缩放
    public void SetScale(int sizePercent)
    {
        _scalePercent = sizePercent;
        float s = (100f + Math.Min(50, Math.Max(-50, sizePercent))) / 100f;
        Scale = new Vector2(s, s);
    }

    // Frame.GetFrame 移植 (LibraryCore/FrameSet.cs 1119 行):
    //   doubleSpeed && !StaticSpeed -> elapsed 翻倍; Reversed -> 倒序累计
    //   返回 [0, FrameCount), FrameCount 表示动画已播完 (由 _Process 决定收尾)
    protected int GetFrameIndex(double nowMs, bool doubleSpeed)
    {
        if (_currentFrame == null) return 0;
        if (_currentFrame.FrameCount <= 1) return 0;

        double elapsed = nowMs - FrameStartMs;
        if (doubleSpeed && !_currentFrame.StaticSpeed) elapsed *= 2.0;

        var delays = _currentFrame.Delays;
        if (_currentFrame.Reversed)
        {
            for (int i = 0; i < delays.Length; i++)
            {
                elapsed -= delays[delays.Length - 1 - i].TotalMilliseconds;
                if (elapsed >= 0) continue;
                // FrameSet.GetFrame returns the logical index i here; the
                // reversed delay lookup controls timing, not the returned
                // frame number.  Returning FrameCount-1-i reverses twice.
                return i;
            }
        }
        else
        {
            for (int i = 0; i < delays.Length; i++)
            {
                elapsed -= delays[i].TotalMilliseconds;
                if (elapsed >= 0) continue;
                return i;
            }
        }
        return _currentFrame.FrameCount;
    }

    public override void _Process(double delta)
    {
        double nowMs = Godot.Time.GetTicksMsec();

        // 网络对象可能在刚创建、替换外观或资源异步加载期间尚未拿到动画帧。
        // 这种对象仍然要保留在场景树中等待下一帧，不能让渲染循环因空帧崩溃。
        if (_currentFrame == null)
        {
            SetAnimation(MirAnimation.Standing);
            if (_currentFrame == null) return;
        }

        // 双倍速: 原版 (this != User || Observer) && ActionQueue.Count > 1
        // (Godot 客户端玩家走 PlayerRenderer, 这里只有周围物体, 恒非 User)
        bool doubleSpeed = ActionQueue.Count > 1;
        int frame = GetFrameIndex(nowMs, doubleSpeed);

        // 播完 或 被打断且有排队动作 -> 弹下一个 (原版 UpdateFrame 597-610 行)
        if (frame == _currentFrame.FrameCount || (_interupt && ActionQueue.Count > 0))
        {
            DoNextAction();
            if (_currentFrame == null) return;
            frame = GetFrameIndex(nowMs, doubleSpeed);
            if (frame == _currentFrame.FrameCount)
                frame -= 1; // 停末帧
        }

        UpdateMoveOffset(nowMs);

        if (frame != FrameIndex)
        {
            FrameIndex = frame;
            FrameIndexChanged();
            QueueRedraw();
        }
    }

    // 移动期间像素偏移 (原版 MovingOffSet, 平滑分支): 权威格=终点, 偏移从起点回拉
    private void UpdateMoveOffset(double nowMs)
    {
        if (_currentFrame == null)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }
        if (Animation is not (MirAnimation.Walking or MirAnimation.Running))
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }
        if (_currentFrame.FrameCount <= 1)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        double sum = _currentFrame.Sum;
        if (sum <= 0)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        double t = Math.Clamp((nowMs - MoveStartMs) / sum, 0.0, 1.0);
        double k = 1.0 - t; // 1 -> 起点, 0 -> 终点(权威格)
        float xStep = CellWidth * MoveDistance * (float)k;
        float yStep = CellHeight * MoveDistance * (float)k;
        int x = 0, y = 0;
        // 与旧端 MapObject.UpdateFrame 的 Direction switch 完全一致。
        switch (Direction)
        {
            case MirDirection.Up: y = (int)yStep; break;
            case MirDirection.UpRight: x = -(int)xStep; y = (int)yStep; break;
            case MirDirection.Right: x = -(int)xStep; break;
            case MirDirection.DownRight: x = -(int)xStep; y = -(int)yStep; break;
            case MirDirection.Down: y = -(int)yStep; break;
            case MirDirection.DownLeft: x = (int)xStep; y = -(int)yStep; break;
            case MirDirection.Left: x = (int)xStep; break;
            case MirDirection.UpLeft: x = (int)xStep; y = (int)yStep; break;
        }
        x -= x % 2; // 偶数像素对齐 (原版 x -= x % 2)
        y -= y % 2;
        OffsetX = x;
        OffsetY = y;
    }

    // 开始一格(或多格)移动: 终点为服务端权威位置, 起点为当前格
    public void StartMove(System.Drawing.Point to, MirDirection dir, int distance = 1)
    {
        MoveFrom = new System.Drawing.Point(CellX, CellY);
        _targetX = to.X;
        _targetY = to.Y;
        MoveDistance = Math.Max(1, distance);
        Direction = dir;
        MoveStartMs = Godot.Time.GetTicksMsec();
        CellX = to.X;  // 权威格立即到终点, 视觉位置由 OffsetX/OffsetY 回拉
        CellY = to.Y;
        // 原版 MonsterObject.SetAnimation: MirAction.Moving 始终用 Walking，
        // 不按 Distance 选 Running（Running 只用于玩家 PlayerObject）。
        // 怪物帧表通常没有 Running 条目，误用会导致动画回退 Standing → Offset=0 → 瞬移。
        SetAnimation(MirAnimation.Walking);
    }

    public void QueueMove(System.Drawing.Point to, MirDirection dir, int distance)
    {
        distance = Math.Max(1, distance);
        if (Animation is MirAnimation.Walking or MirAnimation.Running)
        {
            _moveQueue.Enqueue((to, dir, distance));
            return;
        }
        StartMove(to, dir, distance);
    }

    // 旧端 RenderY：向上移动时按起点侧的视觉行排序，避免穿层。
    public int RenderY => OffsetX != 0 || OffsetY != 0
        ? (Direction is MirDirection.Up or MirDirection.UpRight or MirDirection.UpLeft
            ? CellY + MoveDistance : CellY)
        : CellY;

    public int DrawFrame => FrameIndex + _currentFrame.StartIndex + _currentFrame.OffSet * (int)Direction;

    // 受击/血量变动后显示的截止时间戳 (毫秒)，原版受击显示 5 秒血条
    public double DrawHealthUntilMs;

    // 对象头顶血条 (受击/伤害后显示 5 秒, 原客户端同款: 黑底 + 绿/黄/红条)
    protected void DrawHealthBar()
    {
        if (!ShowHealthBar || Dead || MaxHealth <= 0) return;
        if (Godot.Time.GetTicksMsec() > DrawHealthUntilMs) return;
        if (this is ObjectRenderer objectRenderer && objectRenderer.Type == ObjectRenderer.Kind.Monster && !ClientSettings.ShowMonsterHealth)
            return;
        float percent = Math.Clamp(Health / (float)MaxHealth, 0f, 1f);
        if (percent <= 0f) return;

        // 原版 MonsterObject.DrawHealth: Interface 80 背景、79 填充，
        // 坐标为 DrawX / DrawY - 55；填充只裁剪源图宽度，不拉伸贴图。
        var background = MirSkin.GetTexture(LibraryFile.Interface, 80);
        var fill = MirSkin.GetTexture(LibraryFile.Interface, 79);
        if (background == null || fill == null) return;

        Vector2 bgSize = background.GetSize();
        Vector2 fillSize = fill.GetSize();
        float x = 24f - bgSize.X / 2f;
        float y = -55f;
        DrawTextureRect(background, new Rect2(x, y, bgSize.X, bgSize.Y), false);

        float width = Math.Clamp((int)(fillSize.X * percent), 1, (int)fillSize.X);
        DrawTextureRectRegion(fill, new Rect2(x + 1f, y + 1f, width, fillSize.Y),
            new Rect2(0, 0, width, fillSize.Y),
            new Color(1f, 1f, 1f, 1f));
    }

    // 计算本节点屏幕位置 (相机锚定玩家, 含移动像素偏移)
    public void ComputeScreenPos(int camCenterX, int camCenterY, int viewRangeX, int viewRangeY,
        float screenOffsetX, float screenOffsetY, MapView mapView = null)
    {
        if (mapView != null)
        {
            Position = mapView.CellToScreen(CellX, CellY, true) + new Vector2(OffsetX, OffsetY);
            return;
        }
        Position = new Vector2(
            (CellX - camCenterX + viewRangeX) * CellWidth + screenOffsetX + OffsetX,
            (CellY - camCenterY + viewRangeY + 1) * CellHeight + screenOffsetY - 34 + OffsetY
        );
    }

    private const int CellWidth = 48;
    private const int CellHeight = 32;
}
