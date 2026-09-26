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
    /// <summary>只画菜单条、不画文字（外置菜单条层用；置位可避免重复渲染一份文本）。</summary>
    public bool StripsOnly { get; set; }

    protected override void DrawControl()
    {
        if (LegacyMenuStrips) DrawLegacyMenuStrips();
        if (StripsOnly) return;   // 纯菜单条层：不重复绘制文字（否则会多出一份文本）
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
    /// 两个实测坑（见审计文档「两次失败定位」）：
    /// 1. **不能用 MirSkin.GetOffset** —— 它返回 WIL **header offset**（F1101 是 (7,-44)），
    ///    而美术可见区实际从画布 **(64,7)** 开始（alpha bbox 实测）。
    ///    用 header offset 会把条子画到偏低约 95px 的位置。
    ///    故这里用实测的 alpha bbox 原点。
    /// 2. 控件宽度必须是菜单条宽度（383），否则被父容器裁掉。
    /// </summary>
    private static readonly Vector2I Frame1101VisibleOrigin = new(64, 7);
    private static readonly Vector2I Frame1102VisibleOrigin = new(64, 10);

    private void DrawLegacyMenuStrips()
    {
        var rows = MenuRows;
        if (rows.Count == 0) return;
        for (int i = 0; i < rows.Count; i++)
        {
            int frame = i == rows.Count - 1 ? 1102 : 1101;
            var tex = MirSkin.GetTexture(LibraryFile.GameInter, frame);
            if (tex == null) continue;
            // alpha bbox 原点：把可见区左上角对齐到该行命中区的左上角。
            var origin = frame == 1102 ? Frame1102VisibleOrigin : Frame1101VisibleOrigin;
            var rect = rows[i].Rect;
            var pos = new Vector2(rect.Position.X - origin.X, rect.Position.Y - origin.Y);
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
