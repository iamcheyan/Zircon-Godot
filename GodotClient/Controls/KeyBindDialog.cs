using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>原版 DXKeyBindWindow：快捷键列表与默认/保存/关闭操作。</summary>
public sealed partial class KeyBindDialog : DXWindow
{
    private readonly DXControl _list;
    private readonly DXVScrollBar _scroll;
    private readonly DXLabel[] _rows;
    private int _selected = -1;
    private int _selectedSlot = 1;
    private readonly Dictionary<KeyBindAction, KeyBindSnapshot> _openedValues = new();

    private sealed class KeyBindSnapshot
    {
        public Key Key1, Key2;
        public bool Control1, Alt1, Shift1, Control2, Alt2, Shift2;
    }

    public KeyBindDialog()
    {
        KeyBindManager.Load();
        foreach (var bind in KeyBindManager.KeyBinds)
            _openedValues[bind.Action] = Snapshot(bind);
        Text = "按键设置";
        HasFooter = true;
        Size = new Vector2I(448, 430); // SetClientSize(430x330)
        AddControl(new LegacyWindowFrame { Size = Size, HasTitle = true, HasFooter = true });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(420, 3) };
        close.MouseClick += (s, e) => WindowManager.Close(this);
        AddControl(close);
        AddControl(new DXLabel { Text = "选择一个条目并按按键来更改。Esc 取消并保存当前按键。", FontSize = 9, Location = new Vector2I(18, 39), Size = new Vector2I(410, 18), IsControl = false });
        _list = new DXControl { Location = new Vector2I(9, 60), Size = new Vector2I(414, 330), Clip = true, Border = true, BorderColour = new Color(.45f, .34f, .16f) };
        AddControl(_list);
        _scroll = new DXVScrollBar { Location = new Vector2I(425, 60), Size = new Vector2I(14, 330), VisibleSize = 20, Change = 3 };
        _scroll.ValueChanged += (s, e) => RefreshRows();
        AddControl(_scroll);
        _rows = new DXLabel[Math.Max(1, KeyBindManager.KeyBinds.Count)];
        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = new DXLabel { FontSize = 8, Location = new Vector2I(8, i * 18), Size = new Vector2I(395, 17), IsControl = true, AutoSize = false, DrawOutline = true, OutlineColour = Colors.Black };
            int row = i;
            _rows[i].MouseClick += (s, e) =>
            {
                int index = row + _scroll.Value;
                if (_selected == index) _selectedSlot = _selectedSlot == 1 ? 2 : 1;
                else { _selected = index; _selectedSlot = 1; }
                RefreshRows();
            };
            _list.AddControl(_rows[i]);
        }
        _scroll.MaxValue = _rows.Length;
        var defaults = MakeButton("Defaults", new Vector2I(9, 397));
        defaults.MouseClick += (s, e) => { KeyBindManager.ResetDefaults(); RefreshRows(); };
        var save = MakeButton("Apply", new Vector2I(328, 397));
        save.MouseClick += (s, e) => { KeyBindManager.Save(); WindowManager.Close(this); };
        AddControl(defaults);
        AddControl(save);
        RefreshRows();
    }

    private DXButton MakeButton(string text, Vector2I location) => new()
    {
        Text = text, FontSize = 9, Location = location, Size = new Vector2I(80, 25), LibraryFile = LibraryFile.Interface, Index = -1,
    };

    private void RefreshRows()
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            int index = i + _scroll.Value;
            var bind = index < KeyBindManager.KeyBinds.Count ? KeyBindManager.KeyBinds[index] : null;
            _rows[i].Visible = bind != null;
            if (bind == null) continue;
            string selectedMark = index == _selected ? (_selectedSlot == 1 ? " <1>" : " <2>") : "";
            _rows[i].Text = $"{bind.Action,-28} [{Format(bind, 1),-18}] [{Format(bind, 2),-18}]{selectedMark}";
            _rows[i].TextColour = index == _selected ? Colors.Yellow : Colors.White;
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion or InputEventKey) AcceptEvent();
            return;
        }
        if (e is InputEventKey key && key.Pressed && _selected >= 0 && _selected < KeyBindManager.KeyBinds.Count)
        {
            var bind = KeyBindManager.KeyBinds[_selected];
            if (key.Keycode == Key.Escape)
            {
                Restore(bind, _selectedSlot);
            }
            else if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt)
            {
                Set(bind, _selectedSlot, Key.None, false, false, false);
            }
            else if (key.Keycode != Key.None)
            {
                Key normalized = key.Keycode is >= Key.Kp0 and <= Key.Kp9
                    ? (Key)((int)Key.Key0 + ((int)key.Keycode - (int)Key.Kp0))
                    : key.Keycode;
                Set(bind, _selectedSlot, normalized, key.CtrlPressed, key.AltPressed, key.ShiftPressed);
            }
            RefreshRows();
            AcceptEvent();
            return;
        }
        base._GuiInput(e);
    }

    private static KeyBindSnapshot Snapshot(KeyBindInfo bind) => new()
    {
        Key1 = bind.Key1, Key2 = bind.Key2,
        Control1 = bind.Control1, Alt1 = bind.Alt1, Shift1 = bind.Shift1,
        Control2 = bind.Control2, Alt2 = bind.Alt2, Shift2 = bind.Shift2,
    };

    private void Restore(KeyBindInfo bind, int slot)
    {
        if (!_openedValues.TryGetValue(bind.Action, out var snapshot)) return;
        Set(bind, 1, snapshot.Key1, snapshot.Control1, snapshot.Alt1, snapshot.Shift1);
        Set(bind, 2, snapshot.Key2, snapshot.Control2, snapshot.Alt2, snapshot.Shift2);
    }

    private static void Set(KeyBindInfo bind, int slot, Key key, bool control, bool alt, bool shift)
    {
        if (slot == 1)
        {
            bind.Key1 = key; bind.Control1 = control; bind.Alt1 = alt; bind.Shift1 = shift;
        }
        else
        {
            bind.Key2 = key; bind.Control2 = control; bind.Alt2 = alt; bind.Shift2 = shift;
        }
    }

    private static string Format(KeyBindInfo bind, int slot)
    {
        bool control = slot == 1 ? bind.Control1 : bind.Control2;
        bool alt = slot == 1 ? bind.Alt1 : bind.Alt2;
        bool shift = slot == 1 ? bind.Shift1 : bind.Shift2;
        Key key = slot == 1 ? bind.Key1 : bind.Key2;
        string text = control ? "Ctrl + " : string.Empty;
        text += alt ? "Alt + " : string.Empty;
        text += shift ? "Shift + " : string.Empty;
        return text + KeyBindManager.GetKeyText(key);
    }

    public bool AuditLayout(out string details)
    {
        details = $"size={Size} list={_list.Location}/{_list.Size} scroll={_scroll.Location}/{_scroll.Size} rows={_rows.Length}";
        return Size == new Vector2I(448, 430) && _list.Location == new Vector2I(9, 60) && _list.Size == new Vector2I(414, 330) && _scroll.Location == new Vector2I(425, 60);
    }
}
