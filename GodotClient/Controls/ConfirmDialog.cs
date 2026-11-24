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
    private Control _handheldContent;
    private Button _handheldYes, _handheldNo;
    private bool _handheldConfirmSelected, _finished;
    internal Func<bool> ContextValid { get; set; }

    public ConfirmDialog(string message, string title, Action confirm)
    {
        _confirm = confirm;

        if (AutoLoginArgs.Handheld)
        {
            BuildHandheld(message, title);
            return;
        }

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
        yes.MouseClick += (o, e) => Finish(true);
        AddControl(yes);
        var no = new DXButton { Text = Lang.CommonControlCancel, Type = DXButton.ButtonType.SmallButton, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(138, 93), Size = new Vector2I(76, 25) };
        no.MouseClick += (o, e) => Finish(false);
        AddControl(no);
    }

    public override void _Ready()
    {
        base._Ready();
        if (_handheldContent != null) HandheldPromptStyle.Center(this, _handheldContent);
    }

    /// <summary>Handle semantic input from the parent: A = Enter, B = Escape.</summary>
    internal bool HandleHandheldKey(Key key)
    {
        if (_handheldYes == null || !IsVisibleInTree()) return false;
        switch (key)
        {
            case Key.Left:
            case Key.Right:
                if (!_finished)
                {
                    _handheldConfirmSelected = key == Key.Left;
                    HandheldPromptStyle.StyleButton(_handheldYes, _handheldConfirmSelected);
                    HandheldPromptStyle.StyleButton(_handheldNo, !_handheldConfirmSelected);
                }
                return true;
            case Key.Enter:
            case Key.KpEnter:
                Finish(_handheldConfirmSelected);
                return true;
            case Key.Escape:
                Finish(false);
                return true;
            default:
                return false;
        }
    }

    private void BuildHandheld(string message, string title)
    {
        Text = title ?? Lang.CommonControlConfirm;
        _handheldContent = HandheldPromptStyle.BuildFrame(this, 264, Text);
        var body = HandheldPromptStyle.AddLabel(_handheldContent, message, new Rect2(20, 48, 480, 144), 16, HandheldPromptStyle.Paper);
        body.VerticalAlignment = VerticalAlignment.Top;
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.ClipText = true;
        body.MaxLinesVisible = 6;
        body.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _handheldYes = HandheldPromptStyle.AddButton(_handheldContent, Lang.LegacyLoginsOkLabel, 20, () => Finish(true));
        _handheldNo = HandheldPromptStyle.AddButton(_handheldContent, Lang.CommonControlCancel, 266, () => Finish(false));
        HandheldPromptStyle.StyleButton(_handheldYes, selected: false);
        HandheldPromptStyle.StyleButton(_handheldNo, selected: true);
        HandheldPromptStyle.AddLabel(_handheldContent, $"← / →     A  {Lang.CommonControlConfirm}     B  {Lang.CommonControlCancel}",
            new Rect2(20, 235, 480, 21), 14, HandheldPromptStyle.Muted);
    }

    public override void Close()
    {
        _finished = true;
        base.Close();
    }

    private void Finish(bool confirmed)
    {
        if (_finished) return;
        _finished = true;
        try { if (confirmed && ContextValid?.Invoke() != false) _confirm?.Invoke(); }
        finally { WindowManager.Close(this); }
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
            () => Finish(true));
        AddLegacyButton(new Rect2I(244, 125, 44, 20), 153, () => Finish(false));
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

/// <summary>Shared image-free slate and gold styling for the two handheld prompts.</summary>
internal static class HandheldPromptStyle
{
    internal static readonly Color Paper = new("f5f0e1"), Muted = new("aeb4bc"), Tile = new("252d37");
    private static readonly Color Slate = new("16191e"), Line = new("4b4b49"), Gold = new("e2bf68"),
        Bright = new("ffea9f"), Selected = new("30291a");

    internal static Control BuildFrame(DXWindow window, int height, string title)
    {
        window.DrawChrome = window.HasTitle = window.ShowCloseButton = window.Movable = false;
        window.Size = new Vector2(520, height);
        var frame = new Panel { Size = window.Size, MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        var style = Box(Slate, Gold);
        style.BorderWidthTop = 3;
        frame.AddThemeStyleboxOverride("panel", style);
        window.AddChild(frame);
        AddLabel(frame, title, new Rect2(20, 10, 480, 34), 22, Bright).TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return frame;
    }

    internal static void Center(DXWindow window, Control frame)
    {
        // Counter the legacy canvas scale on the visual content, not on the window:
        // GameScene clamps the window using its unscaled Size in HUD coordinates.
        Vector2 viewport = window.GetViewportRect().Size;
        float fit = Mathf.Min(1f, Mathf.Min(viewport.X / frame.Size.X, viewport.Y / frame.Size.Y));
        float canvasScale = window.GetGlobalTransformWithCanvas().X.Length();
        frame.Scale = Vector2.One * (fit / canvasScale);
        window.Size = frame.Size * frame.Scale;
        window.Position = ((viewport - frame.Size * fit) / 2f).Floor() / canvasScale;
    }

    internal static Label AddLabel(Control parent, string text, Rect2 rect, int size, Color colour)
    {
        var label = new Label
        {
            Text = text ?? string.Empty, Position = rect.Position, Size = rect.Size,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", MirSkin.GetFont() ?? ThemeDB.FallbackFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        parent.AddChild(label);
        return label;
    }

    internal static Button AddButton(Control parent, string text, int x, Action activate, int bottom = 28)
    {
        var button = new Button
        {
            Text = text, Position = new Vector2(x, parent.Size.Y - 36 - bottom), Size = new Vector2(234, 36),
            ClipText = true, FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontOverride("font", MirSkin.GetFont() ?? ThemeDB.FallbackFont);
        button.AddThemeFontSizeOverride("font_size", 16);
        button.Pressed += activate;
        parent.AddChild(button);
        return button;
    }

    internal static void StyleButton(Button button, bool selected)
    {
        button.AddThemeStyleboxOverride("normal", Box(selected ? Selected : Tile, selected ? Gold : Line));
        button.AddThemeStyleboxOverride("hover", Box(Selected, Bright));
        button.AddThemeStyleboxOverride("pressed", Box(Selected, Gold));
        button.AddThemeStyleboxOverride("disabled", Box(Slate, Line));
        button.AddThemeColorOverride("font_color", selected ? Bright : Paper);
        button.AddThemeColorOverride("font_hover_color", Bright);
        button.AddThemeColorOverride("font_pressed_color", Bright);
        button.AddThemeColorOverride("font_disabled_color", Muted);
    }

    internal static StyleBoxFlat Box(Color fill, Color border)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(1);
        return box;
    }
}
