using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 CommunicationDialog 的好友、收件箱、写信与阅读邮件页。</summary>
public partial class CommunicationDialog : DXWindow
{
    public bool HasUnread => _mails.Exists(x => x != null && !x.Opened);
    public event Action<bool> UnreadChanged;
    private readonly List<ClientMailInfo> _mails = new();
    private readonly List<ClientFriendInfo> _friends = new();
    private readonly List<ClientBlockInfo> _blocks = new();
    private readonly DXImageControl _pageBackground;
    private DXImageControl _background;
    private DXButton _closeButton;
    private DXLabel _titleLabel;
    private DXControl _body;
    private DXVScrollBar _scroll;
    private DXTextArea _detail;
    private DXItemGrid _readGrid;
    private ClientUserItem[] _readMailItems = new ClientUserItem[7];
    private DXTextInput _recipient, _subject;
    private DXTextArea _message;
    private int _page;
    private DXTextInput _friendInput;
    private DXButton _friendStatus, _friendFilter, _friendAdd, _friendRemove;
    private DXButton _blockAdd, _blockRemove;
    private DXButton _receivedCollectAll, _receivedDeleteAll, _receivedNew;
    private DXVScrollBar _messageScroll;
    private DXVScrollBar _readMessageScroll;
    private readonly List<DXButton> _tabs = new();
    private DXButton _sendButton;
    private DXButton _readReplyButton, _readDeleteButton;
    private int _friendStateFilter;
    private int _selectedFriendIndex = -1;
    private DXItemCell[] _sendMailCells = Array.Empty<DXItemCell>();
    private readonly ClientUserItem[] _sendMailItems = new ClientUserItem[5];
    private readonly List<CellLinkInfo> _pendingMailLinks = new();
    private readonly HashSet<(int Mail, int Slot)> _pendingMailItemGets = new();
    private bool _mailSending;

    public CommunicationDialog()
    {
        HasTitle = false;
        Movable = true;
        HasFooter = false;
        // 原版 Interface 200 外框为 296x424，TabControl 从 y=37 开始。
        Size = new Vector2I(296, 424);
        _background = new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 200, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_background);
        _pageBackground = new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 201, FixedSize = true, Size = new Vector2I(296, 316), Location = new Vector2I(0, 60), MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_pageBackground);
        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);
        _titleLabel = new DXLabel { Text = Lang.CommunicationUi175Label, FontSize = 10, TextColour = new Color(1f, 0.85f, 0.3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(296, 18), IsControl = false };
        AddControl(_titleLabel);
        // 原版 DXTabControl：TabControl 位于 y=37，页签从 x=10 按 60px+1px 间距排列，
        // 内容页从 y=60 开始，尺寸为 296x316。
        AddTab(Lang.CommunicationDialogFriendTabLabel, 10, 0);
        AddTab(Lang.CommunicationUi176Label, 71, 1);
        AddTab(Lang.CommunicationMailLabel, 132, 2);
        AddTab(Lang.CommunicationUi178Label, 193, 3);
        _body = new DXControl { Location = new Vector2I(0, 60), Size = new Vector2I(296, 316), Clip = true };
        AddControl(_body);
        // 原版好友/收件/屏蔽页的滚动条始终占位显示，即使当前列表不足一页。
        _scroll = new DXVScrollBar { Location = new Vector2I(265, 119), Size = new Vector2I(20, 252), VisibleSize = 238, Change = 36, HideWhenNoScroll = false };
        _scroll.ValueChanged += (o, e) => { if (_page == 0) RebuildFriends(); else if (_page == 1) RebuildReceived(); else if (_page == 3) RebuildBlockPage(); };
        AddControl(_scroll);
        CreateActionButtons();
        ShowPage(0);
    }

    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(572, 388);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 350;
        _background.Location = new Vector2I(-226, -62);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 350);
        _background.StretchImage = false;
        _titleLabel.Visible = false;
        _pageBackground.Visible = false;
        // 保留旧版 F350 框，但不能把好友/邮件正文设为透明；这些是
        // 正式网络数据，旧版背景只负责承载它们。
        _body.Modulate = Colors.White;
        _scroll.Modulate = Colors.White;
        foreach (var tab in _tabs)
        {
            tab.Modulate = new Color(1, 1, 1, 0);
            tab.Location = new Vector2I(tab.Location.X + 226, 37);
        }
        foreach (var button in new[] { _friendAdd, _friendRemove, _receivedCollectAll, _receivedDeleteAll, _receivedNew, _blockAdd, _blockRemove })
            if (button != null) button.Modulate = new Color(1, 1, 1, 0);
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(536, 354);
        _closeButton.Size = new Vector2I(28, 26);
        UpdateClientAreaForLegacySkin();
    }

    public bool AuditLayout(out string details)
    {
        bool ok = Size == new Vector2I(296, 424)
            && _pageBackground.Size == new Vector2I(296, 316)
            && _pageBackground.Location == new Vector2I(0, 60)
            && _body.Location == new Vector2I(0, 60)
            && _body.Size == new Vector2I(296, 316)
            && _tabs.Count == 4
            && _tabs[0].Location == new Vector2I(10, 37)
            && _tabs[1].Location == new Vector2I(71, 37)
            && _tabs[2].Location == new Vector2I(132, 37)
            && _tabs[3].Location == new Vector2I(193, 37)
            && _friendAdd.Location == new Vector2I(43, 383)
            && _friendRemove.Location == new Vector2I(153, 383)
            && _receivedCollectAll.Location == new Vector2I(15, 383)
            && _receivedDeleteAll.Location == new Vector2I(105, 383)
            && _receivedNew.Location == new Vector2I(195, 383)
            && _blockAdd.Location == new Vector2I(43, 93)
            && _blockRemove.Location == new Vector2I(153, 93);
        details = $"size={Size} body={_body.Location}/{_body.Size} tabs={_tabs.Count}";
        return ok;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(572, 388)
            && _background.Index == 350
            && _body.Modulate.A > 0.99f
            && _scroll.Modulate.A > 0.99f
            && _closeButton.Location == new Vector2I(536, 354);
        details = $"size={Size} background=F{_background.Index} bodyAlpha={_body.Modulate.A:0.##} close={_closeButton.Location}";
        return ok;
    }

    public bool AuditPages(out string details)
    {
        ShowPage(0);
        bool friends = _pageBackground.Index == 201 && _scroll.Visible && _friendStatus != null && _friendFilter != null;
        ShowPage(1);
        bool received = _pageBackground.Index == 202 && _scroll.Visible && _scroll.VisibleSize == 5 && _receivedCollectAll.Visible;
        ShowPage(2);
        bool send = _pageBackground.Index == 203 && !_scroll.Visible && _sendButton.Visible && _messageScroll.Visible;
        ShowPage(3);
        bool blocked = _pageBackground.Index == 204 && _scroll.Visible && _blockAdd.Visible && _blockRemove.Visible;
        ShowPage(0);
        details = $"pages=friends:{friends},received:{received},send:{send},blocked:{blocked}";
        return friends && received && send && blocked;
    }

    private void CreateActionButtons()
    {
        _friendAdd = ActionButton(Lang.CommunicationDialogFriendTabFriendAddButtonLabel, new Vector2I(43, 383), 100);
        _friendAdd.Visible = true;
        _friendAdd.MouseClick += (o, e) =>
        {
            if (_friendInput == null)
            {
                _friendInput = new DXTextInput { Location = new Vector2I(151, 10), Size = new Vector2I(122, 18), MaxLength = Globals.MaxCharacterNameLength };
                _body.AddControl(_friendInput);
                _friendInput.GrabFocus();
                return;
            }
            if (!string.IsNullOrWhiteSpace(_friendInput.Text))
            {
                GameScene.Game?.SendFriendAdd(_friendInput.Text.Trim());
                _body.RemoveControl(_friendInput);
                _friendInput.QueueFree();
                _friendInput = null;
            }
        };
        _friendRemove = ActionButton(Lang.CommunicationDialogFriendTabFriendRemoveButtonLabel, new Vector2I(153, 383), 100);
        _friendRemove.Visible = true;
        _friendRemove.Enabled = false;
        _friendRemove.MouseClick += (o, e) =>
        {
            if (_selectedFriendIndex >= 0) GameScene.Game?.SendFriendRemove(_selectedFriendIndex);
        };

        _receivedCollectAll = ActionButton(Lang.CommunicationAllLabel, new Vector2I(15, 383), 80);
        _receivedDeleteAll = ActionButton(Lang.CommunicationDeleteLabel, new Vector2I(105, 383), 80);
        _receivedNew = ActionButton(Lang.CommunicationDialogReceivedTabNewButtonLabel, new Vector2I(195, 383), 80);
        _receivedCollectAll.MouseClick += (o, e) =>
        {
            foreach (var mail in _mails.Where(x => x?.Items?.Count > 0).Take(15))
            {
                if (!mail.Opened) { mail.Opened = true; GameScene.Game?.SendMailOpened(mail.Index); }
                foreach (var item in mail.Items)
                    if (item != null && _pendingMailItemGets.Add((mail.Index, item.Slot)))
                        GameScene.Game?.SendMailGetItem(mail.Index, item.Slot);
            }
            UnreadChanged?.Invoke(HasUnread);
            RebuildReceived();
        };
        _receivedDeleteAll.MouseClick += (o, e) =>
        {
            foreach (var mail in _mails.Where(x => x?.Items?.Count == 0).Take(15).ToList())
            {
                GameScene.Game?.SendMailDelete(mail.Index);
            }
            // 与原版一致：仅发起删除请求，等待 S.MailDelete 后由 RemoveMail
            // 修改本地列表；否则服务端拒绝时邮件会在客户端凭空消失。
            RebuildReceived();
            UnreadChanged?.Invoke(HasUnread);
        };
        _receivedNew.MouseClick += (o, e) => ShowPage(2);
        foreach (var button in new[] { _friendAdd, _friendRemove, _receivedCollectAll, _receivedDeleteAll, _receivedNew })
            AddControl(button);
        _blockAdd = ActionButton(Lang.CommunicationAddLabel, new Vector2I(43, 93), 100);
        _blockRemove = ActionButton(Lang.CommunicationDeleteLabel2, new Vector2I(153, 93), 100);
        _blockAdd.Visible = _blockRemove.Visible = false;
        AddControl(_blockAdd);
        AddControl(_blockRemove);
        SetActionVisibility(0);
    }

    private DXButton ActionButton(string text, Vector2I location, int width)
        => new()
        {
            Text = text, Type = DXButton.ButtonType.Default, FontSize = 9,
            Size = new Vector2I(width, 25), Location = location,
            LibraryFile = LibraryFile.Interface, Index = -1,
        };

    private void SetActionVisibility(int page, bool read = false)
    {
        if (_friendAdd != null) _friendAdd.Visible = page == 0 && !read;
        if (_friendRemove != null) _friendRemove.Visible = page == 0 && !read;
        if (_receivedCollectAll != null) _receivedCollectAll.Visible = page == 1 && !read;
        if (_receivedDeleteAll != null) _receivedDeleteAll.Visible = page == 1 && !read;
        if (_receivedNew != null) _receivedNew.Visible = page == 1 && !read;
        if (_blockAdd != null) _blockAdd.Visible = page == 3 && !read;
        if (_blockRemove != null) _blockRemove.Visible = page == 3 && !read;
    }

    private void AddTab(string text, int x, int page)
    {
        var tab = new DXButton
        {
            Text = text, FontSize = 9, TextColour = new Color(1f, 0.85f, 0.3f),
            Type = page == 0 ? DXButton.ButtonType.SelectedTab : DXButton.ButtonType.DeselectedTab,
            Size = new Vector2I(60, 21), Location = new Vector2I(x, 37),
            LibraryFile = LibraryFile.Interface, Index = -1, Pressed = page == 0
        };
        tab.MouseClick += (o, e) => ShowPage(page);
        AddControl(tab);
        _tabs.Add(tab);
    }

    public void SetMails(IEnumerable<ClientMailInfo> mails)
    {
        _mails.Clear();
        if (mails != null) _mails.AddRange(mails.Where(x => x != null).OrderByDescending(x => x.Date));
        var valid = _mails.SelectMany(x => (x.Items ?? new List<ClientUserItem>()).Where(i => i != null).Select(i => (x.Index, i.Slot))).ToHashSet();
        _pendingMailItemGets.RemoveWhere(key => !valid.Contains(key));
        if (_page == 1) RebuildReceived();
        UnreadChanged?.Invoke(HasUnread);
    }

    public void SetFriends(IEnumerable<ClientFriendInfo> friends)
    {
        _friends.Clear();
        if (friends != null) _friends.AddRange(friends.Where(x => x != null).OrderBy(x => x.State).ThenBy(x => x.Name));
        if (_page == 0) RebuildFriends();
    }

    public void ApplyFriend(ClientFriendInfo friend)
    {
        if (friend == null) return;
        _friends.RemoveAll(x => x.Index == friend.Index);
        _friends.Add(friend);
        if (_page == 0) RebuildFriends();
    }

    public void RemoveFriend(int index)
    {
        _friends.RemoveAll(x => x.Index == index);
        if (_page == 0) RebuildFriends();
    }

    public void AddMail(ClientMailInfo mail)
    {
        if (mail == null) return;
        _mails.RemoveAll(x => x.Index == mail.Index);
        _mails.Insert(0, mail);
        if (_page == 1) RebuildReceived();
        UnreadChanged?.Invoke(true);
    }

    public void RemoveMail(int index)
    {
        _mails.RemoveAll(x => x.Index == index);
        _pendingMailItemGets.RemoveWhere(key => key.Mail == index);
        if (_page == 1) RebuildReceived();
        UnreadChanged?.Invoke(HasUnread);
    }

    public void RemoveMailItem(int index, int slot)
    {
        _pendingMailItemGets.Remove((index, slot));
        var mail = _mails.FirstOrDefault(x => x.Index == index);
        if (mail?.Items == null) return;
        mail.Items.RemoveAll(x => x != null && x.Slot == slot);
        mail.HasItem = mail.Items.Count > 0;
        if (_detail != null && _page == 1) OpenMail(index);
    }

    // 审计访问器: 只读快照, 不触发 UI 重建
    internal List<ClientMailInfo> MailSnapshot() => _mails.Where(x => x != null).ToList();
    internal ClientMailInfo FindMail(int index) => _mails.FirstOrDefault(x => x?.Index == index);

    public bool AuditMailSendLifecycle(out string details)
    {
        ShowPage(2);
        if (_recipient == null || _subject == null || _message == null)
        {
            details = "send controls missing";
            return false;
        }

        _recipient.Text = "收件人";
        _subject.Text = "主题";
        _message.Text = "正文";
        MailSendResult();
        bool firstPacketRetains = _recipient.Text == "收件人" && _subject.Text == "主题" && _message.Text == "正文";
        ItemsChanged(Array.Empty<CellLinkInfo>(), false);
        bool failureRetains = _recipient.Text == "收件人" && _subject.Text == "主题" && _message.Text == "正文";
        ItemsChanged(Array.Empty<CellLinkInfo>(), true);
        bool successClears = _recipient.Text.Length == 0 && _subject.Text.Length == 0 && _message.Text.Length == 0;
        details = $"first={firstPacketRetains} failure={failureRetains} success={successClears}";
        return firstPacketRetains && failureRetains && successClears;
    }

    /// <summary>断线重连回滚：发送中（pending 链接已锁定）断线时，
    /// CancelPendingMailLinks 解锁来源、清 pending、重置发送锁，重连后可重新发送。</summary>
    public bool AuditDisconnectRollback(out string details)
    {
        ShowPage(2);
        if (_sendMailCells == null || _sendMailCells.Length == 0)
        {
            details = "no send cells";
            return false;
        }
        var info = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x?.StackSize > 1);
        if (info == null)
        {
            details = "no stackable item";
            return false;
        }
        var item = new ClientUserItem(info, 5);
        var cell = _sendMailCells[0];
        cell.ItemGrid = _sendMailItems;
        cell.Slot = 0;
        _sendMailItems[0] = item;
        cell.LinkedSourceGrid = GridType.Inventory;
        cell.LinkedSourceSlot = 0;

        var links = PrepareMailSend();
        bool pendingEstablished = links != null && links.Count == 1;
        _mailSending = true; // 模拟发送中（按钮已按下、等待回包）
        CancelPendingMailLinks();
        bool released = _pendingMailLinks.Count == 0 && !_mailSending;
        var links2 = PrepareMailSend();
        bool resendable = links2 != null;
        CancelPendingMailLinks();
        details = $"pending={pendingEstablished} released={released} resendable={resendable}";
        return pendingEstablished && released && resendable;
    }

    private void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Type = i == page ? DXButton.ButtonType.SelectedTab : DXButton.ButtonType.DeselectedTab;
            _tabs[i].Pressed = i == page;
        }
        _pageBackground.Index = page switch { 0 => 201, 1 => 202, 2 => 203, 3 => 204, _ => 201 };
        foreach (var child in _body.GetChildren())
        {
            if (child is not Node node) continue;
            if (node == _messageScroll || node == _readMessageScroll)
            {
                // 同时从 Godot 节点树和 DXControl.Controls 逻辑树移除；
                // 只 RemoveChild 会留下已脱离场景的控件引用，下一次 UI 树
                // 遍历/换页访问它时会报 disposed object。
                if (node is DXControl scrollControl) _body.RemoveControl(scrollControl);
                else _body.RemoveChild(node);
                if (node is CanvasItem item) item.Visible = false;
            }
            else
            {
                if (node is DXControl control) _body.RemoveControl(control);
                else _body.RemoveChild(node);
                node.QueueFree();
            }
        }
        _detail = null;
        _readGrid = null;
        _recipient = null;
        _subject = null;
        _message = null;
        if (_friendInput != null)
        {
            _body.RemoveControl(_friendInput);
            _friendInput.QueueFree();
            _friendInput = null;
        }
        _scroll.Visible = page is 0 or 1 or 3;
        _scroll.Location = page == 1 ? new Vector2I(265, 63) : new Vector2I(265, 119);
        _scroll.Size = page == 1 ? new Vector2I(20, 308) : new Vector2I(20, 252);
        _scroll.VisibleSize = page == 1 ? 5 : 238;
        _scroll.Change = page == 1 ? 1 : 36;
        if (_sendButton != null) _sendButton.Visible = page == 2;
        if (_readReplyButton != null) _readReplyButton.Visible = false;
        if (_readDeleteButton != null) _readDeleteButton.Visible = false;
        if (_messageScroll != null) _messageScroll.Visible = false;
        if (_readMessageScroll != null) _readMessageScroll.Visible = false;
        SetActionVisibility(page);
        if (page == 0)
        {
            _scroll.Visible = true;
            BuildFriendsPage();
        }
        else if (page == 1) RebuildReceived();
        else if (page == 2) BuildSendPage();
        else BuildBlockPage();
    }

    public void ShowBlockPage() => ShowPage(3);
    public void ShowSendPage() => ShowPage(2);

    public void MailSendResult()
    {
        // 服务端顺序是 MailSend -> ItemsChanged。带附件时，首个回包只表示
        // 请求已入队，来源格仍必须保持锁定，不能允许下一次发送覆盖 pending 链接。
        // 原版没有在这个包到达时清空输入；服务端可能随后以失败的
        // ItemsChanged 结束请求，失败时必须保留用户的表单内容。
    }

    /// <summary>发送前锁定来源；物品真正扣除由后续 ItemsChanged 决定。</summary>
    public List<CellLinkInfo> PrepareMailSend()
    {
        if (_pendingMailLinks.Count > 0)
            return null;

        var links = GetSendLinks();
        _pendingMailLinks.Clear();
        _pendingMailLinks.AddRange(links);
        foreach (var link in links)
        {
            var source = GetSourceCell(link);
            if (source == null) continue;
            source.Locked = true;
            source.UpdateBorder();
        }

        foreach (var cell in _sendMailCells ?? Array.Empty<DXItemCell>())
        {
            if (cell == null) continue;
            if (cell.ItemGrid != null && cell.Slot >= 0 && cell.Slot < cell.ItemGrid.Length)
                cell.ItemGrid[cell.Slot] = null;
            cell.LinkedSourceGrid = GridType.None;
            cell.LinkedSourceSlot = -1;
            cell.RefreshItem();
        }
        Array.Clear(_sendMailItems, 0, _sendMailItems.Length);
        return links;
    }

    public void ItemsChanged(IEnumerable<CellLinkInfo> links, bool success = false)
    {
        foreach (var link in links ?? Enumerable.Empty<CellLinkInfo>())
        {
            for (int i = _pendingMailLinks.Count - 1; i >= 0; i--)
            {
                var pending = _pendingMailLinks[i];
                if (pending.GridType == link.GridType && pending.Slot == link.Slot)
                {
                    GetSourceCell(pending)?.UnlockForTrade();
                    _pendingMailLinks.RemoveAt(i);
                }
            }
        }
        if (_pendingMailLinks.Count == 0 && success)
        {
            if (_page == 2)
            {
                if (_recipient != null) _recipient.Text = string.Empty;
                if (_subject != null) _subject.Text = string.Empty;
                if (_message != null) _message.Text = string.Empty;
            }
            _mailSending = false;
        }
        else if (_pendingMailLinks.Count == 0 && !success)
        {
            // 失败回包结束发送尝试，但保留输入供用户修正后重发。
            _mailSending = false;
        }
    }

    public void CancelPendingMailLinks()
    {
        foreach (var link in _pendingMailLinks)
            GetSourceCell(link)?.UnlockForTrade();
        _pendingMailLinks.Clear();
        _mailSending = false;
    }

    private static DXItemCell GetSourceCell(CellLinkInfo link)
    {
        var game = GameScene.Game;
        if (game == null || link == null) return null;
        var cells = link.GridType switch
        {
            GridType.Inventory => game.InventoryCells,
            GridType.Storage => game.StorageCells,
            GridType.PartsStorage => game.PartsStorageCells,
            GridType.CompanionInventory => game.CompanionInventoryCells,
            _ => Array.Empty<DXItemCell>(),
        };
        return link.Slot >= 0 && link.Slot < cells.Length ? cells[link.Slot] : null;
    }

    private void BuildFriendsPage()
    {
        AddBodyLabel(Lang.CommunicationOnlineLabel, 25, 10, 9, Colors.White).Size = new Vector2I(120, 18);
        _friendStatus = new DXButton { Text = Lang.GuildMemberRowOnlineLabel, FontSize = 9, Size = new Vector2I(122, 18), Location = new Vector2I(151, 10), LibraryFile = LibraryFile.Interface, Index = -1 };
        _friendStatus.MouseClick += (o, e) => GameScene.Game?.CycleOnlineState();
        _body.AddControl(_friendStatus);
        AddBodyLabel(Lang.CommunicationViewStateLabel, 25, 31, 9, Colors.White).Size = new Vector2I(120, 18);
        _friendFilter = new DXButton { Text = FriendFilterText(), FontSize = 9, Size = new Vector2I(122, 18), Location = new Vector2I(151, 31), LibraryFile = LibraryFile.Interface, Index = -1 };
        _friendFilter.MouseClick += (o, e) => { _friendStateFilter = (_friendStateFilter + 1) % 3; _friendFilter.Text = FriendFilterText(); RebuildFriends(); };
        _body.AddControl(_friendFilter);
        RebuildFriends();
    }

    private void BuildBlockPage()
    {
        AddBodyLabel(Lang.CommunicationUi185Label, 8, 5, 11, new Color(1f, 0.85f, 0.3f));
        _blockAdd.MouseClick -= AddBlockInline;
        _blockAdd.MouseClick += AddBlockInline;
        _blockRemove.MouseClick -= RemoveSelectedBlock;
        _blockRemove.MouseClick += RemoveSelectedBlock;
        void AddBlockInline(object sender, EventArgs args)
        {
            var input = new DXTextInput { Location = new Vector2I(151, 10), Size = new Vector2I(122, 18), MaxLength = Globals.MaxCharacterNameLength };
            _body.AddControl(input);
            input.GrabFocus();
            input.TextSubmitted += value =>
            {
                if (!string.IsNullOrWhiteSpace(value)) GameScene.Game?.SendBlockAdd(value.Trim());
                _body.RemoveControl(input);
                input.QueueFree();
            };
        }
        void RemoveSelectedBlock(object sender, EventArgs args)
        {
            if (_blocks.Count > 0) GameScene.Game?.SendBlockRemove(_blocks[Math.Clamp(_scroll.Value, 0, _blocks.Count - 1)].Index);
        }
        int offset = _scroll?.Value ?? 0;
        int y = 65 - offset;
        foreach (var block in _blocks)
        {
            var row = new DXButton { Text = block.Name, FontSize = 10, Size = new Vector2I(170, 27), Location = new Vector2I(8, y), LibraryFile = LibraryFile.Interface, Index = -1 };
            int index = block.Index;
            row.MouseClick += (o, e) => GameScene.Game?.SendBlockRemove(index);
            _body.AddControl(row);
            y += 31;
        }
        _scroll.MaxValue = Math.Max(_scroll.VisibleSize, 72 + _blocks.Count * 31);
    }

    public void SetBlocks(IEnumerable<ClientBlockInfo> blocks) { _blocks.Clear(); if (blocks != null) _blocks.AddRange(blocks.Where(x => x != null)); if (_page == 3) ShowPage(3); }
    public void ApplyBlock(ClientBlockInfo block) { if (block == null) return; _blocks.RemoveAll(x => x.Index == block.Index); _blocks.Add(block); if (_page == 3) ShowPage(3); }
    public void RemoveBlock(int index) { _blocks.RemoveAll(x => x.Index == index); if (_page == 3) ShowPage(3); }

    private void RebuildBlockPage() => ShowPage(3);

    private string FriendFilterText() => _friendStateFilter switch { 1 => Lang.CommunicationOnlineLabel2, 2 => Lang.CommunicationOfflineLabel, _ => Lang.CommunicationFriendLabel };

    /// <summary>
    /// 旧版 CommunicationDialog.UpdateStateLabel：自己的在线状态按钮
    /// 文本 + 颜色（在线绿/离开橙/忙碌红/离线灰）。点击切换后由
    /// GameScene.CycleOnlineState 调用。
    /// </summary>
    public void RefreshOwnState(OnlineState state)
    {
        if (_friendStatus == null) return;
        _friendStatus.Text = state switch
        {
            OnlineState.Online => Lang.GuildMemberRowOnlineLabel,
            OnlineState.Away => Lang.CommunicationAwayLabel,
            OnlineState.Busy => Lang.CommunicationBusyLabel,
            _ => Lang.RankingOfflineLabel,
        };
        _friendStatus.TextColour = state switch
        {
            OnlineState.Online => Colors.LimeGreen,
            OnlineState.Away => Colors.Orange,
            OnlineState.Busy => Colors.Red,
            _ => Colors.Gray,
        };
    }

    private void RebuildFriends()
    {
        if (_page != 0 || _body == null) return;
        foreach (var child in _body.GetChildren().OfType<Node>())
        {
            if (child is DXControl control && control != _friendStatus && control != _friendFilter && control != _friendInput)
            {
                _body.RemoveControl(control);
                control.QueueFree();
            }
        }
        var visible = _friends.Where(x => _friendStateFilter == 0 || (_friendStateFilter == 1 ? x.State != OnlineState.Offline : x.State == OnlineState.Offline)).ToList();
        int offset = _scroll?.Value ?? 0;
        for (int i = 0; i < visible.Count; i++)
        {
            var friend = visible[i];
            string state = friend.State switch { OnlineState.Online => Lang.GuildMemberRowOnlineLabel, OnlineState.Busy => Lang.CommunicationBusyLabel, OnlineState.Away => Lang.CommunicationAwayLabel, _ => Lang.RankingOfflineLabel };
            var row = new DXButton { Text = $"{friend.Name}  [{state}]", FontSize = 10, TextColour = friend.State == OnlineState.Offline ? Colors.Gray : Colors.White, Size = new Vector2I(260, 32), Location = new Vector2I(12, 65 + i * 36 - offset), LibraryFile = LibraryFile.Interface, Index = -1 };
            int friendIndex = friend.Index;
            row.MouseClick += (o, e) => { _selectedFriendIndex = friendIndex; if (_friendRemove != null) _friendRemove.Enabled = true; };
            row.GuiInput += input =>
            {
                if (input is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                {
                    GameScene.Game?.SendFriendRemove(friendIndex);
                    GetViewport().SetInputAsHandled();
                }
            };
            _body.AddControl(row);
        }
        if (visible.Count == 0) AddBodyLabel(Lang.CommunicationFriendLabel2, 10, 92, 10, Colors.White);
        _scroll.MaxValue = Math.Max(_scroll.VisibleSize, 65 + visible.Count * 36);
    }

    private void RebuildReceived()
    {
        _pageBackground.Index = 202;
        foreach (var child in _body.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _body.RemoveControl(control);
            else _body.RemoveChild(node);
            node.QueueFree();
        }
        AddBodyHeader(Lang.CommunicationDialogReceivedTabCategoryLabel, 15, 5, 50);
        AddBodyHeader(Lang.CommunicationDialogReceivedTabTitleLabel, 65, 5, 140);
        AddBodyHeader(Lang.CommunicationDateLabel, 200, 5, 65);
        int offset = _scroll?.Value ?? 0;
        for (int i = 0; i < _mails.Count; i++)
        {
            var mail = _mails[i];
            string category = mail.HasItem ? Lang.ConsignmentItemLabel : mail.Gold > 0 ? Lang.TradeDialogGoldLabel : string.Empty;
            var row = new DXButton { Text = $"{category,-4}{(mail.Opened ? "" : "● ")}{mail.Subject}  {mail.Date:MM/dd}", FontSize = 9, TextColour = mail.Opened ? Colors.White : new Color(1f, 0.85f, 0.3f), Size = new Vector2I(240, 40), Location = new Vector2I(18, 43 + i * 49 - offset), LibraryFile = LibraryFile.Interface, Index = -1 };
            int mailIndex = mail.Index;
            row.MouseClick += (o, e) => OpenMail(mailIndex);
            _body.AddControl(row);
        }
        _scroll.MaxValue = Math.Max(_scroll.VisibleSize, _mails.Count);
    }

    private void OpenMail(int index)
    {
        var mail = _mails.FirstOrDefault(x => x.Index == index);
        if (mail == null) return;
        // 原版已读邮件再次打开只重绘详情，不重复发送 MailOpened。
        if (ShouldSendMailOpened(mail.Opened))
        {
            mail.Opened = true;
            UnreadChanged?.Invoke(HasUnread);
            GameScene.Game?.SendMailOpened(index);
        }
        foreach (var child in _body.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _body.RemoveControl(control);
            else _body.RemoveChild(node);
            node.QueueFree();
        }
        _scroll.Visible = false;
        AddBodyLabel(string.Format(Lang.CommunicationUi198Label, mail.Sender), 15, 8, 9, Colors.White);
        AddBodyLabel(string.Format(Lang.CommunicationUi199Label, mail.Subject), 15, 27, 9, Colors.White);
        AddBodyLabel(string.Format(Lang.CommunicationDateLabel2, mail.Date), 15, 46, 9, Colors.White);
        _detail = new DXTextArea { Text = string.Format(Lang.CommunicationGoldLabel, mail.Message, mail.Gold), Location = new Vector2I(15, 73), Size = new Vector2I(241, 167), ReadOnly = true, MaxLength = 0 };
        _body.AddControl(_detail);
        _pageBackground.Index = 205;
        _readMailItems = new ClientUserItem[7];
        _readGrid = new DXItemGrid
        {
            GridType = GridType.SendMail,
            ItemGrid = _readMailItems,
            GridSize = new Vector2I(7, 1),
            Location = new Vector2I(13, 265),
            ReadOnly = true,
        };
        _body.AddControl(_readGrid);
        _readMessageScroll ??= new DXVScrollBar { Location = new Vector2I(262, 68), Size = new Vector2I(20, 178), VisibleSize = 12, Change = 1, HideWhenNoScroll = false };
        if (_readMessageScroll.GetParent() == null) _body.AddControl(_readMessageScroll);
        _readMessageScroll.Visible = true;
        _readMessageScroll.MaxValue = Math.Max(_readMessageScroll.VisibleSize, (mail.Message ?? string.Empty).Split('\n').Length + 1);
        _readMessageScroll.ValueChanged -= ReadMessageScrollChanged;
        _readMessageScroll.ValueChanged += ReadMessageScrollChanged;
        foreach (var item in mail.Items ?? new List<ClientUserItem>())
        {
            if (item == null || item.Slot < 0 || item.Slot >= _readMailItems.Length) continue;
            _readMailItems[item.Slot] = item;
            var cell = _readGrid.Cells[item.Slot];
            int slot = item.Slot;
            cell.MouseClick += (o, e) =>
            {
                if (!CanGetMailItem(cell.Item) || !_pendingMailItemGets.Add((mail.Index, slot))) return;
                GameScene.Game?.SendMailGetItem(mail.Index, slot);
            };
        }
        foreach (var cell in _readGrid.Cells) cell?.RefreshItem();
        var back = new DXButton { Text = Lang.CommunicationBackLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Size = new Vector2I(65, 25), Location = new Vector2I(8, 289), LibraryFile = LibraryFile.Interface, Index = -1 };
        back.MouseClick += (o, e) => ShowPage(1);
        _body.AddControl(back);

        _readReplyButton ??= new DXButton { Text = Lang.CommunicationMailLabel2, Type = DXButton.ButtonType.Default, FontSize = 9, Size = new Vector2I(100, 25), Location = new Vector2I(43, 384), LibraryFile = LibraryFile.Interface, Index = -1 };
        _readDeleteButton ??= new DXButton { Text = Lang.CommunicationDialogReadTabDeleteButtonLabel, Type = DXButton.ButtonType.Default, FontSize = 9, Size = new Vector2I(100, 25), Location = new Vector2I(153, 384), LibraryFile = LibraryFile.Interface, Index = -1 };
        if (_readReplyButton.GetParent() == null) AddControl(_readReplyButton);
        if (_readDeleteButton.GetParent() == null) AddControl(_readDeleteButton);
        _readReplyButton.Visible = true;
        _readDeleteButton.Visible = true;
        _readReplyButton.MouseClick -= ReplyMail;
        _readReplyButton.MouseClick += ReplyMail;
        _readDeleteButton.MouseClick -= DeleteMail;
        _readDeleteButton.MouseClick += DeleteMail;

        void ReplyMail(object sender, EventArgs args)
        {
            ShowPage(2);
            if (_recipient != null) _recipient.Text = mail.Sender;
            if (_subject != null) _subject.Text = mail.Subject.StartsWith("RE: ", StringComparison.OrdinalIgnoreCase) ? mail.Subject : $"RE: {mail.Subject}";
        }
        void DeleteMail(object sender, EventArgs args)
        {
            if (!CanDeleteMail(mail))
            {
                GameScene.Game?.ReceiveChat("无法删除含物品的邮件", MessageType.System);
                return;
            }
            GameScene.Game?.SendMailDelete(mail.Index);
            ShowPage(1);
        }
    }

    public static bool ShouldSendMailOpened(bool alreadyOpened) => !alreadyOpened;
    public static bool CanGetMailItem(ClientUserItem item) => item != null && item.Slot >= 0;
    public static bool CanDeleteMail(ClientMailInfo mail) => mail?.Items == null || mail.Items.Count == 0;

    private void BuildSendPage()
    {
        AddBodyLabel(Lang.CommunicationUi204Label, 8, 8, 9, new Color(1f, 0.85f, 0.3f));
        _recipient = new DXTextInput { Location = new Vector2I(86, 11), Size = new Vector2I(115, 18), MaxLength = Globals.MaxCharacterNameLength };
        // 原版 RecipientBox_TextChanged：空→原色、合法→绿、否则红。
        _recipient.TextChanged += _ => UpdateRecipientBox();
        _body.AddControl(_recipient);
        AddBodyLabel(Lang.CommunicationUi205Label, 8, 30, 9, new Color(1f, 0.85f, 0.3f));
        _subject = new DXTextInput { Location = new Vector2I(86, 30), Size = new Vector2I(155, 18), MaxLength = 30 };
        _body.AddControl(_subject);
        _message = new DXTextArea { Location = new Vector2I(15, 55), Size = new Vector2I(241, 185), MaxLength = 300 };
        _body.AddControl(_message);
        _messageScroll ??= new DXVScrollBar { Location = new Vector2I(262, 49), Size = new Vector2I(20, 198), VisibleSize = 13, Change = 1, HideWhenNoScroll = false };
        if (_messageScroll.GetParent() == null) _body.AddControl(_messageScroll);
        _messageScroll.Visible = true;
        _message.TextChanged += value => _messageScroll.MaxValue = Math.Max(_messageScroll.VisibleSize, (value ?? string.Empty).Split('\n').Length + 1);
        AddBodyLabel(Lang.CommunicationUi206Label, 10, 246, 9, new Color(1f, 0.85f, 0.3f));
        _sendMailCells = new DXItemCell[5];
        for (int i = 0; i < _sendMailCells.Length; i++)
        {
            _sendMailCells[i] = new DXItemCell { GridType = GridType.SendMail, Slot = i, ItemGrid = _sendMailItems, Location = new Vector2I(13 + i * 35, 265) };
            _body.AddControl(_sendMailCells[i]);
        }
        AddBodyLabel(Lang.TradeDialogGoldLabel, 8, 304, 9, new Color(1f, 0.85f, 0.3f));
        var gold = new DXTextInput { Text = "0", Location = new Vector2I(86, 303), Size = new Vector2I(122, 18), MaxLength = 10 };
        // 原版 SendGoldBox.ValueTextBox.ValueChanged（GoldBox_ValueChanged）：
        // MaxValue=2000000000 钳制 + 边框色（0 原色、合法绿、否则红）。
        gold.TextChanged += _ => UpdateGoldBox(gold);
        _body.AddControl(gold);
        if (_sendButton == null)
        {
            _sendButton = ActionButton(Lang.CommunicationDialogSendTabSendButtonLabel, new Vector2I(113, 383), 70);
            _sendButton.MouseClick += (o, e) =>
            {
                if (!_mailSending && IsMailSendValid(gold, out long amount))
                {
                    var links = PrepareMailSend();
                    if (links == null) return;
                    _mailSending = true;
                    GameScene.Game?.SendMail(_recipient.Text.Trim(), _subject.Text.Trim(), _message.Text, links, amount);
                }
            };
            AddControl(_sendButton);
        }
        _sendButton.Visible = true;
        UpdateSendState(gold);
    }

    private bool IsMailSendValid(DXTextInput gold, out long amount)
    {
        amount = 0;
        long available = GameScene.Game?.Currencies.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        return !_mailSending && _recipient != null && Globals.CharacterReg.IsMatch(_recipient.Text ?? string.Empty)
            && gold != null && GoldBoxValid(gold.Text ?? string.Empty, available) && long.TryParse(gold.Text, out amount);
    }

    private void UpdateSendState(DXTextInput gold = null)
    {
        if (_sendButton == null) return;
        _sendButton.Enabled = IsMailSendValid(gold, out _);
    }

    private static readonly Color _inputGreen = new(0.3f, 0.9f, 0.35f);
    private static readonly Color _inputRed = new(1f, 0.25f, 0.25f);

    /// <summary>原版 DXNumberBox.MaxValue=2000000000 钳制：输入超限自动修正为上限。</summary>
    public static string ClampGoldInput(string text)
        => long.TryParse(text, out long v) && v > 2_000_000_000L ? "2000000000" : text;

    /// <summary>原版 GoldValid = Value >= 0 && Value <= User.Gold.Amount（附加 2e9 上限）。</summary>
    public static bool GoldBoxValid(string text, long available)
        => long.TryParse(text, out long amount) && amount >= 0 && amount <= 2_000_000_000L && amount <= available;

    /// <summary>原版 GoldBox_ValueChanged 边框色：0→原色、合法→绿、否则红。</summary>
    public static Color GoldBorderColour(string text, long available)
        => text == "0" ? DXTextInput.DefaultBorderColour
            : GoldBoxValid(text, available) ? _inputGreen : _inputRed;

    /// <summary>原版 RecipientBox_TextChanged 边框色：空→原色、合法→绿、否则红。</summary>
    public static Color RecipientBorderColour(string text)
        => string.IsNullOrEmpty(text) ? DXTextInput.DefaultBorderColour
            : Globals.CharacterReg.IsMatch(text) ? _inputGreen : _inputRed;

    /// <summary>原版 RecipientBox_TextChanged：空→原色、合法→绿、否则红。</summary>
    private void UpdateRecipientBox()
    {
        _recipient.BorderColour = RecipientBorderColour(_recipient?.Text ?? string.Empty);
        UpdateSendState();
    }

    /// <summary>原版 GoldBox_ValueChanged：DXNumberBox MaxValue=2000000000 钳制
    /// （输入超限自动修正为上限，不改红）；0→原色、合法（≤可用金币）→绿、否则红。</summary>
    private void UpdateGoldBox(DXTextInput gold)
    {
        if (gold == null) return;
        string clamped = ClampGoldInput(gold.Text);
        if (clamped != gold.Text)
        {
            gold.Text = clamped;
            return; // TextChanged 重入，递归终止于钳制后的值
        }
        long available = GameScene.Game?.Currencies.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;
        gold.BorderColour = GoldBorderColour(gold.Text, available);
        UpdateSendState(gold);
    }

    public bool TryRouteItem(DXItemCell source)
    {
        if (_page != 2 || source?.GridType is not (GridType.Inventory or GridType.Storage or GridType.PartsStorage)) return false;
        var target = _sendMailCells.FirstOrDefault(c => c.LinkedSourceSlot < 0);
        if (target == null) return false;
        if (source.Item == null || source.Item.Flags.HasFlag(UserItemFlags.Marriage) ||
            (source.GridType == GridType.Inventory && !GameScene.Game.InSafeZone)) return false;
        source.MoveItem(target);
        return true;
    }

    private List<CellLinkInfo> GetSendLinks() => _sendMailCells
        .Where(c => c.LinkedSourceSlot >= 0)
        .Select(c => new CellLinkInfo { GridType = c.LinkedSourceGrid, Slot = c.LinkedSourceSlot, Count = c.Item?.Count ?? 1 })
        .ToList();

    private DXLabel AddBodyLabel(string text, int x, int y, int size, Color colour)
    {
        var label = new DXLabel { Text = text, FontSize = size, TextColour = colour, Location = new Vector2I(x, y), Size = new Vector2I(280, 80), IsControl = false };
        _body.AddControl(label);
        return label;
    }

    private DXLabel AddBodyHeader(string text, int x, int y, int width)
    {
        var label = AddBodyLabel(text, x, y, 9, Colors.White);
        label.Size = new Vector2I(width, 20);
        label.AutoSize = false;
        label.Align = HorizontalAlignment.Center;
        label.VAlign = VerticalAlignment.Center;
        return label;
    }

    private void ReadMessageScrollChanged(object sender, EventArgs e)
    {
        if (_detail != null) _detail.ScrollVertical = _readMessageScroll.Value;
    }
}
