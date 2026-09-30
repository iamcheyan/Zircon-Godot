using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// EI 原版「公告 / 欢迎」对话框（`GameInter` **F0**，324×462）。
///
/// 用户提供的原版素材定位：GameInter.wil frame #0 = 324×462 的公告板，
/// 深色文本区 + **底部中央一个对勾**；点对勾关闭对话框。
/// 本控件按该素材复刻：整幅 F0 作背景，欢迎文案居中显示在深色文本区，
/// 底部对勾区域作为确认点击区。
/// </summary>
public partial class LegacyEiNoticeDialog : DXWindow
{
    public const int FrameWidth = 324;
    public const int FrameHeight = 462;
    /// <summary>
    /// 在**逻辑画布**（UiScaler.BaseWidth×BaseHeight = 800×600，即原版 mode 3 屏幕区）内居中。
    ///
    /// 证据：原版模态提示框在构造时**居中**（`source-vs-reverse/client.md` §6.2
    /// 「DMessageDlg 按 DialogSize 选背景帧……并居中」）；UiScaler 会把整块逻辑画布
    /// 缩放+居中到真实视口，因此本框只需按逻辑画布居中，任意缩放下都落在屏幕中心。
    ///
    /// 修正：此前按 640×480 算得 (158,9)，而本框在 `ApplyLegacyPregameWindow(800,600)`
    /// **之后**才显示，画布已是 800×600，于是整体偏左上 (80,60)（见复核报告 F2）。
    /// </summary>
    public static readonly Vector2I DefaultLocation = new(
        (int)((UiScaler.BaseWidth - FrameWidth) / 2),
        (int)((UiScaler.BaseHeight - FrameHeight) / 2));

    /// <summary>深色文本区（实测约 (25,40)-(300,425)）。</summary>
    public static readonly Vector2I TextOrigin = new(30, 50);
    public static readonly Vector2I TextSize = new(264, 330);
    public static readonly Color TextColour = new(0xFF / 255f, 0xFA / 255f, 0xC8 / 255f);
    public const int LineHeight = 18;

    /// <summary>底部对勾（确认）被点击。</summary>
    public event System.Action Confirmed;

    private readonly List<DXLabel> _lines = new();
    private string _notice = string.Empty;

    public LegacyEiNoticeDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = false;
        Size = new Vector2I(FrameWidth, FrameHeight);
        Location = DefaultLocation;

        AddControl(new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 0,
            FixedSize = true,
            Size = new Vector2I(FrameWidth, FrameHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // 底部对勾（F0 已烘焙）作为确认点击区：实测约 (145,390)-(200,450)。
        var confirm = new DXControl
        {
            Location = new Vector2I(130, 385),
            Size = new Vector2I(85, 75),
        };
        confirm.MouseClick += (o, e) => Confirmed?.Invoke();
        AddControl(confirm);
    }

    public void SetNotice(string text)
    {
        _notice = text ?? string.Empty;
        foreach (var line in _lines)
        {
            RemoveControl(line);
            line.QueueFree();
        }
        _lines.Clear();

        var wrapped = LegacyEiDialogText.Wrap(_notice, TextSize.X, 12);
        int total = wrapped.Count * LineHeight;
        int y0 = TextOrigin.Y + Mathf.Max(0, (TextSize.Y - total) / 2);
        for (int i = 0; i < wrapped.Count; i++)
        {
            var label = new DXLabel
            {
                Location = new Vector2I(TextOrigin.X, y0 + i * LineHeight),
                Size = new Vector2I(TextSize.X, LineHeight),
                FontSize = 12,
                TextColour = TextColour,
                Align = HorizontalAlignment.Center,
                AutoSize = false,
                IsControl = false,
                Text = wrapped[i],
            };
            AddControl(label);
            _lines.Add(label);
        }
    }
}
