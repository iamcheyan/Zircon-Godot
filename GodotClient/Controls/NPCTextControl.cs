using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>NPC 对话中的原版 DrawTextExtensions 文本：颜色和 [按钮:id] 保持在同一行。</summary>
public sealed partial class NPCTextControl : DXControl
{
    private sealed record Glyph(string Text, Vector2 Position, Color Colour, int ButtonId, Rect2 HitBox, int FontSize);
    private readonly List<Glyph> _glyphs = new();
    private readonly List<(Rect2 Rect, int Id)> _buttons = new();
    private int _hoveredButton = -1;

    public int ContentHeight { get; private set; }

    public NPCTextControl()
    {
        MouseFilter = MouseFilterEnum.Stop;
        IsControl = true;
    }

    /// <summary>
    /// 行距 (px)。现代 NPC 默认 18；旧版 EI 正文按 primary-static 证据
    /// (0x43F460 行距 = textheight+5，默认 0x594=21) 传 21。
    /// </summary>
    public int LinePitch { get; set; } = 18;

    /// <summary>按当前行距折算的换行行数 (供行级滚动计算上下界)。</summary>
    public int LineCount { get; private set; } = 1;

    public void SetContent(string text, int width, int fontSize = 10, int linePitch = 18)
    {
        LinePitch = linePitch;
        _glyphs.Clear();
        _buttons.Clear();
        _hoveredButton = -1;
        Size = new Vector2I(width, Math.Max(linePitch, (int)Size.Y));

        var matches = Regex.Matches(text ?? string.Empty, @"\[(?<Text>.*?):(?<ID>.+?)\]|\{(?<Text>.*?):(?<Colour>.+?)\}");
        int cursor = 0;
        float x = 0, y = 0;
        float lineHeight = linePitch;
        foreach (Match match in matches)
        {
            AddPlain(text?.Substring(cursor, match.Index - cursor) ?? string.Empty, ref x, ref y, width, fontSize, lineHeight);
            string value = match.Groups["Text"].Value;
            int id = -1;
            int.TryParse(match.Groups["ID"].Value, out id);
            Color colour = match.Groups["Colour"].Success ? ParseColour(match.Groups["Colour"].Value) : new Color(1f, .85f, .25f);
            AddStyled(value, colour, id, ref x, ref y, width, fontSize, lineHeight);
            cursor = match.Index + match.Length;
        }
        AddPlain(text?.Substring(cursor) ?? string.Empty, ref x, ref y, width, fontSize, lineHeight);
        ContentHeight = Math.Max((int)lineHeight, (int)y + (x > 0 ? (int)lineHeight : 0));
        LineCount = Math.Max(1, ContentHeight / linePitch);
        Size = new Vector2I(width, ContentHeight);
        QueueRedraw();
    }

    private void AddPlain(string text, ref float x, ref float y, int width, int fontSize, float lineHeight)
        => AddStyled(text, Colors.White, -1, ref x, ref y, width, fontSize, lineHeight);

    private void AddStyled(string text, Color colour, int buttonId, ref float x, ref float y,
        int width, int fontSize, float lineHeight)
    {
        foreach (char character in text ?? string.Empty)
        {
            if (character == '\n') { x = 0; y += lineHeight; continue; }
            string glyph = character.ToString();
            float glyphWidth = MirSkin.MeasureText(glyph, fontSize).X;
            if (x > 0 && x + glyphWidth > width) { x = 0; y += lineHeight; }
            var hit = new Rect2(x, y, Mathf.Max(1, glyphWidth), lineHeight);
            _glyphs.Add(new Glyph(glyph, new Vector2(x, y + MirSkin.ScaledSize(fontSize)), colour, buttonId, hit, MirSkin.ScaledSize(fontSize)));
            if (buttonId >= 0) _buttons.Add((hit, buttonId));
            x += glyphWidth;
        }
    }

    protected override void DrawControl()
    {
        var font = MirSkin.GetFont();
        if (font == null) return;
        foreach (var glyph in _glyphs)
        {
            Color colour = glyph.ButtonId >= 0 && glyph.ButtonId == _hoveredButton
                ? Colors.Red
                : glyph.Colour;
            DrawString(font, glyph.Position, glyph.Text, HorizontalAlignment.Left, -1, glyph.FontSize, colour);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!IsEnabled)
        {
            if (@event is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            int hovered = -1;
            foreach (var button in _buttons)
            {
                if (button.Rect.HasPoint(motion.Position))
                {
                    hovered = button.Id;
                    break;
                }
            }
            if (_hoveredButton != hovered)
            {
                _hoveredButton = hovered;
                QueueRedraw();
            }
            return;
        }
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            foreach (var button in _buttons)
            {
                if (!button.Rect.HasPoint(mouse.Position)) continue;
                if (button.Id == 0) GameScene.Game?.CloseNPCDialog();
                else GameScene.Game?.SendNPCButton(button.Id);
                AcceptEvent();
                return;
            }
        }
        base._GuiInput(@event);
    }

    private static Color ParseColour(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Color(1f, .85f, .25f);
        try
        {
            return value.ToLowerInvariant() switch
            {
            "red" => Colors.Red,
            "green" => Colors.Green,
            "blue" => Colors.CornflowerBlue,
            "yellow" => Colors.Yellow,
            "orange" => new Color(1f, .55f, .1f),
            "white" => Colors.White,
            _ => Color.FromHtml(value.StartsWith("#") ? value : "#" + value),
            };
        }
        catch
        {
            return new Color(1f, .85f, .25f);
        }
    }
}
