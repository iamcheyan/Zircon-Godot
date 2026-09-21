using System.Collections.Generic;
using Godot;

namespace ZirconClient.Controls;

/// <summary>
/// 文本标签 (移植自 Client/Controls/DXLabel.cs)。
/// 用 MirSkin 的中文字体绘制; 支持对齐/描边/阴影/自动尺寸。
/// </summary>
public partial class DXLabel : DXControl
{
    public int FontSize = 12;

    private Color _textColour = Colors.White;
    public Color TextColour
    {
        get => _textColour;
        set { _textColour = value; QueueRedraw(); }
    }

    public bool DrawOutline;
    public Color OutlineColour = Colors.Black;
    public bool DrawShadow;

    /// <summary>在文字底部画一条下划线（旧版物品链接 FontStyle.Underline 的移植）。</summary>
    public bool DrawUnderline;

    public HorizontalAlignment Align = HorizontalAlignment.Left;
    public VerticalAlignment VAlign = VerticalAlignment.Top;

    /// <summary>文字绘制内边距（悬浮提示框等带背景的标签用，避免文字贴边）。</summary>
    public Vector2I TextPadding;

    /// <summary>true: 尺寸跟随文字大小 (旧 DXLabel 默认)</summary>
    public bool AutoSize = true;

    protected override void DrawControl()
    {
        if (string.IsNullOrEmpty(Text)) return;
        var font = MirSkin.GetFont();
        if (font == null) return;

        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        if (canvasScale < 0.01f) canvasScale = 1f;
        var lines = GetLines(Size.X * canvasScale);
        Vector2 textSize = MirSkin.MeasureTextPhysical(lines.Count == 0 ? string.Empty : lines[0], FontSize);
        Vector2 pos = (Vector2)TextPadding * canvasScale;

        float physicalWidth = Size.X * canvasScale;
        float physicalHeight = Size.Y * canvasScale;
        if (Align == HorizontalAlignment.Center) pos.X = TextPadding.X * canvasScale + (physicalWidth - textSize.X - TextPadding.X * canvasScale * 2) / 2f;
        else if (Align == HorizontalAlignment.Right) pos.X = physicalWidth - textSize.X - TextPadding.X * canvasScale;
        float lineHeight = textSize.Y;
        // GetStringSize().Y 是 Godot 实际使用的行高，垂直布局和逐行绘制
        // 必须使用同一个口径。之前这里改用 ascent+descent 计算 blockHeight，
        // 但逐行仍使用 lineHeight，导致按钮/标签的居中基线不一致。
        float ascent = font.GetAscent(MirSkin.PhysicalSize(FontSize));
        float blockHeight = lineHeight * lines.Count;
        if (VAlign == VerticalAlignment.Center) pos.Y = TextPadding.Y * canvasScale + (physicalHeight - blockHeight - TextPadding.Y * canvasScale * 2) / 2f;
        else if (VAlign == VerticalAlignment.Bottom) pos.Y = physicalHeight - blockHeight - TextPadding.Y * canvasScale;

        Color colour = IsEnabled ? TextColour : new Color(TextColour, 0.5f);

        // Godot DrawString 的 Y 是基线 (baseline)，旧版 GDI TextRenderer.DrawText
        // 的 Y 是文本顶部。不补偿会让所有文字整体上移约一个 ascent（升部），
        // 表现为文本偏高。这里把基线 Y 下移 ascent，使视觉位置与旧版一致。
        // （ascent 已在上面居中计算时定义）

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One / canvasScale);
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
            // 旧版 DXLabel 的文字贴图最终落在整数像素上。保留小数位置会
            // 让 CJK 字形经过半像素采样，尤其在低分辨率窗口中看起来发糊。
                Vector2 linePos = new(Mathf.Round(pos.X), Mathf.Round(pos.Y + i * lineHeight + ascent));
                int drawSize = MirSkin.PhysicalSize(FontSize);
            if (DrawOutline)
                // 原版是四次相邻 1px 偏移绘制，不是 Godot 的 4px 外扩描边。
                // 外扩 4px 会吞掉小字号笔画并造成截图中的重影/模糊。
                DrawStringOutline(font, linePos, lines[i], HorizontalAlignment.Left, -1, drawSize, 1, OutlineColour);
            else if (DrawShadow)
                DrawStringOutline(font, linePos + new Vector2(1, 1), lines[i], HorizontalAlignment.Left, -1, drawSize, 1, new Color(0, 0, 0, 0.7f));
            DrawString(font, linePos, lines[i], HorizontalAlignment.Left, -1, drawSize, colour);
                if (DrawUnderline)
                    DrawLine(new Vector2(linePos.X, linePos.Y + drawSize + 1),
                        new Vector2(linePos.X + textSize.X, linePos.Y + drawSize + 1), colour, 1f);
            }
        }
        finally { DrawSetTransform(Vector2.Zero, 0f, Vector2.One); }
    }

    private List<string> GetLines(float availableWidth)
    {
        var result = new List<string>();
        foreach (var source in (Text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            if (AutoSize || Size.X <= 0)
            {
                result.Add(source);
                continue;
            }
            string line = string.Empty;
            foreach (char ch in source)
            {
                string candidate = line + ch;
                if (line.Length > 0 && MirSkin.MeasureTextPhysical(candidate, FontSize).X > availableWidth)
                {
                    result.Add(line);
                    line = ch.ToString();
                }
                else line = candidate;
            }
            result.Add(line);
        }
        return result.Count == 0 ? new List<string> { string.Empty } : result;
    }

    public override void _Draw()
    {
        // 标签通常无背景, 直接画文字; 保留基类背景能力但跳过自身背景绘制顺序
        base._Draw();
    }

    protected override void OnControlAdded(DXControl c) { }
}
