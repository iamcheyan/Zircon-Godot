using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>Standalone 640×360 handheld NPC dialogue sheet. The parent owns B/back and response routing.</summary>
public partial class HandheldNpcDialogue : Control
{
    private const float DesignWidth = 640, DesignHeight = 360;
    private const int OptionsPerPage = 5;
    private static readonly Color Ink = new("101215"), Deep = new("16191e"), Tile = new("1c2027"),
        Selected = new("30291a"), Paper = new("f5f0e1"), Muted = new("9ca3af"),
        Gold = new("e2bf68"), GoldBright = new("ffea9f"), Line = new("343943"), Disabled = new("62666d");

    private readonly Control _sheet = new();
    private readonly RichTextLabel _body = new();
    private readonly List<(int Id, string Label)> _options = new();
    private Font _font;
    private string _title = "";
    private string _pageBody = "";
    private Texture2D _portrait;
    private int _selectedIndex;
    private int _optionPage;
    private bool _pending;
    private int _drawRevision;

    public event Action<int> OptionSelected;

    public int SelectedIndex => _selectedIndex;
    public int OptionCount => _options.Count;
    public int OptionPage => _optionPage;
    public bool IsPending => _pending;
    public int DrawRevision => _drawRevision;

    public HandheldNpcDialogue()
    {
        Name = nameof(HandheldNpcDialogue);
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_sheet);
        _sheet.Name = "Sheet";
        _sheet.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _sheet.Size = new Vector2(DesignWidth, DesignHeight);
        _sheet.MouseFilter = MouseFilterEnum.Ignore;
        _sheet.Draw += DrawSheet;

        _font = MirSkin.GetFont() ?? ThemeDB.FallbackFont;
        _body.Name = "DialogueBody";
        _body.Position = new Vector2(26, 86);
        _body.Size = new Vector2(340, 210);
        _body.BbcodeEnabled = true;
        _body.ScrollActive = true;
        _body.FitContent = false;
        _body.SelectionEnabled = false;
        _body.MouseFilter = MouseFilterEnum.Ignore;
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.AddThemeFontOverride("normal_font", _font);
        _body.AddThemeFontSizeOverride("normal_font_size", MirSkin.ScaledSize(13));
        _body.AddThemeColorOverride("default_color", Paper);
        _body.AddThemeColorOverride("font_outline_color", new Color("08090b"));
        _body.AddThemeConstantOverride("outline_size", 1);
        _body.AddThemeConstantOverride("line_separation", 3);
        _sheet.AddChild(_body);

        Resized += Fit;
        Visible = false;
        SetProcess(false);
    }

    /// <summary>Replace the displayed page. A new page starts at its first option and clears pending state.</summary>
    public void SetPage(string title, string body, Texture2D portrait, List<(int id, string label)> options)
    {
        _title = title ?? "";
        _portrait = portrait;
        _options.Clear();
        if (options != null)
            foreach (var option in options)
                _options.Add((option.id, option.label ?? ""));
        _selectedIndex = 0;
        _optionPage = 0;
        _pending = false;
        _pageBody = SanitizeBbCode(body ?? "");
        RefreshBody();
        _sheet.QueueRedraw();
    }

    /// <summary>Set after an asynchronous request completes; pending pages cannot send duplicate choices.</summary>
    public void SetPending(bool pending)
    {
        if (_pending == pending) return;
        _pending = pending;
        _sheet.QueueRedraw();
    }

    /// <summary>Scroll the prose with the handheld right stick. Positive values scroll down.</summary>
    public void ScrollBody(int direction)
    {
        if (!Visible || direction == 0) return;
        var scrollBar = _body.GetVScrollBar();
        scrollBar.Value = Math.Clamp(scrollBar.Value + Math.Sign(direction) * 3, scrollBar.MinValue, scrollBar.MaxValue);
    }

    /// <summary>Route directional, confirm, and shoulder-page input. Returns false for parent-owned B/back.</summary>
    public bool HandleInput(Key key)
    {
        if (!Visible) return false;
        switch (key)
        {
            case Key.Up:
                MoveSelection(-1);
                return true;
            case Key.Down:
                MoveSelection(1);
                return true;
            case Key.L:
                PageOptions(-1);
                return true;
            case Key.R:
                PageOptions(1);
                return true;
            case Key.Enter:
            case Key.KpEnter:
            case Key.Space:
                ConfirmSelection();
                return true;
            case Key.Escape:
                return false;
            default:
                return false;
        }
    }

    public bool ConfirmSelection()
    {
        if (!Visible || _pending || _options.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _options.Count)
            return false;
        _pending = true;
        _sheet.QueueRedraw();
        OptionSelected?.Invoke(_options[_selectedIndex].Id);
        return true;
    }

    public void Open()
    {
        Visible = true;
        Fit();
        _sheet.QueueRedraw();
    }

    public void Close() => Visible = false;

    private void MoveSelection(int delta)
    {
        if (_pending || _options.Count == 0) return;
        int next = Math.Clamp(_selectedIndex + delta, 0, _options.Count - 1);
        if (next == _selectedIndex) return;
        _selectedIndex = next;
        _optionPage = next / OptionsPerPage;
        RefreshBody();
        _sheet.QueueRedraw();
    }

    /// <summary>Advance or retreat the selectable option page (L1 = -1, R1 = +1).</summary>
    public void PageOptions(int direction)
    {
        if (_pending || direction == 0 || _options.Count <= OptionsPerPage) return;
        int delta = Math.Sign(direction);
        int pageCount = (_options.Count + OptionsPerPage - 1) / OptionsPerPage;
        _optionPage = Math.Clamp(_optionPage + delta, 0, pageCount - 1);
        int first = _optionPage * OptionsPerPage;
        int end = Math.Min(first + OptionsPerPage, _options.Count) - 1;
        _selectedIndex = Math.Clamp(_selectedIndex, first, end);
        RefreshBody();
        _sheet.QueueRedraw();
    }

    private void RefreshBody()
    {
        string fullOption = _options.Count > _selectedIndex
            ? _options[_selectedIndex].Label.Replace("[", "[lb]").Replace("]", "[rb]").Replace("[lb[rb]", "[lb]") : "";
        // The list can ellipsize, but the complete response (including any price)
        // is always readable in the scrollable prose before the player confirms.
        _body.Text = _pageBody + (fullOption.Length == 0 ? "" : "\n\n[color=#e2bf68]当前回应[/color]\n" + fullOption);
        _body.GetVScrollBar().Value = 0;
    }

    private void Fit()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        float scale = Math.Min(Size.X / DesignWidth, Size.Y / DesignHeight);
        _sheet.Scale = Vector2.One * scale;
        _sheet.Position = (Size - _sheet.Size * scale) / 2;
    }

    private string FitTitle(string text, float width)
    {
        if (_font == null) return text;
        int size = MirSkin.ScaledSize(18);
        string result = text;
        while (result.Length > 1 && _font.GetStringSize(result, fontSize: size).X > width)
            result = result[..^1];
        return result == text ? result : result + "…";
    }

    private void Text(float x, float y, string text, int size, Color color, float width)
    {
        if (_font == null || string.IsNullOrEmpty(text)) return;
        _sheet.DrawString(_font, new Vector2(Mathf.Round(x), Mathf.Round(y)), text,
            HorizontalAlignment.Left, width, MirSkin.ScaledSize(size), color);
    }

    private void Panel(Rect2 rect, Color fill)
    {
        _sheet.DrawRect(rect, fill);
        _sheet.DrawRect(rect, Line, false, 1);
        _sheet.DrawRect(new Rect2(rect.Position + Vector2.One, rect.Size - Vector2.One * 2), new Color("272c34"), false, 1);
    }

    private void DrawSheet()
    {
        _drawRevision++;
        _sheet.DrawRect(new Rect2(0, 0, DesignWidth, DesignHeight), Ink);
        _sheet.DrawRect(new Rect2(0, 0, DesignWidth, 3), Gold);
        _sheet.DrawRect(new Rect2(0, 3, DesignWidth, 58), Deep);
        _sheet.DrawRect(new Rect2(0, 61, DesignWidth, 1), Line);

        if (_portrait != null)
        {
            var box = new Rect2(16, 9, 46, 46);
            Panel(box, Tile);
            Vector2 textureSize = _portrait.GetSize();
            if (textureSize.X > 0 && textureSize.Y > 0)
            {
                Vector2 drawSize = textureSize * Math.Min(40f / textureSize.X, 40f / textureSize.Y);
                _sheet.DrawTextureRect(_portrait, new Rect2(box.Position + (box.Size - drawSize) / 2, drawSize), false);
            }
        }
        else
        {
            Panel(new Rect2(16, 9, 46, 46), Tile);
            Text(22, 37, "NPC", 8, Muted, 34);
        }

        Text(76, 29, FitTitle(string.IsNullOrWhiteSpace(_title) ? "旅人" : _title, 420), 18, GoldBright, 420);
        Text(77, 47, "NPC 对话", 10, Muted, 140);
        Text(546, 29, "手记 · 交谈", 10, Muted, 82);

        Panel(new Rect2(16, 72, 360, 238), Deep);
        Panel(new Rect2(386, 72, 238, 238), Deep);
        _sheet.DrawRect(new Rect2(26, 78, 340, 1), new Color("3a3323"));
        Text(26, 75, "对话", 10, Gold, 48);
        Text(396, 75, "回应", 10, Gold, 48);

        int start = _optionPage * OptionsPerPage;
        int count = Math.Min(OptionsPerPage, Math.Max(0, _options.Count - start));
        const float rowY = 84, rowH = 39, gap = 3;
        for (int i = 0; i < count; i++)
        {
            int optionIndex = start + i;
            bool focused = optionIndex == _selectedIndex;
            var row = new Rect2(395, rowY + i * (rowH + gap), 220, rowH);
            _sheet.DrawRect(row, focused ? Selected : Tile);
            _sheet.DrawRect(row, focused ? GoldBright : Line, false, 1);
            if (focused) _sheet.DrawRect(new Rect2(row.Position.X, row.Position.Y + 4, 3, row.Size.Y - 8), Gold);
            string prefix = focused ? "›" : "·";
            Text(row.Position.X + 9, row.Position.Y + 18, prefix, 13, focused ? GoldBright : Muted, 14);
            _sheet.DrawMultilineString(_font,
                new Vector2(row.Position.X + 26, row.Position.Y + 15), FitOptionLabel(_options[optionIndex].Label, 184),
                HorizontalAlignment.Left, 184, MirSkin.ScaledSize(11), 2,
                _pending ? Disabled : focused ? Paper : Muted);
        }

        if (_options.Count == 0)
            Text(401, 111, "此刻没有可回应的话。", 11, Muted, 194);
        else if (count == 0)
            Text(401, 111, "等待回应…", 11, Muted, 194);

        int pageCount = Math.Max(1, (_options.Count + OptionsPerPage - 1) / OptionsPerPage);
        Text(397, 302, pageCount > 1 ? $"L1 ‹   {_optionPage + 1} / {pageCount}   › R1" : $"{_options.Count} 个回应", 9, Muted, 210);
        if (_pending)
            Text(397, 288, "正在等待回应…", 9, Gold, 205);

        _sheet.DrawRect(new Rect2(0, 322, DesignWidth, 38), Deep);
        _sheet.DrawRect(new Rect2(0, 322, DesignWidth, 1), Line);
        FooterHint(18, "↑↓", "选择");
        FooterHint(116, "A", _pending ? "请稍候" : "回应");
        FooterHint(216, "B", "返回");
        Text(324, 345, "右摇杆 · 滚动对话", 10, Paper, 190);
        Text(532, 346, "选择 · 继续", 10, Gold, 96);
        if (_pending)
            _sheet.DrawRect(new Rect2(0, 0, DesignWidth, DesignHeight), new Color(0.02f, 0.025f, 0.035f, .16f));
    }

    private string FitOptionLabel(string text, float width)
    {
        if (_font == null || string.IsNullOrEmpty(text)) return text ?? "";
        int fontSize = MirSkin.ScaledSize(11);
        int line = 1;
        var output = new StringBuilder();
        var currentLine = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character == '\n')
            {
                if (line == 2) return output.Append(currentLine).Append("…").ToString();
                output.Append(currentLine).Append('\n');
                currentLine.Clear();
                line++;
                continue;
            }
            string candidate = currentLine.ToString() + character;
            if (currentLine.Length > 0 && _font.GetStringSize(candidate, fontSize: fontSize).X > width)
            {
                if (line == 2)
                {
                    while (currentLine.Length > 0 && _font.GetStringSize(currentLine.ToString() + "…", fontSize: fontSize).X > width)
                        currentLine.Length--;
                    return output.Append(currentLine).Append("…").ToString();
                }
                output.Append(currentLine).Append('\n');
                currentLine.Clear();
                line++;
            }
            currentLine.Append(character);
        }
        return output.Append(currentLine).ToString();
    }

    private void FooterHint(float x, string button, string label)
    {
        _sheet.DrawRect(new Rect2(x, 332, 25, 17), new Color("252b36"));
        _sheet.DrawRect(new Rect2(x, 332, 25, 17), new Color("5a5038"), false, 1);
        Text(x + 3, 344, button, 9, GoldBright, 21);
        Text(x + 31, 345, label, 10, Paper, 72);
    }

    private static string SanitizeBbCode(string source)
    {
        // Keep only inert formatting tags. Links, images, custom effects and unknown tags are rendered as text.
        var output = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length;)
        {
            if (source[i] != '[')
            {
                output.Append(source[i++]);
                continue;
            }
            int end = source.IndexOf(']', i + 1);
            if (end < 0)
            {
                output.Append("[lb]");
                i++;
                continue;
            }
            string tag = source.Substring(i + 1, end - i - 1).Trim();
            if (IsSafeTag(tag)) output.Append('[').Append(tag).Append(']');
            else output.Append("[lb]").Append(tag.Replace("[", "[lb]", StringComparison.Ordinal)).Append("[rb]");
            i = end + 1;
        }
        return output.ToString();
    }

    private static bool IsSafeTag(string tag)
    {
        string lower = tag.ToLowerInvariant();
        if (lower is "b" or "/b" or "i" or "/i" or "u" or "/u" or "s" or "/s"
            or "code" or "/code" or "center" or "/center" or "left" or "/left"
            or "right" or "/right" or "fill" or "/fill" or "p" or "/p" or "br" or "lb" or "rb")
            return true;
        if (lower is "color" or "/color" or "font_size" or "/font_size") return true;
        if (lower.StartsWith("color=#", StringComparison.Ordinal) && (lower.Length == 10 || lower.Length == 13))
        {
            for (int i = 7; i < lower.Length; i++)
                if (!Uri.IsHexDigit(lower[i])) return false;
            return true;
        }
        if (lower.StartsWith("font_size=", StringComparison.Ordinal)
            && int.TryParse(lower.AsSpan(10), out int size)) return size is >= 8 and <= 20;
        return false;
    }
}
