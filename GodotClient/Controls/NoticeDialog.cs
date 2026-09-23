using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 旧版 EI 公告/提示窗口（GameInter F602）。
/// F602 同时被旧版系统公告横幅复用；这里实现的是 id=15 的独立公告板窗口。
/// </summary>
public partial class NoticeDialog : DXWindow
{
    private readonly DXImageControl _background;
    private readonly DXButton _closeButton;
    private readonly DXButton _actionButton;
    private readonly DXTextArea _text;

    public NoticeDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = false;
        Size = new Vector2I(584, 252);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 602,
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 602),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 161,
            HoverIndex = 162,
            PressedIndex = 162,
            Location = new Vector2I(548, 16),
            Size = new Vector2I(28, 26),
        };
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);

        _actionButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 606,
            HoverIndex = 607,
            PressedIndex = 607,
            Location = new Vector2I(496, 27),
            Size = new Vector2I(40, 20),
        };
        _actionButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_actionButton);

        _text = new DXTextArea
        {
            Location = new Vector2I(23, 94),
            Size = new Vector2I(500, 112),
            ReadOnly = true,
            MaxLength = 0,
            Text = string.Empty,
        };
        AddControl(_text);
    }

    public void SetNotice(string text) => _text.Text = text ?? string.Empty;

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(584, 252)
            && _background.LibraryFile == LibraryFile.GameInter && _background.Index == 602
            && _closeButton.Location == new Vector2(548, 16)
            && _actionButton.Location == new Vector2(496, 27)
            && _text.Location == new Vector2(23, 94)
            && _text.Size == new Vector2(500, 112);
        details = $"size={Size} frame={_background.Index} close={_closeButton.Location} action={_actionButton.Location} text={_text.Size}@{_text.Location}";
        return ok;
    }
}
