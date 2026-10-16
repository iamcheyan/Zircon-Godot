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

    /// <summary>
    /// 每行起始缩进 (px)。legacy 有头像时把正文整体推到头像右侧（参考头像帧可见区
    /// 右缘 161，正文列起点取 167），使所有行左边界一致、不再被头像压住；
    /// 也让"文本区"与"换行宽度"解耦：文本区横跨面板内宽，正文靠缩进让位。
    /// </summary>
    public int LeftIndent { get; set; }

    public void SetContent(string text, int width, int fontSize = 10, int linePitch = 18)
    {
        LinePitch = linePitch;
        _glyphs.Clear();
        _buttons.Clear();
        _hoveredButton = -1;
        Size = new Vector2I(DrawWidth > 0 ? DrawWidth : width, Math.Max(linePitch, (int)Size.Y));

        // ID 必须限定为整数：NPC 正文里的链接文字可能自带冒号（例如"移动至比奇城所需金钱 : 500 钱"），
        // 用 (?<ID>.+?) 会把第一个冒号后面的内容当成 ID，导致 int.TryParse 失败、选项不可点。
        var matches = Regex.Matches(text ?? string.Empty, @"\[(?<Text>.*?):(?<ID>-?\d+)\]|\{(?<Text>.*?):(?<Colour>.+?)\}");
        int cursor = 0;
        float x = LeftIndent, y = 0;
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
        // 宽度用 DrawWidth（>0 时）—— 否则会把第 57 行设的宽度覆盖回换行宽度，
        // 菜单条就被裁到 149（实测诊断 ctrl=(149,504) 即由此而来）。
        Size = new Vector2I(DrawWidth > 0 ? DrawWidth : width, ContentHeight);
        QueueRedraw();
    }

    private void AddPlain(string text, ref float x, ref float y, int width, int fontSize, float lineHeight)
        => AddStyled(text, Colors.White, -1, ref x, ref y, width, fontSize, lineHeight);

    private void AddStyled(string text, Color colour, int buttonId, ref float x, ref float y,
        int width, int fontSize, float lineHeight)
    {
        foreach (char character in text ?? string.Empty)
        {
            if (character == '\n') { x = LeftIndent; y += lineHeight; continue; }
            string glyph = character.ToString();
            float glyphWidth = MirSkin.MeasureText(glyph, fontSize).X;
            if (x > LeftIndent && x + glyphWidth > width) { x = LeftIndent; y += lineHeight; }
            var hit = new Rect2(x, y, Mathf.Max(1, glyphWidth), lineHeight);
            _glyphs.Add(new Glyph(glyph, new Vector2(x, y + MirSkin.ScaledSize(fontSize)), colour, buttonId, hit, MirSkin.ScaledSize(fontSize)));
            if (buttonId >= 0) _buttons.Add((hit, buttonId));
            x += glyphWidth;
        }
    }

    // 2026-10-16 修正：此前以为 F1101 是"菜单条底图"，想给每个内嵌选项行画一条。
    // 独立解码 ~/mir2ei/LegacyEI/Data/GameInter.wil 后确认：F1101 = 512x32、不透明区
    // (64,7)-(447,25) = 383x18，内容就是**面板中段的黑色身体 + 左右两道木栏**（与 F1100
    // 身体两侧同款），即窗口纵向平铺用的中段，不是装饰性菜单底图。画在选项行背后是
    // 黑压黑、看不见 —— 该思路作废，选项行就是面板黑底上的文字行。
    // 真正的窗口结构：F1100(138) + N×F1101(18) + F1102(44，末行带 ✕)，见 NPCDialog。

    /// <summary>诊断用：返回前若干选项行的命中区摘要（确认菜单条 Y 是否与行对齐）。</summary>
    public string DebugRowSummary()
    {
        var sb = new System.Text.StringBuilder();
        var rows = MenuRows;
        for (int i = 0; i < rows.Count && i < 3; i++)
        {
            var r = rows[i].Rect;
            sb.Append($"[{rows[i].Id}]@({r.Position.X},{r.Position.Y})+{r.Size.X}x{r.Size.Y} ");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 菜单条的行 = **按选项 Id 合并**后的命中区。
    ///
    /// 注意 `_buttons` 是**逐字符**收集的（AddPlain 里每写一个字符就 Add 一次），
    /// 直接遍历它会给每个字形画一条菜单条 —— 实测 50 条堆叠、且行 Y 全在同一行。
    /// 这里把同一 Id 的字符合并成一个横向并集矩形（Y 取该选项首字符所在行）。
    /// </summary>
    public IReadOnlyList<(Rect2 Rect, int Id)> MenuRows
    {
        get
        {
            var rows = new List<(Rect2 Rect, int Id)>();
            var seen = new Dictionary<int, int>();   // Id -> rows 下标
            foreach (var (rect, id) in _buttons)
            {
                if (seen.TryGetValue(id, out int index))
                {
                    var existing = rows[index].Rect;
                    var merged = existing.Merge(rect);
                    rows[index] = (merged, id);
                }
                else
                {
                    seen[id] = rows.Count;
                    rows.Add((rect, id));
                }
            }
            return rows;
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
            if (glyph.ButtonId >= 0)
            {
                // 原版 FState.pas:6960 下划线对齐
                float y = glyph.Position.Y + 2;
                DrawLine(new Vector2(glyph.HitBox.Position.X, y), new Vector2(glyph.HitBox.Position.X + glyph.HitBox.Size.X, y), colour, 1.0f);
            }
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
