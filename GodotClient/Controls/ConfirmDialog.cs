using ZirconClient.Scripts;
using System;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 通用确认窗口。**legacy 模式下改用 EI 原版 F950**（见 LogoutConfirmDialog），
/// 现代模式保留原有的 Interface F281 / 252×128 外观。
///
/// 原版 F950 是**一个窗口承载 8 个业务调用点**的共享确认框
/// （`confirmation-prompt-evidence.json`），我方此前所有确认流程都用 F281 自制框，
/// 与 EI 外观不符。这里让 legacy 走 F950，一次修正所有调用点。
///
/// 注意：原版 F950 **没有标题**（文本只有 message，无 title 字段），
/// 故 legacy 下忽略传入的 title。
/// </summary>
public sealed partial class ConfirmDialog : DXWindow
{
    private readonly Action _confirm;

    public ConfirmDialog(string message, string title, Action confirm)
    {
        _confirm = confirm;

        if (AutoLoginArgs.LegacyUi)
        {
            BuildLegacyF950(message);
            return;
        }

        Text = title ?? Lang.CommonControlConfirm;
        HasTitle = false;
        HasFooter = false;
        Size = new Vector2I(252, 128);
        _confirm = confirm;

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 281,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        AddControl(new DXLabel
        {
            Text = Text,
            FontSize = 11,
            TextColour = new Color(1f, .85f, .3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            AutoSize = false,
            Location = new Vector2I(0, 8),
            Size = new Vector2I(252, 18),
            IsControl = false,
        });
        AddControl(new DXLabel
        {
            Text = message ?? string.Empty,
            FontSize = 10,
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Location = new Vector2I(18, 30),
            Size = new Vector2I(216, 48),
            IsControl = false,
        });

        var yes = new DXButton { Text = Lang.LegacyLoginsOkLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(38, 93), Size = new Vector2I(76, 25) };
        yes.MouseClick += (o, e) => { _confirm?.Invoke(); WindowManager.Close(this); };
        AddControl(yes);
        var no = new DXButton { Text = Lang.CommonControlCancel, Type = DXButton.ButtonType.SmallButton, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(138, 93), Size = new Vector2I(76, 25) };
        no.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(no);
    }

    /// <summary>
    /// legacy 外观：EI 原版 F950。几何与帧号全部取自 `confirmation-prompt-evidence.json`：
    ///   窗口 360×190 @ (220,151)；背景 F950 画布 360×190、bbox 从 (0,0) 起 → 放 (0,0)。
    ///   消息矩形 left=24, top=23, right=333, bottom=top+120；文字色 0xC8FAFF。
    ///   YES = 帧 150(normal)/151/152，RECT (51,125,44,20)；
    ///   NO  = 帧 153(normal)/154/155，RECT (244,125,44,20)。
    ///   （151/154 是 **hover** 帧，不是 normal —— 这个坑见 LogoutConfirmDialog 注释。）
    /// </summary>
    private void BuildLegacyF950(string message)
    {
        HasTitle = false;
        HasFooter = false;
        Size = LogoutConfirmDialog.LegacySize;
        Location = LogoutConfirmDialog.LegacyLocation;
        GD.Print($"[LegacyConfirmDialog] legacy 外观: 背景=GameInter[950] size={Size} loc={Location} "
            + $"msg=\"{message}\" buttons=YES(150)@(51,125) NO(153)@(244,125)");

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 950,
            FixedSize = true,
            Size = LogoutConfirmDialog.LegacySize,
            Location = Vector2I.Zero,
            MouseFilter = MouseFilterEnum.Ignore,
            Clip = false,
        });

        AddControl(new DXLabel
        {
            Text = message ?? string.Empty,
            FontSize = 10,
            TextColour = Color.Color8(0xC8, 0xFA, 0xFF),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            AutoSize = false,
            Align = HorizontalAlignment.Left,
            VAlign = VerticalAlignment.Top,
            Location = new Vector2I(24, 23),
            Size = new Vector2I(333 - 24, 120),
            IsControl = false,
        });

        AddLegacyButton(new Rect2I(51, 125, 44, 20), 150,
            () => { _confirm?.Invoke(); WindowManager.Close(this); });
        AddLegacyButton(new Rect2I(244, 125, 44, 20), 153, () => WindowManager.Close(this));
    }

    private void AddLegacyButton(Rect2I rect, int normalFrame, Action action)
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
    }
}
