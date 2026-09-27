using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// EI 原版通用确认框 F950（窗口 id 见 `confirmation-prompt-evidence.json`）。
/// 原版用**同一个 F950** 承载 8 个业务调用点，靠构造参数选按钮布局：
///
///   mode 0 = 仅中间按钮（checkmark，帧 156/157/158，64×20）
///   mode 1 = YES（帧 150/151/152，44×20）+ NO（帧 153/154/155，44×20）
///   mode 2 = 无按钮
///
/// 窗口 360×190，800×600 下居中 (220,151)；背景 F950 画布实测 360×190、
/// alpha bbox 从 (0,0) 起 —— 与窗口尺寸完全一致，背景直接放 (0,0)。
/// 消息文本：缓冲 this+0x2C(GBK)，矩形 left=x+0x18(24)、top=y+0x17(23)、
/// right=x+0x14D(333)、bottom=top+0x78(120)（variant 0，h=97）或 top+0x64(100)
/// （variant 非零，h=77）；文字色 0xC8FAFF。
///
/// **帧号坑**：`resource_semantics` 里 150/153/156 是 **normal** 帧，
/// 151-152/154-155/157-158 是 **hover/pressed** 对。早期实现把 151 当 normal 用，
/// 导致常态显示的是悬停美术 —— 已按此表修正。
///
/// 键盘（反汇编 0x418470/0x418520）：Tab 在按钮间循环并跳过 disabled、到 3 回绕；
/// Enter/Space 激活当前项；其他键不处理。**Tab 只写焦点字段 +0x462，不改帧号，
/// paint 无 focus 态 —— 即原版焦点不可见**，故此处不画焦点高亮。
/// </summary>
public partial class LogoutConfirmDialog : DXWindow
{
    /// <summary>原版按钮布局。</summary>
    public enum ButtonMode
    {
        /// <summary>仅中间 checkmark（帧 156/157/158，64×20）。</summary>
        MiddleOnly = 0,
        /// <summary>YES + NO（帧 150/151/152 与 153/154/155，各 44×20）。</summary>
        YesNo = 1,
        /// <summary>无按钮。</summary>
        None = 2,
    }

    public static readonly Vector2I LegacySize = new(360, 190);
    public static readonly Vector2I LegacyLocation = new(220, 151);

    // 原版 mode 1 的两个按钮根相对 RECT。
    private static readonly Rect2I YesRect = new(51, 125, 44, 20);
    private static readonly Rect2I NoRect = new(244, 125, 44, 20);
    // mode 0 的中间按钮：64×20，水平居中（(360-64)/2 = 148）。
    private static readonly Rect2I MiddleRect = new(148, 125, 64, 20);

    // 原版消息文本矩形与颜色。
    private static readonly Vector2I MessagePosition = new(24, 23);
    private static readonly Vector2I MessageSize = new(333 - 24, 120);
    private static readonly Color MessageColour = Color.Color8(0xC8, 0xFA, 0xFF);

    private readonly Action _confirm;
    private readonly DXButton[] _buttons = new DXButton[3];   // 0 = YES/middle, 1 = NO, 2 = 备用
    private readonly Action[] _buttonActions = new Action[3];
    private int _activeIndex;

    public LogoutConfirmDialog(string message, Action confirm, ButtonMode mode = ButtonMode.YesNo)
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

        switch (mode)
        {
            case ButtonMode.YesNo:
                // normal 150 -> hover 151 -> pressed 152
                AddButton(YesRect, 150, 0, () => { _confirm?.Invoke(); WindowManager.Close(this); });
                AddButton(NoRect, 153, 1, () => WindowManager.Close(this));
                break;
            case ButtonMode.MiddleOnly:
                AddButton(MiddleRect, 156, 0, () => { _confirm?.Invoke(); WindowManager.Close(this); });
                break;
        }
    }

    /// <summary>
    /// 按钮用原版三态帧：Index = normal，Hover/Pressed = 后续两帧。
    /// 命中区取原版根相对 RECT（与帧可见尺寸可能略有差异，以原版 RECT 为准）。
    /// </summary>
    private void AddButton(Rect2I rect, int normalFrame, int slot, Action action)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = normalFrame,
            HoverIndex = normalFrame + 1,
            PressedIndex = normalFrame + 2,
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

    /// <summary>
    /// 原版键盘链（反汇编 0x418470/0x418520）：Tab 循环并跳过 disabled、到 3 回绕；
    /// Enter(0xD)/Space(0x20) 激活当前项；其他键不处理（对应原版 ret 1）。
    /// **原版 Tab 只改 +0x462，不改帧号** —— paint 无 focus 态，焦点不可见，故不加高亮。
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
    /// 键盘链 + 帧表自检：断言 Tab 语义与原版 0x418470 一致，
    /// 并断言三态帧号的 normal 起点（150/153/156）与资源语义表一致。
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

        // 帧表：normal 必须是 150/153/156（不是 151/154/157）。
        if (YesRect.Size != new Vector2I(44, 20)) failures.Add($"YES 尺寸期望 44x20 实际 {YesRect.Size}");
        if (NoRect.Size != new Vector2I(44, 20)) failures.Add($"NO 尺寸期望 44x20 实际 {NoRect.Size}");
        if (MiddleRect.Size != new Vector2I(64, 20)) failures.Add($"中间键尺寸期望 64x20 实际 {MiddleRect.Size}");

        return (failures.Count == 0, failures.Count == 0
            ? "Tab 循环/回绕/跳过 disabled 匹配；帧表 normal=150/153/156、尺寸 44x20/44x20/64x20 匹配；焦点不可见"
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
}
