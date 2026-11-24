using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZirconClient.HandheldActions;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>Native per-character binding editor sized for the 640x360 handheld viewport.</summary>
internal partial class HandheldActionBindings : Control
{
    private readonly GameScene _game;
    private HandheldActionConfiguration _draft;
    private IReadOnlyList<HandheldBindingChoice> _choices = Array.Empty<HandheldBindingChoice>();
    private Font _font;
    private int _tab, _slot, _category, _choice, _settingsRow;
    private bool _picker, _timingSettings;
    private string _message = "方向选择 · A 更改 · X 清空 · B 返回";
    private static readonly Color Ink = new("14221b"), Deep = new("101c16"), Card = new("203527"), Focus = new("34482e"), Paper = new("f1eddb"), Muted = new("b6c1ad"), Gold = new("d9bc73"), Edge = new("476044");
    private static readonly string[] Tabs = { "第 1 页 · 6", "第 2 页 · 6", "固定快捷 · 6" };
    private static readonly string[] Direct = { "X", "Y", "↑", "↓", "←", "→" };
    private static readonly string[] Fixed = { "L2 · 短按", "L2 · 长按", "R2 · 短按", "R2 · 长按", "L1 + R1", "L2 + R2" };
    private static readonly HandheldButton[] Buttons = { HandheldButton.X, HandheldButton.Y, HandheldButton.Up, HandheldButton.Down, HandheldButton.Left, HandheldButton.Right };
    private static readonly string[] FixedKeys = { "L2tap", "L2hold", "R2tap", "R2hold", "L1+R1", "L2+R2" };
    private static readonly ActionKind[] Categories = { ActionKind.Skill, ActionKind.Item, ActionKind.Menu, ActionKind.Empty };
    private static readonly string[] CategoryLabels = { "技能", "物品", "功能", "清空" };

    internal static Rect2[] CardBounds() => Enumerable.Range(0, 6)
        .Select(i => new Rect2(18 + i % 3 * 132, 83 + i / 3 * 101, 124, 91)).ToArray();

    public HandheldActionBindings(GameScene game)
    {
        _game = game;
        Name = "HandheldActionBindings";
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        Visible = false;
        SetProcess(false);
    }

    public void Open()
    {
        _draft = _game.CloneHandheldActionConfig();
        _tab = _slot = _category = _choice = 0;
        _picker = _timingSettings = false;
        _message = "方向选择 · A 更改 · X 清空 · B 返回";
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public void Close()
    {
        _picker = _timingSettings = false;
        _draft = null;
        Visible = false;
        SetProcess(false);
    }

    public void HandleKey(Key key)
    {
        if (!Visible || _draft == null) return;
        if (_timingSettings) { HandleTimingKey(key); return; }
        if (_picker) { HandlePickerKey(key); return; }
        switch (key)
        {
            case Key.Escape: Close(); return;
            case Key.Q: _tab = (_tab + Tabs.Length - 1) % Tabs.Length; _slot = 0; break;
            case Key.E: _tab = (_tab + 1) % Tabs.Length; _slot = 0; break;
            case Key.Left: _slot = _slot % 3 == 0 ? Math.Min(_slot + 2, 5) : _slot - 1; break;
            case Key.Right: _slot = _slot % 3 == 2 ? _slot - 2 : _slot + 1; break;
            case Key.Up: _slot = _slot < 3 ? _slot + 3 : _slot - 3; break;
            case Key.Down: _slot = _slot < 3 ? _slot + 3 : _slot - 3; break;
            case Key.Enter: case Key.KpEnter: OpenPicker(); break;
            case Key.Space: ClearBinding(); break;
            case Key.F13: _timingSettings = true; _settingsRow = 0; _message = "↑↓ 选择 · ←→ 调整 · B 返回"; break;
        }
        QueueRedraw();
    }

    private void OpenPicker()
    {
        _picker = true;
        _category = 0;
        _choice = 0;
        LoadChoices();
        _message = "L1 / R1 切类别 · A 替换绑定 · B 保留原绑定";
    }

    private void HandlePickerKey(Key key)
    {
        if (_choices.Count == 0 && key is Key.Left or Key.Right or Key.Up or Key.Down) return;
        switch (key)
        {
            case Key.Escape: _picker = false; _message = "未更改当前绑定"; break;
            case Key.Q: _category = (_category + Categories.Length - 1) % Categories.Length; _choice = 0; LoadChoices(); break;
            case Key.E: _category = (_category + 1) % Categories.Length; _choice = 0; LoadChoices(); break;
            case Key.Left: if (_choice % 2 == 1) _choice--; break;
            case Key.Right: _choice = _choice % 2 == 1 || _choice + 1 >= _choices.Count ? _choice : _choice + 1; break;
            case Key.Up: _choice = Math.Max(0, _choice - 2); break;
            case Key.Down: _choice = Math.Min(Math.Max(0, _choices.Count - 1), _choice + 2); break;
            case Key.Enter: case Key.KpEnter:
                if (_choices.Count != 0) CommitBinding(_choices[_choice].Binding);
                break;
        }
        QueueRedraw();
    }

    private void LoadChoices()
    {
        _choices = _game.HandheldBindingChoices(Categories[_category]);
        if (_choices.Count == 0) _message = _category == 1 ? "没有可直接使用的物品。" : "没有可绑定的项目。";
        _choice = Math.Clamp(_choice, 0, Math.Max(0, _choices.Count - 1));
    }

    private ActionBinding GetBinding(int index) => _tab < 2
        ? _draft.Pages[_tab].Bindings[Buttons[index]]
        : _draft.FixedSlots[FixedKeys[index]];

    private void AssignBinding(ActionBinding binding)
    {
        if (_tab < 2) _draft.Pages[_tab].Bindings[Buttons[_slot]] = Copy(binding);
        else _draft.FixedSlots[FixedKeys[_slot]] = Copy(binding);
    }

    private void CommitBinding(ActionBinding binding)
    {
        var previous = Copy(GetBinding(_slot));
        AssignBinding(binding);
        string error = _game.SaveHandheldActionBindings(_draft);
        if (error == null)
        {
            _draft = _game.CloneHandheldActionConfig();
            _picker = false;
            _message = "已保存 · 新绑定已立即生效";
        }
        else
        {
            AssignBinding(previous);
            _message = "保存失败 · 原绑定保留 · " + error;
        }
    }

    private void ClearBinding() => CommitBinding(ActionBinding.Empty());

    private static ActionBinding Copy(ActionBinding source) => new()
    {
        Kind = source.Kind, StableId = source.StableId, SkillMode = source.SkillMode, MenuAction = source.MenuAction
    };

    private void HandleTimingKey(Key key)
    {
        if (key == Key.Escape) { _timingSettings = false; _message = "返回绑定页"; QueueRedraw(); return; }
        if (key == Key.Up) _settingsRow = (_settingsRow + 1) % 2;
        else if (key == Key.Down) _settingsRow = (_settingsRow + 1) % 2;
        else if (key is Key.Left or Key.Right)
        {
            long oldLong = _draft.LongPressThresholdMs, oldChord = _draft.ChordWindowMs;
            long delta = key == Key.Right ? 50 : -50;
            if (_settingsRow == 0)
            {
                _draft.LongPressThresholdMs = Math.Clamp(oldLong + delta, HandheldActionEngine.MinThresholdMs, HandheldActionEngine.MaxThresholdMs);
                _draft.ChordWindowMs = Math.Min(_draft.ChordWindowMs, _draft.LongPressThresholdMs);
            }
            else _draft.ChordWindowMs = Math.Clamp(oldChord + (key == Key.Right ? 10 : -10), 0, _draft.LongPressThresholdMs);
            string error = _game.SaveHandheldActionBindings(_draft);
            if (error == null) { _draft = _game.CloneHandheldActionConfig(); _message = "已保存 · 阈值立即生效"; }
            else { _draft.LongPressThresholdMs = oldLong; _draft.ChordWindowMs = oldChord; _message = "保存失败 · 设置保留原值 · " + error; }
        }
        QueueRedraw();
    }

    private HandheldBindingChoice CurrentChoice() => _game.DescribeHandheldBinding(GetBinding(_slot));
    private string SlotLabel() => _tab < 2 ? Direct[_slot] : Fixed[_slot];
    private static string BindingKind(ActionKind kind) => kind switch
    {
        ActionKind.Skill => "技能", ActionKind.Item => "物品", ActionKind.Menu => "功能", _ => "未绑定"
    };

    private void Text(Vector2 at, string text, int size, Color color, float width = -1)
        => DrawString(_font, at, text ?? "", HorizontalAlignment.Left, width, MirSkin.ScaledSize(size), color);

    private void Icon(Rect2 bounds, Texture2D texture)
    {
        if (texture == null) return;
        var size = texture.GetSize();
        if (size.X <= 0 || size.Y <= 0) return;
        size *= Math.Min(bounds.Size.X / size.X, bounds.Size.Y / size.Y);
        DrawTextureRect(texture, new Rect2(bounds.Position + (bounds.Size - size) / 2, size), false);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ink);
        DrawRect(new Rect2(0, 0, 640, 48), Deep);
        Text(new(18, 25), _picker ? "选择绑定至 " + Tabs[_tab] + " · " + SlotLabel() : _timingSettings ? "操作时间设置" : "操作绑定", 16, Paper);
        Text(new(444, 24), "手札 · 系统 / 操作", 10, Muted);
        DrawTabs();
        if (_timingSettings) DrawTimingSettings();
        else if (_picker) DrawPicker();
        else DrawBindingCards();
        DrawFooter();
    }

    private void DrawTabs()
    {
        Text(new(19, 68), "L1", 9, Gold);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = new Rect2(47 + i * 115, 51, 108, 25);
            DrawRect(r, _tab == i && !_picker && !_timingSettings ? Focus : Card);
            if (_tab == i && !_picker && !_timingSettings) DrawRect(new Rect2(r.Position.X, r.End.Y - 2, r.Size.X, 2), Gold);
            Text(r.Position + new Vector2(8, 17), Tabs[i], 10, Paper);
        }
        Text(new(402, 68), "R1", 9, Gold);
    }

    private void DrawBindingCards()
    {
        var boxes = CardBounds();
        for (int i = 0; i < boxes.Length; i++)
        {
            var r = boxes[i]; var choice = _game.DescribeHandheldBinding(GetBinding(i));
            DrawRect(r, i == _slot ? Focus : Card);
            DrawRect(r, i == _slot ? Gold : Edge, false, i == _slot ? 2 : 1);
            Text(r.Position + new Vector2(7, 15), _tab < 2 ? Direct[i] : Fixed[i], 10, Gold);
            Icon(new Rect2(r.Position + new Vector2(7, 25), new Vector2(31, 31)), choice.Icon);
            Text(r.Position + new Vector2(44, 38), choice.Name, 10, Paper, 75);
            string summary = choice.Binding.Kind == ActionKind.Empty ? "未绑定" : BindingKind(choice.Binding.Kind) + " · " + choice.Detail;
            Text(r.Position + new Vector2(44, 55), summary, 8, Muted, 75);
        }
        var current = CurrentChoice();
        var side = new Rect2(426, 83, 198, 192);
        DrawRect(side, Deep); DrawRect(side, Edge, false, 1);
        Icon(new Rect2(440, 96, 42, 42), current.Icon);
        Text(new(489, 108), SlotLabel() + " · " + current.Name, 11, Gold, 124);
        Text(new(440, 154), current.Detail, 10, Paper, 170);
        Text(new(440, 181), "A 更改绑定", 10, Muted);
        Text(new(440, 198), "X 清空当前槽", 10, Muted);
        Text(new(440, 224), "方向键选择位置", 9, Muted);
        Text(new(440, 242), "L1 / R1 切换标签", 9, Muted);
        Text(new(440, 260), "L3 操作时间与组合窗口", 8, Muted);
        if (_tab < 2)
        {
            Text(new(19, 292), "右摇杆选目标：方向夹角优先", 9, Muted, 390);
            Text(new(19, 307), "夹角相同再看距离；规则固定，不可配置", 9, Muted, 390);
        }
        Text(new(19, 321), _message, 9, _message.StartsWith("保存失败", StringComparison.Ordinal) ? new Color("f0a28a") : Muted, 600);
    }

    private void DrawPicker()
    {
        DrawRect(new Rect2(14, 82, 612, 224), Deep); DrawRect(new Rect2(14, 82, 612, 224), Edge, false, 1);
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            var r = new Rect2(23 + category * 88, 91, 82, 24);
            DrawRect(r, _category == category ? Focus : Card);
            if (_category == category) DrawRect(new Rect2(r.Position.X, r.End.Y - 2, r.Size.X, 2), Gold);
            Text(r.Position + new Vector2(18, 16), CategoryLabels[category], 10, Paper);
        }
        Text(new(30, 134), "L1 / R1 切换类别", 8, Muted);
        int first = Math.Clamp((_choice / 10) * 10, 0, Math.Max(0, _choices.Count - 1));
        for (int i = first; i < Math.Min(_choices.Count, first + 10); i++)
        {
            int local = i - first, col = local % 2, row = local / 2;
            var r = new Rect2(24 + col * 174, 144 + row * 29, 166, 25);
            DrawRect(r, i == _choice ? Focus : Card);
            if (i == _choice) DrawRect(r, Gold, false, 1);
            Icon(new Rect2(r.Position + new Vector2(3, 2), new Vector2(20, 20)), _choices[i].Icon);
            Text(r.Position + new Vector2(28, 16), _choices[i].Name, 9, i == _choice ? Gold : Paper, 132);
        }
        var current = CurrentChoice();
        var candidate = _choices.Count == 0 ? null : _choices[_choice];
        DrawRect(new Rect2(386, 91, 228, 204), Ink); DrawRect(new Rect2(386, 91, 228, 204), Edge, false, 1);
        Text(new(399, 111), "当前绑定", 9, Muted);
        Icon(new Rect2(399, 120, 36, 36), current.Icon);
        Text(new(442, 135), current.Name, 10, Paper, 156);
        Text(new(399, 171), "候选动作", 9, Muted);
        Icon(new Rect2(399, 180, 36, 36), candidate?.Icon);
        Text(new(442, 195), candidate?.Name ?? "无可用候选", 10, Gold, 156);
        Text(new(399, 226), candidate?.Detail ?? "当前分类没有可绑定项目。", 8, Muted, 201);
        Text(new(399, 250), "施法/使用沿用现有校验", 8, Muted, 201);
        Text(new(399, 266), "A 确认替换 · B 保留原值", 8, Paper, 201);
        Text(new(19, 319), _message, 9, _message.StartsWith("保存失败", StringComparison.Ordinal) ? new Color("f0a28a") : Muted, 600);
    }

    private void DrawTimingSettings()
    {
        DrawRect(new Rect2(30, 86, 580, 194), Deep); DrawRect(new Rect2(30, 86, 580, 194), Edge, false, 1);
        Text(new(49, 116), "长按阈值", 12, _settingsRow == 0 ? Gold : Paper);
        Text(new(300, 116), $"{_draft.LongPressThresholdMs} ms", 13, Paper);
        Text(new(49, 155), "组合识别窗口", 12, _settingsRow == 1 ? Gold : Paper);
        Text(new(300, 155), $"{_draft.ChordWindowMs} ms", 13, Paper);
        Text(new(49, 201), "方向键选择项目，左右调整。组合窗口不能超过长按阈值。", 9, Muted, 530);
        Text(new(49, 225), "当前目标规则：右摇杆方向夹角优先，其次距离；此规则不可配置。", 9, Muted, 530);
        Text(new(49, 257), "长按 100–5000 ms · 组合 0–长按阈值", 9, Gold);
        Text(new(19, 319), _message, 9, _message.StartsWith("保存失败", StringComparison.Ordinal) ? new Color("f0a28a") : Muted, 600);
    }

    private void DrawFooter()
    {
        DrawRect(new Rect2(0, 330, 640, 30), Deep);
        string footer = _timingSettings ? "↑↓ 选择设置   ←→ 调整   B 返回"
            : _picker ? "方向选择   L1/R1 类别   A 确认替换   B 取消"
            : "方向选择   A 更改   X 清空   L3 时间设置   B 返回";
        Text(new(18, 350), footer, 10, Paper, 610);
    }
}

