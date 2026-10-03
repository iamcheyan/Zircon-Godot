using Godot;

namespace ZirconClient.Scripts;

/// <summary>
/// 最早入口（autoload，排在 NetworkManager 之前）：在第一帧渲染之前，
/// 把窗口尺寸设成当前模式**预游戏屏幕区**的尺寸。
///
/// 原版屏幕区按 mode 分两档（`login-flow-evidence.json::mode_state_machine`）：
///   - **mode 0 / 2**（登录 / 服务器列表 / 选角 / 建角 / CreateChr / StartGame 过场）
///     = **640×480** —— 写入点 `0x419BF9 → 0x45D270(&0x8AB7A8, 0x280, 0x1E0, 0x10, 1|2)`；
///   - **mode 3**（进游戏）= **800×600** —— `0x419377 → 0x45D270(..., 0x320, 0x258, 0x10)`。
///
/// Godot 的窗口由 `project.godot`（`window/size/viewport_*` = 1024×768）创建，
/// 而首个场景 `LoginScene._Ready` 要等到 splash 之后才跑。若不在这里纠正：
///   1) 开机 splash 与登录首帧都以 1024×768 出现，随后才缩到 640×480（实测窗口连跳）；
///   2) `ApplyDisplaySettings` 在 legacy 下还会按 GameSize 再设一次
///      （`--window` 裸参时 = 屏幕的 75%，1920×1200 上就是 1440×900），
///      实测 `[UiScaler]` 在 20ms 内连打 640×480 → 1024×768 → 1440×900 → 640×480 四行。
///
/// 这里只负责**预游戏**那一档；进游戏的 800×600 由选角屏过场结束 / GameScene
/// 的 `ApplyLegacyPregameWindow(800, 600)` 接管。现代（`--zircon-ui`）模式不做处理，
/// 仍按 GameSize 走原有逻辑。
/// </summary>
public partial class BootWindow : Node
{
    /// <summary>原版预游戏（mode 0/2）屏幕区尺寸。见类注释的 `0x45D270` 调用点。</summary>
    public const int PregameWidth = 640;
    public const int PregameHeight = 480;

    public override void _EnterTree()
    {
        if (!AutoLoginArgs.LegacyUi) return;
        ClientSettings.ApplyLegacyPregameWindow(PregameWidth, PregameHeight);
    }
}
