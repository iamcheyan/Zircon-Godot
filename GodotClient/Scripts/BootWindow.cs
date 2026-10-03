using Godot;

namespace ZirconClient.Scripts;

/// <summary>
/// 最早入口（autoload，排在 NetworkManager 之前）：**一次性**确定 legacy 会话的窗口尺寸，
/// 并在之后记住用户拖动后的尺寸。
///
/// 原版屏幕区按 mode 分两档（`login-flow-evidence.json::mode_state_machine`，都由 `0x45D270` 设定）：
///   - mode 0 / 2（登录 / 服务器列表 / 选角 / 建角 / CreateChr / StartGame 过场）= 640×480
///     （`0x419BF9 → 0x45D270(&0x8AB7A8, 0x280, 0x1E0, 0x10, 1|2)`）；
///   - mode 3（进游戏）= 800×600（`0x419377 → 0x45D270(..., 0x320, 0x258, 0x10)`）。
///
/// 也就是说**原版会自己改窗口大小**。按设计要求改成：
///   - 窗口尺寸**只由用户决定**（拖边缘缩放），游戏在登录/选角/进游戏之间**不再改窗口**；
///   - 用户拖出来的尺寸记进 `ClientSettings.LegacyWindowSize`，下次启动沿用；
///   - 内容按窗口等比适配：预游戏 640×480 画布等比填满（见 `UiScaler.PregameTransform`），
///     进游戏的 HUD 沿用既有的 `GameScene.RefreshUiScale`。
///
/// 另一件事：Godot 的窗口由 `project.godot`（1024×768）创建，若不在这里纠正，
/// 开机 splash 会一直停在 1024×768，登录场景再缩一次 —— 实测 `[UiScaler]` 20ms 内
/// 连打 640×480 → 1024×768 → 1440×900 → 640×480 四行。
/// </summary>
public partial class BootWindow : Node
{
    /// <summary>用户拖动缩放后的落盘去抖：等窗口尺寸稳定这么久再写配置。</summary>
    private const double PersistDelaySeconds = 0.6;

    private double _pendingPersistAt;
    private bool _initialized;

    public override void _EnterTree()
    {
        if (!AutoLoginArgs.LegacyUi) return;
        // 必须先读配置：窗口尺寸来自 ClientSettings.LegacyWindowSize，
        // 而 LoginScene/SelectScene 的 ClientSettings.Load() 要等首个场景 _Ready 才跑。
        ClientSettings.Load();
        ClientSettings.ApplyLegacyWindow(ClientSettings.ResolveLegacyWindowSize());
        _initialized = true;

        Viewport viewport = GetViewport();
        if (viewport != null) viewport.SizeChanged += OnViewportSizeChanged;
        SetProcess(true);
    }

    private void OnViewportSizeChanged()
    {
        if (!_initialized) return;
        // 启动阶段 ApplyLegacyWindow 自身也会触发一次 SizeChanged，延时落盘把它吞掉；
        // 用户连续拖动时只会在停下来之后写一次配置。
        SchedulePersist();
    }

    private void SchedulePersist()
        => _pendingPersistAt = Time.GetTicksMsec() / 1000.0 + PersistDelaySeconds;

    /// <summary>
    /// 只有**真实游戏流程场景**里的窗口变化才算「用户拖出来的尺寸」。
    /// 审计/测试场景（`UITestScene`、`MapTestScene`、`ZlViewer` 等）会自己改窗口尺寸，
    /// 若不排除，它们会把测试用的尺寸写进 `LegacyWindowSize` —— 实测跑一次
    /// `UITestScene --ui-audit` 之后配置里冒出了 `(1022,739)`。
    ///
    /// 不能用 `GetTree().CurrentScene`：`EnterGameScene()` 是 `Root.AddChild(game)` +
    /// `QueueFree()`，**没有** `ChangeSceneTo`，进游戏后 `CurrentScene` 是 null
    /// （实测日志 `persist scene=<null>`）。改为遍历根节点的子节点类型。
    /// </summary>
    private bool IsGameplayScene()
    {
        Node root = GetTree()?.Root;
        if (root == null) return false;
        foreach (Node child in root.GetChildren())
        {
            if (child is LoginScene or SelectScene or GameScene) return true;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        if (_pendingPersistAt <= 0) return;
        if (Time.GetTicksMsec() / 1000.0 < _pendingPersistAt) return;
        _pendingPersistAt = 0;
        if (!IsGameplayScene()) return;
        ClientSettings.RememberLegacyWindowSize(DisplayServer.WindowGetSize());
    }
}
