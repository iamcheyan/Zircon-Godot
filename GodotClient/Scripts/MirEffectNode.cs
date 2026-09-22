using System;
using Godot;
using Library;
using ZirconClient.Formats;

namespace ZirconClient.Scripts;

/// <summary>
/// 通用序列帧特效节点（移植自原版 Client/Models/MirEffect.cs）。
/// 支持: 目标/地图锚定、Delays 帧推进、Loop/Reversed、Blend 混合、
///       方向×Skip、CompleteAction/FrameIndexChanged 回调。
/// 用 Godot 序列帧节点承载原版 EffectNode 的完整播放语义。
/// </summary>
public partial class MirEffectNode : Node2D
{
    protected static readonly ShaderMaterial BlendMaterial = LegacyBlendMaterial.Create();
    public enum EffectLayer
    {
        Floor,
        Object,
        Final,
    }

    // 锚定: 跟随对象(_target!=null)或固定格子坐标
    protected MapObjectNode _target;
    protected Node2D _targetNode;
    private Func<int> _targetRenderYFn;
    public int MapCellX, MapCellY;
    protected Func<Vector2> _cameraFn; // GameScene.ComputeObjectScreenPos 委托

    // 图库
    public LibraryFile File;   // Setup 时记录, 供审计比对 (原三元组之一)
    protected ZlLibrary _lib;

    // 帧序列
    public int StartIndex;
    public int FrameCount;
    public double[] Delays;       // 每帧延迟(ms)
    public bool Loop;
    public bool Reversed;
    public int Skip = 10;         // 方向间隔(原版默认10)
    public MirDirection Direction;

    // 绘制
    public bool Blend;            // 混合绘制
    public float BlendRate = 0.7f;
    public float Opacity = 1f;
    public bool UseOffSet = true; // true 使用图库 OffSet；false 从节点左上角绘制
    public int FrameLight;
    public Color FrameLightColour = Colors.White;
    // true=用 GetEffectTexture(黑色透明键抠除, 适合背景为 opaque 黑的帧);
    // false=用 GetImageTexture(仅靠 Dxt1 alpha 通道透明, 不做 RGB 抠除)。
    // 暗色火焰/冰系帧经 Dxt1 压缩后主体 RGB 可能 ≤32, 透明键会误删主体 → 设 false。
    public bool UseEffectTransparency = true;

    // 生命周期
    protected double _startMs;
    public Action CompleteAction;
    public Action<int> FrameIndexChanged;
    protected int _frameIndex = -1;

    // Z 排序 (对应原版 DrawType: Floor/Object/Final)
    public int ZLayer = 60;
    public EffectLayer DrawType = EffectLayer.Object;

    // 附加偏移
    public int AdditionalOffX, AdditionalOffY;

    /// <summary>对应旧端 MirEffect.StartTime，和每帧 Delay 完全独立。</summary>
    public void SetStartDelay(double delayMs) => _startMs = Godot.Time.GetTicksMsec() + Math.Max(0, delayMs);

    /// <summary>
    /// 初始化特效。
    /// file: 图库; startIndex/frameCount: 起始帧与帧数; frameDelayMs: 每帧毫秒;
    /// target: 跟随对象(null=用格子); mapCellX/Y: 地图格子; cameraFn: 格子→屏幕坐标换算。
    /// </summary>
    public void Setup(LibraryFile file, int startIndex, int frameCount, double frameDelayMs,
        MapObjectNode target, int mapCellX, int mapCellY, Func<Vector2> cameraFn)
    {
        File = file;
        _lib = LibraryCache.Get(file);
        StartIndex = startIndex;
        FrameCount = frameCount;
        _target = target;
        MapCellX = mapCellX;
        MapCellY = mapCellY;
        _cameraFn = cameraFn;

        Delays = new double[frameCount];
        for (int i = 0; i < frameCount; i++)
            Delays[i] = frameDelayMs;

        _startMs = Godot.Time.GetTicksMsec();
        UpdateRenderLayer();
    }

    /// <summary>
    /// 旧端 MirEffect.Target 的对象锚定版本。PlayerRenderer 和 MapObjectNode
    /// 都必须跟随实时 Position，不能退化成施法包中的静态格子。
    /// </summary>
    public void SetupTarget(LibraryFile file, int startIndex, int frameCount, double frameDelayMs,
        Node2D target, Func<int> targetRenderYFn)
    {
        _lib = LibraryCache.Get(file);
        File = file;
        StartIndex = startIndex;
        FrameCount = frameCount;
        _target = target as MapObjectNode;
        _targetNode = target;
        _targetRenderYFn = targetRenderYFn;
        Delays = new double[frameCount];
        for (int i = 0; i < frameCount; i++) Delays[i] = frameDelayMs;
        _startMs = Godot.Time.GetTicksMsec();
        UpdateRenderLayer();
    }

    public void SetDelay(int frame, double ms)
    {
        if (frame >= 0 && frame < Delays.Length) Delays[frame] = ms;
    }

    public override void _Process(double delta)
    {
        double now = Godot.Time.GetTicksMsec();
        int frame = GetFrame(now);

        if (Reversed) frame = FrameCount - frame - 1;

        if (frame >= FrameCount)
        {
            CompleteAction?.Invoke();
            QueueFree();
            return;
        }

        if (frame != _frameIndex)
        {
            _frameIndex = frame;
            FrameIndexChanged?.Invoke(frame);
        }

        // 位置跟随目标或固定格子。目标节点可能已被移除（怪物被
        // S.ObjectRemove 释放后仍播放中的一次性特效）；旧端 MirEffect.Target
        // 是托管 MapObject，目标移除后效果会冻结在最后 DrawX/DrawY 直到帧序列
        // 播完，不会抛异常。这里对已释放的节点同样冻结在最后位置继续播放，
        // 避免 ObjectDisposedException（Node2D.get_Position 访问已释放原生对象）。
        if (_targetNode != null && IsInstanceValid(_targetNode))
            // 特效与身体共用锚点：旧端 MirEffect.Target 锚在 MapObject.DrawX/DrawY
            // （与身体同一帧），Godot 身体/对象节点锚在 Position（objectBaseline）。
            // 不能减 32——那会把特效放回旧端格子原点帧，相对身体恒高 32px（盾浮头）。
            Position = _targetNode.Position;
        else if (_target != null && IsInstanceValid(_target))
            // Setup(对象锚定) 分支：跟随对象节点（如地上物品光效）。
            Position = _target.Position;
        else if (_cameraFn != null)
            Position = _cameraFn();

        Position += new Vector2(AdditionalOffX, AdditionalOffY);
        UpdateRenderLayer();

        QueueRedraw();
    }

    protected void UpdateRenderLayer()
    {
        if (DrawType == EffectLayer.Object && _targetNode is PlayerRenderer)
        {
            // 持续 Buff/护盾围绕角色，但角色身体必须绘制在其上方；
            // 否则魔法盾等帧的非透明像素会遮住腿部。
            ZIndex = RenderOrder.PlayerBuffEffect;
            return;
        }
        // 目标已释放时按格子回退，不再读 _targetRenderYFn/_target（可能访问
        // 已释放对象）；与 _Process 的冻结语义一致，目标移除后按最后格子排序。
        bool targetAlive = (_targetNode != null && IsInstanceValid(_targetNode)) ||
                           (_target != null && IsInstanceValid(_target));
        ZIndex = DrawType switch
        {
            EffectLayer.Floor => RenderOrder.FloorEffects,
            EffectLayer.Final => RenderOrder.FinalEffects,
            _ => RenderOrder.ObjectEffectAtFoot(targetAlive
                ? (_targetRenderYFn?.Invoke() ?? _target?.RenderY ?? MapCellY)
                : MapCellY),
        };
    }

    protected int CurrentRenderY => (_targetNode != null && IsInstanceValid(_targetNode)) ||
                                    (_target != null && IsInstanceValid(_target))
        ? (_targetRenderYFn?.Invoke() ?? _target?.CellY ?? MapCellY)
        : MapCellY;

    protected int GetFrame(double now)
    {
        double elapsed = now - _startMs;

        if (Loop)
            elapsed = elapsed % TotalDuration;
        else if (elapsed >= TotalDuration)
            return FrameCount;

        if (Reversed)
        {
            for (int i = 0; i < Delays.Length; i++)
            {
                elapsed -= Delays[Delays.Length - 1 - i];
                if (elapsed >= 0) continue;
                return i;
            }
        }
        else
        {
            for (int i = 0; i < Delays.Length; i++)
            {
                elapsed -= Delays[i];
                if (elapsed >= 0) continue;
                return i;
            }
        }
        return FrameCount;
    }

    public double TotalDuration
    {
        get
        {
            double t = 0;
            foreach (var d in Delays) t += d;
            return t;
        }
    }

    public int DrawFrame => _frameIndex + StartIndex + (int)Direction * Skip;

    public override void _Draw()
    {
        // 设置开关门控：关闭"显示特效/粒子"时不绘制（客户端特效唯一总闸）
        if (!ClientSettings.DrawEffects || !ClientSettings.DrawParticles) return;
        // Keep the legacy NORMAL screen blend. The shader discards fully
        // transparent pixels before sampling SCREEN_TEXTURE, so it cannot
        // turn the sprite's transparent rectangle into an opaque square.
        Material = Blend ? BlendMaterial : null;
        if (_lib == null || _frameIndex < 0) return;
        int df = DrawFrame;
        if (df < 0 || df >= _lib.Images.Length) return;

        var img = _lib.Images[df];
        if (img == null || img.Width <= 0 || img.Height <= 0) return;

        // Legacy ImageType.Image still applies the library's colour-key
        // transparency before the DirectX blend.  GetImageTexture() is the
        // raw decoded frame in Godot and leaves the rectangular key colour
        // visible, which is why skill frames can appear as shifted squares.
        // 但暗色火焰/冰系帧经 Dxt1 压缩后主体 RGB≤32, 透明键会误删主体;
        // 这类帧(背景已是 Dxt1 alpha=0 透明)用 GetImageTexture 即可。
        var tex = UseEffectTransparency ? _lib.GetEffectTexture(df) : _lib.GetImageTexture(df);
        if (tex == null) return;

        // MirLibrary.Draw uses the supplied position as the top-left when
        // useOffSet=false; centered particles use their own centered path.
        float ox = UseOffSet ? img.OffSetX : 0f;
        float oy = UseOffSet ? img.OffSetY : 0f;

        var destRect = new Rect2(ox, oy, img.Width, img.Height);
        var srcRect = new Rect2(0, 0, img.Width, img.Height);

        // Old MirEffect.Draw uses DrawColour (white by default). The
        // FrameLightColour is only the light/effect-light colour; applying it
        // to the sprite itself turns FireColour=OrangeRed into a solid red
        // overlay, unlike the original client.
        Color c = new(1f, 1f, 1f, Blend ? 1f : Opacity);
        DrawTextureRectRegion(tex, destRect, srcRect, c);
    }
}
