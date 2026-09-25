using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Scripts;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Controls;

/// <summary>
/// 角色窗口 (移植自 Client/Scenes/Views/CharacterDialog.cs 的装备部分):
/// Interface 110 底图, 17 个基础装备槽 (EquipmentSlot 0-16, 钓鱼槽 17-21 不建格)。
/// 空槽底图按原版索引绘制在格子下层 (20% 透明)。
/// </summary>
public partial class CharacterDialog : DXWindow
{
    public DXItemCell[] Grid;
    public DXLabel WeightLabel;
    private DXLabel _characterNameLabel, _guildNameLabel, _guildRankLabel;
    private DXImageControl _marriageIcon, _guildFlagBase, _guildFlagOverlay;
    private DXLabel _marriageLabel;
    private DXControl _fameControl;
    private DXImageControl _background;
    private DXButton _closeButton;
    private DXButton _legacyViewToggle;
    private bool _legacyEquipmentView;
    private PaperDoll _doll;
    private DXControl _attributePanel;
    private DXControl _hermitPanel;
    private DXControl _statsPanel;
    private DXLabel _statsText;
    private readonly List<DXControl> _statPages = new();
    private readonly List<DXButton> _statsTabs = new();
    private readonly List<(DXButton Button, int Background)> _mainTabs = new();
    private readonly List<(DXLabel Value, Stat Stat, Stat? MinStat, int Mode)> _statBindings = new();
    private DXLabel _wearWeightValue, _handWeightValue;
    private int _statsPage;
    private readonly List<(DXLabel Label, Stat Stat)> _attributeValues = new();
    private readonly List<(DXLabel Label, DXLabel Value)> _legacyAttributeLabels = new();
    private readonly List<(DXLabel Label, DXLabel Value, string Name, Stat? Stat, Stat? MaxStat)> _legacyExpandedLabels = new();
    private static readonly Color LegacyAttributeLabelColour = new(250f / 255f, 225f / 255f, 200f / 255f);
    private static readonly Color LegacyAttributeValueColour = new(250f / 255f, 250f / 255f, 250f / 255f);
    private static readonly string[] LegacyFirstAttributeNames =
    {
        "LEVEL", "HP", "MP", "经验", "包袱负重", "装备负重", "腕力",
        "准确", "敏捷", "魔法躲避", "毒物躲避", "中毒恢复", "生命恢复", "魔法恢复",
    };
    private bool _legacyEiLayout;
    private DXLabel _disciplineLabel;
    private DXButton _disciplineButton;
    private DXImageControl _disciplineLevelImage;
    private DXLabel _disciplineLevelValue, _disciplineExperienceLabel;
    private readonly List<DXImageControl> _disciplineMagicIcons = new();
    private readonly ClientUserItem[] _inspectItems = new ClientUserItem[17];
    private bool _inspectMode;
    private bool _draggingBackground;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartPosition;
    private string _partnerName = string.Empty;
    private string _ownPartnerName = string.Empty;
    private int _inspectGuildFlag = -1;
    private System.Drawing.Color _inspectGuildColour = System.Drawing.Color.White;

    private static readonly Vector2I OwnSize = new(331, 488);
    private static readonly Vector2I InspectSize = new(331, 374);

    // 槽位 -> 空槽底图索引 (按 EquipmentSlot 枚举索引 0~16 严格对应)
    private static readonly int[] SlotBackgrounds = new int[17]
    {
        -1,  // 0: Weapon
        -1,  // 1: Armour
        -1,  // 2: Helmet
        38,  // 3: Torch
        33,  // 4: Necklace
        32,  // 5: BraceletL
        32,  // 6: BraceletR
        31,  // 7: RingL
        31,  // 8: RingR
        36,  // 9: Shoes
        40,  // 10: Poison
        39,  // 11: Amulet
        81,  // 12: Flower
        82,  // 13: HorseArmour
        104, // 14: Emblem
        -1,  // 15: Shield
        34,  // 16: Costume
    };

    private static readonly Vector2I[] SlotPositions = new Vector2I[17]
    {
        new(58, 122),   // 0: Weapon
        new(120, 123),  // 1: Armour
        new(140, 90),   // 2: Helmet
        new(10, 196),   // 3: Torch
        new(10, 157),   // 4: Necklace
        new(244, 157),  // 5: BraceletL
        new(283, 157),  // 6: BraceletR
        new(244, 196),  // 7: RingL
        new(283, 196),  // 8: RingR
        new(10, 235),   // 9: Shoes
        new(244, 274),  // 10: Poison
        new(283, 235),  // 11: Amulet
        new(244, 235),  // 12: Flower
        new(283, 118),  // 13: HorseArmour
        new(244, 118),  // 14: Emblem
        new(170, 170),  // 15: Shield
        new(10, 118),   // 16: Costume
    };

    public CharacterDialog()
    {
        // 原版 CharacterDialog 继承 DXImageControl，不绘制通用标题栏。
        HasTitle = false;
        // 角色面板没有标题栏，不能使用 DXWindow 的通用拖动：装备格拦截
        // MouseUp 时根窗口可能收不到释放事件。拖动由背景控件显式转发到
        // 窗口，空白区仍可拖动，子控件的点击不会误移动整个面板。
        Movable = false;
        Text = Lang.CharacterCharacterTabLabel;
        Size = _inspectMode ? InspectSize : OwnSize;

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 110,
            FixedSize = true,
            Size = Size,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _background.MouseDown += (_, _) => BeginBackgroundDrag();
        _background.MouseMove += (_, _) => ApplyBackgroundDrag();
        _background.MouseUp += (_, _) => FinishBackgroundDrag();
        AddControl(_background);

        // 原版 CharacterTab_BeforeChildrenDraw 直接使用 (130,270) 绘制锚点。
        // 这里已经是窗口绘制坐标，不能再额外加 CharacterTab 的 Y 偏移。
        _doll = new PaperDoll();
        _doll.Position = new Vector2(130, 280);
        AddChild(_doll);

        _closeButton = new DXButton
        {
            LibraryFile = LibraryFile.Interface,
            Index = 15,
        };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);

        _legacyViewToggle = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 171,
            HoverIndex = 172,
            PressedIndex = 172,
            Location = new Vector2I(176, 264),
            Size = new Vector2I(36, 36),
        };
        _legacyViewToggle.MouseClick += (_, _) => ToggleLegacyView();
        AddControl(_legacyViewToggle);

        AddTab(Lang.CharacterCharacterTabLabel, 0, 110);
        // 开发阶段暂不开放“修炼”和“隐士”页签；相关面板仍保留在代码中，
        // 以后重新启用时无需恢复界面结构，但当前没有任何可见入口。

        _attributePanel = new DXControl
        {
            Location = new Vector2I(0, 45),
            Size = new Vector2I(331, 443),
            Clip = true,
            Visible = false,
        };
        AddControl(_attributePanel);
        _hermitPanel = new DXControl
        {
            Location = new Vector2I(0, 45),
            Size = new Vector2I(331, 443),
            Clip = true,
            Visible = false,
        };
        AddControl(_hermitPanel);
        BuildAttributePanel();
        _statsPanel = new DXControl
        {
            Location = new Vector2I(0, 364),
            Size = new Vector2I(331, 124),
            Clip = true,
            Visible = !_inspectMode,
        };
        AddControl(_statsPanel);
        BuildStatsPanel();

        // 原版 CharacterTab 坐标在 Y=45 (19 标题栏 + 26 页签栏):
        // 姓名面板位于 (92, 51) 相对 CharacterTab -> 绝对坐标 (92, 96)。
        var namePanel = new DXControl
        {
            Location = new Vector2I(93, 51),
            Size = new Vector2I(137, 68),
            IsControl = false,
        };
        AddControl(namePanel);
        _characterNameLabel = CreateCharacterLabel(9, new Color(0.87f, 1f, 0.87f));
        _characterNameLabel.Size = new Vector2I(137, 20);
        _guildNameLabel = CreateCharacterLabel(9, new Color(1f, 1f, 0.71f));
        _guildNameLabel.Location = new Vector2I(0, 18);
        _guildNameLabel.Size = new Vector2I(137, 15);
        _guildRankLabel = CreateCharacterLabel(8, new Color(0.78f, 0.78f, 0.78f));
        _guildRankLabel.Size = new Vector2I(137, 15);
        _guildRankLabel.Location = new Vector2I(0, 31);
        namePanel.AddControl(_characterNameLabel);
        namePanel.AddControl(_guildNameLabel);
        namePanel.AddControl(_guildRankLabel);

        // 文本面板坐标直接沿用原版窗口坐标。
        _marriageIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 1298,
            Location = new Vector2I(96, 105),
            Visible = false,
            IsControl = false,
        };
        _marriageLabel = new DXLabel
        {
            TextColour = new Color(1f, .55f, .75f),
            AutoSize = false,
            Size = new Vector2I(117, 18),
            Location = new Vector2I(112, 100),
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
            Visible = false,
        };
        AddControl(_marriageIcon);
        AddControl(_marriageLabel);

        // 原版声望特效由 PaperDoll 按 GameInter 帧链绘制；保留相同的命中区域。
        _fameControl = new DXControl
        {
            Location = new Vector2I(235, 61),
            Size = new Vector2I(34, 36),
            IsControl = false,
        };
        AddControl(_fameControl);

        // 查看角色时，行会旗帜是 GameInter Image + Overlay 两层。
        _guildFlagBase = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = -1,
            Location = new Vector2I(30, 150),
            Visible = false,
            IsControl = false,
        };
        _guildFlagOverlay = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = -1,
            Location = new Vector2I(30, 150),
            Visible = false,
            IsControl = false,
            UseOverlayTexture = true,
        };
        AddControl(_guildFlagBase);
        AddControl(_guildFlagOverlay);

        Grid = new DXItemCell[17];

        for (int i = 0; i < 17; i++)
        {
            int idx = i;
            bool isBodySlot = i == (int)EquipmentSlot.Weapon || i == (int)EquipmentSlot.Armour
                || i == (int)EquipmentSlot.Helmet || i == (int)EquipmentSlot.Shield;
            var cell = new DXItemCell
            {
                Location = SlotPositions[i] + new Vector2I(0, 45), // 相对窗口 (标题栏19 + 页签栏26 = 45)
                ItemGrid = null, // GameScene 注入 Equipment
                Slot = i,
                GridType = GridType.Equipment,
                Hidden = isBodySlot,
            };
            cell.Size = i switch
            {
                (int)EquipmentSlot.Weapon => new Vector2I(65, 90),
                (int)EquipmentSlot.Armour => new Vector2I(70, 150),
                (int)EquipmentSlot.Helmet => new Vector2I(35, 35),
                (int)EquipmentSlot.Amulet or (int)EquipmentSlot.Shoes => new Vector2I(36, 75),
                _ => new Vector2I(36, 36),
            };
            int bgIndex = SlotBackgrounds[i];
            if (bgIndex >= 0)
            {
                cell.BeforeDraw += (o, e) => DrawSlotBackground(cell, bgIndex);
            }
            AddControl(cell);
            Grid[i] = cell;
        }

        WeightLabel = new DXLabel
        {
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Location = new Vector2I(15, 395),
            AutoSize = false,
            Size = new Vector2I(301, 18),
            FontSize = 8,
            IsControl = false,
        };
        AddControl(WeightLabel);
    }

    /// <summary>按最早 EI 客户端 GameInter F200 的装备页坐标重排。</summary>
    public void ApplyLegacyEiLayout(bool resetView = true)
    {
        _legacyEiLayout = true;
        if (resetView) _legacyEquipmentView = false;
        Clip = true;
        _background.LibraryFile = LibraryFile.GameInter;
        _background.StretchImage = false;

        foreach (var tab in _mainTabs) tab.Button.Visible = false;
        _attributePanel.Visible = false;
        _hermitPanel.Visible = false;
        _statsPanel.Visible = false;
        WeightLabel.Visible = false;
        _characterNameLabel.Visible = false;
        _guildNameLabel.Visible = false;
        _guildRankLabel.Visible = false;
        _marriageIcon.Visible = false;
        _marriageLabel.Visible = false;
        _fameControl.Visible = false;
        _guildFlagBase.Visible = false;
        _guildFlagOverlay.Visible = false;
        _doll.Visible = true;
        _doll.Position = new Vector2(122, 164);
        BuildLegacyAttributeLabels();
        BuildLegacyExpandedPanel();

        // These are the original window-relative SetRect records. The three
        // large records are PaperDoll/Equip.wil visual regions, but remain
        // actual cells so their hit-test and wire slot index are preserved.
        var confirmed = new Dictionary<EquipmentSlot, (Vector2I Position, Vector2I Size)>
        {
            [EquipmentSlot.Weapon] = (new(86, 114), new(60, 90)),
            [EquipmentSlot.Armour] = (new(38, 70), new(53, 84)),
            [EquipmentSlot.Necklace] = (new(94, 71), new(49, 33)),
            [EquipmentSlot.Helmet] = (new(27, 264), new(38, 38)),
            [EquipmentSlot.Torch] = (new(177, 70), new(38, 38)),
            [EquipmentSlot.BraceletL] = (new(27, 186), new(38, 38)),
            [EquipmentSlot.BraceletR] = (new(175, 186), new(38, 38)),
            [EquipmentSlot.RingL] = (new(27, 227), new(38, 38)),
            [EquipmentSlot.RingR] = (new(175, 227), new(38, 38)),
            [EquipmentSlot.Shoes] = (new(64, 264), new(38, 38)),
            [EquipmentSlot.Poison] = (new(103, 264), new(38, 38)),
        };
        foreach (var cell in Grid)
        {
            if (cell == null) continue;
            var slot = (EquipmentSlot)cell.Slot;
            cell.Visible = confirmed.TryGetValue(slot, out var geometry);
            if (!cell.Visible) continue;
            cell.Location = geometry.Position;
            cell.Size = geometry.Size;
            cell.Hidden = false;
            bool characterArea = slot is EquipmentSlot.Weapon or EquipmentSlot.Armour or EquipmentSlot.Necklace;
            cell.DrawItemIconEnabled = !characterArea;
            cell.ItemLibraryFile = characterArea ? LibraryFile.Equip : LibraryFile.Inventory;
            cell.RefreshItem();
        }

        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(212, 298);
        _closeButton.Size = new Vector2I(28, 26);
        _legacyViewToggle.Location = new Vector2I(176, 264);
        _legacyViewToggle.Size = new Vector2I(36, 36);
        _legacyViewToggle.Visible = true;
        SetLegacyView(_legacyEquipmentView);
        GD.Print($"[LegacyCharacter] view={(_legacyEquipmentView ? "equipment" : "attribute")} " +
            $"root={Size} background=GameInter[{_background.Index}] art={_background.Size} offset={_background.Location} " +
            $"hitRecords={confirmed.Count} paperDoll={_doll.Position}");
    }

    private void BuildLegacyAttributeLabels()
    {
        if (_legacyAttributeLabels.Count > 0) return;
        for (int i = 0; i < LegacyFirstAttributeNames.Length; i++)
        {
            int y = 0x43 + i * 15;
            var label = new DXLabel
            {
                FontSize = 8,
                TextColour = LegacyAttributeLabelColour,
                AutoSize = true,
                Location = new Vector2I(0xFF, y),
                IsControl = false,
                Visible = false,
                Text = LegacyFirstAttributeNames[i],
            };
            var value = new DXLabel
            {
                FontSize = 8,
                TextColour = LegacyAttributeValueColour,
                AutoSize = true,
                Location = new Vector2I(0xFF + 76, y),
                IsControl = false,
                Visible = false,
            };
            AddControl(label);
            AddControl(value);
            _legacyAttributeLabels.Add((label, value));
        }
    }

    private void RefreshLegacyAttributeLabels()
    {
        if (_legacyAttributeLabels.Count == 0) return;
        var game = GameScene.Game;
        var stats = game?.PlayerStats;
        int value(Stat stat) => stats == null ? 0 : stats[stat];
        string experience = game == null || game.PlayerMaxExperience <= 0
            ? string.Empty
            : $"{game.PlayerExperience / game.PlayerMaxExperience * 100m:0.00}%";
        string[] values =
        {
            game?.PlayerLevel.ToString() ?? string.Empty,
            game == null ? string.Empty : $"{game.CurrentHealth}/{value(Stat.Health)}",
            game == null ? string.Empty : $"{game.CurrentMana}/{value(Stat.Mana)}",
            experience,
            game == null ? string.Empty : $"{game.BagWeight}/{value(Stat.BagWeight)}",
            game == null ? string.Empty : $"{game.WearWeight}/{value(Stat.WearWeight)}",
            string.Empty, // EI 腕力字段的原始 byte 尚未映射到 Zircon Stat。
            value(Stat.Accuracy).ToString(),
            value(Stat.Agility).ToString(),
            string.Empty, // 魔法躲避：无独立 Zircon 语义证据。
            string.Empty, // 毒物躲避：不把 PoisonResistance 冒充为原字段。
            string.Empty, // 中毒恢复：无独立 Zircon 语义证据。
            string.Empty, // 生命恢复：无独立 Zircon 语义证据。
            string.Empty, // 魔法恢复：无独立 Zircon 语义证据。
        };
        for (int i = 0; i < _legacyAttributeLabels.Count; i++)
        {
            _legacyAttributeLabels[i].Label.Text = LegacyFirstAttributeNames[i];
            _legacyAttributeLabels[i].Value.Text = values[i];
        }

        foreach (var entry in _legacyExpandedLabels)
        {
            entry.Label.Text = entry.Name;
            entry.Label.TextColour = entry.Name is "防御" or "攻击" or "魔法" or "魔法防御力"
                ? Colors.Black : LegacyAttributeLabelColour;
            if (entry.Stat == null)
            {
                // EI draws 魔法/魔法防御力 as label-only rows in this paint region.
                entry.Value.Text = string.Empty;
                entry.Value.Visible = false;
                continue;
            }

            int min = stats?[entry.Stat.Value] ?? 0;
            entry.Value.Text = entry.MaxStat == null
                ? min.ToString()
                : $"{min}-{stats?[entry.MaxStat.Value] ?? 0}";
            entry.Value.Visible = true;
        }
    }

    private void BuildLegacyExpandedPanel()
    {
        if (_legacyExpandedLabels.Count > 0) return;

        var rows = new (string Name, Stat? Stat, Stat? MaxStat)[]
        {
            ("防御", Stat.MinAC, Stat.MaxAC),
            ("攻击", Stat.MinDC, Stat.MaxDC),
            ("魔法", null, null), // EI 本窗口仅绘制标签，不显示值。
            ("火(火焰)", Stat.FireAttack, null),
            ("冰(冰冻)", Stat.IceAttack, null),
            ("电(雷电)", Stat.LightningAttack, null),
            ("风(狂风)", Stat.WindAttack, null),
            ("治疗(神圣)", Stat.HolyAttack, null),
            ("攻击(黑暗)", Stat.DarkAttack, null),
            ("召唤(幻影)", Stat.PhantomAttack, null),
            ("魔法防御力", null, null), // EI 本窗口仅绘制标签，不显示值。
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int y = 0x1E + i * 15;
            var label = new DXLabel
            {
                FontSize = 8,
                TextColour = row.Name is "防御" or "攻击" or "魔法" or "魔法防御力"
                    ? Colors.Black : LegacyAttributeLabelColour,
                AutoSize = true,
                Location = new Vector2I(0x17F, y),
                IsControl = false,
                Visible = false,
                Text = row.Name,
            };
            var value = new DXLabel
            {
                FontSize = 8,
                TextColour = LegacyAttributeValueColour,
                AutoSize = true,
                Location = new Vector2I(0x1C3, y),
                IsControl = false,
                Visible = false,
            };
            AddControl(label);
            AddControl(value);
            _legacyExpandedLabels.Add((label, value, row.Name, row.Stat, row.MaxStat));
        }
    }


    public bool AuditLegacyEiLayout(out string details)
    {
        ShowOwn();
        int visibleSlots = Grid?.Count(cell => cell?.Visible == true) ?? 0;
        Vector2I originalLocation = Location;
        bool initial = _background.Index == 200;
        ToggleLegacyView();
        bool expanded = _background.Index == 201
            && Size == new Vector2I(520, 328)
            && Location == originalLocation
            && _background.Location == new Vector2I(-252, -92)
            && _legacyViewToggle.Index == 168
            && _legacyAttributeLabels.All(entry => entry.Label.Visible)
            && _legacyExpandedLabels.All(entry => entry.Label.Visible);
        ToggleLegacyView();
        bool restored = _background.Index == 200
            && Size == new Vector2I(244, 328)
            && Location == originalLocation
            && _background.Location == new Vector2I(-6, -92)
            && _legacyViewToggle.Index == 171;
        var expectedSlots = new Dictionary<EquipmentSlot, (Vector2I Position, Vector2I Size)>
        {
            [EquipmentSlot.Weapon] = (new(86, 114), new(60, 90)),
            [EquipmentSlot.Armour] = (new(38, 70), new(53, 84)),
            [EquipmentSlot.Helmet] = (new(27, 264), new(38, 38)),
            [EquipmentSlot.Torch] = (new(177, 70), new(38, 38)),
            [EquipmentSlot.Necklace] = (new(94, 71), new(49, 33)),
            [EquipmentSlot.BraceletL] = (new(27, 186), new(38, 38)),
            [EquipmentSlot.BraceletR] = (new(175, 186), new(38, 38)),
            [EquipmentSlot.RingL] = (new(27, 227), new(38, 38)),
            [EquipmentSlot.RingR] = (new(175, 227), new(38, 38)),
            [EquipmentSlot.Shoes] = (new(64, 264), new(38, 38)),
            [EquipmentSlot.Poison] = (new(103, 264), new(38, 38)),
        };
        bool slotGeometry = expectedSlots.All(pair => Grid.Any(cell => cell != null
            && (EquipmentSlot)cell.Slot == pair.Key
            && cell.Visible
            && cell.Location == pair.Value.Position
            && cell.Size == pair.Value.Size));
        bool ok = initial && expanded && restored
            && Size == new Vector2I(244, 328)
            && _background.Index == 200
            && _legacyViewToggle.Location == new Vector2I(176, 264)
            && _legacyViewToggle.Size == new Vector2I(36, 36)
            && visibleSlots == expectedSlots.Count
            && slotGeometry;
        details = $"size={Size} background=F{_background.Index} expanded={expanded} toggle={_legacyViewToggle.Location}/{_legacyViewToggle.Size} visibleSlots={visibleSlots} slots={slotGeometry} switch={expanded && restored}";
        return ok;
    }

    private void ToggleLegacyView() => SetLegacyView(!_legacyEquipmentView);

    private void SetLegacyView(bool equipmentView)
    {
        _legacyEquipmentView = equipmentView;
        _legacyViewToggle.Index = equipmentView ? 168 : 171;
        _legacyViewToggle.HoverIndex = equipmentView ? 169 : 172;
        _legacyViewToggle.PressedIndex = equipmentView ? 169 : 172;
        foreach (var entry in _legacyAttributeLabels)
        {
            entry.Label.Visible = equipmentView;
            entry.Value.Visible = equipmentView && !string.IsNullOrEmpty(entry.Value.Text);
        }
        foreach (var entry in _legacyExpandedLabels)
        {
            entry.Label.Visible = equipmentView;
            entry.Value.Visible = equipmentView && entry.Stat != null;
        }

        Size = equipmentView ? new Vector2I(520, 328) : new Vector2I(244, 328);
        Clip = true;
        _background.Index = equipmentView ? 201 : 200;
        // F200/F201 each has a transparent top/left canvas; align the alpha
        // bbox to the same root origin instead of compensating at the window.
        _background.Location = equipmentView ? new Vector2I(-252, -92) : new Vector2I(-6, -92);
        _background.Size = MirSkin.GetSize(LibraryFile.GameInter, _background.Index);
        UpdateClientAreaForLegacySkin();
        RefreshLegacyAttributeLabels();
        GD.Print($"[LegacyCharacter] switch view={(equipmentView ? "equipment" : "attribute")} " +
            $"root={Size} background=F{_background.Index} offset={_background.Location} " +
            $"toggle={_legacyViewToggle.Location}/{_legacyViewToggle.Size}");
        QueueRedraw();
    }

    /// <summary>仅供旧版窗口直达验收：打开右侧扩展属性面板。</summary>
    public void SetLegacyExpandedForAudit()
    {
        if (_legacyEiLayout && !_legacyEquipmentView)
            ToggleLegacyView();
    }

    private void AddTab(string text, int x, int backgroundIndex)
    {
        var tab = new DXButton
        {
            Text = text,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(56, 22),
            Location = new Vector2I(8 + x, 18),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Type = backgroundIndex == 110 ? DXButton.ButtonType.SelectedTab : DXButton.ButtonType.DeselectedTab,
        };
        tab.MouseClick += (o, e) => SelectTab(backgroundIndex);
        AddControl(tab);
        _mainTabs.Add((tab, backgroundIndex));
    }

    private void SelectTab(int backgroundIndex)
    {
        _background.Index = backgroundIndex;
        foreach (var tab in _mainTabs)
            tab.Button.Type = tab.Background == backgroundIndex
                ? DXButton.ButtonType.SelectedTab
                : DXButton.ButtonType.DeselectedTab;
        bool character = backgroundIndex == 110;
        bool discipline = backgroundIndex == 112;
        bool hermit = backgroundIndex == 111;
        bool inspect = _inspectMode;
        _doll.Visible = character;
        _attributePanel.Visible = discipline && !inspect;
        _hermitPanel.Visible = hermit && !inspect;
        _statsPanel.Visible = character && !inspect;
        foreach (var cell in Grid)
            if (cell != null) cell.Visible = character;
        WeightLabel.Visible = character && !inspect;
    }

    /// <summary>显示原版 Inspect 包中的角色资料；装备格只读，不能把查看对象的装备当成本地装备移动。</summary>
    public void ApplyInspect(S.Inspect info)
    {
        if (info == null) return;
        _inspectMode = true;
        _background.Index = 115;
        Size = InspectSize;
        _background.Size = InspectSize;
        _doll.Visible = true;
        _attributePanel.Visible = false;
        _hermitPanel.Visible = false;
        _statsPanel.Visible = false;
        WeightLabel.Visible = false;
        _characterNameLabel.Text = $"{info.Name} Lv.{info.Level} {info.Class.Local()}";
        _guildNameLabel.Text = info.GuildName ?? string.Empty;
        _guildRankLabel.Text = info.GuildRank ?? string.Empty;
        _partnerName = info.Partner ?? string.Empty;
        _inspectGuildFlag = info.GuildFlag;
        _inspectGuildColour = info.GuildColour;
        RefreshMarriageAndGuild();
        RefreshLegacyAttributeLabels();

        Array.Clear(_inspectItems, 0, _inspectItems.Length);
        foreach (var item in info.Items ?? new List<ClientUserItem>())
        {
            if (item == null || item.Slot < 0 || item.Slot >= _inspectItems.Length) continue;
            _inspectItems[item.Slot] = item;
            if (item.Info == null) item.Complete();
        }
        _doll.SetInspect(info, _inspectItems);
        foreach (var cell in Grid)
        {
            if (cell == null) continue;
            cell.ItemGrid = _inspectItems;
            cell.GridType = GridType.Inspect;
            cell.ReadOnly = true;
            cell.RefreshItem();
            cell.Visible = true;
        }
        QueueRedraw();
    }

    /// <summary>重新切回自己的角色页，恢复可操作装备格和纸娃娃。</summary>
    public void ShowOwn()
    {
        _inspectMode = false;
        _background.Index = 110;
        Size = OwnSize;
        _background.Size = OwnSize;
        _doll.ClearInspect();
        _partnerName = _ownPartnerName;
        _inspectGuildFlag = -1;
        _inspectGuildColour = System.Drawing.Color.White;
        _doll.Visible = true;
        _attributePanel.Visible = false;
        _hermitPanel.Visible = false;
        _statsPanel.Visible = true;
        WeightLabel.Visible = true;
        RefreshMarriageAndGuild();
        foreach (var cell in Grid)
        {
            if (cell == null) continue;
            cell.ItemGrid = GameScene.Game?.Equipment;
            cell.GridType = GridType.Equipment;
            cell.ReadOnly = false;
            cell.RefreshItem();
            cell.Visible = true;
        }
        if (_legacyEiLayout)
        {
            // 真实登录/换装路径会调用 ShowOwn；旧版模式下必须重新应用
            // F200/F201 和已确认的 38×38 格子，不能恢复到现代 F110。
            ApplyLegacyEiLayout(resetView: false);
            RefreshLegacyAttributeLabels();
        }
        QueueRedraw();
    }

    private void BuildAttributePanel()
    {
        _disciplineLevelImage = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 215,
            FixedSize = true,
            Size = new Vector2I(256, 192),
            Location = new Vector2I(37, 64),
            IsControl = false,
        };
        _attributePanel.AddControl(_disciplineLevelImage);
        _attributePanel.AddControl(new DXLabel { Text = Lang.MainPanelLevelHint, FontSize = 9, Location = new Vector2I(13, 313), IsControl = false });
        _disciplineLevelValue = new DXLabel
        {
            Text = "0",
            FontSize = 9,
            TextColour = Colors.White,
            AutoSize = false,
            Size = new Vector2I(46, 18),
            Location = new Vector2I(116, 314),
            Align = HorizontalAlignment.Center,
            IsControl = false,
        };
        _attributePanel.AddControl(_disciplineLevelValue);
        _attributePanel.AddControl(new DXLabel { Text = Lang.CompanionDialogCompanionTabExpLabel, FontSize = 9, Location = new Vector2I(13, 336), IsControl = false });
        _disciplineExperienceLabel = new DXLabel
        {
            Text = "0/0",
            FontSize = 9,
            TextColour = Colors.White,
            AutoSize = false,
            Size = new Vector2I(303, 18),
            Location = new Vector2I(14, 336),
            Align = HorizontalAlignment.Center,
            IsControl = false,
        };
        _attributePanel.AddControl(_disciplineExperienceLabel);
        _disciplineLabel = new DXLabel { Text = Lang.CharacterUi72Label, FontSize = 9, TextColour = new Color(1f, .85f, .3f), Location = new Vector2I(14, 358), Size = new Vector2I(150, 18), IsControl = false };
        _attributePanel.AddControl(_disciplineLabel);
        _disciplineButton = new DXButton { Text = Lang.CharacterUi73Label, FontSize = 9, Size = new Vector2I(120, 27), Location = new Vector2I(182, 266), LibraryFile = LibraryFile.Interface, Index = -1 };
        _disciplineButton.MouseClick += (o, e) => GameScene.Game?.SendIncreaseDiscipline();
        _attributePanel.AddControl(_disciplineButton);
        RefreshDiscipline();

        for (int i = 0; i < 4; i++)
        {
            var icon = new DXImageControl
            {
                LibraryFile = LibraryFile.MagicIcon,
                Index = -1,
                FixedSize = true,
                Size = new Vector2I(36, 36),
                Location = new Vector2I(51 + i * 62, 380),
                IsControl = true,
            };
            int slot = i;
            icon.MouseClick += (o, e) => ClearDisciplineMagic(slot);
            _attributePanel.AddControl(icon);
            _disciplineMagicIcons.Add(icon);
        }
        var hermitStats = new[] { Stat.MaxAC, Stat.MaxMR, Stat.Health, Stat.Mana, Stat.MaxDC, Stat.MaxMC, Stat.MaxSC, Stat.WeaponElement };
        for (int i = 0; i < hermitStats.Length; i++)
        {
            var stat = hermitStats[i];
            var button = new DXButton { Text = string.Format(Lang.CharacterUi74Label, stat), FontSize = 8, Size = new Vector2I(78, 23), Location = new Vector2I(18 + (i % 4) * 82, 225 + (i / 4) * 26), LibraryFile = LibraryFile.Interface, Index = -1 };
            button.MouseClick += (o, e) => GameScene.Game?.SendHermit(stat);
            _hermitPanel.AddControl(button);
        }
        RefreshDisciplineMagicIcons();
    }

    private void BuildStatsPanel()
    {
        string[] names = { Lang.CharacterCharacterTabStatsAttackTabLabel, Lang.CharacterCharacterTabStatsDefenseTabLabel, Lang.CharacterCharacterTabStatsWeightTabLabel, Lang.GameStoreDialogOtherLabel, Lang.CharacterUi75Label, Lang.CharacterUi76Label, Lang.CharacterUi77Label };
        for (int i = 0; i < names.Length; i++)
        {
            int page = i;
            var tab = new DXButton
            {
                Text = names[i], FontSize = 8, LibraryFile = LibraryFile.Interface, Index = -1,
                Location = new Vector2I(21 + i * 44, 0), Size = new Vector2I(43, 20),
            };
            tab.MouseClick += (o, e) => SelectStatsPage(page);
            _statsPanel.AddControl(tab);
            _statsTabs.Add(tab);

            var content = new DXControl
            {
                Location = new Vector2I(0, 21),
                Size = new Vector2I(331, 103),
                Clip = true,
                Visible = i == 0,
                IsControl = false,
            };
            _statsPanel.AddControl(content);
            _statPages.Add(content);
        }

        AddStatRow(0, Lang.CharacterCharacterTabStatsAttackTabLabel, Stat.MaxDC, Stat.MinDC, 15, 6);
        AddStatRow(0, Lang.MagicDialogTitle, Stat.MaxMC, Stat.MinMC, 15, 28);
        AddStatRow(0, Lang.CharacterTaoLabel, Stat.MaxSC, Stat.MinSC, 15, 50);
        AddStatRow(0, Lang.CharacterUi79Label, Stat.CriticalDamage, null, 15, 72);
        AddStatRow(0, Lang.MainPanelAccuracyHint, Stat.Accuracy, null, 168, 6);
        AddStatRow(0, Lang.CharacterUi80Label, Stat.AttackSpeed, null, 168, 28);
        AddStatRow(0, Lang.CommonStatusHoly, Stat.Luck, null, 168, 50);
        AddStatRow(0, Lang.CharacterUi81Label, Stat.CriticalChance, null, 168, 72);

        AddStatRow(1, Lang.CharacterCharacterTabStatsDefenseTabLabel, Stat.MaxAC, Stat.MinAC, 15, 6);
        AddStatRow(1, Lang.CommonStatusMR, Stat.MaxMR, Stat.MinMR, 15, 28);
        AddStatRow(1, Lang.CharacterUi82Label, Stat.Agility, null, 168, 6);
        AddStatRow(1, Lang.CharacterUi83Label, Stat.LifeSteal, null, 168, 28);

        AddWeightRow(2, Lang.CharacterWeightLabel, true, 15, 6);
        AddWeightRow(2, Lang.CharacterWeightLabel2, false, 15, 28);

        AddStatRow(3, Lang.CharacterUi86Label, Stat.Comfort, null, 15, 6);
        AddStatRow(3, Lang.CharacterUi87Label, Stat.PickUpRadius, null, 15, 28);
        AddStatRow(3, Lang.CharacterGoldLabel, Stat.GoldRate, null, 168, 6);
        AddStatRow(3, Lang.CharacterUi89Label, Stat.DropRate, null, 168, 28);
        AddStatRow(3, Lang.CharacterExpLabel, Stat.ExperienceRate, null, 168, 50);

        (Stat stat, string name, int icon)[] elements =
        {
            (Stat.FireAttack, Lang.CharacterUi91Label, 600), (Stat.IceAttack, Lang.CharacterUi92Label, 601),
            (Stat.LightningAttack, Lang.CharacterUi93Label, 602), (Stat.WindAttack, Lang.CharacterUi94Label, 603),
            (Stat.HolyAttack, Lang.CharacterUi95Label, 604), (Stat.DarkAttack, Lang.CommonStatusDark, 605),
            (Stat.PhantomAttack, Lang.CharacterUi96Label, 606),
        };
        AddElementPage(4, elements, 1);

        (Stat stat, string name, int icon)[] resistances =
        {
            (Stat.FireResistance, Lang.CharacterUi91Label, 600), (Stat.IceResistance, Lang.CharacterUi92Label, 601),
            (Stat.LightningResistance, Lang.CharacterUi93Label, 602), (Stat.WindResistance, Lang.CharacterUi94Label, 603),
            (Stat.HolyResistance, Lang.CharacterUi95Label, 604), (Stat.DarkResistance, Lang.CommonStatusDark, 605),
            (Stat.PhantomResistance, Lang.CharacterUi96Label, 606), (Stat.PhysicalResistance, Lang.CommonStatusPhysical, 1517),
        };
        AddElementPage(5, resistances, 2);
        AddElementPage(6, resistances, 3);
        RefreshStatsPanel();
    }

    private void AddStatRow(int page, string title, Stat stat, Stat? minStat, int x, int y)
    {
        _statPages[page].AddControl(new DXLabel
        {
            Text = $"{title}:", FontSize = 8, TextColour = Colors.White,
            Location = new Vector2I(x, y), Size = new Vector2I(45, 16), AutoSize = false, IsControl = false,
        });
        var value = new DXLabel
        {
            Text = "0", FontSize = 8, TextColour = Colors.White,
            Location = new Vector2I(x + 45, y), Size = new Vector2I(100, 16), AutoSize = false,
            Align = HorizontalAlignment.Right, IsControl = false,
        };
        _statPages[page].AddControl(value);
        _statBindings.Add((value, stat, minStat, 0));
    }

    private void AddWeightRow(int page, string title, bool wear, int x, int y)
    {
        _statPages[page].AddControl(new DXLabel
        {
            Text = $"{title}:", FontSize = 8, TextColour = Colors.White,
            Location = new Vector2I(x, y), Size = new Vector2I(80, 16), AutoSize = false, IsControl = false,
        });
        var value = new DXLabel
        {
            Text = "0 / 0", FontSize = 8, TextColour = Colors.White,
            Location = new Vector2I(x + 80, y), Size = new Vector2I(100, 16), AutoSize = false,
            Align = HorizontalAlignment.Right, IsControl = false,
        };
        _statPages[page].AddControl(value);
        if (wear) _wearWeightValue = value; else _handWeightValue = value;
    }

    private void AddElementPage(int page, (Stat stat, string name, int icon)[] elements, int mode)
    {
        const int rowSpacing = 22;
        for (int i = 0; i < elements.Length; i++)
        {
            int column = i >= 4 ? 1 : 0;
            int row = i >= 4 ? i - 4 : i;
            int x = column == 0 ? 15 : 168;
            int y = 6 + row * rowSpacing;
            var item = elements[i];
            _statPages[page].AddControl(new DXLabel
            {
                Text = $"{item.name}:", FontSize = 8, TextColour = Colors.White,
                Location = new Vector2I(x, y), Size = new Vector2I(55, 16), AutoSize = false, IsControl = false,
            });
            var icon = new DXImageControl
            {
                LibraryFile = item.icon >= 1500 ? LibraryFile.GameInter : LibraryFile.ProgUse,
                Index = item.icon, FixedSize = true, Size = new Vector2I(18, 18),
                Location = new Vector2I(x + 58, y - 3), IsControl = false,
            };
            var value = new DXLabel
            {
                Text = "0", FontSize = 8, TextColour = new Color(.35f, .35f, .35f),
                Location = new Vector2I(x + 79, y), Size = new Vector2I(50, 16), AutoSize = false,
                Align = HorizontalAlignment.Right, IsControl = false, Tag = icon,
            };
            _statPages[page].AddControl(icon);
            _statPages[page].AddControl(value);
            _statBindings.Add((value, item.stat, null, mode));
        }
    }

    private void SelectStatsPage(int page)
    {
        _statsPage = Mathf.Clamp(page, 0, _statPages.Count - 1);
        for (int i = 0; i < _statPages.Count; i++)
            _statPages[i].Visible = i == _statsPage;
        RefreshStatsPanel();
    }

    private void RefreshStatsPanel()
    {
        var stats = GameScene.Game?.PlayerStats;
        if (stats == null)
        {
            foreach (var binding in _statBindings) binding.Value.Text = "0";
            return;
        }

        foreach (var binding in _statBindings)
        {
            int current = stats[binding.Stat];
            if (binding.Mode == 1)
            {
                binding.Value.Text = current > 0 ? $"+{current}" : "0";
                binding.Value.TextColour = current > 0 ? new Color(.1f, .8f, 1f) : new Color(.35f, .35f, .35f);
                if (binding.Value.Tag is DXImageControl icon) icon.ForeColour = current > 0 ? Colors.White : new Color(.35f, .35f, .35f);
            }
            else if (binding.Mode == 2)
            {
                binding.Value.Text = current > 0 ? $"x{current}" : "0";
                binding.Value.TextColour = current > 0 ? new Color(.4f, 1f, .3f) : new Color(.35f, .35f, .35f);
                if (binding.Value.Tag is DXImageControl icon) icon.ForeColour = current > 0 ? Colors.White : new Color(.35f, .35f, .35f);
            }
            else if (binding.Mode == 3)
            {
                binding.Value.Text = current < 0 ? $"x{Math.Abs(current)}" : "0";
                binding.Value.TextColour = current < 0 ? new Color(1f, .35f, .35f) : new Color(.35f, .35f, .35f);
                if (binding.Value.Tag is DXImageControl icon) icon.ForeColour = current < 0 ? Colors.White : new Color(.35f, .35f, .35f);
            }
            else
            {
                binding.Value.Text = binding.MinStat.HasValue
                    ? $"{stats[binding.MinStat.Value]}-{current}"
                    : current.ToString();
            }
        }
        if (_wearWeightValue != null)
            _wearWeightValue.Text = $"{GameScene.Game.WearWeight} / {stats[Stat.WearWeight]}";
        if (_handWeightValue != null)
            _handWeightValue.Text = $"{GameScene.Game.HandWeight} / {stats[Stat.HandWeight]}";
    }

    private static DXLabel CreateCharacterLabel(int fontSize, Color colour) => new()
    {
        FontSize = fontSize,
        TextColour = colour,
        Align = HorizontalAlignment.Center,
        VAlign = VerticalAlignment.Center,
        AutoSize = false,
        Size = new Vector2I(137, 18),
        IsControl = false,
    };

    public override void _Process(double delta)
    {
        if (_draggingBackground)
            ApplyBackgroundDrag();
        if (_draggingBackground && !Input.IsMouseButtonPressed(MouseButton.Left))
            FinishBackgroundDrag();
        if (_inspectMode) return;
        var info = GameScene.Game?.StartInfo;
        if (info == null) return;
        _characterNameLabel.Text = info.Name ?? string.Empty;
        _guildNameLabel.Text = info.GuildName ?? string.Empty;
        _guildRankLabel.Text = info.GuildRank ?? string.Empty;
        RefreshMarriageAndGuild();
        var stats = GameScene.Game.PlayerStats;
        foreach (var entry in _attributeValues)
            entry.Label.Text = stats[entry.Stat].ToString();
        RefreshStatsPanel();
    }

    private void BeginBackgroundDrag()
    {
        _draggingBackground = true;
        _dragStartMouse = GetViewport().GetMousePosition() / GameScene.UiScale;
        _dragStartPosition = Position;
        WindowManager.BringToFront(this);
    }

    private void ApplyBackgroundDrag()
    {
        if (!_draggingBackground) return;
        Vector2 mouse = GetViewport().GetMousePosition() / GameScene.UiScale;
        Vector2 target = _dragStartPosition + mouse - _dragStartMouse;
        Vector2 viewport = GetViewportRect().Size / GameScene.UiScale;
        Position = new Vector2(
            Mathf.Clamp(target.X, 0, Mathf.Max(0, viewport.X - Size.X)),
            Mathf.Clamp(target.Y, 0, Mathf.Max(0, viewport.Y - Size.Y)));
    }

    private void FinishBackgroundDrag()
    {
        _draggingBackground = false;
    }

    private void DrawSlotBackground(DXItemCell cell, int index)
    {
        if (cell.Item != null) return;

        var tex = MirSkin.GetTexture(LibraryFile.Interface, index);
        if (tex == null) return;

        var imgSize = tex.GetSize();
        float x = (cell.Size.X - imgSize.X) / 2f;
        float y = (cell.Size.Y - imgSize.Y) / 2f;
        cell.DrawTextureRect(tex, new Rect2(x, y, imgSize.X, imgSize.Y), false, new Color(1f, 1f, 1f, 0.2f));
    }

    public void SetWeight(int wearWeight, int handWeight)
    {
        // 上限取 PlayerStats（与 RefreshStatsPanel 一致），不能用写死的 0，
        // 否则玩家面板显示 "42 / 0" 会误以为超重。
        var stats = GameScene.Game?.PlayerStats;
        int wearMax = stats?[Stat.WearWeight] ?? 0;
        int handMax = stats?[Stat.HandWeight] ?? 0;
        WeightLabel.Text = string.Format(Lang.CharacterWeightLabel3, wearWeight, handWeight);
        if (_wearWeightValue != null) _wearWeightValue.Text = $"{wearWeight} / {wearMax}";
        if (_handWeightValue != null) _handWeightValue.Text = $"{handWeight} / {handMax}";
        RefreshLegacyState();
    }

    /// <summary>刷新 EI 状态页中来自协议事件的动态字段。</summary>
    public void RefreshLegacyState()
    {
        if (!_legacyEiLayout || _inspectMode) return;
        RefreshLegacyAttributeLabels();
        QueueRedraw();
    }

    public void SetPartner(string name)
    {
        _ownPartnerName = name ?? string.Empty;
        if (!_inspectMode) _partnerName = _ownPartnerName;
        if (!_inspectMode) RefreshMarriageAndGuild();
    }

    private void RefreshMarriageAndGuild()
    {
        bool married = !string.IsNullOrWhiteSpace(_partnerName);
        if (_marriageIcon != null) _marriageIcon.Visible = married;
        if (_marriageLabel != null)
        {
            _marriageLabel.Text = _partnerName;
            _marriageLabel.Visible = married;
        }

        int flag = _inspectMode ? _inspectGuildFlag : -1;
        bool visible = _inspectMode && flag >= 0;
        int index = visible ? 1690 + flag : -1;
        if (_guildFlagBase != null) { _guildFlagBase.Index = index; _guildFlagBase.Visible = visible; }
        if (_guildFlagOverlay != null)
        {
            _guildFlagOverlay.Index = index;
            _guildFlagOverlay.ForeColour = ToGodot(_inspectGuildColour);
            _guildFlagOverlay.Visible = visible;
        }
    }

    public bool AuditLayout(out string details)
    {
        bool valid = Size == OwnSize
            && _background.Index == 110
            && _doll.Position == new Vector2(130, 280)
            && Grid.Length == 17
            && _marriageIcon.Position == new Vector2(96, 105)
            && _marriageLabel.Position == new Vector2(112, 100)
            && _fameControl.Position == new Vector2(235, 61);
        details = $"size={Size} grid={Grid.Length} doll={_doll.Position} name={_characterNameLabel.Size}/{_guildNameLabel.Size}/{_guildRankLabel.Size} "
            + $"nameY=0/{_guildNameLabel.Position.Y}/{_guildRankLabel.Position.Y} marriage={_marriageIcon.Position}/{_marriageLabel.Position} fame={_fameControl.Position}";
        return valid;
    }

    public bool AuditTabs(out string details)
    {
        SelectTab(112);
        bool discipline = _attributePanel.Visible && !_hermitPanel.Visible && !_doll.Visible;
        SelectTab(111);
        bool hermit = _hermitPanel.Visible && !_attributePanel.Visible && !_doll.Visible;
        SelectTab(110);
        bool character = _doll.Visible && !_attributePanel.Visible && !_hermitPanel.Visible && _statsPanel.Visible;
        details = $"discipline={discipline} hermit={hermit} character={character}";
        return discipline && hermit && character;
    }

    public bool AuditStats(out string details)
    {
        bool pages = _statPages.Count == 7 && _statsTabs.Count == 7;
        bool exclusive = true;
        for (int i = 0; i < _statPages.Count; i++)
        {
            SelectStatsPage(i);
            exclusive &= _statPages.Count(page => page.Visible) == 1 && _statPages[i].Visible;
        }
        SelectStatsPage(0);
        bool rows = _statBindings.Count >= 31
            && _wearWeightValue != null
            && _handWeightValue != null;
        details = $"tabs={_statsTabs.Count} pages={_statPages.Count} bindings={_statBindings.Count} exclusive={exclusive}";
        return pages && exclusive && rows;
    }

    private static Color ToGodot(System.Drawing.Color c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private void RefreshDisciplineMagicIcons()
    {
        var classType = GameScene.Game?.StartInfo?.Class;
        var infos = Globals.MagicInfoList?.Binding?
            .Where(x => x.School == MagicSchool.Discipline && x.Class == classType)
            .OrderBy(x => x.NeedLevel1)
            .Take(_disciplineMagicIcons.Count)
            .ToList() ?? new List<MagicInfo>();

        for (int i = 0; i < _disciplineMagicIcons.Count; i++)
        {
            var icon = _disciplineMagicIcons[i];
            var info = i < infos.Count ? infos[i] : null;
            icon.Index = info?.Icon ?? -1;
            bool learned = info != null && GameScene.Game?.UserMagics.ContainsKey(info) == true;
            icon.ForeColour = learned ? Colors.White : new Color(.35f, .35f, .35f);
            icon.Tag = info;
        }
    }

    private void ClearDisciplineMagic(int slot)
    {
        if (slot < 0 || slot >= _disciplineMagicIcons.Count) return;
        if (_disciplineMagicIcons[slot].Tag is not MagicInfo info) return;
        if (GameScene.Game?.UserMagics.TryGetValue(info, out var magic) != true || magic == null) return;

        magic.Set1Key = SpellKey.None;
        magic.Set2Key = SpellKey.None;
        magic.Set3Key = SpellKey.None;
        magic.Set4Key = SpellKey.None;
        GameScene.Game.SendMagicKey(magic.Info.Magic, magic.Set1Key, magic.Set2Key, magic.Set3Key, magic.Set4Key);
        GameScene.Game.RefreshMagicBars();
    }

    public void RefreshDiscipline()
    {
        var discipline = GameScene.Game?.StartInfo?.Discipline;
        if (_disciplineLabel == null) return;
        int level = discipline?.Level ?? 0;
        var next = Globals.DisciplineInfoList?.Binding?.FirstOrDefault(x => x.Level == level + 1);
        _disciplineLevelImage.Index = 215 + Mathf.Clamp(level, 0, 20);
        _disciplineLevelValue.Text = level.ToString();
        _disciplineExperienceLabel.Text = next == null
            ? $"{discipline?.Experience ?? 0}/Max"
            : $"{discipline?.Experience ?? 0}/{next.RequiredExperience}";
        _disciplineLabel.Text = next == null
            ? (discipline == null ? Lang.CharacterUi72Label : string.Format(Lang.CharacterExpLabel2, level, discipline.Experience))
            : string.Format(Lang.CharacterGoldLabel2, level, discipline?.Experience ?? 0, next.RequiredExperience, next.RequiredGold);
        if (_disciplineButton != null) _disciplineButton.Enabled = next != null && (GameScene.Game?.StartInfo?.Level ?? 0) >= next.RequiredLevel;
        RefreshDisciplineMagicIcons();
    }
}
