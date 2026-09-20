using System;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 原版 DXTextBox 的多行 Godot 对应控件。TextEdit 只负责输入，外层 DXControl
/// 负责 Mir 风格的暗底、边框和逻辑坐标，避免窗口中混用原生默认主题。
/// </summary>
public sealed partial class DXTextArea : DXControl
{
    private readonly TextEdit _edit;
    private bool _focusWhenReady;
    private int _maxLength;
    private int _fontSize = 10;

    public event Action<string> TextChanged;

    public new string Text
    {
        get => _edit.Text;
        set => SetText(value ?? string.Empty);
    }

    public bool ReadOnly
    {
        get => _edit.Editable == false;
        set => _edit.Editable = !value;
    }

    public int ScrollVertical
    {
        get => (int)_edit.ScrollVertical;
        set => _edit.ScrollVertical = Math.Max(0, value);
    }

    public int MaxLength
    {
        get => _maxLength;
        set => _maxLength = Math.Max(0, value);
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = Math.Max(1, value);
            _edit.AddThemeFontSizeOverride("font_size", MirSkin.ScaledSize(_fontSize));
        }
    }

    public DXTextArea()
    {
        Border = true;
        BorderColour = new Color(.55f, .4f, .18f);
        BackColour = new Color(0f, 0f, 0f, .7f);
        _edit = new TextEdit
        {
            Position = new Vector2(3, 2),
            Size = new Vector2(Mathf.Max(1, Size.X - 6), Mathf.Max(1, Size.Y - 4)),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            MouseFilter = MouseFilterEnum.Stop,
            ContextMenuEnabled = true,
        };
        var font = MirSkin.GetFont();
        if (font != null) _edit.AddThemeFontOverride("font", font);
        _edit.AddThemeFontSizeOverride("font_size", MirSkin.ScaledSize(_fontSize));
        _edit.AddThemeColorOverride("font_color", Colors.White);
        _edit.AddThemeColorOverride("font_placeholder_color", new Color(1, 1, 1, .55f));
        _edit.AddThemeColorOverride("caret_color", new Color(1f, .85f, .3f));
        _edit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _edit.TextChanged += OnTextChanged;
        AddChild(_edit);
        Resized += ResizeEditor;
    }

    public new void GrabFocus()
    {
        if (!IsInsideTree())
        {
            _focusWhenReady = true;
            return;
        }

        _edit.GrabFocus();
    }

    public override void _Ready()
    {
        base._Ready();
        if (_focusWhenReady)
        {
            _focusWhenReady = false;
            _edit.GrabFocus();
        }
    }

    private void ResizeEditor()
    {
        _edit.Position = new Vector2(3, 2);
        _edit.Size = new Vector2(Mathf.Max(1, Size.X - 6), Mathf.Max(1, Size.Y - 4));
    }

    private void OnTextChanged()
    {
        if (_maxLength > 0 && _edit.Text.Length > _maxLength)
        {
            int caret = Math.Min(_edit.GetCaretColumn(), _maxLength);
            _edit.Text = _edit.Text[.._maxLength];
            _edit.SetCaretColumn(caret);
        }
        TextChanged?.Invoke(_edit.Text);
    }

    private void SetText(string value)
    {
        if (_maxLength > 0 && value.Length > _maxLength) value = value[.._maxLength];
        if (_edit.Text == value) return;
        _edit.Text = value;
    }
}
