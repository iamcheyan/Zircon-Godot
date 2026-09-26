using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Scripts;
using C = Library.Network.ClientPackets;

namespace ZirconClient.Controls;

/// <summary>原版 GroupDialog 的成员面板与 LFG 列表。</summary>
public partial class GroupDialog : DXWindow
{
    private readonly Dictionary<uint, string> _members = new();
    private readonly List<ClientLookingForGroup> _lfg = new();
    private DXControl _memberPanel;
    private DXControl _lfgPanel;
    private DXTextInput _inviteName;
    private bool _allowGroup;
    private DXButton _allowButton;
    private DXControl _invitePanel;
    private bool _lfgEnabled;
    private readonly List<DXLabel> _memberLabels = new();
    private readonly List<GroupLfgRowControl> _lfgRows = new();
    private DXButton _removeButton;
    private DXButton _optionsButton;
    private DXButton _addButton;
    private DXButton _lfgButton;
    private DXButton _closeButton;
    private DXButton _legacyPermissionButton;
    private DXImageControl _background;
    private DXLabel _titleLabel;
    private DXLabel _allowLabel;
    private DXVScrollBar _lfgScroll;
    private DXCheckButton _allowCheck;
    private GroupLfgInputDialog _lfgDialog;
    private uint _selectedMember;
    private bool _legacyEiLayout;

    public override void Close()
    {
        if (!_legacyEiLayout) GameScene.Game?.SendGroupNotify(false);
        base.Close();
    }

    public GroupDialog()
    {
        HasTitle = false;
        Movable = true;
        HasFooter = false;
        Size = new Vector2I(240, 424);
        _background = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 900, FixedSize = true, StretchImage = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_background);
        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => GameScene.Game?.CloseGroupDialog();
        AddControl(_closeButton);
        _titleLabel = new DXLabel { Text = Lang.GroupGroupLabel, FontSize = 10, TextColour = new Color(1f, 0.85f, 0.3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I(240, 18), IsControl = false };
        AddControl(_titleLabel);

        _allowCheck = new DXCheckButton(string.Empty) { Location = new Vector2I(166, 40), Size = new Vector2I(18, 18) };
        _allowCheck.MouseClick += (o, e) => ToggleAllow();
        AddControl(_allowCheck);
        _allowLabel = new DXLabel { Text = Lang.GroupAllowLabel, FontSize = 9, Location = new Vector2I(186, 40), Size = new Vector2I(48, 18), IsControl = false };
        AddControl(_allowLabel);

        _memberPanel = new DXControl { Location = new Vector2I(13, 60), Size = new Vector2I(194, 148), Clip = true };
        AddControl(_memberPanel);

        _inviteName = new DXTextInput { Location = new Vector2I(14, 260), Size = new Vector2I(130, 23), Visible = false };
        AddControl(_inviteName);
        var invite = Button(Lang.GroupInviteLabel, new Vector2I(149, 260), new Vector2I(62, 23));
        invite.Visible = false;
        invite.MouseClick += (o, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_inviteName.Text))
                GameScene.Game?.SendGroupInvite(_inviteName.Text.Trim());
            _inviteName.Text = string.Empty;
            _inviteName.Visible = false;
            invite.Visible = false;
        };
        _addButton = new DXButton { Type = DXButton.ButtonType.AddButton, Size = new Vector2I(36, 36), Location = new Vector2I(35, 217), LibraryFile = LibraryFile.Interface };
        _addButton.MouseClick += (o, e) => { _inviteName.Visible = !_inviteName.Visible; invite.Visible = _inviteName.Visible; if (_inviteName.Visible) _inviteName.GrabFocus(); };
        AddControl(_addButton);
        _removeButton = new DXButton { Type = DXButton.ButtonType.RemoveButton, Size = new Vector2I(36, 36), Location = new Vector2I(81, 217), LibraryFile = LibraryFile.Interface, Enabled = false };
        _removeButton.MouseClick += (o, e) => RemoveSelectedMember();
        AddControl(_removeButton);
        _lfgButton = new DXButton { Type = DXButton.ButtonType.LFGButton, Size = new Vector2I(36, 36), Location = new Vector2I(127, 217), LibraryFile = LibraryFile.Interface };
        _lfgButton.MouseClick += OnLfgButtonClick;
        AddControl(_lfgButton);
        _optionsButton = new DXButton { Type = DXButton.ButtonType.OptionsButton, Size = new Vector2I(36, 36), Location = new Vector2I(173, 217), LibraryFile = LibraryFile.Interface, Enabled = false };
        AddControl(_optionsButton);

        AddControl(new DXLabel { Text = "队伍名称", FontSize = 9, TextColour = new Color(1f, 0.85f, 0.3f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(101, 20), Location = new Vector2I(12, 272), IsControl = false });
        AddControl(new DXLabel { Text = "状态", FontSize = 9, TextColour = new Color(1f, 0.85f, 0.3f), Align = HorizontalAlignment.Center, AutoSize = false, Size = new Vector2I(95, 20), Location = new Vector2I(114, 272), IsControl = false });
        _lfgPanel = new DXControl { Location = Vector2I.Zero, Size = new Vector2I(240, 424), Clip = true, MouseFilter = MouseFilterEnum.Ignore };
        AddControl(_lfgPanel);
        _lfgScroll = new DXVScrollBar { Location = new Vector2I(210, 268), Size = new Vector2I(24, 140), VisibleSize = 5, Change = 1, Border = false, BackColour = Colors.Transparent };
        _lfgScroll.UpButton.Index = 61; _lfgScroll.UpButton.LibraryFile = LibraryFile.Interface;
        _lfgScroll.DownButton.Index = 62; _lfgScroll.DownButton.LibraryFile = LibraryFile.Interface;
        _lfgScroll.PositionBar.Index = 60; _lfgScroll.PositionBar.LibraryFile = LibraryFile.Interface;
        _lfgScroll.ValueChanged += (s, e) => RebuildLfg();
        AddControl(_lfgScroll);
        for (int i = 0; i < 5; i++)
        {
            var row = new GroupLfgRowControl { Location = new Vector2I(13, 293 + i * 21), Visible = false };
            row.MouseClick += (s, e) => RequestLfg(((GroupLfgRowControl)s).Info);
            AddControl(row);
            _lfgRows.Add(row);
        }
        RebuildMembers();
    }

    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(256, 244);
        _background.LibraryFile = LibraryFile.GameInter;
        _background.Index = 900;
        _background.Location = new Vector2I(0, -6);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, 900);
        _background.StretchImage = false;
        _titleLabel.Visible = false;

        _allowCheck.Visible = false;
        _allowLabel.Visible = false;
        _legacyPermissionButton ??= new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 920,
            HoverIndex = 921,
            PressedIndex = 921,
            Location = new Vector2I(9, 52),
            Size = new Vector2I(28, 26),
        };
        if (_legacyPermissionButton.GetParent() == null)
        {
            _legacyPermissionButton.MouseClick += (_, _) => ToggleAllow();
            AddControl(_legacyPermissionButton);
        }
        // EI 直接把成员名画在窗口坐标中，列表没有独立的 101px 裁剪框。
        // 保留根窗范围的裁切，使超过可见行的名字由窗口底边自然裁掉。
        _memberPanel.Location = Vector2I.Zero;
        _memberPanel.Size = Size;
        _memberPanel.MouseFilter = MouseFilterEnum.Ignore;

        // F900 底部三个原版动作热区：邀请、移除、离队。EI 没有 LFG 编辑器。
        DXButton[] actions = { _addButton, _removeButton, _lfgButton };
        Vector2I[] actionLocations = { new(17, 197), new(80, 197), new(159, 197) };
        Vector2I[] actionSizes = { new(60, 20), new(76, 20), new(76, 20) };
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i].Location = actionLocations[i];
            actions[i].Size = actionSizes[i];
            actions[i].Modulate = new Color(1, 1, 1, 0);
        }
        _lfgButton.MouseClick -= OnLfgButtonClick;
        _lfgButton.MouseClick -= OnLegacyLeaveClick;
        _lfgButton.MouseClick += OnLegacyLeaveClick;
        _optionsButton.Visible = false;
        _inviteName.Location = new Vector2I(17, 194);
        _lfgPanel.Visible = false;
        _lfgScroll.Visible = false;
        foreach (var row in _lfgRows) row.Visible = false;

        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(224, 212);
        _closeButton.Size = new Vector2I(28, 26);
        UpdateClientAreaForLegacySkin();
    }

    private DXButton Button(string text, Vector2I location, Vector2I size)
    {
        var button = new DXButton { Text = text, FontSize = 9, TextColour = new Color(1f, 0.85f, 0.3f), Location = location, Size = size, LibraryFile = LibraryFile.Interface, Index = -1 };
        AddControl(button);
        return button;
    }

    public void AddMember(uint objectId, string name)
    {
        _members[objectId] = name ?? objectId.ToString();
        RebuildMembers();
    }

    public void RemoveMember(uint objectId)
    {
        _members.Remove(objectId);
        RebuildMembers();
    }

    public bool IsMember(uint objectId) => _members.ContainsKey(objectId);

    public void SetLfg(IEnumerable<ClientLookingForGroup> list)
    {
        _lfg.Clear();
        if (list != null) _lfg.AddRange(list.Where(x => x != null));
        RebuildLfg();
    }

    public void SetOwnLfg(ClientLookingForGroup group)
    {
        if (group == null) return;
        SetLfg(_lfg.Append(group));
    }

    public void ShowInvite(string name)
    {
        if (_invitePanel != null)
        {
            RemoveControl(_invitePanel);
            _invitePanel.QueueFree();
        }
        _invitePanel = new DXControl { Location = new Vector2I(12, 165), Size = new Vector2I(216, 48), BackColour = new Color(0.05f, 0.03f, 0.02f, .96f), Border = true, BorderColour = new Color(1f, .75f, .25f) };
        _invitePanel.AddControl(new DXLabel { Text = $"{name ?? Lang.GroupUnknownLabel} 邀请你组队", FontSize = 9, Location = new Vector2I(6, 4), Size = new Vector2I(204, 18), IsControl = false });
        var accept = new DXButton { Text = Lang.GroupAcceptLabel, FontSize = 9, Size = new Vector2I(70, 20), Location = new Vector2I(24, 25), LibraryFile = LibraryFile.Interface, Index = -1 };
        accept.MouseClick += (o, e) => { GameScene.Game?.SendGroupResponse(name, true); RemoveControl(_invitePanel); _invitePanel.QueueFree(); _invitePanel = null; };
        _invitePanel.AddControl(accept);
        var reject = new DXButton { Text = Lang.GroupDeclineLabel, FontSize = 9, Size = new Vector2I(70, 20), Location = new Vector2I(120, 25), LibraryFile = LibraryFile.Interface, Index = -1 };
        reject.MouseClick += (o, e) => { GameScene.Game?.SendGroupResponse(name, false); RemoveControl(_invitePanel); _invitePanel.QueueFree(); _invitePanel = null; };
        _invitePanel.AddControl(reject);
        AddControl(_invitePanel);
    }

    public void ToggleAllow()
    {
        _allowGroup = !_allowGroup;
        _allowCheck.Checked = _allowGroup;
        GameScene.Game?.SendGroupSwitch(_allowGroup);
    }

    public void SetAllow(bool allow)
    {
        _allowGroup = allow;
        if (_allowCheck != null) _allowCheck.Checked = allow;
    }

    private void RebuildMembers()
    {
        foreach (var label in _memberLabels)
        {
            _memberPanel.RemoveControl(label);
            label.QueueFree();
        }
        _memberLabels.Clear();
        int i = 0;
        var members = _legacyEiLayout ? _members : _members.Take(Globals.GroupLimit);
        foreach (var pair in members)
        {
            var location = _legacyEiLayout
                ? new Vector2I(i % 2 == 0 ? 145 : 45, 90 + 20 * (i / 2))
                : new Vector2I(10 + 100 * (i % 2), 5 + 20 * (i / 2));
            var label = new DXLabel
            {
                Text = pair.Value,
                FontSize = 10,
                TextColour = _legacyEiLayout || pair.Key != _selectedMember ? Colors.White : Colors.LimeGreen,
                Location = location,
                Size = new Vector2I(95, 20),
                IsControl = true,
                AutoSize = false,
            };
            label.MouseClick += (s, e) => SelectMember(pair.Key);
            _memberPanel.AddControl(label);
            _memberLabels.Add(label);
            i++;
        }
        _removeButton.Enabled = _selectedMember != 0 && _members.ContainsKey(_selectedMember);
    }

    private void RebuildLfg()
    {
        var list = _lfg.Where(x => x?.Enabled == true).OrderBy(x => x.GroupName, StringComparer.Ordinal).ToList();
        _lfgScroll.MaxValue = list.Count;
        for (int i = 0; i < _lfgRows.Count; i++)
        {
            int index = i + _lfgScroll.Value;
            var row = _lfgRows[i];
            row.Info = index < list.Count ? list[index] : null;
            row.Visible = row.Info != null;
        }
    }

    private void SelectMember(uint objectId)
    {
        _selectedMember = objectId;
        RebuildMembers();
    }

    private void OnLfgButtonClick(object sender, EventArgs e) => OpenLfgEditor();

    private void OnLegacyLeaveClick(object sender, EventArgs e)
    {
        // EI 的 0x3FE 是离队动作；当前服务端用关闭组队权限同时执行 GroupLeave。
        GameScene.Game?.SendGroupSwitch(false);
    }

    private void RemoveSelectedMember()
    {
        if (_selectedMember == 0) return;
        GameScene.Game?.SendGroupRemove(_selectedMember);
        _selectedMember = 0;
        RebuildMembers();
    }

    private void RequestLfg(ClientLookingForGroup info)
    {
        if (info == null || !info.Enabled || !_allowGroup || info.CurrentCount >= info.MaxCount || _members.Count > 0) return;
        GameScene.Game?.SendGroupRequest(info.LeaderName);
    }

    private void OpenLfgEditor()
    {
        if (_lfgDialog != null)
        {
            WindowManager.Close(_lfgDialog);
            _lfgDialog = null;
        }
        var own = _lfg.FirstOrDefault(x => x != null && x.LeaderName == GameScene.Game?.StartInfo?.Name);
        _lfgDialog = new GroupLfgInputDialog(own, (enabled, name, type, count) =>
        {
            _lfgEnabled = enabled;
            GameScene.Game?.SendGroupLfg(enabled, string.IsNullOrWhiteSpace(name) ? GameScene.Game?.StartInfo?.Name : name, type, count);
        });
        WindowManager.Open(_lfgDialog, GameScene.Game?.UILayer ?? GetParent());
    }

    public bool AuditLayout(out string details)
    {
        bool buttons = _removeButton.Location == new Vector2I(81, 217)
            && _optionsButton.Location == new Vector2I(173, 217)
            && _allowCheck.Location == new Vector2I(166, 40);
        bool members = _memberPanel.Location == new Vector2I(13, 60)
            && _memberPanel.Size == new Vector2I(194, 148);
        bool lfg = _lfgScroll.Location == new Vector2I(210, 268)
            && _lfgScroll.Size == new Vector2I(24, 140)
            && _lfgScroll.VisibleSize == 5
            && _lfgRows.Count == 5
            && _lfgRows[0].Location == new Vector2I(13, 293)
            && _lfgRows[4].Location == new Vector2I(13, 377);
        details = $"size={Size} members={_memberPanel.Location}/{_memberPanel.Size} buttons=4 lfg={_lfgRows.Count} scroll={_lfgScroll.Location}/{_lfgScroll.VisibleSize}";
        return Size == new Vector2I(240, 424) && buttons && members && lfg;
    }
}

/// <summary>原版 GroupLFGRow 的三列固定宽度行。</summary>
public sealed partial class GroupLfgRowControl : DXControl
{
    private readonly DXLabel _name;
    private readonly DXLabel _status;
    private readonly DXLabel _type;

    public ClientLookingForGroup Info { get; set; }

    public GroupLfgRowControl()
    {
        Size = new Vector2I(194, 19);
        _name = AddLabel(0, 100);
        _status = AddLabel(101, 50);
        _type = AddLabel(151, 42);
        MouseEnter += (s, e) => SetHighlight(true);
        MouseLeave += (s, e) => SetHighlight(false);
    }

    private DXLabel AddLabel(int x, int width)
    {
        var label = new DXLabel { FontSize = 9, Size = new Vector2I(width, 19), Location = new Vector2I(x, 0), Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, IsControl = false };
        AddControl(label);
        return label;
    }

    public new bool Visible
    {
        get => base.Visible;
        set
        {
            base.Visible = value;
            if (value) RefreshStatus();
        }
    }

    private void SetHighlight(bool selected)
    {
        var colour = selected ? new Color(.4f, .4f, .4f, .5f) : Colors.Transparent;
        _name.BackColour = colour; _status.BackColour = colour; _type.BackColour = colour;
    }

    private void RefreshStatus()
    {
        _name.Text = Info?.GroupName ?? string.Empty;
        _status.Text = Info == null ? string.Empty : $"[{Info.CurrentCount:D2}/{Info.MaxCount:D2}]";
        _type.Text = Info?.GroupType ?? string.Empty;
        _name.TextColour = _status.TextColour = _type.TextColour = Info?.Enabled == true ? Colors.LimeGreen : Colors.White;
    }
}
