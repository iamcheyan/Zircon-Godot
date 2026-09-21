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
    public const float BaseHeight = 768f;
    public const float BaseWidth = 1024f;

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
    /// 把缩放层 Transform 更新为当前视口倍率 + 居中偏移。
    /// 居中偏移保证 4K 下整幅 1024x768 逻辑画布放大后位于屏幕中央
    /// （GameScene HUD 是贴边布局不需要居中；登录/选人是整幅画布需要）。
    /// </summary>
    public static void UpdateScale(CanvasLayer layer, Viewport viewport)
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer)) return;
        float scale = ComputeScale(viewport);
        Vector2 vp = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        if (vp.X <= 0 || vp.Y <= 0) vp = DisplayServer.WindowGetSize();
        // 调试钩子：ZIRCON_UI_SCALE 强制倍率（Xvfb 无头环境视口固定 1024x768
        // 无法模拟真全屏，用它强制 scale=2 验证放大/居中效果）。缺省 -1=自动。
        string force = System.Environment.GetEnvironmentVariable("ZIRCON_UI_SCALE");
        GD.Print($"[UiScaler] force={force ?? "<null>"}");
        if (!string.IsNullOrEmpty(force) && float.TryParse(force, out float forced) && forced > 0f)
            scale = forced;
        MirSkin.SetUiScale(scale);
        Vector2 offset = (vp - new Vector2(BaseWidth, BaseHeight) * scale) / 2f;
        offset.X = Mathf.Max(offset.X, 0f);
        offset.Y = Mathf.Max(offset.Y, 0f);
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
