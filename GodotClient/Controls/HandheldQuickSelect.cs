using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 掌机快速选择面板。技能/腰带共用中央图标栏；方向键、右摇杆和 L1/R1 选择，
/// Enter 确认，Escape 取消。面板使用固定节点池，不影响桌面键盘窗口。
/// </summary>
public partial class HandheldQuickSelect : Control
{
    public enum SelectKind { None, Magic, Belt }

    private const int VisibleSlots = 5;
    private const float SlotGap = 4f;
    private const float PanelHeight = 190f;
    private const string NavigationHint = "方向键 / 右摇杆 / L1 R1 选择";

    private readonly GameScene _game;
    private readonly List<ClientUserMagic> _magics = new();
    private readonly Control[] _slots = new Control[VisibleSlots];
    private readonly TextureRect[] _icons = new TextureRect[VisibleSlots];
    private readonly Label[] _slotLabels = new Label[VisibleSlots];
    private readonly Label[] _slotNumbers = new Label[VisibleSlots];
    private SelectKind _kind;
    private int _cursor;
    private Label _title;
    private Label _hint;
    private Label _detail;
    private Control _panel;
    private double _nextStickMoveMs;
    private double _messageUntilMs;

    public bool IsOpen => _kind != SelectKind.None;
    public int SelectedIndex => _cursor;
    public SelectKind Kind => _kind;

    public HandheldQuickSelect(GameScene game)
    {
        _game = game;
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1024, 768);
        ZIndex = 200;
        Visible = false;
        Build();
    }

    private void Build()
    {
        _panel = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.07f, 0.94f),
            Position = new Vector2(20, 55),
            Size = new Vector2(600, PanelHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_panel);
        _title = LabelAt(_panel, new Vector2(16, 10), "", 18, Colors.White);
        _detail = LabelAt(_panel, new Vector2(16, 151), "", 14, new Color(1f, 0.86f, 0.35f));
        _hint = LabelAt(_panel, new Vector2(16, 171), "", 13,
            new Color(0.86f, 0.88f, 0.95f));

        for (int i = 0; i < VisibleSlots; i++)
        {
            var cell = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
            _panel.AddChild(cell);
            _slots[i] = cell;

            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            cell.AddChild(icon);
            _icons[i] = icon;
            _slotNumbers[i] = LabelAt(cell, new Vector2(3, 2), "", 12, new Color(1f, 0.83f, 0.3f));
            _slotLabels[i] = LabelAt(cell, new Vector2(2, 42), "", 13, Colors.White);
            _slotLabels[i].HorizontalAlignment = HorizontalAlignment.Center;
            _slotLabels[i].ClipText = true;
        }
    }

    private static Label LabelAt(Node parent, Vector2 pos, string text, int size, Color colour)
    {
        var label = new Label { Position = pos, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        var font = MirSkin.GetFont();
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        parent.AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        if (_game == null || _panel == null) return;
        if (!Visible) return;
        // This handheld layout is measured in viewport pixels, not desktop HUD units.
        Scale = Vector2.One / GameScene.UiScale;
        var viewport = _game.GetViewport().GetVisibleRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;

        float panelWidth = Mathf.Max(1f, Mathf.Min(600f, viewport.X - 16f));
        float panelHeight = Mathf.Max(1f, Mathf.Min(PanelHeight, viewport.Y - 16f));
        _panel.Size = new Vector2(panelWidth, panelHeight);
        _panel.Position = new Vector2(Mathf.Max(8f, (viewport.X - panelWidth) / 2f),
            Mathf.Max(8f, (viewport.Y - panelHeight) / 2f));
        LayoutSlots(panelWidth, panelHeight);

        if (_messageUntilMs > 0 && Godot.Time.GetTicksMsec() >= _messageUntilMs)
        {
            _messageUntilMs = 0;
            Visible = IsOpen;
        }
        if (IsOpen && _kind == SelectKind.Magic && _cursor >= 0 && _cursor < _magics.Count)
            UpdateDetail();
    }

    private void LayoutSlots(float panelWidth, float panelHeight)
    {
        float innerWidth = Mathf.Max(10f, panelWidth - 32f);
        float cellWidth = Mathf.Max(1f, (innerWidth - SlotGap * (VisibleSlots - 1)) / VisibleSlots);
        float cellHeight = Mathf.Max(1f, Mathf.Min(82f, panelHeight - 82f));
        float top = Mathf.Max(32f, (panelHeight - cellHeight) / 2f - 2f);
        for (int i = 0; i < VisibleSlots; i++)
        {
            _slots[i].Position = new Vector2(16f + i * (cellWidth + SlotGap), top);
            _slots[i].Size = new Vector2(cellWidth, cellHeight);
            _icons[i].Position = new Vector2(2f, 13f);
            _icons[i].Size = new Vector2(Mathf.Max(1f, cellWidth - 4f), Mathf.Max(1f, cellHeight - 32f));
            _slotLabels[i].Position = new Vector2(2f, cellHeight - 18f);
            _slotLabels[i].Size = new Vector2(Mathf.Max(1f, cellWidth - 4f), 16f);
        }
        _detail.Position = new Vector2(16f, panelHeight - 39f);
        _detail.Size = new Vector2(Mathf.Max(1f, panelWidth - 32f), 18f);
        _detail.ClipText = true;
        _hint.Position = new Vector2(16f, panelHeight - 20f);
        _hint.Size = new Vector2(Mathf.Max(1f, panelWidth - 32f), 16f);
        _hint.ClipText = true;
    }

    public void OpenMagic()
    {
        _magics.Clear();
        if (_game?.UserMagics != null)
            _magics.AddRange(_game.UserMagics.Values
                .Where(x => x?.Info != null)
                .OrderBy(x => x.Info.NeedLevel1)
                .ThenBy(x => x.Info.Name));
        if (_magics.Count == 0)
        {
            ShowMessage("没有已学习的技能");
            return;
        }
        _kind = SelectKind.Magic;
        _cursor = 0;
        _messageUntilMs = 0;
        _title.Text = "技能选择";
        _hint.Text = NavigationHint + "   A 确认   B 返回";
        RefreshPanel();
    }

    public void OpenBelt()
    {
        if (_game == null) return;
        _kind = SelectKind.Belt;
        _cursor = 0;
        _messageUntilMs = 0;
        _title.Text = "腰带选择";
        _hint.Text = NavigationHint + "   A 使用   B 返回";
        RefreshPanel();
    }

    public bool HandleKey(Key key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Q:
                Move(-1); return true;
            case Key.Right:
            case Key.Down:
            case Key.E:
                Move(1); return true;
            case Key.Enter:
            case Key.KpEnter:
                Confirm(); return true;
            case Key.Escape:
                Close(); return true;
            default:
                return false;
        }
    }

    public void Move(int delta)
    {
        int count = _kind == SelectKind.Magic ? _magics.Count : _kind == SelectKind.Belt ? Globals.MaxBeltCount : 0;
        if (count == 0) return;
        _cursor = (_cursor + delta % count + count) % count;
        RefreshPanel();
    }

    public void MoveFromStick(Vector2 axis, double now)
    {
        if (axis.Length() < 0.55f || now < _nextStickMoveMs) return;
        Move(Mathf.Abs(axis.X) >= Mathf.Abs(axis.Y)
            ? (axis.X > 0 ? 1 : -1)
            : (axis.Y > 0 ? 1 : -1));
        _nextStickMoveMs = now + 180.0;
    }

    public void Confirm()
    {
        if (!IsOpen) return;
        SelectKind selectedKind = _kind;
        int selectedIndex = _cursor;
        ClientUserMagic magic = selectedKind == SelectKind.Magic && selectedIndex >= 0 && selectedIndex < _magics.Count
            ? _magics[selectedIndex] : null;
        bool passive = magic?.Info?.Property == MagicProperty.Passive;

        Close();
        if (selectedKind == SelectKind.Magic)
        {
            if (magic == null) return;
            if (passive)
            {
                ShowMessage("被动技能仅供查看，不能施放");
                return;
            }
            if (!_game.TryUseHandheldMagic(magic))
                ShowMessage("技能未能使用");
        }
        else if (selectedKind == SelectKind.Belt && !_game.TryUseHandheldBelt(selectedIndex))
        {
            ShowMessage($"腰带第 {selectedIndex + 1} 格为空或当前无法使用");
        }
    }

    public void Close()
    {
        _kind = SelectKind.None;
        Visible = false;
        _messageUntilMs = 0;
    }

    private void RefreshPanel()
    {
        Visible = true;
        int count = _kind == SelectKind.Magic ? _magics.Count : Globals.MaxBeltCount;
        int first = _cursor / VisibleSlots * VisibleSlots;
        for (int i = 0; i < VisibleSlots; i++)
        {
            int index = first + i;
            bool inRange = index < count;
            bool selected = inRange && index == _cursor;
            _slots[i].Visible = inRange;
            ((ColorRect)_slots[i]).Color = selected
                ? new Color(0.78f, 0.58f, 0.17f, 0.9f)
                : new Color(0.04f, 0.06f, 0.12f, 0.9f);
            _icons[i].Texture = null;
            _slotNumbers[i].Text = "";
            _slotLabels[i].Text = "";
            if (!inRange) continue;

            if (_kind == SelectKind.Magic)
            {
                var magic = _magics[index];
                _icons[i].Texture = MirSkin.GetTexture(LibraryFile.MagicIcon, magic.Info.Icon);
                _slotLabels[i].Text = magic.Info.Local() ?? magic.Info.Name;
                _slotNumbers[i].Text = magic.Info.Property == MagicProperty.Passive ? "被动" : $"Lv.{magic.Level}";
            }
            else
            {
                var item = _game.GetHandheldBeltItem(index);
                if (item?.Info != null)
                    _icons[i].Texture = MirSkin.GetTexture(DXItemCell.ItemIconLibraryFile, DXItemCell.GetItemDrawIndex(item));
                _slotLabels[i].Text = item?.Info?.Local() ?? item?.Info?.ItemName ?? "空";
                long amount = _game.GetHandheldBeltCount(index);
                _slotNumbers[i].Text = amount > 0 ? amount.ToString() : (index == 9 ? "0" : (index + 1).ToString());
            }
        }
        if (_kind == SelectKind.Magic) UpdateDetail();
        else
        {
            var info = _game.GetHandheldBeltItem(_cursor)?.Info;
            _detail.Text = $"腰带槽位  {(_cursor + 1)}/{Globals.MaxBeltCount}  {info?.Local() ?? info?.ItemName ?? "空"}";
        }
    }

    private void UpdateDetail()
    {
        if (_cursor < 0 || _cursor >= _magics.Count) return;
        var magic = _magics[_cursor];
        string name = magic.Info.Local() ?? magic.Info.Name;
        DateTime now = Library.Time.Now;
        string cooldown = magic.NextCast > now
            ? $"冷却 {Math.Max(0, (int)Math.Ceiling((magic.NextCast - now).TotalSeconds))}秒"
            : "就绪";
        string property = magic.Info.Property == MagicProperty.Passive ? "被动·仅查看" : "主动";
        _detail.Text = $"{name}  Lv.{magic.Level}  耗蓝 {magic.Cost}  {cooldown}  {property}  {(_cursor + 1)}/{_magics.Count}";
    }

    private void ShowMessage(string text)
    {
        _kind = SelectKind.None;
        foreach (var slot in _slots) slot.Visible = false;
        _title.Text = "";
        _detail.Text = "";
        _hint.Text = text;
        Visible = true;
        _messageUntilMs = Godot.Time.GetTicksMsec() + 1800;
    }
}
