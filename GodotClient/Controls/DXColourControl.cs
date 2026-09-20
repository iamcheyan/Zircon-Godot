using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 DXColourControl/DXColourPicker 的 Godot 移植。</summary>
public sealed partial class DXColourControl : DXControl
{
    private DXColourPicker _picker;
    public event EventHandler<EventArgs> BackColourChanged;

    public DXColourControl()
    {
        Size = new Vector2I(40, 15);
        Border = true;
        BorderColour = new Color(1f, .75f, .25f);
        BackColour = Colors.Black;
        MouseClick += OpenPicker;
    }

    private void OpenPicker(object sender, EventArgs args)
    {
        if (_picker != null && IsInstanceValid(_picker)) WindowManager.Close(_picker);
        _picker = new DXColourPicker(this, BackColour);
        WindowManager.Open(_picker, GameScene.Game?.UILayer ?? GetParent());
    }

    internal void ApplyColour(Color colour)
    {
        BackColour = colour;
        BackColourChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>原版 DXColourControlPair：聊天文字前景色/背景色两个相邻色块。</summary>
public sealed partial class DXColourControlPair : DXControl
{
    public readonly DXColourControl ForeColourControl;
    public readonly DXColourControl BackColourControl;

    public DXColourControlPair()
    {
        Size = new Vector2I(40, 16);
        ForeColourControl = new DXColourControl { Location = Vector2I.Zero, Size = new Vector2I(20, 16) };
        BackColourControl = new DXColourControl { Location = new Vector2I(20, 0), Size = new Vector2I(20, 16) };
        AddControl(ForeColourControl);
        AddControl(BackColourControl);
    }
}

public sealed partial class DXColourPicker : DXWindow
{
    private readonly DXColourControl _target;
    private readonly DXColourPalette _palette;
    private readonly DXTextInput _red, _green, _blue;
    private readonly DXControl _colourBox;
    private readonly DXLabel _noneLabel;
    private Color _selected;
    private readonly Color _previous;

    public DXColourPicker(DXColourControl target, Color previous)
    {
        _target = target;
        _previous = previous;
        _selected = previous;
        Text = Lang.CommonControlColourPickerTitle;
        HasFooter = true;
        Movable = true;
        Size = new Vector2I(380, 253);
        AddControl(new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = true });

        // RenderingCore/ColourPaletteHelper 的原版尺寸是 200x149。
        _palette = new DXColourPalette { Location = new Vector2I(20, 40), Size = new Vector2I(200, 149) };
        _palette.ColourPicked += SetSelected;
        AddControl(_palette);

        AddControl(new DXLabel { Text = Lang.DXColourControlUi283Label, FontSize = 9, Location = new Vector2I(282, 44), IsControl = false });
        AddControl(new DXLabel { Text = Lang.DXColourControlUi284Label, FontSize = 9, Location = new Vector2I(282, 69), IsControl = false });
        AddControl(new DXLabel { Text = Lang.DXColourControlUi285Label, FontSize = 9, Location = new Vector2I(282, 94), IsControl = false });
        _red = AddChannel(303, 40);
        _green = AddChannel(303, 65);
        _blue = AddChannel(303, 90);
        _red.TextChanged += value => ChannelChanged();
        _green.TextChanged += value => ChannelChanged();
        _blue.TextChanged += value => ChannelChanged();

        AddControl(new DXLabel { Text = Lang.CommonControlColourPickerColourLabel, FontSize = 9, Location = new Vector2I(282, 174), IsControl = false });
        _colourBox = new DXControl { Location = new Vector2I(303, 172), Size = new Vector2I(55, 20), Border = true, BorderColour = new Color(1f, .75f, .25f) };
        AddControl(_colourBox);
        _noneLabel = new DXLabel { Text = Lang.CommonControlColourPickerNoneLabel, FontSize = 9, Location = new Vector2I(315, 174), Visible = previous.A <= 0, IsControl = false };
        AddControl(_noneLabel);

        var select = new DXButton { Text = Lang.CommonControlSelect, Type = DXButton.ButtonType.Default, FontSize = 9, Location = new Vector2I(105, 218), Size = new Vector2I(80, 25), LibraryFile = LibraryFile.Interface, Index = -1 };
        select.MouseClick += (o, e) => { _target.ApplyColour(_selected); WindowManager.Close(this); };
        AddControl(select);
        var cancel = new DXButton { Text = Lang.CommonControlCancel, Type = DXButton.ButtonType.Default, FontSize = 9, Location = new Vector2I(195, 218), Size = new Vector2I(80, 25), LibraryFile = LibraryFile.Interface, Index = -1 };
        cancel.MouseClick += (o, e) => { _target.ApplyColour(_previous); WindowManager.Close(this); };
        AddControl(cancel);
        var empty = new DXButton { Text = Lang.GuildDialogStorageTabClearButtonLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Location = new Vector2I(280, 115), Size = new Vector2I(78, 25), LibraryFile = LibraryFile.Interface, Index = -1 };
        empty.MouseClick += (o, e) => SetSelected(new Color(0, 0, 0, 0));
        AddControl(empty);
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(350, 3) };
        close.MouseClick += (o, e) => { _target.ApplyColour(_previous); WindowManager.Close(this); };
        AddControl(close);
        SetSelected(previous);
    }

    private DXTextInput AddChannel(int x, int y)
    {
        var input = new DXTextInput { Location = new Vector2I(x, y), Size = new Vector2I(55, 20), MaxLength = 3 };
        AddControl(input);
        return input;
    }

    private void ChannelChanged()
    {
        if (!int.TryParse(_red.Text, out int r) || !int.TryParse(_green.Text, out int g) || !int.TryParse(_blue.Text, out int b)) return;
        SetSelected(new Color(Mathf.Clamp(r, 0, 255) / 255f, Mathf.Clamp(g, 0, 255) / 255f, Mathf.Clamp(b, 0, 255) / 255f));
    }

    private void SetSelected(Color colour)
    {
        _selected = colour;
        _palette.Selected = colour;
        _colourBox.BackColour = colour;
        _colourBox.Visible = colour.A > 0;
        _noneLabel.Visible = colour.A <= 0;
        _red.Text = Mathf.RoundToInt(colour.R * 255f).ToString();
        _green.Text = Mathf.RoundToInt(colour.G * 255f).ToString();
        _blue.Text = Mathf.RoundToInt(colour.B * 255f).ToString();
    }
}

public sealed partial class DXColourPalette : DXControl
{
    public event Action<Color> ColourPicked;
    public Color Selected = Colors.White;
    private readonly Texture2D _texture;

    public DXColourPalette()
    {
        string palettePath = ProjectSettings.GlobalizePath("res://../Debug/Client/Data/Pallete.png");
        var image = new Image();
        if (image.Load(palettePath) == Error.Ok)
            _texture = ImageTexture.CreateFromImage(image);
    }

    protected override void DrawControl()
    {
        if (_texture != null)
        {
            DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
            return;
        }
        const int columns = 32;
        const int rows = 16;
        float cellWidth = Size.X / columns;
        float cellHeight = Size.Y / rows;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            float hue = y / (float)(rows - 1);
            float saturation = x / (float)(columns - 1);
            float value = 1f;
            DrawRect(new Rect2(x * cellWidth, y * cellHeight, cellWidth + 1, cellHeight + 1), Color.FromHsv(hue, saturation, value));
        }
        var marker = new Rect2(Selected.R * Size.X - 3, (1f - Selected.G) * Size.Y - 3, 6, 6);
        DrawRect(marker, Colors.White, false, 1f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        base._GuiInput(e);
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            float saturation = Mathf.Clamp(mb.Position.X / Mathf.Max(1f, Size.X), 0f, 1f);
            float hue = Mathf.Clamp(mb.Position.Y / Mathf.Max(1f, Size.Y), 0f, 1f);
            ColourPicked?.Invoke(Color.FromHsv(hue, saturation, 1f));
        }
    }
}
