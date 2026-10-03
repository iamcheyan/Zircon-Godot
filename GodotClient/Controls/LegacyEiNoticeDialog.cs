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
    /// 修正：此前按 640×480 算得 (158,9)，而本框显示时所在的层是按 800×600 画布
    /// （`UiScaler.UpdateScale`，与 `GameScene` 同一套基准）缩放的，于是整体偏左上 (80,60)
    /// （见复核报告 F2）。窗口尺寸现在只由用户决定、不再由 `ApplyLegacyPregameWindow`
    /// 在进游戏前切到 800×600，但本层仍用 800×600 基准并把画布居中到窗口，
    /// 所以本框在任何窗口尺寸下都落在**窗口中心**。
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
        // **不提供 Zircon 的关闭 ✕**：该按钮由 DXWindow 用现代 Interface[15] 帧绘制，
        // 是移植版外壳控件，EI 原版预游戏对话框族没有它 ——
        //   • 本框素材 GameInter F0 只有底部中央一个「对勾」（本文件头注释 / 用户素材定位）；
        //   • 同族确认框 0x418030（GameInter F950）的绘制 0x4182A0 也只画背景+标题+自身按钮，
        //     无标题栏关闭控件（RESEARCH_LOG Finding 82 / Round 5756）。
        // 关闭 ✕ 只会把对话框隐藏而无人接手 → `_uiLayer` 仍隐藏，客户端永久黑屏、无法进入
        // 游戏（复核报告 F1）。去掉它即恢复「只能点对勾确认」的原版语义。
        ShowCloseButton = false;
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
