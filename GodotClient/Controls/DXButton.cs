using System;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 三态按钮 (移植自 Client/Controls/DXButton.cs)。
/// 贴图三态 (普通/悬停/按下) + 居中文字; 点击触发 MouseClick。
/// </summary>
public partial class DXButton : DXImageControl
{
    public enum ButtonType
    {
        Normal,
        SmallButton,
        LongButton,
        YellowButton,
        GreenButton,
        Default,
        SelectedTab,
        DeselectedTab,
        AddButton,
        RemoveButton,
        LFGButton,
        OptionsButton,
    }

    public ButtonType Type = ButtonType.Normal;

    public bool CanBePressed = true;
    public new bool HasFocus;

    private DXLabel _label;

    public DXLabel Label => _label;

    /// <summary>按钮文字相对按钮背景的垂直微调。</summary>
    private float _textOffsetY;
    public float TextOffsetY
    {
        get => _textOffsetY;
        set
        {
            _textOffsetY = value;
            if (_label != null) _label.TextOffsetY = value;
        }
    }

    public DXButton()
    {
        // 原版 DXButton 构造函数默认 Sound = ButtonA；特殊控件仍可
        // 覆盖为 ButtonB/ButtonC/None。
        Sound = Library.SoundIndex.ButtonA;
    }

    public override void _Ready()
    {
        base._Ready();
        if (_label == null)
        {
            _label = new DXLabel
            {
                Name = "Label",
                Align = HorizontalAlignment.Center,
                VAlign = VerticalAlignment.Center,
                Text = Text,
                FontSize = FontSize,
                TextColour = TextColour,
                TextOffsetY = TextOffsetY,
            };
            AddChild(_label);
            // 自绘控件不走 Godot 的容器布局管线。只设置 FullRect 锚点时，
            // 在按钮已经入树或尺寸后来变化的情况下，子标签的实际 Size
            // 可能仍停留在旧值，导致文字相对按钮背景向左偏移。
            SyncLabelRect();
            _label.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationResized)
            SyncLabelRect();
    }

    private void SyncLabelRect()
    {
        if (_label == null) return;
        _label.Position = Vector2.Zero;
        _label.Size = Size;
    }

    private int _fontSize = 12;
    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            if (_label != null) _label.FontSize = value;
        }
    }

    private Color _textColour = Colors.White;
    public Color TextColour
    {
        get => _textColour;
        set
        {
            _textColour = value;
            if (_label != null) _label.TextColour = value;
        }
    }

    public new string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            if (_label != null) _label.Text = value;
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        // 不可用按钮仍需要接收并吞掉鼠标事件，避免事件继续落到窗口/地图，
        // 但绝不能触发基类 MouseClick（例如灰掉的购买/修理按钮）。
        if (e is InputEventMouseButton blocked
            && blocked.ButtonIndex is MouseButton.Left or MouseButton.Right
            && !CanBePressed)
        {
            AcceptEvent();
            return;
        }
        base._GuiInput(e);

        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && CanBePressed)
            {
                Pressed = true;
                HasFocus = true;
                QueueRedraw();
            }
            else if (!mb.Pressed && Pressed)
            {
                Pressed = false;
                QueueRedraw();
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // Godot 的 GuiInput 在鼠标移出控件后可能收不到 release；基类会复位
        // IsPressed，但按钮自己的三态标记也必须同步复位，否则按钮会永久显示按下。
        if (Pressed && !Input.IsMouseButtonPressed(MouseButton.Left))
            Pressed = false;
    }

    private bool _pressed;
    public bool Pressed
    {
        get => _pressed;
        set
        {
            if (_pressed == value) return;
            _pressed = value;
            QueueRedraw();
        }
    }

    protected override void DrawControl()
    {
        int index = GetCurrentIndex();
        if (index >= 0)
        {
            base.DrawControl();
            // 按钮文字画在贴图上; 若贴图缺失则画一个底色框保证可见
            if (MirSkin.GetTexture(LibraryFile, index) == null)
                DrawFallbackButton();
            return;
        }

        // 原版 DXButton 在 Index 未指定时并不是空矩形，而是由 Interface
        // 的左右端片和可拉伸中片拼成的按钮。这里保留同一套端片索引，
        // 使 Default/SmallButton/Tab 以及功能图标按钮在所有窗口中一致。
        if (!DrawGeneratedButton())
            DrawFallbackButton();
    }

    private void DrawFallbackButton()
    {
        Color back = IsHovered ? new Color(0.4f, 0.4f, 0.6f, 0.8f) : new Color(0.25f, 0.25f, 0.3f, 0.8f);
        DrawRect(new Rect2(Vector2.Zero, Size), back);
    }

    private bool DrawGeneratedButton()
    {
        if (LibraryFile != Library.LibraryFile.Interface || Size.X <= 0 || Size.Y <= 0)
            return false;

        switch (Type)
        {
            case ButtonType.AddButton: return DrawSingleButtonTexture(241);
            case ButtonType.RemoveButton: return DrawSingleButtonTexture(242);
            case ButtonType.LFGButton: return DrawSingleButtonTexture(243);
            case ButtonType.OptionsButton: return DrawSingleButtonTexture(245);
            case ButtonType.SmallButton: return DrawButtonParts(41, 43, 42);
            case ButtonType.SelectedTab: return DrawButtonParts(56, 58, 57);
            case ButtonType.DeselectedTab: return DrawButtonParts(53, 55, 54);
            case ButtonType.Default:
            case ButtonType.Normal:
            default: return DrawButtonParts(16, 18, 17);
        }
    }

    private bool DrawSingleButtonTexture(int index)
    {
        var texture = MirSkin.GetTexture(Library.LibraryFile.Interface, index);
        if (texture == null) return false;
        DrawTextureRect(texture, new Rect2(Vector2.Zero, Size), false,
            IsEnabled ? Colors.White : new Color(.32f, .32f, .32f, 1f));
        return true;
    }

    private bool DrawButtonParts(int leftIndex, int middleIndex, int rightIndex)
    {
        var left = MirSkin.GetTexture(Library.LibraryFile.Interface, leftIndex);
        var middle = MirSkin.GetTexture(Library.LibraryFile.Interface, middleIndex);
        var right = MirSkin.GetTexture(Library.LibraryFile.Interface, rightIndex);
        if (left == null || middle == null || right == null) return false;

        float leftWidth = left.GetWidth();
        float rightWidth = right.GetWidth();
        float middleWidth = Mathf.Max(0f, Size.X - leftWidth - rightWidth);
        Color tint = IsEnabled ? Colors.White : new Color(.32f, .32f, .32f, 1f);
        DrawTextureRect(left, new Rect2(0, 0, leftWidth, Size.Y), false, tint);
        if (middleWidth > 0)
            DrawTextureRect(middle, new Rect2(leftWidth, 0, middleWidth, Size.Y), false, tint);
        DrawTextureRect(right, new Rect2(Size.X - rightWidth, 0, rightWidth, Size.Y), false, tint);
        return true;
    }
}
