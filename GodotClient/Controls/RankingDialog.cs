using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Scripts;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Controls;

/// <summary>
/// 原版 RankingDialog 的 Godot 版本。原版有窄版(210)和带观察面板的全榜版(211)，
/// 两者共用分页列表，列表固定十行，滚动条以行索引滚动。
/// </summary>
public partial class RankingDialog : DXWindow
{
    private readonly bool _full;
    private readonly List<DXControl> _rows = new();
    private DXControl _list;
    private DXVScrollBar _scroll;
    private DXLabel _detail;
    private DXControl _inspectPanel;
    private PaperDoll _inspectDoll;
    private DXLabel _inspectName;
    private DXLabel _inspectGuild;
    private DXLabel _inspectGuildRank;
    private DXLabel _inspectLevel;
    private readonly ClientUserItem[] _inspectItems = new ClientUserItem[17];
    private int _selected = -1;
    private string _filter = Lang.RankingUi132Label;
    private bool _onlineOnly;
    private string _selectedName;
    private readonly List<RankInfo> _ranks = new();
    internal IReadOnlyList<RankInfo> Ranks => _ranks;
    internal bool HasRankResponse { get; private set; }
    private int _total;
    private DXTextInput _search;
    private DXButton _classButton;
    private DXButton _observeButton;
    private RequiredClass _classFilter = RequiredClass.None;

    public void ApplyRankings(S.Rankings packet)
    {
        if (packet == null) return;
        HasRankResponse = true;
        _ranks.Clear();
        _ranks.AddRange(packet.Ranks ?? new List<RankInfo>());
        _total = packet.Total;
        _scroll.Value = 0;
        RefreshRows();
    }

    public void ApplyInspect(S.Inspect packet)
    {
        if (_detail == null || packet == null) return;
        _detail.Text = $"{packet.Name}\nLv. {packet.Level} · {packet.Class.Local()}\n\n" +
            $"行会：{packet.GuildName ?? "-"}\n职位：{packet.GuildRank ?? "-"}\n" +
            string.Format(Lang.RankingEquipmentLabel, packet.Items?.Count ?? 0);

        if (!_full || _inspectPanel == null) return;

        Array.Clear(_inspectItems, 0, _inspectItems.Length);
        foreach (var item in packet.Items ?? new List<ClientUserItem>())
        {
            if (item == null || item.Slot < 0 || item.Slot >= _inspectItems.Length) continue;
            if (item.Info == null) item.Complete();
            _inspectItems[item.Slot] = item;
        }

        _inspectName.Text = packet.Name ?? string.Empty;
        _inspectGuild.Text = packet.GuildName ?? string.Empty;
        _inspectGuildRank.Text = packet.GuildRank ?? string.Empty;
        _inspectLevel.Text = $"Lv. {packet.Level} - Cl. {packet.Class.Local()}";
        _inspectDoll.SetInspect(packet, _inspectItems);
        foreach (var cell in _inspectPanel.Controls)
        {
            if (cell is DXItemCell itemCell)
            {
                itemCell.ItemGrid = _inspectItems;
                itemCell.RefreshItem();
            }
        }
    }

    public bool AuditInspectLayout(out string details)
    {
        int cells = 0;
        if (_inspectPanel != null)
            foreach (var control in _inspectPanel.Controls)
                if (control is DXItemCell) cells++;

        bool valid = _full && Size == new Vector2I(576, 456)
            && _inspectPanel?.Size == new Vector2I(252, 456)
            && _inspectDoll != null && _inspectDoll.Position.IsEqualApprox(new Vector2(100, 290))
            && cells == 17 && _inspectLevel?.Position == new Vector2I(77, 419);
        details = $"size={Size} inspect={_inspectPanel?.Size} cells={cells} doll={_inspectDoll?.Position}";
        return valid;
    }

    public RankingDialog(bool fullRanking = false)
    {
        _full = fullRanking;
        HasTitle = false;
        Movable = true;
        HasFooter = false;
        Size = fullRanking ? new Vector2I(576, 456) : new Vector2I(330, 456);

        AddControl(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = fullRanking ? 211 : 210, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);
        AddControl(new DXLabel { Text = Lang.RankingRankingLabel, FontSize = 10, TextColour = new Color(1f, .85f, .3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I((int)Size.X, 18), IsControl = false });

        int listX = fullRanking ? 246 : 12;
        AddControl(new DXLabel { Text = Lang.RankingCharacterLabel, FontSize = 10, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(listX + 8, 48), IsControl = false });
        _search = new DXTextInput { Location = new Vector2I(listX + 13, 68), Size = new Vector2I(147, 18) };
        AddControl(_search);
        var searchButton = new DXButton { Text = Lang.RankingDialogSearchButtonLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Size = new Vector2I(60, 25), Location = new Vector2I(listX + 164, 66), LibraryFile = LibraryFile.Interface, Index = -1 };
        searchButton.MouseClick += (o, e) => { if (!string.IsNullOrWhiteSpace(_search.Text)) GameScene.Game?.SendRankSearch(_search.Text.Trim()); };
        AddControl(searchButton);
        if (fullRanking)
        {
            _observeButton = new DXButton { Text = Lang.RankingDialogObserveButtonLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Size = new Vector2I(60, 25), Location = new Vector2I(listX + 229, 66), LibraryFile = LibraryFile.Interface, Index = -1, Enabled = false };
            _observeButton.MouseClick += (o, e) =>
            {
                if (_selected >= 0 && _selected < _ranks.Count)
                    GameScene.Game?.SendRankingInspect(_ranks[_selected].Index);
            };
            AddControl(_observeButton);
        }

        _classButton = new DXButton { Text = Lang.RankingClassLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Size = new Vector2I(122, 25), Location = new Vector2I(listX + 12, 39), LibraryFile = LibraryFile.Interface, Index = -1 };
        _classButton.MouseClick += (o, e) => CycleClassFilter();
        AddControl(_classButton);
        var online = new DXButton { Text = Lang.RankingOnlineLabel, Type = DXButton.ButtonType.SmallButton, FontSize = 9, Size = new Vector2I(96, 25), Location = new Vector2I(listX + 139, 39), LibraryFile = LibraryFile.Interface, Index = -1 };
        online.MouseClick += (o, e) => { _onlineOnly = !_onlineOnly; online.Text = _onlineOnly ? Lang.RankingAllLabel : Lang.RankingOnlineLabel; _scroll.Value = 0; RefreshRows(); GameScene.Game?.RequestRankings(0, _onlineOnly, _classFilter); };
        AddControl(online);

        _list = new DXControl { Location = new Vector2I(listX, 122), Size = new Vector2I(330, 286), Clip = true };
        AddControl(_list);
        _scroll = new DXVScrollBar { Location = new Vector2I(listX + 304, 122), Size = new Vector2I(20, 286), VisibleSize = 11, Change = 5 };
        _scroll.ValueChanged += (o, e) => { RefreshRows(); GameScene.Game?.RequestRankings(_scroll.Value, _onlineOnly, _classFilter); };
        AddControl(_scroll);
        _list.MouseWheel += _scroll.DoMouseWheel;

        if (fullRanking)
        {
            _detail = new DXLabel { Text = Lang.RankingCharacterLabel2, FontSize = 11, TextColour = Colors.White, DrawOutline = true, OutlineColour = Colors.Black, Location = new Vector2I(18, 72), Size = new Vector2I(212, 280), IsControl = false };
            _detail.Visible = false;
            AddControl(_detail);
            BuildInspectPanel();
        }
        RefreshRows();
    }

    /// <summary>
    /// 原版 RankingDialog 的 InspectPanel：左侧 252x456 观察区，人物和装备槽
    /// 坐标直接对应 Client/Scenes/Views/RankingDialog.cs 的全榜模式。
    /// </summary>
    private void BuildInspectPanel()
    {
        _inspectPanel = new DXControl
        {
            Location = Vector2I.Zero,
            Size = new Vector2I(252, 456),
            PassThrough = true,
        };
        AddControl(_inspectPanel);

        var namePanel = new DXControl
        {
            Location = new Vector2I(64, 71),
            Size = new Vector2I(130, 46),
            IsControl = false,
        };
        _inspectName = InspectLabel(20, new Color(0.87f, 1f, 0.87f), new Vector2I(0, 0), new Vector2I(130, 20));
        _inspectGuild = InspectLabel(15, new Color(1f, 1f, 0.71f), new Vector2I(0, 18), new Vector2I(130, 15));
        _inspectGuildRank = InspectLabel(13, new Color(0.78f, 0.78f, 0.78f), new Vector2I(0, 32), new Vector2I(130, 15));
        namePanel.AddControl(_inspectName);
        namePanel.AddControl(_inspectGuild);
        namePanel.AddControl(_inspectGuildRank);
        _inspectPanel.AddControl(namePanel);

        _inspectDoll = new PaperDoll
        {
            Position = new Vector2(100, 290),
            Size = new Vector2(180, 220),
        };
        _inspectPanel.AddChild(_inspectDoll);

        AddInspectCell(EquipmentSlot.Weapon, new Vector2I(28, 142), new Vector2I(65, 90), -1, true);
        AddInspectCell(EquipmentSlot.Armour, new Vector2I(90, 143), new Vector2I(70, 150), -1, true);
        AddInspectCell(EquipmentSlot.Shield, new Vector2I(140, 230), new Vector2I(36, 36), -1, true);
        AddInspectCell(EquipmentSlot.Helmet, new Vector2I(110, 150), new Vector2I(35, 35), -1, true);
        AddInspectCell(EquipmentSlot.Emblem, new Vector2I(159, 360), new Vector2I(36, 36), 104, false);
        AddInspectCell(EquipmentSlot.HorseArmour, new Vector2I(276, 118), new Vector2I(36, 36), -1, true);
        AddInspectCell(EquipmentSlot.Torch, new Vector2I(120, 360), new Vector2I(36, 36), 38, false);
        AddInspectCell(EquipmentSlot.Necklace, new Vector2I(198, 204), new Vector2I(36, 36), 33, false);
        AddInspectCell(EquipmentSlot.BraceletL, new Vector2I(24, 243), new Vector2I(36, 36), 32, false);
        AddInspectCell(EquipmentSlot.BraceletR, new Vector2I(198, 243), new Vector2I(36, 36), 32, false);
        AddInspectCell(EquipmentSlot.RingL, new Vector2I(24, 282), new Vector2I(36, 36), 31, false);
        AddInspectCell(EquipmentSlot.RingR, new Vector2I(198, 282), new Vector2I(36, 36), 31, false);
        AddInspectCell(EquipmentSlot.Flower, Vector2I.Zero, new Vector2I(36, 36), -1, true);
        AddInspectCell(EquipmentSlot.Poison, Vector2I.Zero, new Vector2I(36, 36), -1, true);
        AddInspectCell(EquipmentSlot.Amulet, new Vector2I(198, 321), new Vector2I(36, 75), 39, false);
        AddInspectCell(EquipmentSlot.Shoes, new Vector2I(24, 321), new Vector2I(36, 75), 36, false);
        AddInspectCell(EquipmentSlot.Costume, new Vector2I(24, 204), new Vector2I(36, 36), 34, false);

        _inspectLevel = new DXLabel
        {
            FontSize = 10,
            TextColour = Colors.White,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Size = new Vector2I(148, 16),
            Location = new Vector2I(77, 419),
            IsControl = false,
        };
        _inspectPanel.AddControl(_inspectLevel);
    }

    private static DXLabel InspectLabel(int fontSize, Color colour, Vector2I location, Vector2I size)
    {
        return new DXLabel
        {
            FontSize = fontSize,
            TextColour = colour,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Size = size,
            Location = location,
            IsControl = false,
        };
    }

    private void AddInspectCell(EquipmentSlot slot, Vector2I location, Vector2I size, int background, bool hidden)
    {
        var cell = new DXItemCell
        {
            Location = location,
            Size = size,
            ItemGrid = _inspectItems,
            Slot = (int)slot,
            GridType = GridType.Inspect,
            ReadOnly = true,
            Hidden = hidden,
        };
        if (background >= 0)
        {
            int index = background;
            cell.BeforeDraw += (o, e) =>
            {
                if (cell.Item != null) return;
                var tex = MirSkin.GetTexture(LibraryFile.Interface, index);
                if (tex == null) return;
                var texSize = tex.GetSize();
                cell.DrawTextureRect(tex, new Rect2((cell.Size.X - texSize.X) / 2f, (cell.Size.Y - texSize.Y) / 2f, texSize.X, texSize.Y), false,
                    new Color(1f, 1f, 1f, 0.2f));
            };
        }
        _inspectPanel.AddControl(cell);
    }

    private void RefreshRows()
    {
        foreach (var row in _rows) { _list.RemoveControl(row); row.QueueFree(); }
        _rows.Clear();
        _scroll.MaxValue = Mathf.Max(_scroll.VisibleSize, _total > 0 ? _total : _ranks.Count);
        int start = _scroll.Value;
        for (int i = 0; i < 11 && start + i < _ranks.Count; i++)
        {
            var rankInfo = _ranks[start + i];
            int rank = rankInfo.Rank;
            string selectedName = rankInfo.Name ?? string.Empty;
            int selected = start + i;
            var row = new DXControl { Location = new Vector2I(0, 16 + i * 23), Size = new Vector2I(288, 22), BackColour = selected == _selected ? new Color(0.45f, 0.08f, 0.05f, 0.65f) : Colors.Transparent };
            row.AddControl(new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = rankInfo.Online ? 3625 : 3624, Location = new Vector2I(2, 6), IsControl = false });
            AddRowLabel(row, rank.ToString(), 10, 31, selected == _selected ? new Color(1f, .9f, .2f) : Colors.White);
            AddRowLabel(row, $"Lv. {rankInfo.Level}", 40, 43, selected == _selected ? new Color(1f, .9f, .2f) : Colors.White);
            AddRowLabel(row, rankInfo.Rebirth > 0 ? $"{selectedName} [{rankInfo.Rebirth}]" : selectedName, 82, 168, selected == _selected ? new Color(1f, .9f, .2f) : Colors.White);
            string change = rankInfo.RankChange == 0 ? "-" : (rankInfo.RankChange > 0 ? $"▲{rankInfo.RankChange}" : $"▼{Math.Abs(rankInfo.RankChange)}");
            AddRowLabel(row, change, 249, 40, rankInfo.RankChange > 0 ? Colors.OrangeRed : rankInfo.RankChange < 0 ? Colors.DodgerBlue : Colors.White);
            row.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { _selected = selected; _selectedName = selectedName; RefreshRows(); } };
            _list.AddControl(row);
            _rows.Add(row);
        }
        if (_detail != null && _selected >= 0 && _selected < _ranks.Count)
        {
            var selectedRank = _ranks[_selected];
            _detail.Text = $"第 {selectedRank.Rank} 名\n\n角色：{selectedRank.Name}\n等级：{selectedRank.Level}\n职业：{selectedRank.Class}\n状态：{(selectedRank.Online ? "在线" : Lang.GuildMemberRowOnlineLabel)}";
        }
        if (_observeButton != null)
            _observeButton.Enabled = _selected >= 0 && _selected < _ranks.Count
                && _ranks[_selected].Online && _ranks[_selected].Observable;
    }

    private static void AddRowLabel(DXControl row, string text, int x, int width, Color colour)
    {
        row.AddControl(new DXLabel
        {
            Text = text,
            FontSize = 9,
            TextColour = colour,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Size = new Vector2I(width, 22),
            Location = new Vector2I(x, 0),
            IsControl = false,
        });
    }

    private void CycleClassFilter()
    {
        _classFilter = _classFilter switch
        {
            RequiredClass.None => RequiredClass.Warrior,
            RequiredClass.Warrior => RequiredClass.Wizard,
            RequiredClass.Wizard => RequiredClass.Taoist,
            RequiredClass.Taoist => RequiredClass.Assassin,
            _ => RequiredClass.None,
        };
        _classButton.Text = _classFilter switch
        {
            RequiredClass.Warrior => Lang.NewCharacterSelectedClassLabel,
            RequiredClass.Wizard => Lang.RankingUi145Label,
            RequiredClass.Taoist => Lang.RankingUi146Label,
            RequiredClass.Assassin => Lang.RankingUi147Label,
            _ => Lang.RankingClassLabel,
        };
        _selected = -1;
        _scroll.Value = 0;
        RefreshRows();
        GameScene.Game?.RequestRankings(0, _onlineOnly, _classFilter);
    }
}
