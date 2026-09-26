using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Library;
using Library.SystemModels;
using MirDB;
using S = Library.Network.ServerPackets;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>NPC 对话：现代布局使用 GameInter 380/381/382；legacy EI 使用 F1100。</summary>
public partial class NPCDialog : DXWindow
{
    private readonly DXControl _textArea;
    private readonly NPCTextControl _text;
    private readonly DXControl _textColumn2Area;
    private readonly NPCTextControl _textColumn2;
    private readonly DXVScrollBar _scroll;
    private readonly DXButton _scrollUp;
    private readonly DXButton _scrollDown;
    private readonly List<DXButton> _buttons = new();
    private readonly List<DXImageControl> _rowBackgrounds = new();
    private readonly DXImageControl _headerBackground;
    private readonly DXImageControl _footerBackground;
    private DXButton _closeButton;
    private NPCPage _page;
    private readonly NPCGoodsPanel _goods;
    private readonly NPCRepairPanel _repair;
    private readonly NPCAdvancedPanel _advanced;
    private bool _legacyLayout;
    private int _scrollLine;

    // 旧版 F1100 常量 (来源: Mir3-Research docs/research/ei-ui-layout/
    // npc-window-render-evidence.json, primary-static):
    //   构造尺寸 552×176, 背景 F1100。
    //   文本绘制原点 window+(150,40), 白色; line pitch = textheight+5,
    //   默认值 21。正文裁剪区宽 290、高 136 是本实现基于根框和原点
    //   推导的适配值，不是证据文件直接给出的独立 RECT。
    //   子控件证据位置：close candidate (7,141), up candidate (290,145),
    //   down candidate (306,136); 资源帧只证明视觉状态，业务语义来自
    //   0x440290 的静态命中/门控路径。
    // 本实现按该静态路径接入关闭与行级上下滚动；mode=1 且 overflow=1
    // 的 14px 分支需要原版 token/layout state，当前 NPCPage 不暴露该状态，
    // 因此普通/长文本统一采用证据中的默认 21px 行距。
    private const int LegacyTextX = 150;
    private const int LegacyTextY = 40;
    // N5 证据 npc-dialog-family-evidence.json：type-1 文本的换行门是 0x95 = 149px，
    // 行数 >=7 时切到第二列（x = 0x131 = 305）。此前用 290 是自推导值，在任一
    // 背景原点下都会越出 F1100 可见面板右缘（384）。
    private const int LegacyTextWidth = 149;
    private const int LegacyTextColumn2X = 305;
    private const int LegacyTextHeight = 136; // 根框底部 176 - 文本原点 40
    private const int LegacyFontSize = 10;    // ScaledSize -> 12px 点阵
    private const int LegacyLinePitch = 21;   // evidence default_line_spacing_px



    public NPCDialog()
    {
        HasTitle = false; HasFooter = false; Movable = false; Size = new Vector2I(380, 204);
        _headerBackground = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 380, FixedSize = true, Size = new Vector2I(380, 140), MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_headerBackground);
        _footerBackground = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 382, FixedSize = true, Size = new Vector2I(380, 64), Location = new Vector2I(0, 140), MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_footerBackground);
        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(350, 3) };
        _closeButton.MouseClick += (o, e) => CloseNpc(); AddControl(_closeButton);
        _textArea = new DXControl { Location = new Vector2I(15, 45), Size = new Vector2I(350, 95), Clip = true }; AddControl(_textArea);
        _text = new NPCTextControl { Size = new Vector2I(340, 1000) }; _textArea.AddControl(_text);
        // N5 第二列：证据 npc-dialog-family-evidence.json 说行数 >=7 时切到
        // x = 0x131 = 305。与第一列同样 149 宽、136 高（136/21 = 6 行/列），
        // 所以第二列渲染的是同一段文本向上偏移 6 行（6*21 = 126）后的窗口。
        _textColumn2Area = new DXControl { Location = new Vector2I(15, 45), Size = new Vector2I(350, 95), Clip = true, Visible = false };
        AddControl(_textColumn2Area);
        _textColumn2 = new NPCTextControl { Size = new Vector2I(340, 1000), Location = new Vector2I(0, -6 * 21) };
        _textColumn2Area.AddControl(_textColumn2);
        _scroll = new DXVScrollBar { Location = new Vector2I(350, 45), Size = new Vector2I(14, 95), VisibleSize = 95, Change = 1, HideWhenNoScroll = false, BackColour = Colors.Transparent, Border = false };
        _scroll.UpButton.LibraryFile = LibraryFile.GameInter; _scroll.UpButton.Index = 387;
        _scroll.DownButton.LibraryFile = LibraryFile.GameInter; _scroll.DownButton.Index = 385;
        _scroll.PositionBar.LibraryFile = LibraryFile.None; _scroll.PositionBar.Index = -1;
        _scroll.ValueChanged += (o, e) => _text.Position = new Vector2(0, -_scroll.Value); AddControl(_scroll);
        // 旧版 F1100 底部滚动箭头 (12×8, F52/53 上, F54/55 下); 仅在 legacy 布局显示。
        _scrollUp = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 52, HoverIndex = 53, PressedIndex = 53, FixedSize = true, Size = new Vector2I(12, 8), Visible = false, Sound = SoundIndex.None };
        _scrollUp.MouseClick += (o, e) => ScrollLegacy(-1);
        _scrollDown = new DXButton { LibraryFile = LibraryFile.GameInter, Index = 54, HoverIndex = 55, PressedIndex = 55, FixedSize = true, Size = new Vector2I(12, 8), Visible = false, Sound = SoundIndex.None };
        _scrollDown.MouseClick += (o, e) => ScrollLegacy(1);
        AddControl(_scrollUp); AddControl(_scrollDown);
        _goods = new NPCGoodsPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_goods);
        _repair = new NPCRepairPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_repair);
        _advanced = new NPCAdvancedPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_advanced);
    }

    /// <summary>
    /// 旧版 EI NPC 根窗口 (F1100, 552×176)。几何来自
    /// Mir3-Research/ei-ui-layout/npc-window-render-evidence.json (primary-static)。
    /// 关闭/箭头位置采用证据中的 static hit-test 子控件位置；资源帧仅表示
    /// normal/highlight 视觉状态。正文原点为 (150,40)，现代右侧滚动条
    /// 在 legacy 下隐藏，滚动改由静态命中路径对应的底部箭头按行步进。
    /// 正文和交易子面板仍复用现有业务链，不改变网络/业务逻辑。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyLayout = true;
        Size = new Vector2I(552, 176);
        _headerBackground.LibraryFile = LibraryFile.GameInter;
        _headerBackground.Index = 1100;
        _headerBackground.Location = Vector2I.Zero;
        _headerBackground.Size = MirSkin.GetSize(LibraryFile.GameInter, 1100);
        _headerBackground.StretchImage = false;
        _footerBackground.Visible = false;
        _textArea.Location = new Vector2I(LegacyTextX, LegacyTextY);
        _textArea.Size = new Vector2I(LegacyTextWidth, LegacyTextHeight);
        _textArea.Clip = true;
        _textColumn2Area.Location = new Vector2I(LegacyTextColumn2X, LegacyTextY);
        _textColumn2Area.Size = new Vector2I(LegacyTextWidth, LegacyTextHeight);
        _textColumn2Area.Clip = true;
        // 关闭：证据中的 static hit-test 子控件位置 (7,141)。
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(7, 141);
        _closeButton.Size = new Vector2I(28, 26);
        // 滚动箭头：证据中的 static hit-test 子控件位置；
        // 资源帧仅表示 normal/highlight 视觉状态。
        _scroll.Visible = false;
        _scrollUp.Visible = true;
        _scrollDown.Visible = true;
        _scrollUp.Location = new Vector2I(290, 145);
        _scrollDown.Location = new Vector2I(306, 136);
        // 新页打开时回到顶部 (原版 0x440630: [0x3BC]=0)，并按当前正文高度刷新箭头。
        _scrollLine = 0;
        _text.Position = new Vector2(0, 0);
        UpdateLegacyScrollEnabled();
        UpdateClientAreaForLegacySkin();
        // 商品面板（商店窗 id2 的购买态）一并切到旧版几何：GameInter F1000 / 300x304 /
        // 行距 46 / close 1010-1011 / confirm 1012-1013。
        _goods.ApplyLegacyEiLayout();
        _legacyGoodsPlaced = true;
        PlaceGoodsPanel();
    }

    /// <summary>
    /// 原版商店窗（id2）是**独立窗口**，证据给出其屏幕位置为 (0,184)
    /// （store-window-render-evidence.json::window_candidate.screen_origin_proof：
    ///  \"state-0 content rect = (0,186,300,304); panel drawn at screen (0,184)-(299,490)\"）。
    /// 我方把商品面板做成 NPC 窗的子面板（现代布局是挂在 NPC 窗下方 (0, Size.Y)），
    /// 所以要用绝对屏幕坐标反推相对位置，否则面板会被排到屏幕外、底部被裁掉
    /// —— 这一点只查面板自身几何的审计是发现不了的，靠截图才暴露。
    /// </summary>
    private void PlaceGoodsPanel()
    {
        _goods.Location = new Vector2I(
            LegacyStoreScreenX - (int)Location.X,
            LegacyStoreScreenY - (int)Location.Y);
    }

    /// <summary>原版商店窗（id2）的屏幕原点，证据值 (0,184)。</summary>
    public static readonly Vector2I LegacyStoreScreen = new(0, 184);
    private const int LegacyStoreScreenX = 0;
    private const int LegacyStoreScreenY = 184;
    private bool _legacyGoodsPlaced;

    /// <summary>legacy 行级滚动：_scrollLine ∈ [0, 行数-可视行数]，步进 1。</summary>
    private void ScrollLegacy(int delta)
    {
        if (!_legacyLayout) return;
        int maxScroll = GetLegacyMaxScroll();
        _scrollLine = Mathf.Clamp(_scrollLine + delta, 0, maxScroll);
        _text.Position = new Vector2(0, -_scrollLine * LegacyLinePitch);
        UpdateLegacyScrollEnabled();
    }

    private int GetLegacyMaxScroll()
    {
        int visibleLines = LegacyTextHeight / LegacyLinePitch;
        return Math.Max(0, _text.LineCount - visibleLines);
    }

    private void UpdateLegacyScrollEnabled()
    {
        int maxScroll = GetLegacyMaxScroll();
        _scrollUp.Enabled = maxScroll > 0 && _scrollLine > 0;
        _scrollDown.Enabled = maxScroll > 0 && _scrollLine < maxScroll;
    }

    /// <summary>
    /// 验收测试场用：商品面板（商店窗 id2 购买态）的 legacy 布局审计。
    /// 除了面板自身几何，还校验**绝对屏幕位置** —— 上一版只查自身几何，
    /// 面板被排到屏幕外（底部裁掉）时审计仍然 PASS，是截图才暴露的。
    /// </summary>
    public bool AuditLegacyGoods(out string details)
    {
        bool own = _goods.AuditLegacyEiLayout(out string ownDetails);
        var screen = new Vector2I(_goods.Location.X + (int)Location.X, _goods.Location.Y + (int)Location.Y);
        bool placed = screen == LegacyStoreScreen;
        details = $"screen={screen} expected={LegacyStoreScreen} placed={placed} | {ownDetails}";
        return own && placed;
    }

    /// <summary>
    /// 验收测试场用：强制显示商品面板。测试场不连服务器、没有 NPC 商品数据，
    /// SetGoods 不会把 Visible 置真；这里只为了让截图能看到 F1000 外框、
    /// 购买按钮与列表区域，不伪造任何商品行。
    /// </summary>
    public void ShowGoodsForTest()
    {
        _goods.ApplyLegacyEiLayout();
        _goods.Visible = true;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok =
            Size == new Vector2I(552, 176)
            && _headerBackground.Index == 1100
            && _textArea.Location == new Vector2I(LegacyTextX, LegacyTextY)
            && _textArea.Size == new Vector2I(LegacyTextWidth, LegacyTextHeight)
            && _closeButton.Index == 161 && _closeButton.Location == new Vector2I(7, 141)
            && _closeButton.Size == new Vector2I(28, 26)
            && _scrollUp.Index == 52 && _scrollUp.Location == new Vector2I(290, 145)
            && _scrollUp.Size == new Vector2I(12, 8)
            && _scrollDown.Index == 54 && _scrollDown.Location == new Vector2I(306, 136)
            && _scrollDown.Size == new Vector2I(12, 8)
            && !_scroll.Visible && _scrollUp.Visible && _scrollDown.Visible;
        details = $"size={Size} bg=F{_headerBackground.Index} "
            + $"text={_textArea.Location}/{_textArea.Size} "
            + $"close=F{_closeButton.Index}@{_closeButton.Location}/{_closeButton.Size} "
            + $"up=F{_scrollUp.Index}@{_scrollUp.Location}/{_scrollUp.Size} "
            + $"down=F{_scrollDown.Index}@{_scrollDown.Location}/{_scrollDown.Size} "
            + $"legacyScrollHidden={!_scroll.Visible}";
        return ok;
    }

    /// <summary>验收测试场 (独立场景, 无服务器) 专用: F1100 几何审计 + 行级滚动状态一次取回。</summary>
    public (bool Ok, string Details, int Line, int MaxLine, float TextOffsetY) LegacyEiSelfState()
    {
        bool ok = AuditLegacyEiLayout(out string details);
        return (ok, details, _scrollLine, GetLegacyMaxScroll(), _text.Position.Y);
    }

    // 验收测试场专用句柄: 直接驱动 F1100 的关闭/上箭头/下箭头与正文控件,
    // 走的是控件真实的 _GuiInput 输入处理链 (与真实点击同一代码路径)。
    public DXButton LegacyCloseButton => _closeButton;
    public DXButton LegacyScrollUpButton => _scrollUp;
    public DXButton LegacyScrollDownButton => _scrollDown;
    public NPCTextControl LegacyText => _text;

    public void ShowPage(S.NPCResponse response)
    {
        _page = response?.Page;
        if (_page == null) return;
        bool selling = _page.DialogType == NPCDialogType.BuySell && _page.Types is { Count: > 0 };
        if (!selling) GameScene.Game?.EndInventoryNpcSale();
        string raw = _page.Say ?? string.Empty;
        raw = Regex.Replace(raw, @"\<(?<Text>.*?):(?<Default>.+?)\>", match =>
        {
            string id = match.Groups["Text"].Value;
            var value = response.Values?.Find(x => x.ID.ToString() == id);
            return value?.Value ?? match.Groups["Default"].Value;
        });
        var buttonMatches = Regex.Matches(raw, @"\[(?<Text>.*?):(?<ID>.+?)\]");
        // NPCIMG/FCOLOR 是原版对话脚本的行级 token
        // （npc-dialog-family-evidence.json type4_0x43FF92）：
        //   NPCIMG <n> -> atoi 后取 NPCFace.wil 裸帧号 n 画头像
        //   FCOLOR <n> -> atoi 后取调色板 [eax*4 + 0x47C4A8] 作为菜单文字色
        // 调色板 16 项 BGR 已从证据逐项解出（npc-body-strip-evidence.json）。
        // FCOLOR 之后的正文行改用该色（用本控件已支持的 {text:colour} 语法）。
        // NPCIMG 的头像位置证据未给（只有 0x466130 的 blit 调用），暂不绘制。
        if (_legacyLayout) raw = ApplyLegacyFColor(raw);
        _text.SetContent(raw,
            _legacyLayout ? LegacyTextWidth : 340,
            _legacyLayout ? LegacyFontSize : 10,
            _legacyLayout ? LegacyLinePitch : 18);
        // N5 两列：每列 136/21 = 6 行，行数超过 6 才启用第二列。
        if (_legacyLayout)
        {
            bool twoColumn = _text.LineCount > LegacyTextHeight / LegacyLinePitch;
            _textColumn2Area.Visible = twoColumn;
            if (twoColumn)
            {
                _textColumn2.SetContent(raw, LegacyTextWidth, LegacyFontSize, LegacyLinePitch);
            }
        }
        else
        {
            _textColumn2Area.Visible = false;
        }
        if (_legacyLayout)
        {
            GD.Print($"[LegacyNPC] lines={_text.LineCount} twoColumn={_textColumn2Area.Visible} "
                + $"col1={_textArea.Location}/{_textArea.Size} col2={_textColumn2Area.Location}/{_textColumn2Area.Size}");
        }
        int pageTextHeight = _text.ContentHeight;
        foreach (var button in _buttons) { RemoveControl(button); button.QueueFree(); } _buttons.Clear();
        // 原版按钮不是单独一行的 DXButton，而是画在正文中的可点击文字区域。
        // NPCTextControl 已经保留了这些区域；只有协议没有内嵌按钮时才使用
        // Page.Buttons 作为兼容性的后备入口。
        int y = _legacyLayout ? LegacyTextY + 10 : 151;
        if (buttonMatches.Count == 0 && _page.Buttons != null) foreach (var option in _page.Buttons)
        {
            var button = new DXButton { Text = string.Format(Lang.NPCUi357Label, option.ButtonID), FontSize = 10, TextColour = new Color(1f, .85f, .3f), LibraryFile = LibraryFile.GameInter, Index = -1, Location = new Vector2I(_legacyLayout ? LegacyTextX + 10 : 18, y), Size = new Vector2I(_legacyLayout ? 270 : 330, 20) };
            int id = option.ButtonID; button.MouseClick += (o, e) => GameScene.Game?.SendNPCButton(id); AddControl(button); _buttons.Add(button); y += 22;
        }
        // 现代布局：文字超出 140+64 客户区时，每 20px 增加一张
        // GameInter 381 中间行，最多 6 行；底框始终是 382。
        // legacy F1100 用固定 552×176 + 单张 F1100 背景, 无 381 行/382 底框,
        // 故整段现代动态尺寸仅在非 legacy 下执行, 由末尾 ApplyLegacyEiLayout() 收尾。
        if (!_legacyLayout)
        {
            int rowCount = Math.Clamp((pageTextHeight - 124) / 20, 0, 6);
            int footerY = 140 + rowCount * 20;
            Size = new Vector2I(380, footerY + 64);
            foreach (var row in _rowBackgrounds) { RemoveControl(row); row.QueueFree(); }
            _rowBackgrounds.Clear();
            for (int i = 0; i < rowCount; i++)
            {
                var row = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 381, FixedSize = true, Size = new Vector2I(380, 20), Location = new Vector2I(0, 140 + i * 20), MouseFilter = MouseFilterEnum.Ignore, ZIndex = -10 };
                AddControl(row); _rowBackgrounds.Add(row);
            }
            _footerBackground.Location = new Vector2I(0, footerY);
            _textArea.Size = new Vector2I(350, Math.Max(0, (int)Size.Y - 59));
            _scroll.Size = new Vector2I(14, Math.Max(0, (int)Size.Y - 59));
            _scroll.VisibleSize = (int)_textArea.Size.Y;
            _scroll.MaxValue = Math.Max(0, pageTextHeight - (int)_textArea.Size.Y + 14);
        }
        else
        {
            // legacy: 清理任何可能遗留的现代行背景, 避免 F381 帧泄漏进 F1100。
            foreach (var row in _rowBackgrounds) { RemoveControl(row); row.QueueFree(); }
            _rowBackgrounds.Clear();
        }
        // legacy：商品面板的位置由 PlaceGoodsPanel 按原版商店窗的屏幕原点 (0,184) 固定，
        // 这里不能按现代公式挂在 NPC 窗下方 (0, Size.Y)，否则面板会被排到屏幕外。
        if (_legacyGoodsPlaced) PlaceGoodsPanel();
        else _goods.Location = new Vector2I(0, (int)Size.Y);
        _goods.SetGoods(_page.Goods, _page.Currency, _page.Types?.Select(x => x.ItemType));
        _goods.Visible = _page.DialogType == NPCDialogType.BuySell && _page.Goods != null && _page.Goods.Count > 0;
        if (selling)
        {
            GameScene.Game?.ShowInventoryForNpcSale(_page.Currency, _page.Types.Select(x => x.ItemType));
            _goods.Visible = true;
        }
        _repair.AllowedTypes = _page.Types?.Select(x => x.ItemType);
        _repair.Visible = _page.DialogType == NPCDialogType.Repair;
        if (_repair.Visible)
            GameScene.Game?.SetInventoryLegacyMode(InventoryMode.Repair);
        _repair.Location = new Vector2I(0, (int)Size.Y);
        _advanced.HidePanel();
        GameScene.Game?.CloseNPCCompanionStorage();
        if (_page.DialogType != NPCDialogType.None && _page.DialogType != NPCDialogType.BuySell && _page.DialogType != NPCDialogType.Repair &&
            _page.DialogType != NPCDialogType.Socketing && _page.DialogType != NPCDialogType.SocketCombine &&
            (_page.DialogType != NPCDialogType.CompanionManage || GameScene.IsCompanionEnabled) &&
            _page.DialogType != NPCDialogType.Consignment)
        {
            _advanced.Configure(_page.DialogType);
            _advanced.Location = new Vector2I(0, (int)Size.Y);
            if (_page.DialogType == NPCDialogType.CompanionManage)
                GameScene.Game?.OpenNPCCompanionStorage();
        }
        if (_page.DialogType == NPCDialogType.Consignment && GameScene.IsConsignmentEnabled)
            GameScene.Game?.OpenConsignmentDialog();
        WindowManager.Open(this, GameScene.Game?.UILayer ?? GetParent());
        if (_page.DialogType == NPCDialogType.None)
            GameScene.Game?.OpenNPCQuestList(GameScene.Game.NPCObjectId);
        else
        {
            GameScene.Game?.CloseNPCQuestDialogs();
            GameScene.Game?.CloseNPCSocketDialogs();
            if (_page.DialogType == NPCDialogType.Socketing)
                GameScene.Game?.OpenNPCSocketDialog();
            else if (_page.DialogType == NPCDialogType.SocketCombine)
                GameScene.Game?.OpenNPCSocketCombineDialog();
        }
        // ShowPage 根据正文高度重建现代 NPC 尺寸；旧版 F1100 的协议回包也必须
        // 回到固定 552×176 根框，否则真实打开 NPC 时会悄悄退回 380×204。
        if (_legacyLayout)
            ApplyLegacyEiLayout();
    }

    public void RepairResult(Library.Network.ServerPackets.NPCRepair packet) => _repair.RepairResult(packet);
    public void ClearAdvancedLinks(IEnumerable<CellLinkInfo> links) => _advanced.CompleteLinks(links);
    public void CancelPendingLinks()
    {
        var links = _repair.CancelLinks();
        links.AddRange(_advanced.CancelLinks());
        links.AddRange(_goods.CancelAllLinks());
        foreach (var link in links)
            GameScene.Game?.UnlockItemLink(link);
    }
    public void CancelUnsubmittedLinks()
    {
        var links = _repair.CancelDisplayedLinks();
        links.AddRange(_advanced.CancelUnsubmittedLinks());
        links.AddRange(_goods.CancelUnsubmittedLinks());
        foreach (var link in links)
            GameScene.Game?.UnlockItemLink(link);
    }
    public void ItemsChanged(IEnumerable<CellLinkInfo> links) => _goods.ItemsChanged(links);
    public void SetRefineList(IEnumerable<ClientRefineInfo> list) => _advanced.SetRefineList(list);
    public void RemoveRefine(int index) => _advanced.RemoveRefine(index);
    public void RefreshRefineList() => _advanced.RefreshRefineList();
    public void ShowRollResult(int type, int result) => _advanced.ShowRollResult(type, result);

    public bool TryRouteItem(DXItemCell source)
    {
        if (!Visible || source?.Item == null) return false;
        if (GameScene.Game?.TryRouteItemToSocket(source) == true) return true;
        if (_repair.Visible && _repair.TryRouteItem(source)) return true;
        if (_goods.Visible && _goods.TrySelectForSale(source)) return true;
        if (_advanced.Visible && _advanced.TryRouteItem(source)) return true;
        return false;
    }

    public bool TrySelectItemForSale(DXItemCell source)
        => Visible && _goods.TrySelectForSale(source);

    public bool CanAcceptAdvancedLink(DXItemCell source, DXItemCell target)
        => Visible && _advanced.Visible && _advanced.CanAcceptLink(source, target);

    public bool CanAcceptRepairLink(DXItemCell source)
        => Visible && _repair.Visible && _repair.CanAcceptSource(source);

    private void CloseNpc()
    {
        CancelUnsubmittedLinks();
        _advanced.HidePanel();
        if (GameScene.Game != null)
            GameScene.Game.CloseNPCDialog();
        else
            // 独立测试场 (无 GameScene) 下仍要能本地关闭, 供 F1100 关闭验收。
            WindowManager.Close(this);
    }

    // The legacy dialog sends NPCClose whenever it becomes hidden, including
    // when Escape closes the top window. WindowManager.CloseTop only knows
    // about DXWindow, so preserve that protocol edge here as well.
    /// <summary>
    /// EI 对话脚本的 FCOLOR 行 token。证据 npc-body-strip-evidence.json：
    /// `mov ecx,[eax*4+0x47c4a8]`，0x47C4A8 是 16 项 BGR 调色板
    /// （0..15：0x000000,0x0000FF,0x008000,0x008080,0x808080,0x000080,
    /// 0x808000,0x800000,0xC0C0C0,0x800080,0x00FF00,0xFF0000,0xFFFFFF,
    /// 0xFF00FF,0xFFFF00,0x00FFFF）。值是 Windows COLORREF（0x00BBGGRR），
    /// 所以取色时要按 R=低字节、B=高字节还原。
    /// FCOLOR 之后的正文行用该色；NPCIMG 行按证据暂不绘制（位置未给）。
    /// </summary>
    private static string ApplyLegacyFColor(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("FCOLOR", StringComparison.Ordinal)) return text;
        var output = new List<string>();
        Color? current = null;
        foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("FCOLOR ", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed[7..].Trim(), out int index) && index >= 0 && index < LegacyFColorPalette.Length)
                {
                    current = LegacyFColorPalette[index];
                    GD.Print($"[LegacyFColor] index={index} -> RGB({current.Value.R * 255:0},{current.Value.G * 255:0},{current.Value.B * 255:0})");
                }
                continue;
            }
            if (trimmed.StartsWith("NPCIMG ", StringComparison.OrdinalIgnoreCase)) continue;
            output.Add(current == null || trimmed.Length == 0
                ? line
                : $"{{{line}:{current.Value.ToHtml(false)}}}");
        }
        return string.Join("\n", output);
    }

    /// <summary>0x47C4A8 的 16 项 BGR 调色板，已按 COLORREF 还原为 RGB。</summary>
    private static readonly Color[] LegacyFColorPalette =
    {
        Color.Color8(0x00, 0x00, 0x00), // 0 0x000000
        Color.Color8(0xFF, 0x00, 0x00), // 1 0x0000FF red
        Color.Color8(0x00, 0x80, 0x00), // 2 0x008000
        Color.Color8(0x80, 0x80, 0x00), // 3 0x008080
        Color.Color8(0x80, 0x80, 0x80), // 4 0x808080
        Color.Color8(0x80, 0x00, 0x00), // 5 0x000080 maroon (R=0x80,G=0,B=0)
        Color.Color8(0x00, 0x80, 0x80), // 6 0x808000
        Color.Color8(0x00, 0x00, 0x80), // 7 0x800000
        Color.Color8(0xC0, 0xC0, 0xC0), // 8 0xC0C0C0
        Color.Color8(0x80, 0x00, 0x80), // 9 0x800080
        Color.Color8(0x00, 0xFF, 0x00), // 10 0x00FF00
        Color.Color8(0x00, 0x00, 0xFF), // 11 0xFF0000 blue
        Color.Color8(0xFF, 0xFF, 0xFF), // 12 0xFFFFFF
        Color.Color8(0xFF, 0x00, 0xFF), // 13 0xFF00FF
        Color.Color8(0x00, 0xFF, 0xFF), // 14 0xFFFF00
        Color.Color8(0xFF, 0xFF, 0x00), // 15 0x00FFFF
    };

    public override void Close()
    {
        base.Close();
        GameScene.Game?.SendNPCClose();
    }
}
