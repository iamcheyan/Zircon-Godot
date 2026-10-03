using System;
using Godot;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 登录/选人场景的 UI 缩放（与 GameScene 的 UiScale 逻辑一致）。
///
/// GameScene 的 HUD 挂在 CanvasLayer 上并应用 UiScale Transform（逻辑画布
/// 1024x768 放大到窗口）。但 LoginScene/SelectScene 是独立场景，没有这套
/// 逻辑——窗口放大（2 倍）时它们仍按 1024x768 布局，UI 不跟随缩放。
///
/// 用法：场景 _Ready 时创建 CanvasLayer("UiScaleLayer") 挂到场景，把 UI
/// 根节点 AddChild 到该层，然后调用 UpdateScale()。
///
/// 布局约定：UI 元素必须按逻辑画布 1024x768 坐标布局（与 GameScene HUD
/// 一致），UpdateScale 会计算 1..2 倍缩放并附加居中偏移，把整个逻辑画布
/// 居中到真实视口。不要在真实视口坐标系里定位——叠加缩放 Transform 后
/// 元素会渲染到屏幕外（4K 下 dialog 等会超出 3840x2160）。
/// </summary>
public static class UiScaler
{
    // **EI 各 mode 的屏幕区尺寸不同**（证据：login-flow-evidence.json mode 写入者）
    //   - mode 0 / mode 2（登录、选角、建角、CreateChr/StartGame 过场）= **640×480**
    //     0x419BF9 → 0x45D270(&0x8AB7A8, 0x280=640, 0x1E0=480, 0x10, 1|2)
    //     运行期反证：SCREEN0001.jpg（登录屏）按 640×480 反解完全吻合——视频矩形
    //     (0,60)-(640,420) 铺满整宽；按 800×600 反解则视频只占左侧 80%，与图不符。
    //   - mode 3（进游戏后）= **800×600**：0x419377 → 0x45D270(..., 0x320, 0x258, 0x10)。
    //
    // 原版是**按 mode 改窗口大小**的。本客户端改成「窗口只由用户决定」：窗口尺寸启动后
    // 不再变化，内容按窗口适配。于是这里有两套变换：
    //   - **预游戏**（登录/选角/过场）：<see cref="PregameTransform"/> —— 以 640×480 画布
    //     等比 **fit** 到窗口（宽 ≥4:3 时高度填满、两侧留黑），**不做 [1,2] 钳制**，
    //     玩家把窗口拖大画面就等比放大填满。
    //   - **进游戏 / 公告框**：沿用 <see cref="ComputeScale"/><see cref="ComputeOffset"/>
    //     （800×600 基准、钳在 [1,2]、居中），与 `GameScene.RefreshUiScale` 一致。
    //
    // BaseWidth/BaseHeight 是**进游戏/公告框**那一套的基准；预游戏用下面的
    // PregameWidth/PregameHeight，别混用。
    // **不要顺手改**：F50 背景仍画 (0,0) 且尺寸 640x480 不拉伸；角色模型中心仍是
    // 640x480 那张图自身的中心 (320,240)，不是本画布中心。
    public const float BaseHeight = 600f;
    public const float BaseWidth = 800f;

    /// <summary>原版**预游戏**屏幕区（mode 0/2）：登录 / 服务器列表 / 选角 / 建角 / 过场。</summary>
    public const float PregameWidth = 640f;
    public const float PregameHeight = 480f;

    /// <summary>
    /// 预游戏内容的等比倍率：`min(h/480, w/640)` —— 窗口宽高比 ≥ 4:3 时**高度填满**、
    /// 宽度居中两侧留黑；窗口更"窄高"时以宽度为准，保证内容永远完整可见（不裁切）。
    /// **不钳制**：窗口拖大就等比放大，拖小就等比缩小。
    /// </summary>
    public static float PregameScale(Viewport viewport)
    {
        Vector2 size = ViewportSize(viewport);
        if (size.X <= 0 || size.Y <= 0) return 1f;
        return Mathf.Min(size.Y / PregameHeight, size.X / PregameWidth);
    }

    /// <summary>
    /// 预游戏（640×480 画布）的「等比缩放 + 居中」变换。
    /// 直接赋给 CanvasLayer.Transform，或把 Scale/Position 套到根 Viewport 上的 Control
    /// （例如 StartGame 过场视频，它挂在根上、不经过 `_uiLayer`）。
    /// </summary>
    public static Transform2D PregameTransform(Viewport viewport)
    {
        float scale = PregameScale(viewport);
        Vector2 size = ViewportSize(viewport);
        Vector2 offset = new(
            Mathf.Max((size.X - PregameWidth * scale) / 2f, 0f),
            Mathf.Max((size.Y - PregameHeight * scale) / 2f, 0f));
        return new Transform2D(scale, 0f, 0f, scale, offset.X, offset.Y);
    }

    /// <summary>把预游戏变换套到挂在根 Viewport 上的控件上。</summary>
    public static void ApplyPregameTransform(Control control, Viewport viewport)
    {
        if (control == null || !GodotObject.IsInstanceValid(control)) return;
        Transform2D transform = PregameTransform(viewport);
        control.Scale = transform.Scale;
        control.Position = transform.Origin;
    }

    private static Vector2 ViewportSize(Viewport viewport)
    {
        Vector2 size = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        if (size.X <= 0 || size.Y <= 0) size = DisplayServer.WindowGetSize();
        return size;
    }

    /// <summary>按视口大小计算 UI 缩放倍率（1..2，与 GameScene.RefreshUiScale 一致）。</summary>
    public static float ComputeScale(Viewport viewport)
    {
        // 与 GameScene.RefreshUiScale 完全一致：基于视口大小。
        // 窗口模式（无 stretch）视口=设计尺寸 1024x768 → scale=1（不变）；
        // 真全屏/大窗口下视口=屏幕分辨率（如 3840x2160）→ scale=2，UI 放大。
        Vector2 size = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        if (size.X <= 0 || size.Y <= 0) size = DisplayServer.WindowGetSize();
        if (size.X <= 0 || size.Y <= 0) return 2f;
        float byHeight = size.Y / BaseHeight;
        float byWidth = size.X / BaseWidth;
        return Mathf.Clamp(Mathf.Min(byHeight, byWidth), 1f, 2f);
    }

    /// <summary>
    /// 当前**生效**的 UI 倍率：视口推算值，若设了 `ZIRCON_UI_SCALE` 则用强制值。
    /// 任何「不经过缩放层、但要和原版屏幕区对齐」的控件（例如挂在根 Viewport 上的
    /// StartGame 过场视频）都必须用它，而不是裸的 <see cref="ComputeScale"/> ——
    /// 后者只看视口：1280×960 视口算出 1.6，而强制倍率是 2，两者不一致时
    /// 视频会被画成 1024×768 塞进 1280×960 窗口。
    /// </summary>
    public static float EffectiveScale(Viewport viewport)
    {
        float scale = ComputeScale(viewport);
        string force = System.Environment.GetEnvironmentVariable("ZIRCON_UI_SCALE");
        if (!string.IsNullOrEmpty(force)
            && float.TryParse(force, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float forced)
            && forced > 0f)
            scale = forced;
        return scale;
    }

    /// <summary>把逻辑画布 (BaseWidth×BaseHeight) 在给定视口下居中所需的偏移（非负）。</summary>
    public static Vector2 ComputeOffset(Viewport viewport, float scale)
    {
        Vector2 vp = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        if (vp.X <= 0 || vp.Y <= 0) vp = DisplayServer.WindowGetSize();
        Vector2 offset = (vp - new Vector2(BaseWidth, BaseHeight) * scale) / 2f;
        return new Vector2(Mathf.Max(offset.X, 0f), Mathf.Max(offset.Y, 0f));
    }

    /// <summary>
    /// 把缩放层 Transform 更新为当前视口倍率 + 居中偏移。
    /// 居中偏移保证 4K 下整幅 1024x768 逻辑画布放大后位于屏幕中央
    /// （GameScene HUD 是贴边布局不需要居中；登录/选人是整幅画布需要）。
    /// </summary>
    public static void UpdateScale(CanvasLayer layer, Viewport viewport)
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer)) return;
        // 调试钩子：ZIRCON_UI_SCALE 强制倍率（Xvfb 无头环境视口固定 1024x768
        // 无法模拟真全屏，用它强制 scale=2 验证放大/居中效果）。缺省 -1=自动。
        string force = System.Environment.GetEnvironmentVariable("ZIRCON_UI_SCALE");
        GD.Print($"[UiScaler] force={force ?? "<null>"}");
        float scale = EffectiveScale(viewport);
        Vector2 vp = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        if (vp.X <= 0 || vp.Y <= 0) vp = DisplayServer.WindowGetSize();
        MirSkin.SetUiScale(scale);
        Vector2 offset = ComputeOffset(viewport, scale);
        GD.Print($"[UiScaler] scale={scale} viewport={vp} offset={offset}");
        layer.Transform = new Transform2D(scale, 0, 0, scale, offset.X, offset.Y);
    }

    /// <summary>
    /// 溢出审计（调试）：遍历缩放层整棵树，列出所有超出 1024x768 逻辑画布的可见控件。
    /// 用法：ZIRCON_UI_AUDIT=1 时场景 _Ready 后自动调用。
    /// 原理：层 Transform 暂置恒等 → GetGlobalRect() 即逻辑画布坐标 → 越界者打印。
    /// </summary>
    public static void AuditOverflow(CanvasLayer layer, string sceneName)
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer)) return;
        Transform2D saved = layer.Transform;
        layer.Transform = Transform2D.Identity; // 让全局坐标回到逻辑画布系
        int found = 0;
        Walk(layer, sceneName, ref found);
        layer.Transform = saved;
        GD.Print($"[UiScaler] 审计完成 {sceneName}: {found} 个溢出控件");
    }

    private static void Walk(Node node, string sceneName, ref int found)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control c && c.IsVisibleInTree() && c.Size.X > 1 && c.Size.Y > 1)
            {
                Rect2 r = c.GetGlobalRect();
                bool over = r.End.X > BaseWidth + 2 || r.End.Y > BaseHeight + 2
                            || r.Position.X < -2 || r.Position.Y < -2;
                if (over)
                {
                    found++;
                    GD.Print($"[UiScaler] 溢出 {sceneName}: {c.GetType().Name} '{c.Name}' " +
                             $"rect=({r.Position.X:F0},{r.Position.Y:F0})-({r.End.X:F0},{r.End.Y:F0}) " +
                             $"超出 {(r.End.X > BaseWidth + 2 ? $"右+{r.End.X - BaseWidth:F0} " : "")}" +
                             $"{(r.End.Y > BaseHeight + 2 ? $"下+{r.End.Y - BaseHeight:F0}" : "")}");
                }
            }
            Walk(child, sceneName, ref found);
        }
    }
}
