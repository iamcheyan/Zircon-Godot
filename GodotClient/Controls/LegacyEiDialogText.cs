using System.Collections.Generic;
using System.Text;

namespace ZirconClient.Controls;

/// <summary>
/// EI 对话框文案折行工具（按像素宽度）。
/// 用 <see cref="MirSkin.MeasureText"/> 的**逻辑**宽度与逻辑宽度比较，
/// 因此与 <c>UiScaler</c> 缩放无关；等价于原版 <c>StringDivide</c>。
/// </summary>
public static class LegacyEiDialogText
{
    public static List<string> Wrap(string text, int maxWidth, int fontSize)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) { result.Add(string.Empty); return result; }

        foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(string.Empty); continue; }
            var line = new StringBuilder();
            foreach (char ch in paragraph)
            {
                if (line.Length > 0
                    && MirSkin.MeasureText(line.ToString() + ch, fontSize).X > maxWidth)
                {
                    result.Add(line.ToString());
                    line.Clear();
                }
                line.Append(ch);
            }
            result.Add(line.ToString());
        }
        return result;
    }
}
