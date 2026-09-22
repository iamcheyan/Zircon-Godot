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
    private DXImageControl _background;
    private MagicSchool _selectedSchool;
    private List<MagicSchool> _tabOrder = new();
    private int _tabPageStart;
    private DXButton _tabPrevious;
    private DXButton _tabNext;
    private DXButton _closeButton;
    private bool _legacyEiLayout;
    private readonly List<DXImageControl> _legacySkillSlots = new();

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

        _background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 164,
            Location = new Vector2I(0, 66),
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

    /// <summary>按最早 EI 客户端 GameInter F400 的书本界面重排。</summary>
    public void ApplyLegacyEiLayout()
    {
        _legacyEiLayout = true;
        Size = new Vector2I(452, 380);
        _header.LibraryFile = LibraryFile.GameInter;
        _header.Index = 400;
        _header.Location = new Vector2I(-30, -67);
        _header.Size = MirSkin.GetSize(LibraryFile.GameInter, 400);
        _header.StretchImage = false;
        _background.Visible = false;
        _tabPrevious.Visible = false;
        _tabNext.Visible = false;

        _list.Location = new Vector2I(15, 235);
        _list.Size = new Vector2I(410, 125);
        _scrollBar.Location = new Vector2I(425, 235);
        _scrollBar.Size = new Vector2I(18, 125);
        _scrollBar.VisibleSize = 125;

        _closeButton.LibraryFile = LibraryFile.GameInter;
        _closeButton.Index = 161;
        _closeButton.HoverIndex = 162;
        _closeButton.PressedIndex = 162;
        _closeButton.Location = new Vector2I(418, 348);
        _closeButton.Size = new Vector2I(28, 26);
        BuildLegacySchoolButtons();
        BuildLegacySkillSlots();
        UpdateClientAreaForLegacySkin();
    }

    private void BuildLegacySkillSlots()
    {
        foreach (var slot in _legacySkillSlots)
        {
            RemoveControl(slot);
            slot.QueueFree();
        }
        _legacySkillSlots.Clear();

        // 12 个格子来自 simulator/layout.json 与 skill-grid-magic-exp-evidence：
        // 4 列、3 行、36×36，GameInter F410..F421 是 Magic.exp 的基准图标。
        for (int i = 0; i < 12; i++)
        {
            int index = i;
            var slot = new DXImageControl
            {
                LibraryFile = LibraryFile.GameInter,
                Index = 410 + i,
                FixedSize = true,
                Location = new Vector2I(30 + (i % 4) * 40, 60 + (i / 4) * 40),
                Size = new Vector2I(36, 36),
                IsControl = true,
                MouseFilter = MouseFilterEnum.Pass,
                TooltipText = $"旧版技能格 {i + 1} · Magic.exp 图标 F{410 + i}",
            };
            slot.MouseClick += (_, _) =>
            {
                if (index < _cells.Count)
                    _cells[index].GrabFocus();
            };
            AddControl(slot);
            _legacySkillSlots.Add(slot);
        }
    }

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
            (MagicSchool.Dark, 450, new(2, 196)),
            (MagicSchool.Phantom, 452, new(1, 231)),
            (MagicSchool.Physical, 454, new(2, 266)),
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

    /// <summary>从 GameScene.UserMagics 刷新技能列表。</summary>
    public void Refresh()
    {
        var game = GameScene.Game;
        if (game == null) return;

        // StartInfo 在窗口创建之后才到达，头图必须在刷新时重新选职业。
        if (!_legacyEiLayout) _header.Index = HeaderIndex();

        var grouped = GetVisibleMagicInfos(game)
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

        if (_legacyEiLayout)
        {
            BuildLegacySchoolButtons();
            if (grouped.Count > 0 && !grouped.Any(g => g.Key == _selectedSchool))
                _selectedSchool = grouped[0].Key;
            SelectSchool(_selectedSchool);
            return;
        }

        if (grouped.Count == 0) return;
        if (!grouped.Any(g => g.Key == _selectedSchool))
            _selectedSchool = grouped[0].Key;

        // 原版是按 School 切换的 Tab；这里保留同样语义，用文字按钮兼容没有
        // 对应 Interface tab 素材的情况。
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

    private void SelectSchool(MagicSchool school)
    {
        _selectedSchool = school;
        int selectedIndex = _tabOrder.IndexOf(school);
        if (selectedIndex >= 0)
        {
            if (selectedIndex < _tabPageStart) _tabPageStart = selectedIndex;
            else if (selectedIndex >= _tabPageStart + TabCapacity)
                _tabPageStart = selectedIndex - TabCapacity + 1;
            UpdateTabLayout();
        }
        foreach (var c in _cells)
        {
            _list.RemoveControl(c);
            c.QueueFree();
        }
        _cells.Clear();

        var game = GameScene.Game;
        if (game == null) return;
        foreach (var entry in GetVisibleMagicInfos(game)
                     .Where(x => x.Info.School == school)
                     .OrderBy(x => x.Info.NeedLevel1)
                     .ThenBy(x => x.Info.Name, StringComparer.Ordinal))
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

    private static List<(MagicInfo Info, ClientUserMagic UserMagic)> GetVisibleMagicInfos(GameScene game)
    {
        var result = new List<(MagicInfo, ClientUserMagic)>();
        foreach (var info in Globals.MagicInfoList?.Binding ?? Enumerable.Empty<MagicInfo>())
        {
            if (info == null || info.School is MagicSchool.None or MagicSchool.Discipline)
                continue;
            game.UserMagics.TryGetValue(info, out var userMagic);
            if (userMagic == null && info.Class != game.StartInfo?.Class)
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

/// <summary>单个技能行 (移植自原版 MagicCell：图标、名称、等级、经验和快捷键绑定)。</summary>
public partial class MagicCellView : DXControl
{
    private readonly MagicInfo _info;
    private readonly ClientUserMagic _magic;
    private ZlLibrary _iconLib;

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
        if (@event is not InputEventKey key || !key.Pressed) return;
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

    public override void _Ready()
    {
        _iconLib = LibraryCache.Get(LibraryFile.MagicIcon);
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
        var tex = _iconLib?.GetImageTexture(_info.Icon);
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
