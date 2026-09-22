using System;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 FilterDropBox：10 条掉落名称过滤词，保存后由拾取/显示逻辑读取。</summary>
public sealed partial class FilterDropDialog : DXWindow
{
    private readonly DXTextInput[] _filters = new DXTextInput[10];

    public FilterDropDialog()
    {
        Text = "掉落过滤";
        // 原版 SetClientSize(266, 371)：包含标题栏、边框和无底栏区域后
        // 的实际窗口尺寸为 284x429。
        Size = new Vector2I(284, 429);
        AddControl(new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = false });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(254, 3) };
        close.MouseClick += (s, e) => WindowManager.Close(this);
        AddControl(close);
        for (int i = 0; i < _filters.Length; i++)
        {
            int slot = i;
            AddControl(new DXLabel { Text = $"Filter {i + 1}", FontSize = 10, Location = new Vector2I(20, 50 + i * 28), IsControl = false });
            _filters[i] = new DXTextInput { Location = new Vector2I(90, 50 + i * 28), Size = new Vector2I(150, 18) };
            AddControl(_filters[i]);
        }
        var save = new DXButton
        {
            Text = "保存",
            Type = DXButton.ButtonType.SmallButton,
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Size = new Vector2I(80, 25),
            Location = new Vector2I(100, 399),
        };
        save.MouseClick += (s, e) => Save();
        AddControl(save);
    }

    public void LoadFilters(string[] filters)
    {
        for (int i = 0; i < _filters.Length; i++)
            _filters[i].Text = filters != null && i < filters.Length ? filters[i] ?? string.Empty : string.Empty;
    }

    public string[] Save()
    {
        var result = new string[_filters.Length];
        for (int i = 0; i < result.Length; i++) result[i] = _filters[i].Text.Trim();
        GameScene.Game?.SetDropFilters(result);
        return result;
    }
}

/// <summary>可嵌入 DX 窗口的文本输入，保留 Godot 输入法/复制粘贴能力。</summary>
public sealed partial class DXTextInput : DXControl
{
    /// <summary>原版 Constants.PrimaryColour(198,166,99) 的输入框默认边框色。</summary>
    public static readonly Color DefaultBorderColour = new(.55f, .4f, .18f);
    private readonly LineEdit _edit;
    private bool _focusWhenReady;
    private int _fontSize = 10;
    /// <summary>输入文字相对输入框顶部的垂直微调。</summary>
    public float TextOffsetY
    {
        get => _textOffsetY;
        set
        {
            _textOffsetY = value;
            if (_edit != null) _edit.Position = new Vector2(2, value);
        }
    }
    private float _textOffsetY;
    public event Action<string> TextChanged;
    public event Action<string> TextSubmitted;
    /// <summary>输入框按 Escape 时触发（原版 DXTextBox 的 KeyPress Escape 路径）。</summary>
    public event Action Canceled;
    /// <summary>输入框按 ↑/↓ 时触发（聊天历史导航）。</summary>
    public event Action HistoryUp;
    public event Action HistoryDown;

    public new string Text
    {
        get => _edit.Text;
        set => _edit.Text = value ?? string.Empty;
    }

    public bool Secret
    {
        get => _edit.Secret;
        set => _edit.Secret = value;
    }

    public int MaxLength
    {
        get => _edit.MaxLength;
        set => _edit.MaxLength = Math.Max(0, value);
    }

    public int CaretColumn
    {
        get => _edit.CaretColumn;
        set => _edit.CaretColumn = value;
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

    /// <summary>包装的 LineEdit 是否拥有键盘焦点。</summary>
    public new bool HasFocus => _edit.HasFocus();

    public new void GrabFocus()
    {
        // Dialogs are assembled in their constructor, before WindowManager adds
        // them to the scene tree. Godot cannot focus a child at that point;
        // remember the request and apply it from _Ready instead of emitting
        // "!is_inside_tree()" errors (and losing the intended keyboard focus).
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
    public new void ReleaseFocus() => _edit.ReleaseFocus();

    public DXTextInput()
    {
        Border = true;
        BorderColour = DefaultBorderColour;
        _edit = new LineEdit { Flat = true, MouseFilter = MouseFilterEnum.Stop, Position = new Vector2(2, _textOffsetY), Size = new Vector2(Size.X - 4, Size.Y) };
        var emptyStyle = new StyleBoxEmpty();
        _edit.AddThemeStyleboxOverride("normal", emptyStyle);
        _edit.AddThemeStyleboxOverride("focus", emptyStyle);
        _edit.AddThemeStyleboxOverride("read_only", emptyStyle);
        var font = MirSkin.GetFont();
        if (font != null) _edit.AddThemeFontOverride("font", font);
        _edit.AddThemeFontSizeOverride("font_size", MirSkin.ScaledSize(_fontSize));
        _edit.AddThemeColorOverride("font_color", Colors.White);
        _edit.AddThemeColorOverride("font_placeholder_color", new Color(1f, 1f, 1f, .55f));
        _edit.AddThemeColorOverride("caret_color", new Color(1f, .85f, .3f));
        AddChild(_edit);
        _edit.TextChanged += value => TextChanged?.Invoke(value);
        _edit.TextSubmitted += value => TextSubmitted?.Invoke(value);
        _edit.GuiInput += e =>
        {
            if (e is InputEventKey key && key.Pressed)
            {
                if (key.Keycode == Key.Escape)
                {
                    Canceled?.Invoke();
                }
                else if (key.Keycode == Key.Up)
                {
                    HistoryUp?.Invoke();
                    _edit.AcceptEvent();
                }
                else if (key.Keycode == Key.Down)
                {
                    HistoryDown?.Invoke();
                    _edit.AcceptEvent();
                }
            }
        };
        Resized += () =>
        {
            _edit.Position = new Vector2(2, _textOffsetY);
            _edit.Size = Size - new Vector2(4, 2);
        };
    }
}
