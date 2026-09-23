using Godot;

namespace ZirconClient.Scripts;

/// <summary>
/// 旧版 EI 主 HUD 的共享逻辑画布和关键几何定义。
/// 测试场与正式 MainPanel 都必须引用这里，避免两套坐标逐渐漂移。
/// </summary>
public static class LegacyHudLayout
{
    public const int LogicalWidth = 800;
    public const int LogicalHeight = 600;
    public const int MainPanelY = 465;

    public static readonly Vector2I MainPanelLocation = new(0, MainPanelY);
    public static readonly Vector2I PlayerOrbLocation = new(49, 13);
    public static readonly Vector2I PlayerOrbSize = new(112, 110);
}
