using System;
using System.Collections.Generic;
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
    private readonly DXButton[] _buttons = new DXButton[2];   // 0 = YES, 1 = NO
    private readonly Action[] _buttonActions = new Action[2];
    private int _activeIndex;

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

        AddButton(YesRect, 151, 0, () => { _confirm?.Invoke(); WindowManager.Close(this); });
        AddButton(NoRect, 154, 1, () => WindowManager.Close(this));
    }

    /// <summary>
    /// 原版键盘链（`confirmation-prompt-evidence.json` + 反汇编 0x418470/0x418520）：
    ///   * **Tab**：`inc byte [esi+0x462]`，到 **3** 回绕，并**跳过空/disabled 槽位**
    ///     （0x4184DE 循环 `cmp ecx,3; jl` 重试）。
    ///   * **Enter(0xD) / Space(0x20)** → `0x418520` 激活当前 `[esi+0x462]` 指向的按钮
    ///     （`test ecx,ecx; je` 跳过空槽）。
    ///   * 其他键 → `ret 1`（不处理）。
    ///
    /// **重要：原版 Tab 只改 `+0x462`，不改帧号** —— paint（0x4182A0）只用 `[edi+0x28]`
    /// 作帧号，帧选择是 normal/hover/pressed 三态，**没有 focus 态**。
    /// 即原版**焦点不可见**：Tab 仅决定 Enter 激活哪个按钮。此处照此实现，
    /// 不额外加焦点高亮（加了反而与原版不符）。
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.Keycode)
        {
            case Key.Tab:
                CycleActive(+1);
                AcceptEvent();
                break;
            case Key.Enter:
            case Key.KpEnter:
            case Key.Space:
                ActivateActive();
                AcceptEvent();
                break;
        }
    }

    /// <summary>
    /// 原版 Tab 的索引推进（纯函数，便于自检）：从 `current` 起按 `step` 找下一个
    /// **可用**按钮，最多试 `count` 次后放弃（原版 0x4184DE 的 `cmp ecx,3; jl` 重试）。
    /// 返回 -1 表示没有可用按钮。
    /// </summary>
    public static int NextActiveIndex(int current, int step, IReadOnlyList<bool> available)
    {
        int count = available.Count;
        if (count == 0) return -1;
        for (int i = 1; i <= count; i++)
        {
            int next = ((current + step * i) % count + count) % count;
            if (available[next]) return next;
        }
        return -1;
    }

    /// <summary>
    /// 键盘链自检：断言 Tab 的循环/回绕/跳过 disabled 语义与原版 0x418470 一致。
    /// </summary>
    public static (bool Ok, string Details) RunKeyboardChainSelfTest()
    {
        var failures = new List<string>();
        var both = new[] { true, true };

        int i0 = NextActiveIndex(0, +1, both);
        if (i0 != 1) failures.Add($"YES+Tab 期望 NO(1) 实际 {i0}");
        int i1 = NextActiveIndex(1, +1, both);
        if (i1 != 0) failures.Add($"NO+Tab 期望回绕到 YES(0) 实际 {i1}");

        var onlyYes = new[] { true, false };
        int i2 = NextActiveIndex(0, +1, onlyYes);
        if (i2 != 0) failures.Add($"NO 禁用时 YES+Tab 期望停在 0 实际 {i2}");

        var none = new[] { false, false };
        int i3 = NextActiveIndex(0, +1, none);
        if (i3 != -1) failures.Add($"全禁用期望 -1 实际 {i3}");

        return (failures.Count == 0, failures.Count == 0
            ? "Tab 循环/回绕/跳过 disabled 全部匹配；焦点不可见（paint 只用帧号，无 focus 态）"
            : string.Join("; ", failures));
    }

    private void CycleActive(int step)
    {
        var available = new bool[_buttons.Length];
        for (int i = 0; i < _buttons.Length; i++)
            available[i] = _buttons[i] != null && _buttons[i].Enabled;
        int next = NextActiveIndex(_activeIndex, step, available);
        if (next >= 0) _activeIndex = next;
    }

    private void ActivateActive()
    {
        var button = _buttons[_activeIndex];
        if (button != null && button.Enabled) _buttonActions[_activeIndex]?.Invoke();
    }

    /// <summary>
    /// 按钮用原版帧 151/152（YES）、154/155（NO）；152/155 是悬停态。
    /// 命中区按原版 RECT，与帧可见尺寸（实测 42×19）略有差异 —— 保留原版矩形。
    /// </summary>
    private void AddButton(Rect2I rect, int index, int slot, Action action)
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
        _buttons[slot] = button;
        _buttonActions[slot] = action;
    }
}
