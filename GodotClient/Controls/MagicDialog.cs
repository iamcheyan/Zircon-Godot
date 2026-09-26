using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Formats;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 技能列表窗口 (移植自 Client/Scenes/Views/MagicDialog.cs)。
/// 列出已学技能 (GameScene.UserMagics), 每行: 图标 + 名称 + 等级 + 当前栏组键位。
/// 包含职业页签、页签翻页、技能列表滚动、经验条和快捷键绑定。
/// 打开: Q 键 (KeyBindAction.MagicWindow=E)。
/// </summary>
public partial class MagicDialog : DXWindow
{
    private readonly List<MagicCellView> _cells = new();
    private readonly Dictionary<MagicSchool, DXButton> _schoolButtons = new();
    private DXControl _list;
    private DXVScrollBar _scrollBar;
    private DXImageControl _header;
    private LegacyUiFrame _background;
    private MagicSchool _selectedSchool;
    private List<MagicSchool> _tabOrder = new();
    private int _tabPageStart;
    private DXButton _tabPrevious;
    private DXButton _tabNext;
    private DXButton _closeButton;
    private bool _legacyEiLayout;
    private const int LegacyPageSize = 6;
    private static readonly int[] LegacySkillRowY = { 26, 72, 118, 164, 210, 256 };
    private const int LegacyDetailX = 235;
    private const int LegacyDetailY = 30;
    private const int LegacyDetailWidth = 165;
    private const int LegacyLineHeight = 15;
    private static readonly MagicSchool[] LegacySchoolOrder =
    {
        MagicSchool.Fire,
        MagicSchool.Ice,
        MagicSchool.Lightning,
        MagicSchool.Wind,
        MagicSchool.Holy,
        MagicSchool.Dark,
        MagicSchool.Phantom,
        MagicSchool.Physical,
    };
    private readonly List<LegacySkillRowView> _legacySkillRows = new();
    private readonly List<(MagicInfo Info, ClientUserMagic UserMagic)> _legacyRuntimeEntries = new();
    private (MagicInfo Info, ClientUserMagic UserMagic)? _legacySelectedSkill;
    private int _legacyPage;
    private LegacySkillDetailView _legacyDetail;
    private DXButton _legacyAuxControl;
    public MagicDialog()
    {
        // 原版 MagicDialog 自己在背景图上创建 TitleLabel，位置为 y=8；
        // 不能使用 DXWindow 的通用标题栏（y=2），否则会与 HeaderImage
        // 重叠并把技能页签整体视觉上推高。
        HasTitle = false;
        Movable = true;
        Text = Lang.MagicSkillLabel;
        Clip = true;
        // 原版固定 419x511；技能按 MagicSchool 分页，每页内部滚动。
        Size = new Vector2I(419, 511);

        _header = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = HeaderIndex(),
            Location = Vector2I.Zero,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_header);

        _background = new LegacyUiFrame
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 400,
            Location = new Vector2I(0, 66),
            Size = new Vector2I(419, 445),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddControl(_background);

        _closeButton = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        _closeButton.Location = new Vector2I((int)Size.X - (int)_closeButton.Size.X - 3, 3);
        _closeButton.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(_closeButton);

        AddControl(new DXLabel
        {
            Text = Lang.MagicDialogTitle,
            FontSize = 10,
            TextColour = new Color(1f, 0.85f, 0.3f),
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Location = new Vector2I(0, 8),
            Size = new Vector2I((int)Size.X, 18),
            IsControl = false,
        });

        _list = new DXControl
        {
            // 原版 MagicTab: TabControl(0,40) + Tab(10,30)，列表单行从
            // Tab 内 (5,7) 开始，绝对坐标为 (15,70)，客户区 369x418。
            Location = new Vector2I(15, 70),
            Size = new Vector2I(375, 418),
            Clip = true,
            IsControl = true,
            PassThrough = false,
        };
        AddControl(_list);

        _scrollBar = new DXVScrollBar
        {
            Location = new Vector2I(390, 68),
            Size = new Vector2I(20, 424),
            VisibleSize = 418,
            Change = 54,
            // 原版 MagicTab 的滚动条不设置 HideWhenNoScroll，始终占位显示。
            HideWhenNoScroll = false,
            BackColour = Colors.Transparent,
            Border = false,
        };
        // 技能页使用 Interface 60/61/62 专用滚动条素材。
        _scrollBar.UpButton.Index = 61;
        _scrollBar.DownButton.Index = 62;
        _scrollBar.PositionBar.Index = 60;
        _scrollBar.ValueChanged += (o, e) => UpdateCellLocations();
        AddControl(_scrollBar);
        _list.MouseWheel += _scrollBar.DoMouseWheel;

        // DXTabControl in the original client exposes left/right buttons when
        // the school tabs do not fit in the 419px window.
        _tabPrevious = CreateTabPagerButton("<", true);
        _tabNext = CreateTabPagerButton(">", false);

        Visible = false;
    }

    /// <summary>
    /// EI 技能书使用 GameInter F400 的原始透明帧；根控件保留当前 452x380
    /// 配准（F400 有效绘制区从 (-30,-67) 开始）。296x332 是另一路
    /// 主初始化元数据，和 wrapper 的最终 SetRect 仍存在研究证据冲突，不能
    /// 在这里伪称已闭合。
    /// </summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(452, 380);
        _header.LibraryFile = LibraryFile.GameInter;
        _header.Index = 400;
        _header.Location = new Vector2I(-30, -67);
        _header.Size = MirSkin.GetSize(LibraryFile.GameInter, 400);
        _header.StretchImage = false;
        if (_legacyDetail == null)
        {
            _legacyDetail = new LegacySkillDetailView
            {
                Size = Size,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddControl(_legacyDetail);
        }
        if (_legacyAuxControl == null)
        {
            // F440/441 (399,340) 20x12 是证据 11 个控件里三个**可点击**帧控件之一
            // （0x43AC80 先处理 +0xD8/+0x18C/+0x240 = 440/441、410/411、412/413），
            // 美术为小叉＝关闭形态。此前做成 DXImageControl + MouseFilter=Ignore，
            // 即真正的关闭控件成了纯装饰，功能被错位的 161/162 顶替。
            _legacyAuxControl = new DXButton
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 440,
                HoverIndex = 441,
                PressedIndex = 441,
                FixedSize = true,
                Size = MirSkin.GetSize(LibraryFile.GameInter, 440),
                Location = new Vector2I(399, 340),
            };
            _legacyAuxControl.MouseClick += (_, _) => Close();
            AddControl(_legacyAuxControl);
        }
        _background.Visible = false;
        _list.Visible = false;
        _scrollBar.Visible = false;

        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(418, 348);
        _closeButton.Size = new Vector2I(28, 26);
        // SKL-04 证据的 11 个控件里**没有 161/162**；可点击的三个帧控件是
        // F440/441、F410/411、F412/413。这个 161/162 是我方多加的，关闭功能
        // 已由 F440/441 承担，故隐藏。
        _closeButton.Visible = false;

        ConfigureLegacyPageControls();
        BuildLegacySchoolButtons();
        BuildLegacySkillRows();
        // 右页改渲染 Magic.exp 段落原文（段号即技能 id）。
        if (_legacyDetail != null) _legacyDetail.LegacyEiLayout = true;
        UpdateClientAreaForLegacySkin();
    }

    private void ConfigureLegacyPageControls()
    {
        _tabPrevious.LibraryFile = LibraryFile.GameInter;
        _tabPrevious.Index = 410;
        _tabPrevious.HoverIndex = 411;
        _tabPrevious.PressedIndex = 411;
        _tabPrevious.Text = string.Empty;
        _tabPrevious.Location = new Vector2I(61, 303);
        _tabPrevious.Size = new Vector2I(32, 14);
        _tabPrevious.Visible = true;
        _tabPrevious.MouseClick -= OnLegacyPreviousPage;
        _tabPrevious.MouseClick += OnLegacyPreviousPage;

        _tabNext.LibraryFile = LibraryFile.GameInter;
        _tabNext.Index = 412;
        _tabNext.HoverIndex = 413;
        _tabNext.PressedIndex = 413;
        _tabNext.Text = string.Empty;
        _tabNext.Location = new Vector2I(366, 303);
        _tabNext.Size = new Vector2I(32, 14);
        _tabNext.Visible = true;
        _tabNext.MouseClick -= OnLegacyNextPage;
        _tabNext.MouseClick += OnLegacyNextPage;
    }

    private void OnLegacyPreviousPage(object sender, EventArgs e) => ChangeLegacyPage(-1);
    private void OnLegacyNextPage(object sender, EventArgs e) => ChangeLegacyPage(1);

    private void ChangeLegacyPage(int delta)
    {
        if (!_legacyEiLayout) return;
        int pageCount = LegacyPageCount();
        int next = Math.Clamp(_legacyPage + delta, 0, Math.Max(0, pageCount - 1));
        if (next == _legacyPage) return;
        _legacyPage = next;
        _legacySelectedSkill = null;
        RefreshLegacySkillRows(_legacyRuntimeEntries);
        GD.Print($"[MagicLegacy] page={_legacyPage + 1}/{pageCount} school={_selectedSchool}");
    }

    private void BuildLegacySkillRows()
    {
        foreach (var row in _legacySkillRows)
        {
            RemoveControl(row);
            row.QueueFree();
        }
        _legacySkillRows.Clear();

        // F400 左页六个浅色技能影槽按 Frame400 的(-30,-67)绘制原点
        // 配准到窗口：32px 图标框中心落在约(74,46+46*i)，行首 y=26+46*i。
        // 0x43A370 的运行时RECT写入仍待确认，所以点击区高度仍标为候选。
        for (int i = 0; i < LegacyPageSize; i++)
        {
            var row = new LegacySkillRowView
            {
                Location = new Vector2I(55, LegacySkillRowY[i]),
                Size = new Vector2I(145, 36),
                Visible = false,
            };
            int index = i;
            row.Selected += () =>
            {
                int absolute = _legacyPage * LegacyPageSize + index;
                if (absolute < 0 || absolute >= _legacyRuntimeEntries.Count) return;
                _legacySelectedSkill = _legacyRuntimeEntries[absolute];
                _legacyDetail?.SetSkill(_legacySelectedSkill);
                RefreshLegacySkillRows(_legacyRuntimeEntries);
                QueueRedraw();
                GD.Print($"[MagicLegacy] selected={_legacySelectedSkill.Value.Info.Name} id={_legacySelectedSkill.Value.Info.Magic}");
            };
            AddControl(row);
            _legacySkillRows.Add(row);
        }
    }

    private void RefreshLegacySkillRows(IEnumerable<(MagicInfo Info, ClientUserMagic UserMagic)> entries)
    {
        var all = entries
            .OrderBy(x => x.Info.NeedLevel1)
            .ThenBy(x => x.Info.Name, StringComparer.Ordinal)
            .ToArray();
        _legacyRuntimeEntries.Clear();
        _legacyRuntimeEntries.AddRange(all);

        int pageCount = LegacyPageCount();
        _legacyPage = Math.Clamp(_legacyPage, 0, Math.Max(0, pageCount - 1));
        int first = _legacyPage * LegacyPageSize;
        for (int i = 0; i < _legacySkillRows.Count; i++)
        {
            var row = _legacySkillRows[i];
            int absolute = first + i;
            if (absolute >= all.Length)
            {
                row.Visible = false;
                continue;
            }

            var entry = all[absolute];
            row.Visible = true;
            row.SetEntry(entry.Info, entry.UserMagic,
                _legacySelectedSkill is { } selected && selected.Info == entry.Info);
        }
        _legacyDetail?.SetPage(_legacyPage, pageCount);
        _legacyDetail?.SetSkill(_legacySelectedSkill);

        _tabPrevious.Visible = _legacyPage > 0;
        _tabNext.Visible = _legacyPage + 1 < pageCount;
        QueueRedraw();
    }

    private int LegacyPageCount()
        => (_legacyRuntimeEntries.Count + LegacyPageSize - 1) / LegacyPageSize;

    private void BuildLegacySchoolButtons()
    {
        foreach (var button in _schoolButtons.Values)
        {
            RemoveControl(button);
            button.QueueFree();
        }
        _schoolButtons.Clear();

        (MagicSchool school, int frame, Vector2I location)[] schools =
        {
            (MagicSchool.Fire, 450, new(5, 21)),
            (MagicSchool.Ice, 452, new(3, 56)),
            (MagicSchool.Lightning, 454, new(4, 91)),
            (MagicSchool.Wind, 456, new(2, 126)),
            (MagicSchool.Holy, 458, new(2, 161)),
            // 8 个页签是 8 组互不相同的美术（像素解码确认 450=火焰 452=雪花
            // 454=闪电 456=漩涡 458=四叶 460=钩状 462="2" 464=冠/三叉）。
            // 此前 黑暗/幻影/剑 复用了 火/冰/电 的帧 450/452/454，是错的；
            // skill-window-context.json 里那份重复帧记录也是错的，以
            // skill-window-render-loop-evidence.json::controls 为准。
            (MagicSchool.Dark, 460, new(2, 196)),
            (MagicSchool.Phantom, 462, new(1, 231)),
            (MagicSchool.Physical, 464, new(2, 266)),
        };
        _tabOrder = schools.Select(x => x.school).ToList();
        foreach (var entry in schools)
        {
            MagicSchool school = entry.school;
            var button = new DXButton
            {
                LibraryFile = LibraryFile.GameInter,
                Index = entry.frame,
                HoverIndex = entry.frame + 1,
                PressedIndex = entry.frame + 1,
                Location = entry.location,
                Size = MirSkin.GetSize(LibraryFile.GameInter, entry.frame),
            };
            button.MouseClick += (_, _) => SelectSchool(school);
            AddControl(button);
            _schoolButtons[school] = button;
        }
    }

    public bool AuditLayout(out string details)
    {
        bool valid = !HasTitle
            && Size == new Vector2I(419, 511)
            && _background.Location == new Vector2I(0, 66)
            && _list.Location == new Vector2I(15, 70)
            && _list.Size == new Vector2I(375, 418)
            && _scrollBar.Location == new Vector2I(390, 68)
            && _scrollBar.Size == new Vector2I(20, 424)
            && _scrollBar.VisibleSize == 418
            && ! _scrollBar.HideWhenNoScroll
            && TabHeight() > 0;
        details = $"size={Size} list={_list.Location}/{_list.Size} scroll={_scrollBar.Location}/{_scrollBar.Size} tabHeight={TabHeight()}";
        return valid;
    }

    public bool AuditLegacyEiLayout(out string details)
    {
        (MagicSchool school, Vector2I location, int frame)[] expectedTabs =
        {
            (MagicSchool.Fire, new(5, 21), 450),
            (MagicSchool.Ice, new(3, 56), 452),
            (MagicSchool.Lightning, new(4, 91), 454),
            (MagicSchool.Wind, new(2, 126), 456),
            (MagicSchool.Holy, new(2, 161), 458),
            (MagicSchool.Dark, new(2, 196), 460),
            (MagicSchool.Phantom, new(1, 231), 462),
            (MagicSchool.Physical, new(2, 266), 464),
        };
        bool tabsMatch = expectedTabs.All(x => _schoolButtons.TryGetValue(x.school, out var button)
            && button.Location == x.location
            && button.Index == x.frame
            && button.Size == MirSkin.GetSize(LibraryFile.GameInter, x.frame));
        bool navigationMatch = _tabPrevious.Location == new Vector2I(61, 303)
            && _tabPrevious.Index == 410
            && _tabNext.Location == new Vector2I(366, 303)
            && _tabNext.Index == 412;
        bool rowsMatch = _legacySkillRows.Count == LegacyPageSize
            && _legacySkillRows.Select((row, i) => row.Location == new Vector2I(55, LegacySkillRowY[i])
                && row.Size == new Vector2I(145, 36)).All(x => x);
        bool ok = Size == new Vector2I(452, 380)
            && _header.Index == 400
            && _legacyAuxControl != null
            && _legacyAuxControl.Location == new Vector2I(399, 340)
            && _legacyAuxControl.Index == 440
            && _legacySkillRows.Count == LegacyPageSize
            && rowsMatch
            && _schoolButtons.Count == LegacySchoolOrder.Length
            && tabsMatch
            && navigationMatch
            && !_list.Visible
            && !_scrollBar.Visible;
        details = $"size={Size} background=F{_header.Index} categories={_schoolButtons.Count} " +
            $"categoryPositions={tabsMatch} nav={navigationMatch} rows={_legacySkillRows.Count} rowsAligned={rowsMatch} " +
            $"page={_legacyPage + 1}/{Math.Max(1, LegacyPageCount())}";
        return ok;
    }
    /// <summary>从 GameScene.UserMagics 刷新技能列表。</summary>
    public void Refresh()
    {
        var game = GameScene.Game;
        if (game == null) return;

        if (!_legacyEiLayout) _header.Index = HeaderIndex();

        var visible = GetVisibleMagicInfos(game).ToList();
        if (_legacyEiLayout)
        {
            _tabOrder = LegacySchoolOrder.ToList();
            if (!_tabOrder.Contains(_selectedSchool))
                _selectedSchool = _tabOrder[0];
            foreach (var button in _schoolButtons.Values)
            {
                RemoveControl(button);
                button.QueueFree();
            }
            _schoolButtons.Clear();
            BuildLegacySchoolButtons();
            SelectSchool(_selectedSchool, preserveState: true);
            GD.Print($"[MagicLegacy] refresh school={_selectedSchool} skills={visible.Count}");
            return;
        }

        var grouped = visible
            .GroupBy(x => x.Info.School)
            .Where(g => g.Any())
            .OrderBy(g => g.Key)
            .ToList();

        _tabOrder = grouped.Select(g => g.Key).ToList();
        _tabPageStart = Math.Clamp(_tabPageStart, 0, Math.Max(0, _tabOrder.Count - TabCapacity));

        foreach (var button in _schoolButtons.Values)
        {
            RemoveControl(button);
            button.QueueFree();
        }
        _schoolButtons.Clear();

        if (grouped.Count == 0) return;
        if (!grouped.Any(g => g.Key == _selectedSchool))
            _selectedSchool = grouped[0].Key;

        for (int i = 0; i < grouped.Count; i++)
        {
            var school = grouped[i].Key;
            var button = new DXButton
            {
                LibraryFile = LibraryFile.Interface,
                Index = SchoolTabIndex(school),
                HoverIndex = SchoolTabIndex(school) + 1,
                PressedIndex = SchoolTabIndex(school) + 1,
                Text = "",
                Size = new Vector2I(60, 25),
            };
            button.MouseClick += (o, e) => SelectSchool(school);
            AddControl(button);
            _schoolButtons[school] = button;
        }

        UpdateTabLayout();
        SelectSchool(_selectedSchool);
    }

    private const int TabCapacity = 5;

    private DXButton CreateTabPagerButton(string text, bool previous)
    {
        int tabHeight = TabHeight();
        var button = new DXButton
        {
            Text = text,
            FontSize = 12,
            TextColour = new Color(1f, 0.85f, 0.3f),
            Size = new Vector2I(tabHeight, tabHeight),
            Location = new Vector2I(previous ? 0 : 420 - tabHeight, 40),
            LibraryFile = LibraryFile.Interface,
            Index = -1,
        };
        button.MouseClick += (o, e) =>
        {
            if (_legacyEiLayout) return;
            // 原版 DXTabControl 的左右按钮每次移动一个 tab，而不是整页
            // 跳跃；这样选中项的相邻切换行为一致。
            int delta = previous ? -1 : 1;
            _tabPageStart = Math.Clamp(_tabPageStart + delta, 0,
                Math.Max(0, _tabOrder.Count - TabCapacity));
            UpdateTabLayout();
        };
        AddControl(button);
        return button;
    }

    private void UpdateTabLayout()
    {
        const int marginLeft = 56; // 原版 DXTabControl.MarginLeft
        const int padding = 2;     // 原版 DXTabControl.Padding
        const int tabWidth = 60;   // 原版 DXTab.MinimumTabWidth
        int tabHeight = TabHeight();
        bool overflow = _tabOrder.Count * (tabWidth + padding) - padding + marginLeft > 420;
        int firstX = marginLeft;
        if (overflow)
        {
            firstX += tabHeight + padding;
            _tabPrevious.Location = new Vector2I(0, 40);
            _tabNext.Location = new Vector2I(420 - tabHeight, 40);
        }
        int capacity = overflow
            ? Math.Max(1, (420 - firstX - tabHeight - padding + padding) / (tabWidth + padding))
            : _tabOrder.Count;
        _tabPageStart = Math.Clamp(_tabPageStart, 0, Math.Max(0, _tabOrder.Count - capacity));
        for (int i = 0; i < _tabOrder.Count; i++)
        {
            if (!_schoolButtons.TryGetValue(_tabOrder[i], out var button)) continue;
            int visibleIndex = i - _tabPageStart;
            button.Visible = visibleIndex >= 0 && visibleIndex < capacity;
            if (button.Visible)
                button.Location = new Vector2I(firstX + visibleIndex * (tabWidth + padding), 40);
        }

        _tabPrevious.Visible = overflow && _tabPageStart > 0;
        _tabNext.Visible = overflow && _tabPageStart + capacity < _tabOrder.Count;
    }

    private static int SchoolTabIndex(MagicSchool school) => school switch
    {
        MagicSchool.Active => 166,
        MagicSchool.Passive => 168,
        MagicSchool.Toggle => 170,
        MagicSchool.Horse => 172,
        MagicSchool.Fire => 174,
        MagicSchool.Ice => 176,
        MagicSchool.Lightning => 178,
        MagicSchool.Wind => 180,
        MagicSchool.Phantom => 182,
        MagicSchool.Holy => 184,
        MagicSchool.Dark => 186,
        MagicSchool.Physical => 188,
        MagicSchool.Atrocity => 190,
        MagicSchool.Kill => 192,
        MagicSchool.Assassination => 194,
        _ => 170,
    };

    private int HeaderIndex()
    {
        return GameScene.Game?.StartInfo?.Class switch
        {
            MirClass.Warrior => 160,
            MirClass.Wizard => 161,
            MirClass.Taoist => 162,
            MirClass.Assassin => 163,
            _ => 160,
        };
    }
    private void SelectSchool(MagicSchool school, bool preserveState = false)
    {
        var priorSelection = preserveState ? _legacySelectedSkill : null;
        int priorPage = preserveState ? _legacyPage : 0;
        _selectedSchool = school;
        _legacyPage = priorPage;
        _legacySelectedSkill = priorSelection;

        if (!_legacyEiLayout)
        {
            int selectedIndex = _tabOrder.IndexOf(school);
            if (selectedIndex >= 0)
            {
                if (selectedIndex < _tabPageStart) _tabPageStart = selectedIndex;
                else if (selectedIndex >= _tabPageStart + TabCapacity)
                    _tabPageStart = selectedIndex - TabCapacity + 1;
                UpdateTabLayout();
            }
        }

        foreach (var c in _cells)
        {
            _list.RemoveControl(c);
            c.QueueFree();
        }
        _cells.Clear();

        var game = GameScene.Game;
        if (game == null) return;

        var schoolEntries = GetVisibleMagicInfos(game)
            .Where(x => x.Info.School == school)
            .OrderBy(x => x.Info.NeedLevel1)
            .ThenBy(x => x.Info.Name, StringComparer.Ordinal)
            .ToArray();
        if (_legacySelectedSkill is { } selected &&
            !schoolEntries.Any(x => x.Info == selected.Info))
            _legacySelectedSkill = null;

        if (_legacyEiLayout)
        {
            RefreshLegacySkillRows(schoolEntries);
            foreach (var pair in _schoolButtons)
                pair.Value.Visible = true;
            GD.Print($"[MagicLegacy] category={school} count={schoolEntries.Length} page={_legacyPage + 1}/{Math.Max(1, LegacyPageCount())}");
            return;
        }

        foreach (var entry in schoolEntries)
        {
            var cell = new MagicCellView(entry.Info, entry.UserMagic, game.MagicBarSpellSet);
            _list.AddControl(cell);
            _cells.Add(cell);
        }

        _scrollBar.Value = 0;
        _scrollBar.MaxValue = Math.Max(_scrollBar.VisibleSize, _cells.Count * 59 + 9);
        UpdateCellLocations();
        foreach (var pair in _schoolButtons)
            pair.Value.TextColour = pair.Key == school ? new Color(1f, 0.85f, 0.3f) : Colors.White;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_legacyEiLayout || @event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        GD.Print($"[MagicLegacy] key-event key={key.Keycode} shift={key.ShiftPressed} ctrl={key.CtrlPressed} alt={key.AltPressed}");
        if (key.CtrlPressed || key.AltPressed) return;
        int slot = key.Keycode switch
        {
            Key.F1 => 0, Key.F2 => 1, Key.F3 => 2, Key.F4 => 3,
            Key.F5 => 4, Key.F6 => 5, Key.F7 => 6, Key.F8 => 7,
            Key.F9 => 8, Key.F10 => 9, Key.F11 => 10, Key.F12 => 11,
            _ => -1,
        };
        if (slot < 0 || _legacySelectedSkill is not { } selected || selected.UserMagic == null)
            return;

        var game = GameScene.Game;
        if (game == null) return;
        var spellKey = (Library.SpellKey)(slot + 1 + (key.ShiftPressed ? 12 : 0));
        var magic = selected.UserMagic;
        switch (game.MagicBarSpellSet)
        {
            case 1: magic.Set1Key = spellKey; break;
            case 2: magic.Set2Key = spellKey; break;
            case 3: magic.Set3Key = spellKey; break;
            case 4: magic.Set4Key = spellKey; break;
        }
        foreach (var pair in game.UserMagics)
        {
            if (pair.Key == selected.Info || pair.Value == null) continue;
            switch (game.MagicBarSpellSet)
            {
                case 1 when pair.Value.Set1Key == spellKey: pair.Value.Set1Key = Library.SpellKey.None; break;
                case 2 when pair.Value.Set2Key == spellKey: pair.Value.Set2Key = Library.SpellKey.None; break;
                case 3 when pair.Value.Set3Key == spellKey: pair.Value.Set3Key = Library.SpellKey.None; break;
                case 4 when pair.Value.Set4Key == spellKey: pair.Value.Set4Key = Library.SpellKey.None; break;
            }
        }
        game.SendMagicKey(selected.Info.Magic, magic.Set1Key, magic.Set2Key, magic.Set3Key, magic.Set4Key);
        game.RefreshMagicBars();
        GD.Print($"[MagicLegacy] bind skill={selected.Info.Name} set={game.MagicBarSpellSet} key={spellKey}");
        GetViewport().SetInputAsHandled();
    }

    private static List<(MagicInfo Info, ClientUserMagic UserMagic)> GetVisibleMagicInfos(GameScene game)
    {
        var result = new List<(MagicInfo, ClientUserMagic)>();
        foreach (var info in Globals.MagicInfoList?.Binding ?? Enumerable.Empty<MagicInfo>())
        {
            if (info == null || info.School is MagicSchool.None or MagicSchool.Discipline)
                continue;
            game.UserMagics.TryGetValue(info, out var userMagic);
            var classType = game.StartInfo?.Class;
            if (userMagic == null && (!classType.HasValue || !info.MatchesClass(classType.Value)))
                continue;
            if (userMagic?.ItemRequired == true)
            {
                bool hasMagicRing = game.Equipment.Any(item => item?.Info?.ItemEffect == ItemEffect.MagicRing &&
                                                               item.Info.Shape == info.Index);
                if (!hasMagicRing) continue;
            }
            result.Add((info, userMagic));
        }
        return result;
    }

    private void UpdateCellLocations()
    {
        for (int i = 0; i < _cells.Count; i++)
            _cells[i].Position = new Vector2(5, 7 + i * 59 - _scrollBar.Value);
    }

    private static int TabHeight()
    {
        int height = MirSkin.GetSize(LibraryFile.Interface, 19).Y;
        return height > 0 ? height : 19;
    }
}

public partial class LegacySkillRowView : DXControl
{
    private MagicInfo _info;
    private ClientUserMagic _magic;
    private bool _selected;
    public event Action Selected;

    public LegacySkillRowView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        IsControl = true;
    }

    public void SetEntry(MagicInfo info, ClientUserMagic magic, bool selected)
    {
        _info = info;
        _magic = magic;
        _selected = selected;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        base._GuiInput(@event);
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Selected?.Invoke();
            AcceptEvent();
        }
    }

    /// <summary>
    /// EI 技能书右页逐行渲染 Magic.exp 的段落原文，段号即技能 id
    /// （skill-window-render-loop-evidence.json 的 observations）。
    /// 数据来自 ClientData/Magic.exp.txt。
    /// </summary>
    public static string LegacyMagicExpParagraph(int skillId)
    {
        if (skillId < 0) return null;
        EnsureLegacyMagicExpLoaded();
        return _legacyMagicExp.TryGetValue(skillId, out string text) ? text : null;
    }

    private static void EnsureLegacyMagicExpLoaded()
    {
        if (_legacyMagicExpLoaded) return;
        _legacyMagicExpLoaded = true;
        try
        {
            string projectDir = ProjectSettings.GlobalizePath("res://");
            foreach (string candidate in new[]
            {
                System.IO.Path.Combine(projectDir, "..", "ClientData", "Magic.exp.txt"),
                System.IO.Path.Combine(projectDir, "ClientData", "Magic.exp.txt"),
            })
            {
                if (!System.IO.File.Exists(candidate)) continue;
                int currentId = -1;
                var buffer = new List<string>();
                foreach (string raw in System.IO.File.ReadAllLines(candidate))
                {
                    string line = raw.TrimEnd();
                    if (line.StartsWith('#'))
                    {
                        if (currentId >= 0) _legacyMagicExp[currentId] = string.Join("\n", buffer);
                        buffer.Clear();
                        currentId = int.TryParse(line[1..].Trim(), out int id) ? id : -1;
                        continue;
                    }
                    if (currentId >= 0) buffer.Add(line);
                }
                if (currentId >= 0) _legacyMagicExp[currentId] = string.Join("\n", buffer);
                GD.Print($"[LegacyMagicExp] loaded {_legacyMagicExp.Count} paragraphs from {candidate}");
                return;
            }
            GD.Print("[LegacyMagicExp] Magic.exp.txt not found; falling back to generated lines");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LegacyMagicExp] load failed: {ex.Message}");
        }
    }

    private static readonly Dictionary<int, string> _legacyMagicExp = new();
    private static bool _legacyMagicExpLoaded;

    public override void _Draw()
    {
        if (_info == null) return;
        float opacity = _magic == null ? 0.55f : 1f;
        if (_selected)
        {
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.78f, 0.58f, 0.17f, 0.22f), true);
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.42f, 0.23f, 0.05f, 0.8f), false, 1f);
        }
        else if (IsPressed)
        {
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.76f, 0.54f, 0.12f, 0.24f), true);
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.66f, 0.42f, 0.08f, 0.9f), false, 1f);
        }
        else if (IsHovered)
        {
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.84f, 0.67f, 0.28f, 0.12f), true);
            DrawRect(new Rect2(0, 0, Size.X, Size.Y), new Color(0.52f, 0.36f, 0.12f, 0.65f), false, 1f);
        }

        var icon = MirSkin.GetTexture(LibraryFile.MagicIcon, _info.Icon);
        if (icon != null)
        {
            float scale = Mathf.Min(1f, Mathf.Min(32f / icon.GetWidth(), 32f / icon.GetHeight()));
            float width = icon.GetWidth() * scale;
            float height = icon.GetHeight() * scale;
            DrawTextureRect(icon, new Rect2(3 + (32 - width) / 2f, 4 + (32 - height) / 2f, width, height),
                false, new Color(1f, 1f, 1f, opacity));
        }

        var font = MirSkin.GetFont();
        if (font == null) return;
        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        if (canvasScale < 0.01f) canvasScale = 1f;
        int drawSize = MirSkin.PhysicalSize(10);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One / canvasScale);
        try
        {
            Vector2 namePos = new(43 * canvasScale, 14 * canvasScale);
            DrawString(font, namePos, _info.Local() ?? _info.Name ?? string.Empty,
                HorizontalAlignment.Left, 100 * canvasScale, drawSize,
                new Color(0.24f, 0.24f, 0.24f, opacity));
            // skill-tab-header-draw-evidence.json（F848）：原版左页每行只有
            // 技能图标（MIcon.wil，帧取自记录 [skill+6]）+ 技能名（0x45DE50，
            // 色 0x3C3C3C）+ 四边高亮，**没有**状态文本。此前我方在 y=29 多画
            // 了一行「需 N 级」/「等级 N」，已移除。
            // 未做：四边高亮的细节证据未给，暂不绘制。
        }
        finally
        {
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }
}

public partial class LegacySkillDetailView : DXControl
{
    private const int DetailX = 235;
    private const int DetailWidth = 165;
    private (MagicInfo Info, ClientUserMagic UserMagic)? _selected;
    private int _page;
    private int _pageCount = 1;

    /// <summary>由 MagicDialog 在旧版布局时置 true：右页改渲染 Magic.exp 段落原文。</summary>
    public bool LegacyEiLayout;

    public LegacySkillDetailView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        IsControl = false;
    }

    public void SetSkill((MagicInfo Info, ClientUserMagic UserMagic)? selected)
    {
        _selected = selected;
        QueueRedraw();
    }

    public void SetPage(int page, int pageCount)
    {
        _page = Math.Max(0, page);
        _pageCount = Math.Max(1, pageCount);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = MirSkin.GetFont();
        if (font == null) return;
        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        if (canvasScale < 0.01f) canvasScale = 1f;
        int drawSize = MirSkin.PhysicalSize(10);
        var lines = new List<string>();
        if (_selected is { } selected)
        {
            var info = selected.Info;
            var magic = selected.UserMagic;
            // EI 右页逐行渲染 Magic.exp 的**段落原文**（skill-window-render-loop-
            // evidence.json 的 observations：例 #3 共 12 行，[基本技能名]/属性 :/
            // 元素 :/修炼N级需要等级 :/- 修炼值 :/说明 :），段号即技能 id。
            // 且 count==1 时是「一行流文本＝一行渲染、无自动换行」。
            // 现代模式仍用下面这套按当前状态拼的行。
            string paragraph = LegacyEiLayout ? LegacySkillRowView.LegacyMagicExpParagraph(info?.Index ?? -1) : null;
            if (!string.IsNullOrEmpty(paragraph))
            {
                lines.AddRange(paragraph.Replace("\r", string.Empty).Split('\n'));
            }
            else
            {
                lines.Add($"[{info.Local() ?? info.Name ?? string.Empty}]");
                lines.Add($"属性 : {info.Property}");
                lines.Add($"元素 : {SchoolText(info.School)}");
                lines.Add(magic == null ? "状态 : 未学习" : $"等级 : {magic.Level}");
                lines.Add(magic == null
                    ? $"修炼1级需要等级 : {info.NeedLevel1}"
                    : $"修炼值 : {magic.Experience}");
                if (magic == null && info.NeedLevel2 > 0)
                    lines.Add($"修炼2级需要等级 : {info.NeedLevel2}");
                if (magic == null && info.NeedLevel3 > 0)
                    lines.Add($"修炼3级需要等级 : {info.NeedLevel3}");
                if (!string.IsNullOrWhiteSpace(info.Description))
                    lines.Add($"说明 : {info.Description}");
            }
        }

        // 原版 count==1 分支不做自动换行（宽度参数 0），所以 legacy 下直接
        // 逐行绘制 Magic.exp 原文，不走 Wrap。
        bool noWrap = LegacyEiLayout && lines.Count > 0 && lines[0].StartsWith("[");
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One / canvasScale);
        try
        {
            float y = 30f;
            foreach (var source in lines)
            {
                var drawLines = noWrap
                    ? new[] { source }
                    : Wrap(source, font, drawSize, DetailWidth * canvasScale);
                foreach (string line in drawLines)
                {
                    if (y > 290f) break;
                    DrawDetailLine(font, line, new Vector2(DetailX * canvasScale, y * canvasScale),
                        drawSize, canvasScale);
                    y += 15f;
                }
                if (y > 290f) break;
            }

            DrawString(font, new Vector2(117 * canvasScale, 299 * canvasScale), (_page + 1).ToString(),
                HorizontalAlignment.Left, -1, drawSize, new Color("323232"));
            DrawString(font, new Vector2(118 * canvasScale, 309 * canvasScale), _pageCount.ToString(),
                HorizontalAlignment.Left, -1, drawSize, new Color("6496c8"));
        }
        finally
        {
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    private static IEnumerable<string> Wrap(string text, Font font, int drawSize, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        string current = string.Empty;
        foreach (char ch in text)
        {
            string candidate = current + ch;
            if (current.Length > 0 &&
                font.GetStringSize(candidate, HorizontalAlignment.Left, -1, drawSize).X > maxWidth)
            {
                yield return current;
                current = ch.ToString();
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) yield return current;
    }

    private void DrawDetailLine(Font font, string line, Vector2 position, int drawSize, float canvasScale)
    {
        Color colour = line.StartsWith("[", StringComparison.Ordinal)
            ? new Color("96c8fa")
            : new Color("0a320a");
        if (line.StartsWith("[", StringComparison.Ordinal))
        {
            foreach (Vector2 offset in new[]
            {
                new Vector2(-1, -1), new Vector2(1, -1),
                new Vector2(-1, 1), new Vector2(1, 1),
            })
            {
                DrawString(font, position + offset * canvasScale, line,
                    HorizontalAlignment.Left, -1, drawSize, new Color("0a0a0a"));
            }
        }
        DrawString(font, position, line, HorizontalAlignment.Left, -1, drawSize, colour);
    }

    private static string SchoolText(MagicSchool school) => school switch
    {
        MagicSchool.Fire => "火",
        MagicSchool.Ice => "冰",
        MagicSchool.Lightning => "电",
        MagicSchool.Wind => "风",
        MagicSchool.Holy => "神圣",
        MagicSchool.Dark => "黑暗",
        MagicSchool.Phantom => "幻影",
        MagicSchool.Physical => "无",
        _ => school.ToString(),
    };
}

/// <summary>单个技能行 (移植自原版 MagicCell：图标、名称、等级、经验和快捷键绑定)。</summary>
public partial class MagicCellView : DXControl
{
    private readonly MagicInfo _info;
    private readonly ClientUserMagic _magic;

    public MagicCellView(MagicInfo info, ClientUserMagic magic, int spellSet)
    {
        _info = info;
        _magic = magic;
        MouseFilter = MouseFilterEnum.Pass;
        FocusMode = FocusModeEnum.Click;
        Size = new Vector2(369, 54);
    }

    // 点击: 解除当前栏组绑定 (原版 Image_MouseClick)
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
            && new Rect2(9, 9, 36, 36).HasPoint(mb.Position))
        {
            ClearCurrentSetKey();
        }
    }

    // 按键: F1~F12 / Shift+F1~F12 -> 绑定当前栏组 SetXKey
    // (原版 Image_KeyDown 支持 Spell01~Spell24)。
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (AutoLoginArgs.LegacyUi) return;
        if (@event is not InputEventKey key || !key.Pressed || key.CtrlPressed || key.AltPressed) return;
        Vector2 localMouse = GetGlobalMousePosition() - GlobalPosition;
        if (!new Rect2(9, 9, 36, 36).HasPoint(localMouse)) return; // 原版 MouseControl == Image

        if (!TryGetSpellKey(key.Keycode, key.ShiftPressed, out Library.SpellKey spellKey)) return;

        BindCurrentSetKey(spellKey);
    }

    private static bool TryGetSpellKey(Key key, bool shift, out Library.SpellKey spellKey)
    {
        int slot = key switch
        {
            Key.F1 => 0, Key.F2 => 1, Key.F3 => 2, Key.F4 => 3,
            Key.F5 => 4, Key.F6 => 5, Key.F7 => 6, Key.F8 => 7,
            Key.F9 => 8, Key.F10 => 9, Key.F11 => 10, Key.F12 => 11,
            _ => -1,
        };
        if (slot < 0)
        {
            spellKey = Library.SpellKey.None;
            return false;
        }

        spellKey = (Library.SpellKey)(slot + 1 + (shift ? 12 : 0));
        return true;
    }

    private void ClearCurrentSetKey()
    {
        var game = GameScene.Game;
        if (game == null || _magic == null) return;
        int set = game.MagicBarSpellSet;
        switch (set)
        {
            case 1: _magic.Set1Key = Library.SpellKey.None; break;
            case 2: _magic.Set2Key = Library.SpellKey.None; break;
            case 3: _magic.Set3Key = Library.SpellKey.None; break;
            case 4: _magic.Set4Key = Library.SpellKey.None; break;
        }
        SendKeyUpdate(game);
        GD.Print($"[Magic] 解除 {_info.Name} 的 Set{set} 绑定");
        QueueRedraw();
    }

    private void BindCurrentSetKey(Library.SpellKey spellKey)
    {
        var game = GameScene.Game;
        if (game == null || _magic == null) return;
        int set = game.MagicBarSpellSet;
        switch (set)
        {
            case 1: _magic.Set1Key = spellKey; break;
            case 2: _magic.Set2Key = spellKey; break;
            case 3: _magic.Set3Key = spellKey; break;
            case 4: _magic.Set4Key = spellKey; break;
        }
        // 去重: 其他技能若绑了同键, 清掉 (原版 Image_KeyDown 去重)
        foreach (var kv in game.UserMagics)
        {
            if (kv.Key == _info) continue;
            var m = kv.Value;
            if (set == 1 && m.Set1Key == spellKey) m.Set1Key = Library.SpellKey.None;
            if (set == 2 && m.Set2Key == spellKey) m.Set2Key = Library.SpellKey.None;
            if (set == 3 && m.Set3Key == spellKey) m.Set3Key = Library.SpellKey.None;
            if (set == 4 && m.Set4Key == spellKey) m.Set4Key = Library.SpellKey.None;
        }
        SendKeyUpdate(game);
        GD.Print($"[Magic] 绑定 {_info.Name} -> Set{set}=F{(int)spellKey}");
        QueueRedraw();
    }

    private void SendKeyUpdate(GameScene game)
    {
        game.SendMagicKey(_info.Magic, _magic.Set1Key, _magic.Set2Key, _magic.Set3Key, _magic.Set4Key);
        // 刷新快捷栏 + 本列表 (用 GameScene 公开方法或事件)
        game.RefreshMagicBars();
    }

    public override void _Draw()
    {
        if (_info == null) return;
        var game = GameScene.Game;
        float opacity = _magic == null && (game?.PlayerLevel ?? 0) < _info.NeedLevel1 ? 0.3f : 1f;
        var background = MirSkin.GetTexture(LibraryFile.Interface, 165);
        if (background != null)
            DrawTextureRect(background, new Rect2(0, 0, 369, 54), false, new Color(1f, 1f, 1f, opacity));
        else
            DrawRect(new Rect2(0, 0, 369, 54), new Color(0, 0, 0, 0.4f * opacity), true);

        var border = MirSkin.GetTexture(LibraryFile.GameInter2, SchoolBorderIndex(_info.School));
        if (_magic != null && border != null)
            DrawTextureRect(border, new Rect2(4, 4, border.GetWidth(), border.GetHeight()), false,
                new Color(1f, 1f, 1f, opacity));

        // 图标
        var tex = MirSkin.GetTexture(LibraryFile.MagicIcon, _info.Icon);
        if (tex != null)
            DrawTextureRect(tex, new Rect2(9, 9, 36, 36), false, new Color(1f, 1f, 1f, opacity));

        // 技能行文字必须和 DXLabel 一样按物理像素绘制。这里是自绘控件，
        // 不能使用 ScaledSize，否则 CanvasLayer 放大后字体会再次被缩放采样，
        // 表现为发虚且同一行字号不一致。
        DrawPhysicalText(_info.Local() ?? "", new Vector2(54, 18), 12,
            new Color(1f, 1f, 1f, opacity));

        // 等级 / 学习状态
        string levelText = _magic == null ? "未\n学习" : $"等级: {_magic.Level}";
        DrawPhysicalText(levelText, new Vector2(54, 36), 12,
            _magic == null ? new Color(1f, 0.35f, 0.35f, opacity) : new Color(0.8f, 0.8f, 0.8f, opacity));

        string experienceText;
        Color experienceColour = new Color(1f, 0.85f, 0.45f, opacity);
        if (_magic == null)
        {
            // 旧版 MagicDialog: 未学习时 Required Level 按玩家等级红/绿显示。
            experienceText = $"所需等级: {_info.NeedLevel1}";
            experienceColour = (game?.PlayerLevel ?? 0) >= _info.NeedLevel1
                ? new Color(0.5f, 1f, 0.5f, opacity)
                : new Color(1f, 0.35f, 0.35f, opacity);
        }
        else
        {
            float percent = MagicExperiencePercent(_magic);
            var experienceBar = MirSkin.GetTexture(LibraryFile.GameInter2, 812);
            if (experienceBar != null && percent > 0f)
            {
                int width = Mathf.RoundToInt(experienceBar.GetWidth() * percent);
                DrawTextureRect(experienceBar, new Rect2(110, 36, width, experienceBar.GetHeight()), false,
                    new Color(1f, 1f, 1f, opacity));
            }
            experienceText = MagicExperienceText(_magic);
        }
        DrawPhysicalText(experienceText, new Vector2(364, 31), 12, experienceColour,
            HorizontalAlignment.Right, 205);

        // 当前栏组键位
        if (game != null && _magic != null)
        {
            var key = game.MagicBarSpellSet switch
            {
                1 => _magic.Set1Key,
                2 => _magic.Set2Key,
                3 => _magic.Set3Key,
                4 => _magic.Set4Key,
                _ => Library.SpellKey.None,
            };
            if (key != Library.SpellKey.None)
            {
                DrawPhysicalText(SpellKeyText(key), new Vector2(330, 18), 12,
                    new Color(1f, 0.85f, 0.3f, opacity));
            }
        }
    }

    private void DrawPhysicalText(string text, Vector2 logicalPosition, int fontSize, Color colour,
        HorizontalAlignment alignment = HorizontalAlignment.Left, float logicalWidth = -1f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = MirSkin.GetFont();
        if (font == null) return;

        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        if (canvasScale < 0.01f) canvasScale = 1f;

        Vector2 position = logicalPosition * canvasScale;
        float width = logicalWidth > 0 ? logicalWidth * canvasScale : -1f;
        int drawSize = MirSkin.PhysicalSize(fontSize);

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One / canvasScale);
        try
        {
            DrawString(font, new Vector2(Mathf.Round(position.X), Mathf.Round(position.Y)), text,
                alignment, width, drawSize, colour);
        }
        finally
        {
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    private static string SpellKeyText(Library.SpellKey key)
    {
        int value = (int)key;
        if (value <= 0) return string.Empty;
        return value > 12 ? $"Shift+F{value - 12}" : $"F{value}";
    }

    private static int SchoolBorderIndex(MagicSchool school) => school switch
    {
        MagicSchool.Passive => 860,
        MagicSchool.Active => 861,
        MagicSchool.Toggle => 862,
        MagicSchool.Fire => 870,
        MagicSchool.Ice => 871,
        MagicSchool.Lightning => 872,
        MagicSchool.Wind => 873,
        MagicSchool.Phantom => 874,
        MagicSchool.Holy => 880,
        MagicSchool.Dark => 881,
        MagicSchool.Physical => 883,
        MagicSchool.Atrocity => 890,
        MagicSchool.Kill => 891,
        MagicSchool.Assassination => 892,
        _ => 815,
    };

    private static string MagicExperienceText(ClientUserMagic magic)
    {
        if (magic.Level >= Globals.MagicMaxLevel) return "经验: 已满级";
        long required = magic.Level switch
        {
            0 => magic.Info.Experience1,
            1 => magic.Info.Experience2,
            2 => magic.Info.Experience3,
            _ => (magic.Level - 2) * 500L,
        };
        return required <= 0 ? "经验: 0/0" : $"经验: {magic.Experience}/{required}";
    }

    private static float MagicExperiencePercent(ClientUserMagic magic)
    {
        if (magic == null || magic.Level >= Globals.MagicMaxLevel) return 1f;
        decimal required = magic.Level switch
        {
            0 => magic.Info.Experience1,
            1 => magic.Info.Experience2,
            2 => magic.Info.Experience3,
            _ => (magic.Level - 2) * 500,
        };
        if (required <= 0) return 0f;
        return Mathf.Clamp((float)(magic.Experience / required), 0f, 1f);
    }
}
