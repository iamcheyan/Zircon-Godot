using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using S = Library.Network.ServerPackets;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 GuildDialog(Interface 260)：页签、成员滚动列表、仓库筛选和管理按钮。</summary>
public partial class GuildDialog : DXWindow
{
    private readonly DXImageControl _background;
    private readonly DXControl _content;
    private readonly DXVScrollBar _scroll;
    private readonly List<DXLabel> _rows = new();
    private readonly List<DXButton> _tabButtons = new();
    private int _tab;
    private readonly ClientUserItem[] _storageItems = new ClientUserItem[1000];
    private DXItemGrid _storageGrid;
    private ClientGuildInfo _guild;
    private DXTextInput _inviteName;
    private DXButton _inviteButton, _increaseMemberButton, _increaseStorageButton, _manageButton;
    private DXControl _guildInvitePanel;
    private DXTextInput _createName, _createMembers, _createStorage;
    private DXTextArea _noticeArea;
    private DXVScrollBar _noticeScroll;
    private DXTextInput _storageFilter;
    private DXButton _colourPicker;
    private DXImageControl _flagBase, _flagColour;
    private int _previewFlag;
    private DXButton _closeButton;

    public bool HasGuild => _guild != null;
    public long GuildFunds => _guild?.GuildFunds ?? 0;
    public int GuildFlag => _guild?.Flag ?? -1;
    public System.Drawing.Color GuildColour => _guild?.Colour ?? System.Drawing.Color.White;
    public DXItemCell[] GuildStorageCells => _storageGrid?.Cells ?? System.Array.Empty<DXItemCell>();
    public ClientUserItem[] GuildStorageItems => _storageItems;
    public int StorageLimit => _guild?.StorageLimit ?? 0;

    // 原版 RefreshStorage: GridSize = (11, Max(20, Ceil(StorageLimit / 14)))，
    // 容量决定行数；每行 11 列。
    public static Vector2I StorageGridSize(int limit)
        => new(11, Math.Max(20, (int)Math.Ceiling(limit / 14f)));

    // 超出 StorageLimit 的格子不可交互（服务端同样拒绝），客户端先行禁用。
    public static bool StorageCellEnabled(int index, int limit) => index < Math.Max(0, limit);

    public GuildDialog()
    {
        HasTitle = false; HasFooter = false; Movable = true; Size = new Vector2I(456, 556);
        _background = new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 260, MouseFilter = MouseFilterEnum.Ignore }; AddControl(_background);
        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this); AddControl(_closeButton);
        AddControl(new DXLabel { Text = Lang.GuildDialogTitle, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(456, 18), IsControl = false });
        string[] tabs = { Lang.GuildCreateLabel, Lang.GuildDialogMembersTabLabel, Lang.GuildDialogStorageTabLabel, Lang.GuildDialogWarTabLabel, Lang.GuildUi292Label, Lang.GuildDialogCastleTabLabel };
        for (int i = 0; i < tabs.Length; i++) AddTab(tabs[i], 14 + i * 76, i);
        _content = new DXControl { Location = new Vector2I(12, 68), Size = new Vector2I(410, 415), Clip = true }; AddControl(_content);
        _scroll = new DXVScrollBar { Location = new Vector2I(424, 68), Size = new Vector2I(16, 415), VisibleSize = 415, Change = 1 }; _scroll.ValueChanged += (o, e) => RefreshRows(); AddControl(_scroll);
        _inviteName = new DXTextInput { Location = new Vector2I(18, 468), Size = new Vector2I(165, 24) };
        AddControl(_inviteName);
        _inviteButton = new DXButton { Text = Lang.GuildDialogManageTabMembershipAddButtonLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(190, 468), Size = new Vector2I(100, 28) };
        _inviteButton.MouseClick += (o, e) => { if (!string.IsNullOrWhiteSpace(_inviteName.Text)) GameScene.Game?.SendGuildInviteMember(_inviteName.Text.Trim()); };
        AddControl(_inviteButton);
        _increaseMemberButton = new DXButton { Text = Lang.GuildMemberLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(18, 500), Size = new Vector2I(120, 28) };
        _increaseMemberButton.MouseClick += (o, e) => GameScene.Game?.SendGuildIncreaseMember(); AddControl(_increaseMemberButton);
        _increaseStorageButton = new DXButton { Text = Lang.GuildStorageLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(146, 500), Size = new Vector2I(100, 28) };
        _increaseStorageButton.MouseClick += (o, e) => GameScene.Game?.SendGuildIncreaseStorage(); AddControl(_increaseStorageButton);
        _manageButton = new DXButton { Text = Lang.GuildMemberLabel2, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(362, 500), Size = new Vector2I(80, 28) };
        _manageButton.MouseClick += (o, e) => GameScene.Game?.OpenGuildMemberDialog(0, Lang.GuildDialogMembersTabLabel, Lang.GuildDialogMembersTabLabel, GuildPermission.None);
        AddControl(_manageButton);
        UpdateTabVisibility();
        RefreshRows();
    }

    /// <summary>旧版 EI 行会窗口：GameInter F600，根 446×596。</summary>
    public void ApplyLegacyEiLayout()
    {
        Size = new Vector2I(446, 596);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 600;
        _background.FixedSize = true;
        _background.StretchImage = false;
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 600);
        _background.Location = Vector2I.Zero;
        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(418, 570);
        _closeButton.Size = new Vector2I(28, 26);
        _content.Location = new Vector2I(18, 80);
        _content.Size = new Vector2I(410, 415);
        _scroll.Location = new Vector2I(428, 80);
        _scroll.Size = new Vector2I(16, 415);
        UpdateClientAreaForLegacySkin();
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        bool ok = Size == new Vector2I(446, 596)
            && _background.LibraryFile == LibraryFile.GameInter && _background.Index == 600
            && _content.Location == new Vector2(18, 80)
            && _content.Size == new Vector2(410, 415);
        details = $"size={Size} frame={_background.Index} content={_content.Size}@{_content.Location}";
        return ok;
    }

    private void AddTab(string text, int x, int page)
    {
        var tab = new DXButton { Text = text, FontSize = 10, TextColour = new Color(1f, .85f, .3f), LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(x, 39), Size = new Vector2I(68, 25) };
        tab.MouseClick += (o, e) =>
        {
            if (_guild == null && page > 0) return;
            SelectTab(page);
        };
        AddControl(tab);
        _tabButtons.Add(tab);
    }

    private void RefreshRows()
    {
        foreach (var child in _content.GetChildren().OfType<Node>())
        {
            if (child is DXControl control) _content.RemoveControl(control);
            else _content.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        _storageGrid = null;
        // 成员页按像素滚动；仓库页按网格行滚动。两者不能共用同一个
        // VisibleSize，否则行会仓库的 MaxValue 会被错误地压成负值。
        _scroll.VisibleSize = _tab == 2 ? 10 : 415;
        _scroll.Change = 1;
        bool hasGuild = _guild != null;
        _inviteName.Visible = hasGuild;
        _inviteButton.Visible = hasGuild;
        _increaseMemberButton.Visible = hasGuild;
        _increaseStorageButton.Visible = hasGuild;
        _manageButton.Visible = hasGuild;
        var members = _guild?.Members ?? new List<ClientGuildMemberInfo>();
        _scroll.MaxValue = _tab == 1
            ? Mathf.Max(_scroll.VisibleSize, members.Count * 24)
            : _tab == 2 && _guild != null
                ? StorageGridSize(_guild.StorageLimit).Y
                : 0;
        if (_tab == 0)
        {
            if (_guild == null) BuildCreatePage();
            else BuildHomePage(members);
        }
        else if (_tab == 1)
        {
            AddText(Lang.GuildDialogMembersTabLabel, 18, 7);
            AddText(Lang.GuildNameLabel, 18, 28);
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                bool online = member.Online == TimeSpan.MinValue;
                // 旧版成员行三键: 左键=编辑权限/职务 (GuildMemberBox), 右键=大地图定位,
                // 中键=组队邀请 (GuildDialog.cs:2630-2675)。
                var row = new GuildMemberRow(this)
                {
                    Location = new Vector2I(4, 48 + i * 23),
                    Size = new Vector2I(410, 22),
                    IsControl = true,
                };
                row.AddControl(new DXLabel
                {
                    Text = $"{i + 1,2}  {member.Name,-16} {member.Rank,-8} {(online ? Lang.GuildMemberRowOnlineLabel : Lang.RankingOfflineLabel)}  贡献 {member.TotalContribution:#,##0}",
                    FontSize = 9,
                    Location = Vector2I.Zero,
                    IsControl = false,
                });
                row.Member = member;
                _content.AddControl(row);
            }
        }
        else if (_tab == 2)
        {
            AddText(Lang.GuildDialogStorageTabLabel, 18, 5);
            AddText(Lang.GuildDialogStorageTabNameLabel, 18, 28);
            _storageFilter = new DXTextInput { Location = new Vector2I(62, 25), Size = new Vector2I(110, 20) };
            _content.AddControl(_storageFilter);
            var clearFilter = new DXButton { Text = Lang.GuildDialogStorageTabClearButtonLabel, FontSize = 9, Location = new Vector2I(180, 23), Size = new Vector2I(58, 24), LibraryFile = LibraryFile.Interface, Index = -1 };
            clearFilter.MouseClick += (o, e) => { _storageFilter.Text = string.Empty; RefreshRows(); };
            _content.AddControl(clearFilter);
            int storageLimit = Math.Max(0, _guild?.StorageLimit ?? 0);
            int storageRows = StorageGridSize(storageLimit).Y;
            _storageGrid = new DXItemGrid
            {
                GridType = GridType.GuildStorage,
                ItemGrid = _storageItems,
                // 原版行会仓库是 11 列，容量决定行数；不能固定为 10x8，
                // 否则升级仓库后的槽位永远无法点击。
                GridSize = new Vector2I(11, storageRows),
                Location = new Vector2I(8, 45),
                VisibleHeight = 10,
                ScrollValue = _scroll.Value,
                GridPadding = 1,
            };
            _content.AddControl(_storageGrid);
            _storageGrid.CreateGrid();
            for (int i = 0; i < _storageGrid.Cells.Length; i++)
                _storageGrid.Cells[i].Enabled = i < storageLimit;
        }
        else if (_tab == 3) BuildWarPage();
        else if (_tab == 4) BuildStylePage();
        else BuildCastlePage();
        RepositionRows();
    }

    private void BuildCreatePage()
    {
        AddText(Lang.GuildDialogCreateTabCreateButtonLabel, 18, 16);
        AddText(Lang.GuildGuildLabel, 18, 48);
        _createName = new DXTextInput { Location = new Vector2I(150, 43), Size = new Vector2I(190, 20) };
        _content.AddControl(_createName);

        AddText(Lang.GuildCreateLabel2, 18, 82);
        var useGold = new DXCheckButton(string.Empty) { Location = new Vector2I(150, 76), Size = new Vector2I(18, 18), Checked = true };
        var useHorn = new DXCheckButton(string.Empty) { Location = new Vector2I(150, 98), Size = new Vector2I(18, 18) };
        _content.AddControl(useGold);
        _content.AddControl(useHorn);
        AddText(Lang.GuildGoldLabel, 174, 76);
        AddText(Lang.GuildGuildLabel2, 174, 98);
        useGold.Changed += (o, e) => { if (useGold.Checked) useHorn.Checked = false; if (!useGold.Checked && !useHorn.Checked) useGold.Checked = true; };
        useHorn.Changed += (o, e) => { if (useHorn.Checked) useGold.Checked = false; if (!useGold.Checked && !useHorn.Checked) useGold.Checked = true; };

        AddText(Lang.GuildUi302Label, 18, 132);
        AddText(Lang.GuildMemberLabel3, 18, 162);
        _createMembers = new DXTextInput { Text = "0", Location = new Vector2I(150, 157), Size = new Vector2I(80, 20) };
        _content.AddControl(_createMembers);
        AddText(Lang.GuildStorageLabel2, 18, 188);
        _createStorage = new DXTextInput { Text = "0", Location = new Vector2I(150, 183), Size = new Vector2I(80, 20) };
        _content.AddControl(_createStorage);

        AddText(Lang.GuildUi305Label, 18, 224);
        var cost = new DXLabel { Text = "7,500,000", FontSize = 10, TextColour = new Color(1f, .8f, .3f), Location = new Vector2I(150, 224), Size = new Vector2I(150, 20), IsControl = false };
        _content.AddControl(cost);
        void RefreshCreateCost()
        {
            long total = (useGold.Checked ? Globals.GuildCreationCost : 0L)
                + (long)ParseInput(_createMembers.Text) * Globals.GuildMemberCost
                + (long)ParseInput(_createStorage.Text) * Globals.GuildStorageCost;
            cost.Text = $"{Math.Min(int.MaxValue, total):#,##0}";
        }
        _createMembers.TextChanged += value => RefreshCreateCost();
        _createStorage.TextChanged += value => RefreshCreateCost();
        useGold.Changed += (o, e) => RefreshCreateCost();
        useHorn.Changed += (o, e) => RefreshCreateCost();
        RefreshCreateCost();
        var create = new DXButton { Text = Lang.GuildDialogCreateTabCreateButtonLabel, FontSize = 10, Size = new Vector2I(105, 27), Location = new Vector2I(150, 258), LibraryFile = LibraryFile.Interface, Index = -1 };
        create.MouseClick += (o, e) =>
        {
            if (string.IsNullOrWhiteSpace(_createName.Text) || !Globals.GuildNameRegex.IsMatch(_createName.Text.Trim())) return;
            GameScene.Game?.SendGuildCreate(_createName.Text.Trim(), useGold.Checked, ParseInput(_createMembers.Text), ParseInput(_createStorage.Text));
        };
        _content.AddControl(create);
        var starter = new DXButton { Text = Lang.GuildGuildLabel3, FontSize = 10, Size = new Vector2I(125, 27), Location = new Vector2I(18, 258), LibraryFile = LibraryFile.Interface, Index = -1 };
        starter.MouseClick += (o, e) => GameScene.Game?.SendJoinStarterGuild();
        _content.AddControl(starter);
    }

    private static int ParseInput(string text) => int.TryParse(text, out int value) ? Math.Max(0, value) : 0;

    public void ShowInvite(string name, string guildName)
    {
        if (_guildInvitePanel != null)
        {
            RemoveControl(_guildInvitePanel);
            _guildInvitePanel.QueueFree();
        }
        _guildInvitePanel = new DXControl { Location = new Vector2I(45, 185), Size = new Vector2I(350, 70), BackColour = new Color(0.04f, .025f, .02f, .98f), Border = true, BorderColour = new Color(1f, .75f, .25f) };
        _guildInvitePanel.AddControl(new DXLabel { Text = $"{name ?? Lang.GroupUnknownLabel} 邀请你加入行会：{guildName ?? Lang.GroupUnknownLabel}", FontSize = 10, Location = new Vector2I(8, 7), Size = new Vector2I(334, 22), IsControl = false });
        var accept = new DXButton { Text = Lang.GroupAcceptLabel, FontSize = 9, Size = new Vector2I(80, 23), Location = new Vector2I(82, 38), LibraryFile = LibraryFile.Interface, Index = -1 };
        accept.MouseClick += (o, e) => { GameScene.Game?.SendGuildResponse(guildName, true); RemoveControl(_guildInvitePanel); _guildInvitePanel.QueueFree(); _guildInvitePanel = null; };
        _guildInvitePanel.AddControl(accept);
        var reject = new DXButton { Text = Lang.GroupDeclineLabel, FontSize = 9, Size = new Vector2I(80, 23), Location = new Vector2I(188, 38), LibraryFile = LibraryFile.Interface, Index = -1 };
        reject.MouseClick += (o, e) => { GameScene.Game?.SendGuildResponse(guildName, false); RemoveControl(_guildInvitePanel); _guildInvitePanel.QueueFree(); _guildInvitePanel = null; };
        _guildInvitePanel.AddControl(reject);
        AddControl(_guildInvitePanel);
    }

    private void BuildHomePage(List<ClientGuildMemberInfo> members)
    {
        // 原版 HomeTab：公告区 403x252，右侧独立滚动条；统计面板从公告区下方开始。
        AddText(Lang.GuildGuildLabel5, 8, 0);
        _noticeArea = new DXTextArea
        {
            Text = _guild.Notice ?? string.Empty,
            ReadOnly = true,
            Location = new Vector2I(4, 27),
            Size = new Vector2I(382, 252),
            FontSize = 10,
            MaxLength = 1000,
        };
        _content.AddControl(_noticeArea);
        _noticeScroll = new DXVScrollBar
        {
            Location = new Vector2I(388, 24),
            Size = new Vector2I(16, 262),
            VisibleSize = 17,
            Change = 1,
        };
        _noticeScroll.ValueChanged += (o, e) => { if (_noticeArea != null) _noticeArea.ScrollVertical = _noticeScroll.Value; };
        _content.AddControl(_noticeScroll);
        var edit = new DXButton { Text = Lang.GuildDialogHomeTabNoticeEditButtonLabel, FontSize = 9, Size = new Vector2I(60, 24), Location = new Vector2I(328, 0), LibraryFile = LibraryFile.Interface, Index = -1 };
        edit.MouseClick += (o, e) => { if (_noticeArea != null) { _noticeArea.ReadOnly = false; _noticeArea.GrabFocus(); } };
        _content.AddControl(edit);
        var save = new DXButton { Text = Lang.GuildDialogHomeTabNoticeSaveButtonLabel, FontSize = 9, Size = new Vector2I(60, 24), Location = new Vector2I(262, 0), LibraryFile = LibraryFile.Interface, Index = -1 };
        save.MouseClick += (o, e) => { if (_noticeArea != null) { _noticeArea.ReadOnly = true; GameScene.Game?.SendGuildEditNotice(_noticeArea.Text); } };
        _content.AddControl(save);

        AddText(Lang.GuildGuildLabel6, 8, 287);
        AddText(Lang.GuildDialogMembersTabLabel, 18, 317);
        AddText($"{members.Count} / {_guild.MemberLimit}", 120, 317);
        AddText(Lang.ConsignmentGuildLabel, 18, 337);
        AddText($"{_guild.GuildFunds:#,##0}", 120, 337);
        AddText(Lang.GuildUi314Label, 18, 357);
        AddText($"{_guild.DailyGrowth:#,##0}", 120, 357);
        AddText(Lang.GuildContributionLabel, 218, 337);
        AddText($"{_guild.TotalContribution:#,##0}", 320, 337);
        AddText(Lang.GuildContributionLabel2, 218, 357);
        AddText($"{_guild.DailyContribution:#,##0}", 320, 357);
        AddText(Lang.GuildUi317Label, 18, 377);
        AddText($"{_guild.Tax}%", 120, 377);
        var taxInput = new DXTextInput { Text = _guild.Tax.ToString(), Location = new Vector2I(18, 394), Size = new Vector2I(80, 20) };
        _content.AddControl(taxInput);
        var tax = new DXButton { Text = Lang.GuildSettingsLabel, FontSize = 9, Size = new Vector2I(82, 24), Location = new Vector2I(105, 392), LibraryFile = LibraryFile.Interface, Index = -1 };
        tax.MouseClick += (o, e) => { if (long.TryParse(taxInput.Text, out var value)) GameScene.Game?.SendGuildTax(Math.Max(0, value)); };
        _content.AddControl(tax);
    }

    private void BuildStylePage()
    {
        AddText(Lang.GuildGuildLabel8, 18, 16);
        AddText(Lang.GuildUi320Label, 18, 58);
        _previewFlag = _guild?.Flag ?? 0;
        _flagBase = new DXImageControl { LibraryFile = LibraryFile.CastleFlag, Index = _previewFlag * 100, Location = new Vector2I(18, 85), FixedSize = true, Size = new Vector2I(100, 100), MouseFilter = MouseFilterEnum.Ignore };
        var guildColour = _guild?.Colour is System.Drawing.Color value ? value : System.Drawing.Color.White;
        _flagColour = new DXImageControl { LibraryFile = LibraryFile.CastleFlag, Index = _previewFlag * 100, Location = new Vector2I(18, 85), FixedSize = true, Size = new Vector2I(100, 100), MouseFilter = MouseFilterEnum.Ignore, Modulate = ToGodotColor(guildColour) };
        _content.AddControl(_flagBase);
        _content.AddControl(_flagColour);
        var previous = new DXButton { Text = Lang.GuildUi321Label, FontSize = 9, Size = new Vector2I(70, 25), Location = new Vector2I(8, 195), LibraryFile = LibraryFile.Interface, Index = -1 };
        previous.MouseClick += (o, e) => ChangeFlag(-1);
        _content.AddControl(previous);
        var next = new DXButton { Text = Lang.GuildUi322Label, FontSize = 9, Size = new Vector2I(70, 25), Location = new Vector2I(84, 195), LibraryFile = LibraryFile.Interface, Index = -1 };
        next.MouseClick += (o, e) => ChangeFlag(1);
        _content.AddControl(next);
        AddText(Lang.CommonControlColourPickerColourLabel, 230, 58);
        _colourPicker = new DXButton { Text = Lang.GuildSelectLabel, FontSize = 9, BackColour = ToGodotColor(guildColour), Location = new Vector2I(230, 85), Size = new Vector2I(110, 20), LibraryFile = LibraryFile.Interface, Index = -1 };
        _colourPicker.MouseClick += (o, e) =>
        {
            var palette = new[] { Colors.White, new Color(.85f, .2f, .2f), new Color(.2f, .75f, .3f), new Color(.25f, .45f, .95f), new Color(.8f, .65f, .2f) };
            int next = Array.FindIndex(palette, c => c.IsEqualApprox(_colourPicker.BackColour)) + 1;
            _colourPicker.BackColour = palette[next < 0 || next >= palette.Length ? 0 : next];
            if (_flagColour != null) _flagColour.Modulate = _colourPicker.BackColour;
        };
        _content.AddControl(_colourPicker);
        var save = new DXButton { Text = Lang.GuildSaveLabel, FontSize = 9, Size = new Vector2I(90, 25), Location = new Vector2I(230, 125), LibraryFile = LibraryFile.Interface, Index = -1 };
        save.MouseClick += (o, e) => GameScene.Game?.SendGuildColour(_colourPicker.BackColour);
        _content.AddControl(save);
    }

    private void BuildWarPage()
    {
        AddText(Lang.GuildUi325Label, 18, 16);
        AddText(Lang.GuildGuildLabel9, 18, 38);
        var enemy = new DXTextInput { Location = new Vector2I(85, 34), Size = new Vector2I(170, 24) };
        _content.AddControl(enemy);
        var war = new DXButton { Text = Lang.GuildGuildLabel10, FontSize = 9, Size = new Vector2I(92, 24), Location = new Vector2I(265, 34), LibraryFile = LibraryFile.Interface, Index = -1 };
        war.MouseClick += (o, e) => { if (!string.IsNullOrWhiteSpace(enemy.Text)) GameScene.Game?.SendGuildWar(enemy.Text.Trim()); };
        _content.AddControl(war);
        var castles = new List<CastleInfo>();
        if (Globals.MapInfoList != null)
            foreach (var map in Globals.MapInfoList.Binding)
                if (map?.Castles != null) castles.AddRange(map.Castles.Where(x => x != null));
        if (castles.Count == 0)
        {
            AddText(Lang.GuildUi328Label, 18, 58);
            return;
        }
        for (int i = 0; i < castles.Count; i++)
        {
            var castle = castles[i];
            int castleIndex = castle.Index;
            string owner = GameScene.Game?.CastleOwners.TryGetValue(castleIndex, out var value) == true && !string.IsNullOrWhiteSpace(value) ? value : Lang.GuildNoneLabel;
            DateTime warDate = GameScene.Game?.GetCastleWarDate(castleIndex) ?? DateTime.MinValue;
            string schedule = warDate == DateTime.MinValue ? Lang.GuildUi330Label : warDate <= DateTime.Now ? Lang.GuildCastlePanelInProgressText : warDate.ToString("yyyy-MM-dd HH:mm");
            int rowY = 78 + i * 92;
            AddText(string.Format(Lang.GuildUi331Label, castle.Name, owner, schedule), 18, rowY);
            var request = new DXButton { Text = Lang.GuildUi332Label, FontSize = 9, Size = new Vector2I(85, 24), Location = new Vector2I(315, rowY + 10), LibraryFile = LibraryFile.Interface, Index = -1 };
            int index = castleIndex;
            request.MouseClick += (o, e) => GameScene.Game?.SendGuildRequestConquest(index);
            _content.AddControl(request);
        }
        var gates = new DXButton { Text = Lang.GuildUi333Label, FontSize = 9, Size = new Vector2I(85, 24), Location = new Vector2I(18, 360), LibraryFile = LibraryFile.Interface, Index = -1 };
        gates.MouseClick += (o, e) => GameScene.Game?.SendGuildToggleCastleGates();
        _content.AddControl(gates);
        var repairGates = new DXButton { Text = Lang.GuildRepairLabel, FontSize = 9, Size = new Vector2I(85, 24), Location = new Vector2I(112, 360), LibraryFile = LibraryFile.Interface, Index = -1 };
        repairGates.MouseClick += (o, e) => GameScene.Game?.SendGuildRepairCastleGates();
        _content.AddControl(repairGates);
        var repairGuards = new DXButton { Text = Lang.GuildRepairLabel2, FontSize = 9, Size = new Vector2I(85, 24), Location = new Vector2I(206, 360), LibraryFile = LibraryFile.Interface, Index = -1 };
        repairGuards.MouseClick += (o, e) => GameScene.Game?.SendGuildRepairCastleGuards();
        _content.AddControl(repairGuards);
    }

    private void ChangeFlag(int change)
    {
        _previewFlag = (_previewFlag + change + 10) % 10;
        if (_flagBase != null) _flagBase.Index = _previewFlag * 100;
        if (_flagColour != null) _flagColour.Index = _previewFlag * 100;
        GameScene.Game?.SendGuildFlag(_previewFlag);
    }

    private static Godot.Color ToGodotColor(System.Drawing.Color colour)
        => new(colour.R / 255f, colour.G / 255f, colour.B / 255f, colour.A / 255f);

    private void AddText(string text, int x, int y)
    {
        var label = new DXLabel { Text = text, FontSize = 11, TextColour = Colors.White, DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(x, y), IsControl = false }; label.SetMeta("base_y", y); _content.AddControl(label); _rows.Add(label);
    }
    private void RepositionRows() { foreach (var row in _rows) row.Position = new Vector2(row.Position.X, (int)row.GetMeta("base_y") - _scroll.Value); }

    public bool TryRouteItem(DXItemCell source)
    {
        if (_tab != 2 || source?.Item == null || GameScene.Game?.InSafeZone != true) return false;
        if (source.GridType is not (GridType.Inventory or GridType.Storage or GridType.PartsStorage or GridType.Equipment) ||
            source.Item.Info?.CanTrade != true || source.Item.Flags.HasFlag(UserItemFlags.Marriage) ||
            source.Item.Flags.HasFlag(UserItemFlags.Bound)) return false;
        // 原版 MoveItem(DXItemGrid) 只会选择启用且未被临时 Link 占用的格子。
        // 直接选第一个空格会把升级容量之外的禁用格，或已有临时链接的格子当成目标，
        // 导致客户端显示投放成功但服务端拒绝/覆盖链接状态。
        var target = _storageGrid?.Cells?.FirstOrDefault(c =>
            c != null && c.Enabled && c.Item == null && c.LinkedSourceSlot < 0);
        if (target == null) return false;
        source.MoveItem(target);
        return true;
    }

    public void SetGuildItem(int slot, ClientUserItem item)
    {
        if (slot < 0 || slot >= _storageItems.Length) return;
        _storageItems[slot] = item;
        _storageGrid?.RefreshGrid();
    }

    public void ApplyGuild(ClientGuildInfo guild)
    {
        _guild = guild;
        UpdateTabVisibility();
        _background.Index = _guild == null ? 260 : 261;
        ResizeForBackground();
        Array.Clear(_storageItems, 0, _storageItems.Length);
        foreach (var item in guild?.Storage ?? new List<ClientUserItem>())
            if (item != null && item.Slot >= 0 && item.Slot < _storageItems.Length) _storageItems[item.Slot] = item;
        RefreshRows();
    }

    private void ResizeForBackground()
    {
        // 原版根窗口保留 456x556；261~266 只是页签背景子图，
        // 不能把 CastlePanel (y=500) 裁掉。
        _content.Size = new Vector2I(410, 415);
        _scroll.Location = new Vector2I(424, 68);
        _scroll.Size = new Vector2I(16, 415);
        _scroll.VisibleSize = 415;
        _inviteName.Location = new Vector2I(18, 468);
        _inviteButton.Location = new Vector2I(190, 468);
        _increaseMemberButton.Location = new Vector2I(18, 500);
        _increaseStorageButton.Location = new Vector2I(146, 500);
        _manageButton.Location = new Vector2I(362, 500);
        _background.Location = _guild == null && _tab == 0 ? Vector2I.Zero : new Vector2I(0, 62);
    }

    private void UpdateTabVisibility()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
            _tabButtons[i].Visible = _guild != null || i == 0;
        if (_tabButtons.Count > 0)
            _tabButtons[0].Text = _guild == null ? Lang.GuildDialogCreateTabLabel : Lang.GuildDialogHomeTabLabel;
    }

    public bool AuditLayout(out string details)
    {
        bool tabs = _tabButtons.Count == 6
            && _tabButtons[0].Location == new Vector2I(14, 39)
            && Enumerable.Range(0, _tabButtons.Count).All(index => _tabButtons[index].Location == new Vector2I(14 + index * 76, 39));
        bool noGuildTabs = _guild == null && _tabButtons[0].Visible && _tabButtons.Skip(1).All(x => !x.Visible);
        bool background = _background.Index == 260 && _background.Size == new Vector2I(456, 556);
        details = $"size={Size} tabs={_tabButtons.Count} visible={_tabButtons.Count(x => x.Visible)} content={_content.Location}/{_content.Size} scroll={_scroll.Location}/{_scroll.Size}";
        return Size == new Vector2I(456, 556) && tabs && noGuildTabs && background;
    }

    public void SelectTab(int page)
    {
        if (_guild == null && page > 0) return;
        _tab = Math.Clamp(page, 0, 5);
        _background.Index = _tab switch { 1 => 262, 2 => 263, 3 => 264, 4 => 265, 5 => 266, _ => _guild == null ? 260 : 261 };
        ResizeForBackground();
        RefreshRows();
    }

    public bool AuditPageLayouts(out string details)
    {
        var guild = new ClientGuildInfo
        {
            GuildName = "Audit Guild",
            Notice = Lang.GuildGuildLabel11,
            MemberLimit = 20,
            StorageLimit = 22,
            GuildFunds = 123456,
            DailyGrowth = 12,
            TotalContribution = 3456,
            DailyContribution = 78,
            Tax = 5,
            Flag = 0,
            Members = new List<ClientGuildMemberInfo>
            {
                new() { Index = 1, Name = "Audit", Rank = "Leader", Online = TimeSpan.MinValue },
            },
            Storage = new List<ClientUserItem>(),
        };
        ApplyGuild(guild);
        bool home = _noticeArea?.Size == new Vector2I(382, 252) && _noticeScroll?.Size == new Vector2I(16, 262);
        SelectTab(1);
        bool members = _content.GetChildren().OfType<GuildMemberRow>().Any(x => x.Location == new Vector2I(4, 48) && x.Size == new Vector2I(410, 22));
        SelectTab(2);
        bool storage = _storageGrid?.GridSize == new Vector2I(11, 20) && _storageGrid.Location == new Vector2I(8, 45);
        SelectTab(0);
        details = $"home={home} notice={_noticeArea?.Size} members={members} storage={storage} content={_content.Location}/{_content.Size}";
        return home && members && storage;
    }

    private void BuildCastlePage()
    {
        AddText(Lang.GuildUi337Label, 18, 18);
        AddText(Lang.GuildUi338Label, 18, 62);
        var gates = new DXButton { Text = Lang.GuildUi333Label, FontSize = 9, Size = new Vector2I(120, 27), Location = new Vector2I(18, 105), LibraryFile = LibraryFile.Interface, Index = -1 };
        gates.MouseClick += (o, e) => GameScene.Game?.SendGuildToggleCastleGates();
        _content.AddControl(gates);
        var repairGates = new DXButton { Text = Lang.GuildRepairLabel, FontSize = 9, Size = new Vector2I(100, 27), Location = new Vector2I(148, 105), LibraryFile = LibraryFile.Interface, Index = -1 };
        repairGates.MouseClick += (o, e) =>
        {
            var confirm = new ConfirmDialog(Lang.GuildOkLabel, Lang.GuildConfirmLabel, () => GameScene.Game?.SendGuildRepairCastleGates());
            WindowManager.Open(confirm, GameScene.Game?.UILayer ?? GetParent());
        };
        _content.AddControl(repairGates);
        var repairGuards = new DXButton { Text = Lang.GuildRepairLabel2, FontSize = 9, Size = new Vector2I(100, 27), Location = new Vector2I(258, 105), LibraryFile = LibraryFile.Interface, Index = -1 };
        repairGuards.MouseClick += (o, e) =>
        {
            var confirm = new ConfirmDialog(Lang.GuildOkLabel2, Lang.GuildConfirmLabel, () => GameScene.Game?.SendGuildRepairCastleGuards());
            WindowManager.Open(confirm, GameScene.Game?.UILayer ?? GetParent());
        };
        _content.AddControl(repairGuards);
    }
    public void SetGuildNotice(string notice) { if (_guild != null) _guild.Notice = notice; RefreshRows(); }
    public void ShowMarriageInvite(string name)
    {
        var panel = new DXControl { Location = new Vector2I(45, 185), Size = new Vector2I(350, 70), BackColour = new Color(0.04f, .025f, .02f, .98f), Border = true, BorderColour = new Color(1f, .75f, .25f) };
        panel.AddControl(new DXLabel { Text = $"{name ?? Lang.GroupUnknownLabel} 向你求婚", FontSize = 10, Location = new Vector2I(8, 7), Size = new Vector2I(334, 22), IsControl = false });
        var yes = new DXButton { Text = Lang.GroupAcceptLabel, Size = new Vector2I(70, 24), Location = new Vector2I(80, 38), Index = -1 };
        yes.MouseClick += (o, e) => { GameScene.Game?.SendMarriageResponse(true); RemoveControl(panel); panel.QueueFree(); };
        var no = new DXButton { Text = Lang.GroupDeclineLabel, Size = new Vector2I(70, 24), Location = new Vector2I(190, 38), Index = -1 };
        no.MouseClick += (o, e) => { GameScene.Game?.SendMarriageResponse(false); RemoveControl(panel); panel.QueueFree(); };
        panel.AddControl(yes); panel.AddControl(no); AddControl(panel);
    }
    public void ApplyGuildUpdate(S.GuildUpdate packet)
    {
        if (packet == null) return;
        if (_guild == null)
        {
            _guild = new ClientGuildInfo();
            UpdateTabVisibility();
            _background.Index = 261;
            ResizeForBackground();
        }
        _guild.MemberLimit = packet.MemberLimit;
        _guild.StorageLimit = packet.StorageLimit;
        _guild.GuildFunds = packet.GuildFunds;
        _guild.DailyGrowth = packet.DailyGrowth;
        _guild.TotalContribution = packet.TotalContribution;
        _guild.DailyContribution = packet.DailyContribution;
        _guild.Tax = packet.Tax;
        _guild.DefaultRank = packet.DefaultRank;
        _guild.DefaultPermission = packet.DefaultPermission;
        _guild.Colour = packet.Colour;
        _guild.Flag = packet.Flag;
        _guild.Members = packet.Members;
        RefreshRows();
    }
    public void SetMemberOnline(int index, bool online, string name)
    {
        var member = _guild?.Members?.FirstOrDefault(x => x.Index == index);
        if (member == null) return;
        member.Online = online ? TimeSpan.MinValue : TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(name)) member.Name = name;
        RefreshRows();
    }
    public void SetMemberContribution(int index, long contribution)
    {
        var member = _guild?.Members?.FirstOrDefault(x => x.Index == index);
        if (member == null) return;
        member.TotalContribution = contribution;
        RefreshRows();
    }
    public void ChangeGuildFunds(long change) { if (_guild != null) _guild.GuildFunds += change; RefreshRows(); }
    public void RefreshWarPage() { if (_tab == 3) RefreshRows(); }

    private void OnMemberRowLeftClick(ClientGuildMemberInfo member)
    {
        if (member == null) return;
        GameScene.Game?.OpenGuildMemberDialog(member.Index, member.Name, member.Rank, member.Permission);
    }

    private void OnMemberRowRightClick(ClientGuildMemberInfo member)
    {
        if (member == null) return;
        GameScene.Game?.ShowGuildMemberOnMap(member);
    }

    private void OnMemberRowMiddleClick(ClientGuildMemberInfo member)
    {
        if (member == null) return;
        GameScene.Game?.SendGroupInvite(member.Name);
    }

    /// <summary>成员行: 区分左/右/中键 (DXControl.MouseClick 不区分按钮)。</summary>
    private sealed partial class GuildMemberRow : DXControl
    {
        private readonly GuildDialog _owner;
        public ClientGuildMemberInfo Member;

        public GuildMemberRow(GuildDialog owner)
        {
            _owner = owner;
        }

        public override void _GuiInput(InputEvent e)
        {
            if (!IsEnabled)
            {
                if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
                return;
            }
            if (e is InputEventMouseButton mb && mb.Pressed)
            {
                switch (mb.ButtonIndex)
                {
                    case MouseButton.Left: _owner.OnMemberRowLeftClick(Member); break;
                    case MouseButton.Right: _owner.OnMemberRowRightClick(Member); break;
                    case MouseButton.Middle: _owner.OnMemberRowMiddleClick(Member); break;
                }
                AcceptEvent();
                return;
            }
            base._GuiInput(e);
        }
    }
}
