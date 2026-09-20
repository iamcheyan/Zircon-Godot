using System;
using System.Collections.Generic;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>原版 DXCheckBox 的 Godot 资源化版本：GameInter 161/162。</summary>
public sealed partial class ConfigCheckBox : DXControl
{
    private readonly DXImageControl _box;
    private readonly DXLabel _label;
    private bool _checked;
    public event EventHandler<EventArgs> CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _box.Index = value ? 162 : 161;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ConfigCheckBox(string text)
    {
        _label = new DXLabel { Text = text, FontSize = 9, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = Vector2I.Zero, IsControl = false };
        _box = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 161, Location = new Vector2I(0, 1), FixedSize = true, Size = new Vector2I(16, 16), IsControl = false };
        AddControl(_label);
        AddControl(_box);
        Size = new Vector2I(Math.Max(18, (int)MirSkin.MeasureText(text, 9).X + 20), 18);
        _box.Location = new Vector2I(Mathf.RoundToInt(Size.X) - 16, 1);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Checked = !Checked;
            AcceptEvent();
            return;
        }
        base._GuiInput(e);
    }
}

/// <summary>原版 DXSoundBar 的图标、轨道、填充和可点击滑块。</summary>
public sealed partial class ConfigSoundBar : DXControl
{
    private readonly DXImageControl _icon;
    private readonly DXImageControl _outer;
    private readonly DXControl _inner;
    private int _value = 100;
    private bool _muted;
    private bool _dragging;

    public event EventHandler<EventArgs> ValueChanged;
    public event EventHandler<EventArgs> MutedChanged;
    public int Value
    {
        get => _value;
        set
        {
            int v = Mathf.Clamp(value, 0, 100);
            if (_value == v) return;
            _value = v;
            UpdateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value) return;
            _muted = value;
            _icon.Index = value ? 4740 : 4741;
            MutedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ConfigSoundBar()
    {
        Size = new Vector2I(180, 18);
        _outer = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 4743, Location = new Vector2I(20, 3), FixedSize = true, Size = new Vector2I(155, 12), IsControl = false };
        _inner = new DXControl { Location = new Vector2I(22, 5), Size = Vector2I.Zero, BackColour = new Color(1f, .75f, .2f, .75f), IsControl = false };
        _icon = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 4741, Location = new Vector2I(0, 1), FixedSize = true, Size = new Vector2I(16, 16), IsControl = true };
        AddControl(_outer);
        AddControl(_inner);
        AddControl(_icon);
        _icon.MouseClick += (s, e) => Muted = !Muted;
        UpdateVisual();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _dragging = mb.Pressed;
            if (mb.Pressed) UpdateValueFromMouse();
            AcceptEvent();
            return;
        }
        if (_dragging && e is InputEventMouseMotion)
        {
            UpdateValueFromMouse();
            AcceptEvent();
            return;
        }
        base._GuiInput(e);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // 拖到音量条外再松开时，GuiInput 可能收不到 release；不清除会导致
        // 后续任意鼠标移动继续改音量。
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Left))
            _dragging = false;
    }

    private void UpdateValueFromMouse()
    {
        Value = Mathf.RoundToInt(Mathf.Clamp((GetLocalMousePosition().X - 20f) / 155f, 0f, 1f) * 100f);
    }

    private void UpdateVisual()
    {
        _inner.Size = new Vector2I(Mathf.RoundToInt(151f * _value / 100f), 8);
        QueueRedraw();
    }

    public override void _Draw()
        => DrawTexture(MirSkin.GetTexture(LibraryFile.GameInter, _value > 0 ? 4746 : 4745), new Vector2(20 + 151f * _value / 100f - 4, 0));
}

/// <summary>原版 DXComboBox 的轻量资源化版本：按钮显示当前项，点击后展开原版风格列表。</summary>
public sealed partial class ConfigSelect : DXControl
{
    private readonly DXButton _button;
    private readonly DXControl _menu;
    private readonly List<DXButton> _items = new();
    private int _selectedIndex = -1;
    private bool _mouseWasPressed;

    public event EventHandler<EventArgs> SelectedChanged;
    public IReadOnlyList<string> Items => _items.ConvertAll(x => x.Text);
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int next = Mathf.Clamp(value, -1, _items.Count - 1);
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            _button.Text = next >= 0 ? _items[next].Text : string.Empty;
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Text : string.Empty;

    public ConfigSelect()
    {
        Size = new Vector2I(140, 18);
        _button = new DXButton
        {
            Text = string.Empty,
            FontSize = 8,
            TextColour = new Color(.82f, .68f, .38f),
            Size = new Vector2I(140, 18),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
        };
        _button.MouseClick += (s, e) => ToggleMenu();
        AddControl(_button);

        _menu = new DXControl
        {
            Location = new Vector2I(0, 18),
            Size = new Vector2I(140, 1),
            Border = true,
            BorderColour = new Color(.8f, .6f, .2f),
            BackColour = new Color(.02f, .015f, .02f, .98f),
            Clip = true,
            IsControl = true,
            Visible = false,
            // 菜单在打开时移到 ConfigDialog 直属子节点，避开 _page 的 Clip
            // 和后续兄弟控件；仍留在同一个 CanvasLayer，继承 UiScale。
            ZIndex = 1000,
        };
        AddControl(_menu);
        SetProcess(true);
    }

    private DXWindow FindWindow()
    {
        for (Node node = this; node != null; node = node.GetParent())
            if (node is DXWindow window) return window;
        return null;
    }

    public override void _Process(double delta)
    {
        // 点击外部关闭：不使用全屏 catcher（会遮挡对话框内其他控件），
        // 而是轮询鼠标按键状态——菜单可见时，左键按下且鼠标不在菜单区域内 → 关闭。
        if (!_menu.Visible) return;

        bool pressed = Input.IsMouseButtonPressed(MouseButton.Left);
        if (pressed && !_mouseWasPressed)
        {
            Rect2 menuRect = new Rect2(_menu.GlobalPosition, _menu.Size);
            Vector2 mouse = _menu.GetGlobalMousePosition();
            if (!menuRect.HasPoint(mouse))
                CloseMenu();
        }
        _mouseWasPressed = pressed;
    }

    private void OpenMenu()
    {
        DXWindow window = FindWindow();
        if (window == null) return;

        // GlobalPosition 是 CanvasLayer 局部坐标（不含 CanvasLayer Transform），
 // 不能用 GetGlobalTransformWithCanvas()（那是屏幕空间坐标，差 UiScale 倍）。
 Vector2 menuPos = GlobalPosition + new Vector2(0, Size.Y);
 if (_menu.GetParent() != window)
     _menu.Reparent(window, false);
 _menu.GlobalPosition = menuPos;
        _menu.ZIndex = 1000;
        _menu.Visible = true;
        _mouseWasPressed = true; // 避免本次按下立即关闭
    }

    private void ToggleMenu()
    {
        if (_menu.Visible) CloseMenu();
        else OpenMenu();
    }

    private void CloseMenu()
    {
        _menu.Visible = false;
    }

    public void AddItem(string text)
    {
        var item = new DXButton
        {
            Text = text,
            FontSize = 8,
            TextColour = new Color(.82f, .68f, .38f),
            Size = new Vector2I(138, 18),
            Location = new Vector2I(1, 1 + _items.Count * 18),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
        };
        int index = _items.Count;
        item.MouseClick += (s, e) =>
        {
            SelectedIndex = index;
            CloseMenu();
        };
        _menu.AddControl(item);
        _items.Add(item);
        _menu.Size = new Vector2I(140, Math.Max(1, 2 + _items.Count * 18));
        if (_selectedIndex < 0) SelectedIndex = 0;
    }

    public void SelectItem(string text)
    {
        int index = _items.FindIndex(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) SelectedIndex = index;
    }
}

/// <summary>原版 DXConfigSection 的 348px 资源化分组容器。</summary>
public sealed partial class ConfigSectionPanel : DXControl
{
    private readonly DXLabel _title;
    private int _row;
    public ConfigSectionPanel(string title, int rows, int columns = 1)
    {
        Size = new Vector2I(348, 30 + Mathf.CeilToInt(rows / (float)Math.Max(1, columns)) * 20);
        Border = true;
        BorderColour = new Color(.42f, .30f, .14f);
        BackColour = new Color(.04f, .025f, .018f, .75f);
        var header = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 4750, FixedSize = true, Size = new Vector2I(348, 25), IsControl = false };
        AddControl(header);
        _title = new DXLabel { Text = title, FontSize = 9, TextColour = new Color(1f, .82f, .35f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(348, 20), Location = new Vector2I(0, 2), IsControl = false };
        AddControl(_title);
    }

    public void AddOption(string text, ConfigCheckBox check, int columns = 1)
    {
        int column = columns <= 1 ? 0 : _row % columns;
        int row = columns <= 1 ? _row : _row / columns;
        check.Location = new Vector2I(column * (348 / columns) + 8, 27 + row * 20);
        AddControl(check);
        _row++;
    }

    public void AddSelect(string text, ConfigSelect select)
    {
        var label = new DXLabel { Text = text, FontSize = 9, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(8, 27 + _row * 20), IsControl = false };
        select.Location = new Vector2I(192, 27 + _row * 20);
        AddControl(label);
        AddControl(select);
        _row++;
    }

    public void AddSound(string text, ConfigSoundBar bar)
    {
        var label = new DXLabel { Text = text, FontSize = 9, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(8, 27 + _row * 20), IsControl = false };
        bar.Location = new Vector2I(145, 27 + _row * 20);
        AddControl(label);
        AddControl(bar);
        _row++;
    }

    public void AddButton(DXButton button)
    {
        button.Location = new Vector2I(8, 27 + _row * 20);
        AddControl(button);
        _row++;
    }

    public void AddInput(string text, DXTextInput input)
    {
        var label = new DXLabel { Text = text, FontSize = 9, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(8, 27 + _row * 20), IsControl = false };
        input.Location = new Vector2I(145, 27 + _row * 20);
        input.Size = new Vector2I(185, 18);
        AddControl(label);
        AddControl(input);
        _row++;
    }

    public void AddColour(string text, DXColourControl colour, int columns = 2)
    {
        int column = columns <= 1 ? 0 : _row % columns;
        int row = columns <= 1 ? _row : _row / columns;
        int x = column * (348 / columns);
        var label = new DXLabel { Text = text, FontSize = 8, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(x + 8, 27 + row * 20), Size = new Vector2I(125, 18), IsControl = false };
        colour.Location = new Vector2I(x + 135, 27 + row * 20);
        AddControl(label);
        AddControl(colour);
        _row++;
    }

    public void AddColourPair(string text, DXColourControlPair pair, int columns = 2)
    {
        int column = columns <= 1 ? 0 : _row % columns;
        int row = columns <= 1 ? _row : _row / columns;
        int x = column * (348 / columns);
        var label = new DXLabel { Text = text, FontSize = 8, TextColour = new Color(.66f, .49f, .26f), DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(x + 8, 27 + row * 20), Size = new Vector2I(125, 18), IsControl = false };
        pair.Location = new Vector2I(x + 135, 27 + row * 20);
        AddControl(label);
        AddControl(pair);
        _row++;
    }
}
