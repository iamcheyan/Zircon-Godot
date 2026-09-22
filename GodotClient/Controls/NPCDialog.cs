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
    private readonly List<DXButton> _buttons = new();
    private readonly List<DXImageControl> _rowBackgrounds = new();
    private readonly DXImageControl _headerBackground;
    private readonly DXImageControl _footerBackground;
    private DXButton _closeButton;
    private NPCPage _page;
    private readonly NPCGoodsPanel _goods;
    private readonly NPCRepairPanel _repair;
    private readonly NPCAdvancedPanel _advanced;

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
        _goods = new NPCGoodsPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_goods);
        _repair = new NPCRepairPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_repair);
        _advanced = new NPCAdvancedPanel { Location = new Vector2I(0, 204), Visible = false }; AddControl(_advanced);
    }

    /// <summary>旧版 EI NPC 根窗口：F1100 552×176；正文和交易子面板仍复用现有业务。</summary>
    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(552, 176);
        _headerBackground.LibraryFile = LibraryFile.GameInter;
        _headerBackground.Index = 1100;
        _headerBackground.Location = Vector2I.Zero;
        _headerBackground.Size = MirSkin.GetSize(LibraryFile.GameInter, 1100);
        _headerBackground.StretchImage = false;
        _footerBackground.Visible = false;
        _textArea.Location = new Vector2I(20, 28);
        _textArea.Size = new Vector2I(500, 112);
        _scroll.Location = new Vector2I(530, 28);
        _scroll.Size = new Vector2I(18, 112);
        _scroll.VisibleSize = 112;
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(516, 146);
        _closeButton.Size = new Vector2I(28, 26);
        UpdateClientAreaForLegacySkin();
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(552, 176) && _headerBackground.Index == 1100;
        details = $"size={Size} background=F{_headerBackground.Index} text={_textArea.Location}/{_textArea.Size}";
        return ok;
    }

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
        _text.SetContent(raw, 340, 10);
        int pageTextHeight = _text.ContentHeight;
        foreach (var button in _buttons) { RemoveControl(button); button.QueueFree(); } _buttons.Clear();
        // 原版按钮不是单独一行的 DXButton，而是画在正文中的可点击文字区域。
        // NPCTextControl 已经保留了这些区域；只有协议没有内嵌按钮时才使用
        // Page.Buttons 作为兼容性的后备入口。
        int y = 151;
        if (buttonMatches.Count == 0 && _page.Buttons != null) foreach (var option in _page.Buttons)
        {
            var button = new DXButton { Text = string.Format(Lang.NPCUi357Label, option.ButtonID), FontSize = 10, TextColour = new Color(1f, .85f, .3f), LibraryFile = LibraryFile.GameInter, Index = -1, Location = new Vector2I(18, y), Size = new Vector2I(330, 20) };
            int id = option.ButtonID; button.MouseClick += (o, e) => GameScene.Game?.SendNPCButton(id); AddControl(button); _buttons.Add(button); y += 22;
        }
        // 原版 SetSize：文字超出 140+64 客户区时，每 20px 增加一张
        // GameInter 381 中间行，最多 6 行；底框始终是 382。
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
        GameScene.Game?.CloseNPCDialog();
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
