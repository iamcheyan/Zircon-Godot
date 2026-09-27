using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// EI 原版通用确认框 F950，用于「注销人物(Alt+X / HUD idx4)」——
/// 文本「返回游戏人物选择界面？」，type `0x65`。
///
/// 证据（`confirmation-prompt-evidence.json`，primary-static + primary-resource-visual）：
///   * 窗口：F950 **360×190**，默认居中在 800×600 即 **(220,151)**。
///   * 背景：F950 画布实测 **360×190**，alpha bbox 从 **(0,0)** 起 ——
///     与原版窗口尺寸**完全一致**，故背景直接放 (0,0)，不需要偏移补偿。
///     （对比 F800：画布 512×256 但可见区 361×183，需要 (-74,-36) 补偿。）
///   * 按钮 mode 1 = YES + NO：
///       YES = `this+0x238`，帧 **151/152**，根相对 RECT **(51,125,44,20)**
///       NO  = `this+0x3A8`，帧 **154/155**，根相对 RECT **(244,125,44,20)**
///     另 mode 0 = 仅中间 checkmark `this+0x2F0`（157/158）、mode 2 = 无按钮。
///   * 消息文本：缓冲 `this+0x2C`（GBK），矩形 `left=x+0x18(24)`、`top=y+0x17(23)`、
///     `right=x+0x14D(333)`、`bottom=top+0x78(120)`（variant 0）或 `top+0x64(100)`
///     （variant 1）；文字色 **0xC8FAFF**（浅青）。
///   * Tab 切换焦点、Enter/Space 激活（原版行为）。
/// </summary>
public partial class LogoutConfirmDialog : DXWindow
{
    /// <summary>原版窗口尺寸 360×190。</summary>
    public static readonly Vector2I LegacySize = new(360, 190);

    /// <summary>800×600 下居中原点 (220,151)。</summary>
    public static readonly Vector2I LegacyLocation = new(220, 151);

    // 原版 mode 1 的两个按钮根相对 RECT。
    private static readonly Rect2I YesRect = new(51, 125, 44, 20);
    private static readonly Rect2I NoRect = new(244, 125, 44, 20);

    // 原版消息文本矩形与颜色。
    private static readonly Vector2I MessagePosition = new(24, 23);
    private static readonly Vector2I MessageSize = new(333 - 24, 120);
    private static readonly Color MessageColour = Color.Color8(0xC8, 0xFA, 0xFF);

    private readonly Action _confirm;

    public LogoutConfirmDialog(string message, Action confirm)
    {
        _confirm = confirm;
        HasTitle = false;
        HasFooter = false;
        Size = LegacySize;
        Location = LegacyLocation;

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 950,
            FixedSize = true,
            Size = LegacySize,
            Location = Vector2I.Zero,
            MouseFilter = MouseFilterEnum.Ignore,
            Clip = false,
        });

        AddControl(new DXLabel
        {
            Text = message ?? string.Empty,
            FontSize = 10,
            TextColour = MessageColour,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            AutoSize = false,
            Align = HorizontalAlignment.Left,
            VAlign = VerticalAlignment.Top,
            Location = MessagePosition,
            Size = MessageSize,
            IsControl = false,
        });

        AddButton(YesRect, 151, () => { _confirm?.Invoke(); WindowManager.Close(this); });
        AddButton(NoRect, 154, () => WindowManager.Close(this));
    }

    /// <summary>
    /// 按钮用原版帧 151/152（YES）、154/155（NO）；152/155 是悬停态。
    /// 命中区按原版 RECT，与帧可见尺寸（实测 42×19）略有差异 —— 保留原版矩形。
    /// </summary>
    private void AddButton(Rect2I rect, int index, Action action)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = index,
            HoverIndex = index + 1,
            PressedIndex = index + 1,
            FixedSize = true,
            Size = rect.Size,
            Location = rect.Position,
            Sound = SoundIndex.None,
        };
        button.MouseClick += (o, e) => action();
        AddControl(button);
    }
}
