using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Library;
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

    /// <summary>内嵌选项 [text:id] 的命中区 (供验收测试场核对选项与点击区域)。</summary>
    public IReadOnlyList<(Rect2 Rect, int Id)> ButtonAreas => _buttons;

    /// <summary>当前悬停的内嵌选项 id (-1 = 无)。选项点击与悬停共用同一命中区。</summary>
    public int HoveredButtonId => _hoveredButton;

    /// <summary>
    /// 控件自身的绘制宽度（&lt;=0 表示与换行宽度相同）。
    ///
    /// 为什么需要它：legacy 的菜单条 F1101 是 **383 宽、跨两列**（反汇编：取帧宽/高后
    /// SetRect(rect,0,0,w,h)），而正文换行宽度只有 149。若控件宽度 = 换行宽度，
    /// 菜单条会被控件自身裁到 149 —— 实测截图里只剩约 70px 宽的短条。
    /// 故把「换行宽度」与「控件宽度」分开：换行仍按 149，控件放宽到菜单条宽度。
    /// </summary>
    public int DrawWidth { get; set; }

    public void SetContent(string text, int width, int fontSize = 10, int linePitch = 18)
    {
        LinePitch = linePitch;
        _glyphs.Clear();
        _buttons.Clear();
        _hoveredButton = -1;
        Size = new Vector2I(DrawWidth > 0 ? DrawWidth : width, Math.Max(linePitch, (int)Size.Y));

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

    /// <summary>
    /// legacy EI：为每个内嵌选项行绘制原版菜单条。
    ///
    /// 依据（反汇编 0x43F040 + npc-window-render-evidence.json::paint_order）：
    ///   order 2 = 循环绘制 **F1101**（重复的菜单行），计数 [this+0x51C]，每项目标 Y 递增 18
    ///   order 3 = **末项用 F1102**，位于 [this+0x544] + 末项索引*18
    ///   order 1 = F1100 背景（由 NPCDialog 画）
    ///
    /// 我方此前**完全没有** F1101/F1102 的引用：legacy 下只把现代 F381 行移除、不画菜单条，
    /// 所以菜单行是"有可点区域但没有原版底图"。此开关补上这层底图。
    /// </summary>
    public bool LegacyMenuStrips { get; set; }

    protected override void DrawControl()
    {
        if (LegacyMenuStrips) DrawLegacyMenuStrips();
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

    /// <summary>
    /// 按原版规则给每个选项行铺菜单条：末项 F1102、其余 F1101。
    ///
    /// 宽度取**帧的可见尺寸**（F1101 = 383x18、F1102 = 384x44），不是选项命中区的宽度
    /// —— 原版是整行铺满（反汇编：取帧宽/高后 SetRect(rect,0,0,w,h) 再按 384 居中），
    /// 而选项命中区只覆盖文字本身。第一版按命中区宽度画，截图里菜单条只有约 70px 宽。
    ///
    /// 水平：以**文本列**为基准左对齐（原版居中常量 384 即菜单条自身宽，
    /// 我方文本列起点即 X，故直接用列起点）。
    /// 垂直：对齐该选项行的顶部（我方行距 21，原版菜单条高 18，逐行贴合）。
    /// </summary>
    private void DrawLegacyMenuStrips()
    {
        if (_buttons.Count == 0) return;
        for (int i = 0; i < _buttons.Count; i++)
        {
            int frame = i == _buttons.Count - 1 ? 1102 : 1101;
            var tex = MirSkin.GetTexture(LibraryFile.GameInter, frame);
            if (tex == null) continue;
            var size = MirSkin.GetSize(LibraryFile.GameInter, frame);
            var offset = MirSkin.GetOffset(LibraryFile.GameInter, frame);
            var rect = _buttons[i].Rect;
            // 水平：用选项命中区的 X 反推（命中区只覆盖文字，减去 offset 后可见区左缘落在
            //       文字起点附近）。**不要**用控件左缘：F1101 的 offset 是 (64,7)，
            //       直接用 -offset.X 会把条子左移 64px 而被裁掉（实测横条变少变淡）。
            var pos = new Vector2(rect.Position.X - offset.X, rect.Position.Y - offset.Y);
            DrawTexture(tex, pos, Colors.White);
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
