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

/// <summary>原版 NPCDialog：GameInter 380/381/382 框体、可滚动文本和 NPC 选项。</summary>
public partial class NPCDialog : DXWindow
{
    private readonly DXControl _textArea;
    private readonly NPCTextControl _text;
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
    //   根窗 552×176, 背景 F1100 (512×256, alpha bbox 64,59,384,138)
    //   正文绘制原点 window+(150,40), 白色, 行距 = textheight+5 = 21
    //   关闭 F161/162: (x+0x15B, bottom-0x24) = (347,140), 28×26
    //   上箭头 F52/53: (x+0x0B8, bottom-0x1E) = (184,146), 12×8
    //   下箭头 F54/55: (x+0x0C8, bottom-0x1E) = (200,146), 12×8
    // 正文区 (150,40) 起, 宽度到面板右缘 (64+384=448) 内, 取 290。
    // 字号沿用 12px 点阵 (ScaledSize(10))：原版行距闭合为 21 (=textheight+5,
    // 推断原版 textheight=16), 但本机像素字体无 16px 档, 16px 会栅格化发糊;
    // 行距按闭合证据取 21, 字号取点阵原生档, 不宣称像素级一致。
    private const int LegacyTextX = 150;
    private const int LegacyTextY = 40;
    private const int LegacyTextWidth = 290;
    private const int LegacyTextHeight = 136; // 40..176, 根窗裁剪
    private const int LegacyFontSize = 10;    // ScaledSize -> 12px 点阵
    private const int LegacyLinePitch = 21;   // primary-static: 0x594 默认 21


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
    /// Mir3-Research/ei-ui-layout/npc-window-render-evidence.json (primary-static)：
    /// 关闭 (347,140) 28×26、上箭头 (184,146) F52/53、下箭头 (200,146) F54/55、
    /// 正文原点 (150,40)。现代右侧滚动条在 legacy 下隐藏，滚动改由底部箭头
    /// 以"行"为单位步进 (原版 [0x3BC] 逻辑行索引 ±1)。
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
        // 关闭：paint 重定位 (window.x+0x15B, window.bottom-0x24)，固定根下即 (347,140)。
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(347, 140);
        _closeButton.Size = new Vector2I(28, 26);
        // 底部滚动箭头：命中区 12×8；原版仅当溢出标志 [0x58C]==1 时响应，
        // 这里用"正文行数 > 可视行数"作为等价的 overflow 门 (见 ShowPage)。
        _scroll.Visible = false;
        _scrollUp.Visible = true;
        _scrollDown.Visible = true;
        _scrollUp.Location = new Vector2I(184, 146);
        _scrollDown.Location = new Vector2I(200, 146);
        // 新页打开时回到顶部 (原版 0x440630: [0x3BC]=0)，并按当前正文高度刷新箭头。
        _scrollLine = 0;
        _text.Position = new Vector2(0, 0);
        UpdateLegacyScrollEnabled();
        UpdateClientAreaForLegacySkin();
    }

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

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok =
            Size == new Vector2I(552, 176)
            && _headerBackground.Index == 1100
            && _textArea.Location == new Vector2I(LegacyTextX, LegacyTextY)
            && _textArea.Size == new Vector2I(LegacyTextWidth, LegacyTextHeight)
            && _closeButton.Index == 161 && _closeButton.Location == new Vector2I(347, 140)
            && _closeButton.Size == new Vector2I(28, 26)
            && _scrollUp.Index == 52 && _scrollUp.Location == new Vector2I(184, 146)
            && _scrollUp.Size == new Vector2I(12, 8)
            && _scrollDown.Index == 54 && _scrollDown.Location == new Vector2I(200, 146)
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
        _text.SetContent(raw,
            _legacyLayout ? LegacyTextWidth : 340,
            _legacyLayout ? LegacyFontSize : 10,
            _legacyLayout ? LegacyLinePitch : 18);
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
        _goods.Location = new Vector2I(0, (int)Size.Y);
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
    public override void Close()
    {
        base.Close();
        GameScene.Game?.SendNPCClose();
    }
}
