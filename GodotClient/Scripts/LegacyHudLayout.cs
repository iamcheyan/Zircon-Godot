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
    public const int MainPanelHeight = 136;
    public const int MainPanelY = 465;

    // EI F50 内的常驻聊天槽和其下方输入条。坐标相对
    // MainPanel 根，而不是 1024x768 视口；F50 根始终保持 800x136。
    public static readonly Vector2I ChatLogLocation = new(224, 27);
    public static readonly Vector2I ChatLogSize = new(354, 74);
    public static readonly Vector2I ChatInputLocation = new(223, 105);
    public static readonly Vector2I ChatInputSize = new(354, 16);

    public static readonly Vector2I MainPanelLocation = new(0, MainPanelY);
    public static readonly Vector2I PlayerOrbLocation = new(49, 13);
    public static readonly Vector2I PlayerOrbSize = new(112, 110);
}
