using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// EI 原版「退出游戏」确认窗（F800 / 窗口 id 0x64）。
///
/// 证据（primary-static，见 docs/LEGACY_EI_UI_AUDIT_2026-09-23.md 的 EXIT-01）：
///   * `hud-caption-action-tail-evidence.json`：HUD idx3「退出游戏(Alt+Q)」调用
///     `HUD+0x53030` 子对象 vtable `+0x10(1)`。
///   * `window_init_candidates.json` / `window-catalog-evidence.json`：该子对象是
///     id `0x64`、F800、根原点 **(218,176)**、**364×184** 窗。
///   * `confirmation-prompt-evidence.json` cluster2：F800/id`0x64` 是独立窗类，
///     初始化 RECT `(218,176)-(582,360)`；子控件字段 `+0x54`/`+0x108` **复用**
///     F151/152、F154/155 帧族（只是资源帧复用，不能把 F950 的相对 RECT 移过来）。
///   * 美术：GameInter F800 画布 512×256、alpha bbox `(74,36)-(435,219)` = **361×183**。
///     画面韩文「게임을 종료하시겠습니까?」与 **YES / NO 按钮完全烘焙在背景里**。
///
/// YES/NO 的 hit rect 原版证据未给出独立命中分支（EXIT-01 明确记为未决），
/// 本轮**从美术本身量出**：以 F151/F154 帧为模板在 F800 画布内做滑窗匹配，
/// 得到 YES @ 画布 `(242,136)`、NO @ 画布 `(293,136)`，尺寸 44×20。
/// 因美术 bbox(361×183) 与窗口(364×184) 几乎重合，换算到窗口相对即
/// YES **(168,100)**、NO **(219,100)**（242-74=168、136-36=100）。
/// 该换算来自像素匹配而非原版 hit-test 代码，属**推导值**，已在审计文档标注。
/// </summary>
public partial class ExitGameDialog : DXWindow
{
    /// <summary>原版窗口尺寸 364×184（window_init_candidates.json）。</summary>
    public static readonly Vector2I LegacySize = new(364, 184);

    /// <summary>原版根原点 (218,176)（800×600 下）。</summary>
    public static readonly Vector2I LegacyLocation = new(218, 176);

    // F800 画布内 alpha bbox 原点；把画布放到 (-74,-36) 使可见区正好贴合窗口左上角。
    private static readonly Vector2I ArtCanvasOffset = new(-74, -36);

    // 模板匹配得出的按钮命中区（窗口相对）。
    private static readonly Rect2I YesHit = new(168, 100, 44, 20);
    private static readonly Rect2I NoHit = new(219, 100, 44, 20);

    public ExitGameDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Size = LegacySize;
        Location = LegacyLocation;

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 800,
            FixedSize = true,
            Size = new Vector2I(512, 256),
            Location = ArtCanvasOffset,
            MouseFilter = MouseFilterEnum.Ignore,
            // 原版根窗不按窗口裁剪（与 SET-05 同一结论），故显式关掉。
            Clip = false,
        });

        AddHitButton(YesHit, () => GameScene.Game?.ExitClient());
        AddHitButton(NoHit, () => WindowManager.Close(this));
    }

    /// <summary>
    /// 不可见热区：图形全部烘焙在 F800 背景里，这里只放命中框，不画任何帧。
    /// </summary>
    private void AddHitButton(Rect2I rect, Action action)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.None,
            Index = -1,
            Size = rect.Size,
            Location = rect.Position,
            Sound = SoundIndex.None,
        };
        button.MouseClick += (o, e) => action();
        AddControl(button);
    }
}
