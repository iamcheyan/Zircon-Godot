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

    /// <summary>
    /// GameInter[50]（800x136）底部 3 行是**纯黑边**（逐行实测：y=133/134/135 全黑），
    /// 顶部聊天区前 11 行同样纯黑。布局用**内容高度**(136-3) 当窗口高度、美术自然越过
    /// 屏幕底边，否则屏幕最底下会露出素材自带的黑行，看起来像「HUD 没贴底」。
    /// 见 docs/LEGACY_UI_WINDOW_CATALOG.md 的贴底说明。
    /// </summary>
    public const int MainPanelBottomBlackRows = 3;
    public const int MainPanelContentHeight = MainPanelHeight - MainPanelBottomBlackRows;

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
