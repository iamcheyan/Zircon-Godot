using System;
using System.Collections.Generic;
using Godot;

namespace ZirconClient.Controls;

public sealed record GmInspectionRow(string Title, string Detail, int Id);

/// <summary>Local inspection UI. The parent owns visibility and handles the requested actions.</summary>
public sealed partial class GmInspectionPanel : Control
{
    private static readonly string[] Tabs = { "地图", "传送点", "NPC" };
    private readonly bool _handheld;
    private readonly PanelContainer _panel;
    private readonly Label _summary;
    private readonly LineEdit _search;
    private readonly ItemList _list;
    private readonly RichTextLabel _detail;
    private readonly Button _travel;
    private readonly Button[] _tabs = new Button[Tabs.Length];
    private readonly int?[] _selectionByTab = new int?[Tabs.Length];
    private readonly List<GmInspectionRow> _visibleRows = new();
    private IReadOnlyList<GmInspectionRow>[] _rows = { Array.Empty<GmInspectionRow>(), Array.Empty<GmInspectionRow>(), Array.Empty<GmInspectionRow>() };
    private int _tab;
    private int _index = -1;
    private Control _handheldFocus;
    private Action<int> _travelRequested;

    public event Action<int> MapSelected;
    public event Action<int> TravelRequested
    {
        add { _travelRequested += value; UpdateTravelButton(); }
        remove { _travelRequested -= value; UpdateTravelButton(); }
    }
    public event Action CloseRequested;

    public GmInspectionPanel(bool handheld)
    {
        _handheld = handheld;
        Name = nameof(GmInspectionPanel);
        MouseFilter = MouseFilterEnum.Stop;
        LayoutMode = 0;
        Hide();

        var shade = new ColorRect { Color = new Color(0, 0, 0, .65f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _panel = new PanelContainer { Theme = CreateTheme() };
        AddChild(_panel);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(layout);

        var header = new HBoxContainer();
        layout.AddChild(header);
        var title = new Label { Text = "GM 巡检台", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color("f0d491"));
        header.AddChild(title);
        if (!handheld)
        {
            var close = new Button { Text = "关闭", CustomMinimumSize = new Vector2(78, 30) };
            close.Pressed += () => CloseRequested?.Invoke();
            header.AddChild(close);
        }

        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2 };
        _summary.AddThemeColorOverride("font_color", new Color("c6ccd3"));
        layout.AddChild(_summary);

        var tabs = new HBoxContainer();
        layout.AddChild(tabs);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int tab = i;
            var button = new Button
            {
                Text = Tabs[i], ToggleMode = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 30), FocusMode = handheld ? FocusModeEnum.None : FocusModeEnum.All
            };
            button.Pressed += () => SelectTab(tab);
            tabs.AddChild(button);
            _tabs[i] = button;
        }

        if (!handheld)
        {
            _search = new LineEdit { PlaceholderText = "按名称、编号或详情筛选当前页", ClearButtonEnabled = true };
            _search.TextChanged += _ => RefreshRows();
            layout.AddChild(_search);
        }

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        layout.AddChild(body);
        _list = new ItemList
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, AllowReselect = true, AllowSearch = false,
            MaxColumns = 1, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        _list.ItemSelected += index => SelectRow((int)index, browseMap: !_handheld, reveal: false);
        body.AddChild(_list);
        _detail = new RichTextLabel
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f,
            FocusMode = handheld ? FocusModeEnum.All : FocusModeEnum.None,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        body.AddChild(_detail);

        var footer = new HBoxContainer { CustomMinimumSize = new Vector2(0, 32) };
        layout.AddChild(footer);
        var hint = new Label
        {
            Text = handheld ? "L1/R1 切页 · ↑↓ 选择/滚动 · ←→ 焦点 · A 查看 · B 关闭"
                : "选择地图只查看，不传送 · Esc / Ctrl+Shift+F8 关闭",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color("c6ccd3"));
        footer.AddChild(hint);
        _travel = new Button { Text = "Teleport to map", CustomMinimumSize = new Vector2(138, 32) };
        _travel.Pressed += RequestTravel;
        footer.AddChild(_travel);

        _handheldFocus = _list;
        if (handheld)
        {
            foreach (Control target in new Control[] { _list, _detail, _travel })
                target.FocusEntered += () => _handheldFocus = target;
        }
        VisibilityChanged += () =>
        {
            if (IsVisibleInTree())
            {
                FitPanel();
                if (_handheld) RestoreHandheldFocus();
            }
            else if (GetViewport()?.GuiGetFocusOwner() is Control focus && IsAncestorOf(focus))
                focus.ReleaseFocus();
        };
        SelectTab(0);
    }

    public override void _Ready()
    {
        GetViewport().SizeChanged += FitPanel;
        FitPanel();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= FitPanel;

    public void Present(string summary, IReadOnlyList<GmInspectionRow> maps, IReadOnlyList<GmInspectionRow> portals, IReadOnlyList<GmInspectionRow> npcs)
    {
        _summary.Text = summary ?? string.Empty;
        _summary.TooltipText = _summary.Text;
        _summary.Visible = _summary.Text.Length > 0;
        _rows = new[] { maps, portals, npcs };
        RefreshRows();
    }

    public bool HandleKey(InputEventKey key)
    {
        if (!IsVisibleInTree()) return false;
        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (code == Key.Escape || (!_handheld && code == Key.F8 && key.CtrlPressed && key.ShiftPressed && !key.AltPressed && !key.MetaPressed))
        {
            if (key.Pressed && !key.Echo) CloseRequested?.Invoke();
            return true;
        }
        if (!_handheld) return false;

        switch (code)
        {
            case Key.Q:
            case Key.E:
                if (key.Pressed && !key.Echo)
                    SelectTab((_tab + (code == Key.Q ? Tabs.Length - 1 : 1)) % Tabs.Length);
                return true;
            case Key.Up:
            case Key.Down:
                if (!key.Pressed) return true;
                int direction = code == Key.Up ? -1 : 1;
                if (_handheldFocus == _detail)
                    _detail.GetVScrollBar().Value += direction * 24;
                else if (_handheldFocus == _list && _visibleRows.Count > 0)
                    SelectRow(Math.Clamp(_index + direction, 0, _visibleRows.Count - 1), browseMap: false, reveal: true);
                return true;
            case Key.Left:
            case Key.Right:
                if (key.Pressed && !key.Echo)
                {
                    if (code == Key.Left) (_handheldFocus == _travel ? (Control)_detail : _list).GrabFocus();
                    else if (_handheldFocus == _list) _detail.GrabFocus();
                    else if (_handheldFocus == _detail && CanTravel) _travel.GrabFocus();
                }
                return true;
            case Key.Enter:
            case Key.KpEnter:
                if (key.Pressed && !key.Echo)
                {
                    if (_handheldFocus == _travel) RequestTravel();
                    else if (_handheldFocus == _list && _index >= 0)
                    {
                        if (_tab == 0) BrowseMap();
                        else _detail.GrabFocus();
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private bool HasMapSelection => _tab == 0 && _index >= 0;
    private bool CanTravel => _travelRequested != null && HasMapSelection;

    private void BrowseMap()
    {
        if (HasMapSelection) MapSelected?.Invoke(_visibleRows[_index].Id);
    }

    private void RequestTravel()
    {
        if (CanTravel) _travelRequested?.Invoke(_visibleRows[_index].Id);
    }

    private void FitPanel()
    {
        if (!IsInsideTree()) return;
        Size = GetViewportRect().Size;
        Vector2 max = _handheld ? new Vector2(616, 336) : new Vector2(820, 550);
        Vector2 available = (Size - new Vector2(24, 24)).Max(Vector2.Zero);
        _panel.Size = available.Min(max);
        _panel.Position = ((Size - _panel.Size) / 2).Floor();
    }

    private void SelectTab(int tab)
    {
        _tab = tab;
        for (int i = 0; i < _tabs.Length; i++) _tabs[i].SetPressedNoSignal(i == tab);
        if (_handheld) _handheldFocus = _list;
        RefreshRows();
    }

    private void RefreshRows()
    {
        GmInspectionRow previous = _index >= 0 ? _visibleRows[_index] : null;
        int? preferred = _selectionByTab[_tab];
        string query = _search?.Text.Trim() ?? string.Empty;
        _list.Clear();
        _visibleRows.Clear();
        int index = -1;
        foreach (var row in _rows[_tab])
        {
            if (query.Length > 0 && !(row.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(row.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                && !row.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (index < 0 && row.Id == preferred) index = _visibleRows.Count;
            _list.AddItem(row.Title ?? string.Empty);
            _visibleRows.Add(row);
        }

        _index = _visibleRows.Count == 0 ? -1 : Math.Max(0, index);
        if (_index >= 0)
        {
            if (query.Length == 0) _selectionByTab[_tab] = _visibleRows[_index].Id;
            _list.Select(_index);
            _list.EnsureCurrentIsVisible();
        }
        UpdateDetail();
        // Don't leave the teleport button focused if the selected destination changed on refresh.
        if (_handheldFocus == _travel && CanTravel && previous?.Id != _visibleRows[_index].Id) _handheldFocus = _list;
        if (_handheld && IsVisibleInTree()) RestoreHandheldFocus();
    }

    private void SelectRow(int index, bool browseMap, bool reveal)
    {
        if ((uint)index >= (uint)_visibleRows.Count) return;
        _index = index;
        _selectionByTab[_tab] = _visibleRows[index].Id;
        if (reveal)
        {
            _list.Select(index);
            _list.EnsureCurrentIsVisible();
        }
        UpdateDetail();
        if (browseMap) BrowseMap();
    }

    private void UpdateDetail()
    {
        string text;
        if (_index < 0)
            text = string.IsNullOrWhiteSpace(_search?.Text) ? "当前没有可显示的条目。" : "没有符合筛选条件的条目。";
        else
        {
            var row = _visibleRows[_index];
            text = string.IsNullOrWhiteSpace(row.Detail) ? row.Title ?? string.Empty : $"{row.Title}\n\n{row.Detail}";
        }
        if (_detail.Text != text)
        {
            _detail.Text = text;
            _detail.GetVScrollBar().Value = 0;
        }
        UpdateTravelButton();
    }

    private void UpdateTravelButton()
    {
        _travel.Visible = _tab == 0 && _travelRequested != null;
        _travel.Disabled = !CanTravel;
        if (_handheldFocus == _travel && !CanTravel)
        {
            _handheldFocus = _list;
            if (IsVisibleInTree()) RestoreHandheldFocus();
        }
    }

    private void RestoreHandheldFocus()
    {
        _handheldFocus.GrabFocus();
        if (_handheldFocus == _list && _index >= 0) _list.EnsureCurrentIsVisible();
    }

    private static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFont = MirSkin.GetFont(), DefaultFontSize = 16 };
        theme.SetStylebox("panel", "PanelContainer", Box("20262e", "637081", 12));
        theme.SetStylebox("panel", "ItemList", Box("151a20", "495360", 6));
        theme.SetStylebox("normal", "RichTextLabel", Box("151a20", "495360", 10));
        theme.SetStylebox("selected", "ItemList", Box("324454", "324454", 0));
        theme.SetStylebox("selected_focus", "ItemList", Box("524628", "e5c477", 0));
        theme.SetStylebox("pressed", "Button", Box("524628", "e5c477", 5));
        var focus = Box("151a20", "e5c477", 0);
        focus.DrawCenter = false;
        theme.SetStylebox("focus", "ItemList", focus);
        theme.SetStylebox("focus", "RichTextLabel", focus);
        theme.SetStylebox("focus", "Button", focus);
        theme.SetConstant("v_separation", "ItemList", 6);
        theme.SetConstant("line_separation", "RichTextLabel", 4);
        return theme;
    }

    private static StyleBoxFlat Box(string background, string border, int inset) => new()
    {
        BgColor = new Color(background), BorderColor = new Color(border),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = inset, ContentMarginTop = inset, ContentMarginRight = inset, ContentMarginBottom = inset
    };
}
