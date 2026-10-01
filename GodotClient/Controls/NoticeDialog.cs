using Godot;
using Library;

namespace ZirconClient.Controls;

/// <summary>
/// 旧版 EI 公告/提示窗口（GameInter F602）。
/// F602 同时被旧版系统公告横幅复用；这里实现的是 id=15 的独立公告板窗口。
/// </summary>
public partial class NoticeDialog : DXWindow
{
    private readonly DXImageControl _background;
    private readonly DXButton _closeButton;
    private readonly DXButton _actionButton;
    private readonly DXTextArea _text;

    /// <summary>勾选（F606）被点击时触发，随后窗口关闭。</summary>
    public event System.Action Confirmed;
    /// <summary>关闭（X，F161/162）被点击时触发，随后窗口关闭。</summary>
    public event System.Action Cancelled;

    public NoticeDialog()
    {
        HasTitle = false;
        HasFooter = false;
        Movable = false;
        Size = new Vector2I(584, 252);
        // 原版 F602 公告窗 (id15) 固定位置 (107,110)：主初始化 0x427960 传参
        // (GameInter, 602, x=107, y=110, 584, 252)，800x600 父原点 primary-static。
        Location = new Vector2I(107, 110);

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 602,
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 602),
            // 与 InventoryDialog/CharacterDialog/GroupDialog/QuestDialog/MagicDialog/ConfigDialog
            // 相同的约定：把该帧 alpha 可见区原点对齐到窗口 (0,0)。
            // 素材实测 F602 画布 1024x256、alpha bbox (220,2)-(802,253)，故取 -(220,2)。
            //
            // 证据本身自相矛盾（notice-prompt-window-evidence.json 的 composite_rule 称
            // \"painted at the window rect origin\"，与其余 5 窗的 -bboxOrigin 约定冲突），
            // 故改用**运行截图**裁定：用 (0,0) 时外框整体右偏 220px，
            // 文本区（x=23，证据 23/94）被甩在框外，明显不符原版 —— 截图决定性支持 -bboxOrigin。
            Location = new Vector2I(-220, -2),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,        // 原版关闭钮实参 (arg2,arg3,arg8) = (161,162,-1)、arg9=0：
        // 普通态与悬停态都不画帧（✕ 美术已烘焙进该窗口背景帧，见报告 §10/§10.1 的模板搜索证据），
        // 只有按下态画 arg3=162。

            Index = -1,
            HoverIndex = -1,
            PressedIndex = 162,
            Location = new Vector2I(548, 16),
            Size = new Vector2I(28, 26),
        };
        _closeButton.MouseClick += (o, e) => { Cancelled?.Invoke(); WindowManager.Close(this); };
        AddControl(_closeButton);

        _actionButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 606,
            HoverIndex = 607,
            PressedIndex = 607,
            Location = new Vector2I(496, 27),
            Size = new Vector2I(40, 20),
        };
        _actionButton.MouseClick += (o, e) => { Confirmed?.Invoke(); WindowManager.Close(this); };
        AddControl(_actionButton);

        _text = new DXTextArea
        {
            Location = new Vector2I(23, 94),
            Size = new Vector2I(500, 112),
            ReadOnly = true,
            MaxLength = 0,
            Text = string.Empty,
        };
        AddControl(_text);
    }

    public void SetNotice(string text) => _text.Text = text ?? string.Empty;

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(584, 252)
            && _background.LibraryFile == LibraryFile.GameInter && _background.Index == 602
            // 背景锚点 = alpha 可见区原点 -(220,2)（素材实测 F602 bbox (220,2)-(802,253)）。
            && _background.Location == new Vector2I(-220, -2)
            && _closeButton.Location == new Vector2(548, 16)
            && _actionButton.Location == new Vector2(496, 27)
            && _text.Location == new Vector2(23, 94)
            && _text.Size == new Vector2(500, 112);
        details = $"size={Size} frame={_background.Index}@{_background.Location} close={_closeButton.Location} action={_actionButton.Location} text={_text.Size}@{_text.Location}";
        return ok;
    }
}
