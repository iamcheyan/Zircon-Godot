using System;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

/// <summary>
/// 飞行物特效节点（移植自原版 Client/Models/MirProjectile.cs）。
/// 从 Origin 格子飞向 Target/MapCell，按时间线性插值位置，
/// 到达后触发 CompleteAction 并自删。支持 16 方向帧、Explode、Delay 倍率。
/// </summary>
public partial class MirProjectileNode : MirEffectNode
{
    public System.Drawing.Point Origin;
    public int Speed = 50;
    public bool Explode;
    public int Delay;            // 越大越慢(duration *= Delay)
    public int Direction16;
    public bool Has16Directions = true;

    private Func<int, int, Vector2> _cameraFnByCell;
    private int _targetCellX;
    private int _targetCellY;

    public void SetupProjectile(LibraryFile file, int startIndex, int frameCount, double frameDelayMs,
        MapObjectNode target, int mapCellX, int mapCellY,
        System.Drawing.Point origin, Func<int, int, Vector2> cameraFnByCell)
    {
        Setup(file, startIndex, frameCount, frameDelayMs, target, mapCellX, mapCellY, null);

        Origin = origin;
        Direction = MirDirection.Up;
        _cameraFnByCell = cameraFnByCell;
        _targetCellX = mapCellX;
        _targetCellY = mapCellY;
    }

    public void SetupProjectileTarget(LibraryFile file, int startIndex, int frameCount, double frameDelayMs,
        Node2D target, Func<int> targetRenderYFn, System.Drawing.Point origin,
        Func<int, int, Vector2> cameraFnByCell)
    {
        SetupTarget(file, startIndex, frameCount, frameDelayMs, target, targetRenderYFn);
        Origin = origin;
        Direction = MirDirection.Up;
        _cameraFnByCell = cameraFnByCell;
        _targetCellX = 0;
        _targetCellY = 0;
    }

    public override void _Process(double delta)
    {
        double now = Godot.Time.GetTicksMsec();

        if (_cameraFnByCell == null)
        {
            CompleteAction?.Invoke();
            QueueFree();
            return;
        }

        // 原版每帧重新取目标位置。这样目标移动、玩家滚屏和玩家自身的
        // MovingOffSet 都会反映到飞行轨迹，而不是沿着旧屏幕坐标漂移。
        // 目标节点可能已被释放（原版 Target 是托管 MapObject，释放后按
        // `Target?.CurrentLocation ?? MapTarget` 回退到地图格），这里同样
        // 用 IsInstanceValid 回退到目标格，避免 ObjectDisposedException。
        Vector2 originScreen = _cameraFnByCell(Origin.X, Origin.Y);
        Vector2 targetScreen = (_targetNode != null && IsInstanceValid(_targetNode))
            // 原版 MirProjectile 读取 Target.CurrentLocation（地图格），
            // 不是对象 baseline Position。对所有目标类型统一使用地图格坐标。
            ? _targetNode is MapObjectNode targetObject
                ? _cameraFnByCell(targetObject.CellX, targetObject.CellY)
                : _targetNode is PlayerRenderer player
                    ? _cameraFnByCell(player.CellX, player.CellY)
                    : _targetNode.Position
            : (_target != null && IsInstanceValid(_target))
                ? _cameraFnByCell(_target.CellX, _target.CellY)
                : _cameraFnByCell(_targetCellX, _targetCellY);
        var origin = ToLegacyProjectilePoint(originScreen);
        var target = ToLegacyProjectilePoint(targetScreen);

        Direction = Functions.DirectionFromPoint(origin, target);
        Direction16 = Functions.Direction16(origin, target);
        // 原版 MirProjectile.Process(): duration = Distance(p1, p2) * 1ms,
        // 其中 p = (x, y/32*48) 即等距 48 单位坐标；Delay 是原始倍率
        // (Shuriken Delay=2 → 2 倍慢)，不是百分比。Godot 本地坐标 == 旧端
        // 48/32 像素，因此 ToLegacyProjectilePoint 后的 Distance 直接就是
        // 毫秒数。曾误用 distancePx*1.5 + Delay/10 + 50ms 下限，导致飞行
        // 比原版慢 1.5 倍且同格投掷被拖延——已按原版公式修正。
        long duration = Functions.Distance(origin, target);
        if (Delay > 0) duration *= Delay;
        if (!Has16Directions) Direction16 /= 2;

        // 原版 location == Origin 时立即完成 (duration == 0 分支)。
        if (duration <= 0)
        {
            CompleteAction?.Invoke();
            QueueFree();
            return;
        }

        int frame = GetProjectileFrame(now);
        if (Reversed) frame = FrameCount - frame - 1;
        if (frame != _frameIndex)
        {
            _frameIndex = frame;
            FrameIndexChanged?.Invoke(frame);
        }

        // 原版: Target==null 且非 Explode 的投射物 (如火球的地面落点弹道)
        // 到达后不截停在落点，继续沿直线飞出屏幕才结束；只有挂对象或
        // Explode 的投射物才在 duration 到达点结束。t 不做钳制即可复现。
        // 例外: 带 CompleteAction 的投射物 (有 MapImpact/着弹特效) 必须
        // 在落点截停并触发，否则爆炸会延迟到飞出屏幕外才播。
        double elapsed = now - _startMs;
        bool flyPast = _targetNode == null && _target == null && !Explode && CompleteAction == null;
        double t = flyPast ? elapsed / duration : Math.Clamp(elapsed / duration, 0, 1);
        Position = originScreen.Lerp(targetScreen, (float)t);

        Position += new Vector2(AdditionalOffX, AdditionalOffY);

        if (DrawType == EffectLayer.Object)
        {
            // 原版 MapControl.DrawObjects: 投射物整段飞行固定在目标行深度
            // (Target.RenderY 或 MapTarget.Y)，不从起点行插值。
            // 插值会导致火球生成时与施法者身体同层，视觉上像从身体内部飞出。
            ZIndex = RenderOrder.ObjectEffect(CurrentRenderY);
        }
        else
        {
            UpdateRenderLayer();
        }

        if (elapsed >= duration)
        {
            // 原版: 无 Target 且非 Explode 时若精灵仍在屏内则继续飞行，
            // 完全出屏后才 Complete+Remove。
            if (flyPast && IsProjectileVisible())
            {
                QueueRedraw();
                return;
            }
            CompleteAction?.Invoke();
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    private bool IsProjectileVisible()
    {
        if (_lib == null || _frameIndex < 0 || FrameCount <= 0) return false;
        int frame = _frameIndex + StartIndex + Direction16 * Skip;
        if (frame < 0 || frame >= _lib.Images.Length) return false;
        var image = _lib.Images[frame];
        if (image == null || image.Width <= 0 || image.Height <= 0) return false;

        Rect2 bounds = new(Position.X + (UseOffSet ? image.OffSetX : 0f),
            Position.Y + (UseOffSet ? image.OffSetY : 0f), image.Width, image.Height);
        return bounds.Intersects(new Rect2(Vector2.Zero, GetViewportRect().Size));
    }

    private static System.Drawing.Point ToLegacyProjectilePoint(Vector2 screen)
    {
        // 原客户端把等距屏幕 Y 从 32 高度换算为 48 高度后才计算
        // 方向和距离；这里必须保持该顺序，不能直接用 Godot 的屏幕 Y。
        return new System.Drawing.Point((int)screen.X, (int)(screen.Y / 32f * 48f));
    }

    private int GetProjectileFrame(double now)
    {
        double total = TotalDuration;
        if (total <= 0 || FrameCount <= 0) return 0;
        double elapsed = (now - _startMs) % total;
        for (int i = 0; i < Delays.Length; i++)
        {
            elapsed -= Delays[i];
            if (elapsed < 0) return i;
        }
        return FrameCount - 1;
    }

    public override void _Draw()
    {
        // 设置开关门控：关闭"显示特效/粒子"时不绘制（客户端特效唯一总闸）
        if (!ClientSettings.DrawEffects || !ClientSettings.DrawParticles) return;
        // Use the legacy screen blend; transparent pixels are discarded by
        // the shader before the screen-texture sample is written.
        Material = Blend ? BlendMaterial : null;
        if (_lib == null || _frameIndex < 0) return;
        int df = _frameIndex + StartIndex + Direction16 * Skip;
        if (df < 0 || df >= _lib.Images.Length) return;

        var img = _lib.Images[df];
        if (img == null || img.Width <= 0 || img.Height <= 0) return;
        // Projectile frames use the same legacy colour-key transparency as
        // MirEffect.  The raw image would draw the frame's rectangular key
        // background over terrain and make the fireball look incomplete.
        var tex = _lib.GetEffectTexture(df);
        if (tex == null) return;

        // MirProjectile 继承旧版 MirEffect.Draw：useOffSet=false 时，传入
        // 的 DrawX/DrawY 是贴图左上角，不是中心点。只有独立的 centered
        // 粒子绘制路径才会减去半宽/半高。
        float ox = UseOffSet ? img.OffSetX : 0f;
        float oy = UseOffSet ? img.OffSetY : 0f;

        var destRect = new Rect2(ox, oy, img.Width, img.Height);
        var srcRect = new Rect2(0, 0, img.Width, img.Height);

        // MirProjectile inherits MirEffect.Draw: DrawColour is white by
        // default. FrameLightColour controls lighting, not sprite tint.
        Color c = new(1f, 1f, 1f, Blend ? 1f : Opacity);
        DrawTextureRectRegion(tex, destRect, srcRect, c);
    }
}
