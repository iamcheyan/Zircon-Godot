using System.Collections.Generic;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// EI 原版预游戏**确认框**（`GameInter` F950）。
///
/// 反汇编证据（Mir3-Research `confirmation-prompt-evidence.json`，primary-static）：
///   ctor `0x418030`（8 参，ret 0x20）→ `SelectFrame(GameInter, 950)`，
///   帧头 360×190；`this+4 = 0x3B6`。
///   arg3 = 按钮模式：0 = 只显示中间「对勾」钮（this+0x2F0），
///                     1 = YES（this+0x238）+ NO（this+0x3A8），2 = 无钮。
///   arg4 = 文案（拷进 this+0x2C，GBK）；arg5 = 文案矩形变体
///          （0 → 高 0x78，非 0 → 高 0x64；left=x+0x18, top=y+0x17, right=x+0x14D）；
///   arg6/arg7 = 位置（-1 触发居中，锚点 400/246）。
///   预游戏调用点（0x459352 / 0x4594f9 / 0x4597e4 / 0x45a074）传
///   **(x=140, y=150)**、arg3=0（单对勾）、arg8=0xffff。
///
/// 绘制（paint `0x4182A0`）：背景帧 blit → 文案 `0x45DF20(rect=this+0x18,
/// color=0xC8FAFF, text=this+0x2C)`（GDI DrawTextA flags 0x25 = 居中/垂直居中/单行）
/// → 3 个按钮循环。
///
/// 按钮帧（ctor 立即数，`0x417550` 9 参：arg2→+0x18、arg3→+0x1C、arg8→+0x20；
/// 绘制 `0x417640` 状态 0 画 +0x20 常态、状态 2 画 +0x1C 悬停）：
///   YES  常态 **150** / 悬停 152 / 按下 151   @ (x+0x33, y+0x7D) = (+51,+125) 44×20
///   NO   常态 **153** / 悬停 155 / 按下 154   @ (x+0xF4, y+0x7D) = (+244,+125) 44×20
///   对勾 常态 **156** / 悬停 158 / 按下 157   @ (x+0x93, y+0x7D) = (+147,+125) 64×20
/// </summary>
public partial class LegacyEiDialog : DXControl
{
    public enum ButtonSet
    {
        /// <summary>只显示中间「对勾」钮（原版 arg3=0，预游戏所有提示框）。</summary>
        Check,
        /// <summary>YES + NO（原版 arg3=1）。</summary>
        YesNo,
    }

    public const int FrameWidth = 360;
    public const int FrameHeight = 190;
    /// <summary>预游戏调用点传入的固定位置（0x459xxx 各 call site 的 arg6/arg7）。</summary>
    public static readonly Vector2I DefaultLocation = new(140, 150);
    /// <summary>文案矩形（相对对话框）：(0x18,0x17) 起、宽 0x135、高 0x78。</summary>
    public static readonly Vector2I MessageOrigin = new(0x18, 0x17);
    public static readonly Vector2I MessageSize = new(0x14D - 0x18, 0x78);
    /// <summary>原版文字色 $C8FAFF → RGB(255,250,200)。</summary>
    public static readonly Color MessageColour = new(0xFF / 255f, 0xFA / 255f, 0xC8 / 255f);
    /// <summary>行距（原版 GDI 11px 字 + 行距，取 18）。</summary>
    public const int LineHeight = 18;

    /// <summary>对勾（确认）被点击。</summary>
    public event System.Action Confirmed;
    /// <summary>NO 被点击。</summary>
    public event System.Action Cancelled;

    private readonly List<DXLabel> _lines = new();
    private string _message = string.Empty;

    public LegacyEiDialog(string message, ButtonSet buttons, Vector2I location)
    {
        Location = location;
        Size = new Vector2I(FrameWidth, FrameHeight);

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 950,
            FixedSize = true,
            Size = new Vector2I(FrameWidth, FrameHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        SetMessage(message);

        if (buttons == ButtonSet.Check)
        {
            var check = MakeButton(156, 158, 157, new Vector2I(0x93, 0x7D), new Vector2I(64, 20));
            check.MouseClick += (o, e) => Confirmed?.Invoke();
        }
        else
        {
            var yes = MakeButton(150, 152, 151, new Vector2I(0x33, 0x7D), new Vector2I(44, 20));
            yes.MouseClick += (o, e) => Confirmed?.Invoke();
            var no = MakeButton(153, 155, 154, new Vector2I(0xF4, 0x7D), new Vector2I(44, 20));
            no.MouseClick += (o, e) => Cancelled?.Invoke();
        }
    }

    public string Message
    {
        get => _message;
        set => SetMessage(value);
    }

    /// <summary>
    /// 逐行绘制文案（每行一个居中 DXLabel）。原版 DrawTextA 用 DT_CENTER，
    /// 是**逐行居中**；单个多行 DXLabel 只会按首行居中、其余行左对齐，故这里逐行建控件。
    /// </summary>
    private void SetMessage(string message)
    {
        _message = message ?? string.Empty;
        foreach (var line in _lines)
        {
            RemoveControl(line);
            line.QueueFree();
        }
        _lines.Clear();

        var wrapped = LegacyEiDialogText.Wrap(_message, MessageSize.X, 12);
        int total = wrapped.Count * LineHeight;
        int y0 = MessageOrigin.Y + Mathf.Max(0, (MessageSize.Y - total) / 2);
        for (int i = 0; i < wrapped.Count; i++)
        {
            var label = new DXLabel
            {
                Location = new Vector2I(MessageOrigin.X, y0 + i * LineHeight),
                Size = new Vector2I(MessageSize.X, LineHeight),
                FontSize = 12,
                TextColour = MessageColour,
                Align = HorizontalAlignment.Center,
                AutoSize = false,
                IsControl = false,
                Text = wrapped[i],
            };
            AddControl(label);
            _lines.Add(label);
        }
    }

    private DXButton MakeButton(int normal, int hover, int pressed, Vector2I location, Vector2I size)
    {
        var button = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = normal,
            HoverIndex = hover,
            PressedIndex = pressed,
            FixedSize = true,
            Size = size,
            Location = location,
            Text = string.Empty,
        };
        AddControl(button);
        return button;
    }
}
