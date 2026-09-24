using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>
/// 底部 HUD 主面板 (移植自 Client/Scenes/Views/MainPanel.cs)。
/// GameInter 50 底图; 血/蓝/专注/经验条按原版"缩放"语义绘制 (目标宽 = 图宽 x 百分比);
/// 属性图标 + 标签 + 功能按钮行。数据由 GameScene 通过 Set* 方法注入。
/// </summary>
public partial class MainPanel : DXImageControl
{
    public DXImageControl ExperienceBar;
    public DXControl HealthBar, ManaBar, FocusBar;
    public DXButton CharacterButton, InventoryButton, SpellButton, QuestButton, MailButton,
        BeltButton, GroupButton, MenuButton, CashShopButton;
    public DXButton ExchangeButton, MiniMapButton, SkillEntryButton, ExitButton, LogoutButton,
        PartyButton, GuildButton;
    private readonly List<DXButton> _legacyHudButtons = new();
    public DXImageControl NewMailIcon, AvailableQuestIcon, CompletedQuestIcon;
    public DXImageControl ClassImage, LevelImage, FPImage, CPImage, ACImage, DCImage, MACImage, MCImage, SCImage;
    public DXLabel ClassLabel, LevelLabel, FPLabel, CPLabel, ACLabel, DCLabel, MACLabel, MCLabel, SCLabel,
        HealthLabel, ManaLabel, FocusLabel, AttackModeLabel, PetModeLabel;

    // 数据状态 (GameScene 注入)
    private int _currentHP, _currentMP, _currentFP;
    private decimal _experience, _maxExperience;
    private Stats _stats = new Stats();
    private DXControl _playerOrb;
    private bool _legacyEiStats;

    public MainPanel()
    {
        LibraryFile = LibraryFile.GameInter;
        Index = 50; // 底图, Size 自动
        FixedSize = true;
        Size = new Vector2I(LegacyHudLayout.LogicalWidth, LegacyHudLayout.MainPanelHeight);

        // EI 原版主 HUD 使用 63 号经验条；51 是新版/转换资源里的外框，不能
        // 直接拿来当旧版经验填充。旧版屏幕矩形为 (61,586)-(400,597)，
        // 相对 GameInter[50] 的位置就是 (61,121)，宽 339，高 11。
        ExperienceBar = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 63,
            FixedSize = true,
            Size = new Vector2I(339, 11),
            Location = new Vector2I(61, 121),
            Clip = true,
        };
        ExperienceBar.BeforeDraw += DrawExperienceFill;
        AddControl(ExperienceBar);

        // 原版左侧不是两条细横条，而是 60/61 两个半球资源。它们在旧版
        // 800x600 屏幕中的绘制矩形分别是 (61,496,43,70) 和
        // (105,496,42,70)，转换为主面板相对坐标即 (61,31)/(105,31)。
        HealthBar = CreateBar(61, 31, 43, 70, 60, () => PercentOf(_currentHP, _stats[Stat.Health]));
        ManaBar = CreateBar(105, 31, 42, 70, 61, () => PercentOf(_currentMP, _stats[Stat.Mana]));
        FocusBar = CreateBar(0, 0, 1, 1, 60, () => PercentOf(_currentFP, _stats[Stat.Focus]), glowIndex: 60);
        FocusBar.Visible = false;

        // 旧版玩家球使用 GameInter[60]/[61] 两个 56x110 半球，完整红球为
        // GameInter[62] 的 112x110 贴图。统一用一个 112x110 控件绘制，
        // 避免把完整球误画到右侧环形操作区。
        _playerOrb = new DXControl
        {
            Location = LegacyHudLayout.PlayerOrbLocation,
            Size = LegacyHudLayout.PlayerOrbSize,
            Clip = true,
        };
        _playerOrb.BeforeDraw += DrawPlayerOrb;
        AddControl(_playerOrb);
        HealthBar.Visible = false;
        ManaBar.Visible = false;

        // 旧版 HUD 的 16 个控件不是新版的横向九键排布。这里严格使用
        // 模拟器/反编译证据中的屏幕矩形，所有坐标先减去 HUD 原点 (0,465)。
        // 额外控件也保留，确保右侧环形操作区和左上三个小按钮完整对齐。
        ExchangeButton = CreateButton(80, 81, 204, 2, 24, 16);
        MiniMapButton = CreateButton(82, 83, 228, 2, 24, 16);
        SkillEntryButton = CreateButton(84, 85, 252, 2, 24, 16);
        ExitButton = CreateButton(90, 91, 161, 46, 28, 26);
        LogoutButton = CreateButton(92, 93, 161, 82, 28, 26);
        PartyButton = CreateButton(94, 95, 616, 47, 28, 26);
        GuildButton = CreateButton(96, 97, 616, 82, 28, 26);
        _legacyHudButtons.AddRange(new[] { ExchangeButton, MiniMapButton, SkillEntryButton,
            ExitButton, LogoutButton, PartyButton, GuildButton });

        CharacterButton = CreateButton(110, 111, 648, 70, 40, 38);
        InventoryButton = CreateButton(112, 113, 648, 32, 40, 38);
        SpellButton = CreateButton(100, 101, 703, 16, 40, 38);
        QuestButton = CreateButton(104, 105, 718, 70, 40, 38);
        MailButton = CreateButton(102, 103, 718, 32, 40, 38);
        BeltButton = CreateButton(159, 159, 393, 2, 24, 16);
        GroupButton = CreateButton(108, 109, 664, 86, 40, 38);
        MenuButton = CreateButton(106, 107, 703, 85, 40, 38);
        CashShopButton = CreateButton(114, 115, 665, 16, 40, 38);

        // 原版 MainPanel 在每个按钮/属性图标上提供 Hint；Godot 使用
        // Control.TooltipText 承载相同的悬停提示，键位从已加载的持久化表读取。
        CharacterButton.TooltipText = string.Format(Lang.MainPanelCharacterButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.CharacterWindow));
        InventoryButton.TooltipText = string.Format(Lang.MainPanelInventoryButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.InventoryWindow));
        SpellButton.TooltipText = string.Format(Lang.MainPanelSpellButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.MagicWindow));
        QuestButton.TooltipText = string.Format(Lang.MainPanelQuestButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.QuestLogWindow));
        MailButton.TooltipText = string.Format(Lang.MainPanelMailButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.MailBoxWindow));
        BeltButton.TooltipText = string.Format(Lang.MainPanelBeltButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.BeltWindow));
        GroupButton.TooltipText = string.Format(Lang.MainPanelGroupButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.GroupWindow));
        MenuButton.TooltipText = string.Format(Lang.MainPanelMenuButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.MenuWindow));
        CashShopButton.TooltipText = string.Format(Lang.MainPanelCashShopButtonHint, KeyBindManager.GetKeyBindLabel(KeyBindAction.GameStoreWindow));

        NewMailIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 240,
            IsControl = false,
            Location = new Vector2I(2, 2),
            Visible = false,
        };
        MailButton.AddControl(NewMailIcon);

        AvailableQuestIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 240,
            IsControl = false,
            Location = new Vector2I(2, 2),
            Visible = false,
        };
        QuestButton.AddControl(AvailableQuestIcon);

        CompletedQuestIcon = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 241,
            IsControl = false,
            Location = new Vector2I(2, 2),
            Visible = false,
        };
        QuestButton.AddControl(CompletedQuestIcon);
        AvailableQuestIcon.VisibilityChanged += () =>
        {
            if (CompletedQuestIcon != null)
                CompletedQuestIcon.Location = AvailableQuestIcon.Visible ? new Vector2I(2, QuestButton.Size.Y > CompletedQuestIcon.Size.Y ? (int)QuestButton.Size.Y - (int)CompletedQuestIcon.Size.Y : 2) : new Vector2I(2, 2);
        };

        ClassImage = CreateStatImage(70, 277, 25);
        LevelImage = CreateStatImage(71, 277, 45);
        ClassImage.TooltipText = Lang.MainPanelClassLabel;
        LevelImage.TooltipText = Lang.MainPanelLevelLabel;
        FPImage = CreateStatImage(72, 362, 25);
        CPImage = CreateStatImage(73, 362, 45);
        ACImage = CreateStatImage(66, 445, 25);
        DCImage = CreateStatImage(65, 445, 45);
        MACImage = CreateStatImage(63, 531, 25);
        MCImage = CreateStatImage(62, 541, 45);
        SCImage = CreateStatImage(64, 547, 45);
        // GameInter[62] 是完整玩家红球，不是魔法攻击属性图标；旧版 HUD
        // 的玩家球统一由 _playerOrb 在左侧绘制，不能把同一帧放到右侧属性区。
        MCImage.Visible = false;
        FPImage.TooltipText = "战斗力";
        CPImage.TooltipText = "贡献";
        ACImage.TooltipText = Lang.MainPanelACLabel;
        DCImage.TooltipText = Lang.MainPanelDCLabel;
        MACImage.TooltipText = Lang.MainPanelMRLabel;
        MCImage.TooltipText = Lang.MainPanelMCLabel;
        SCImage.TooltipText = Lang.MainPanelSCLabel;

        ClassLabel = CreateStatLabel(300, 22);
        LevelLabel = CreateStatLabel(300, 42);
        FPLabel = CreateStatLabel(385, 22);
        CPLabel = CreateStatLabel(385, 42);
        ACLabel = CreateStatLabel(470, 22);
        DCLabel = CreateStatLabel(470, 42);
        MACLabel = CreateStatLabel(567, 22);
        MCLabel = CreateStatLabel(567, 42);
        SCLabel = CreateStatLabel(567, 42);

        HealthLabel = CreateBarLabel();
        ManaLabel = CreateBarLabel();
        FocusLabel = CreateBarLabel();
        FocusLabel.Visible = false;

        AttackModeLabel = new DXLabel
        {
            TextColour = Colors.Cyan,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Visible = false,
        };
        AddControl(AttackModeLabel);

        PetModeLabel = new DXLabel
        {
            TextColour = Colors.Cyan,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            Visible = false,
        };
        AddControl(PetModeLabel);
    }

    /// <summary>
    /// 旧版 EI HUD 的静态属性文字不是新版九格属性栏。
    /// 旧版只保留中央等级，以及右下的 AC/DC；其它字段不参与绘制。
    /// 该方法只由 --legacy-hud/--legacy-ui 和独立旧版 HUD 测试场调用。
    /// </summary>
    public void ApplyLegacyEiStatsLayout()
    {
        _legacyEiStats = true;

        // 旧版没有新版属性栏的图标列，也没有职业、FP/CP、MR/MC/SC 文本。
        foreach (DXImageControl image in new[]
        {
            ClassImage, LevelImage, FPImage, CPImage, ACImage, DCImage,
            MACImage, MCImage, SCImage,
        })
            image.Visible = false;

        foreach (DXLabel label in new[]
        {
            ClassLabel, FPLabel, CPLabel, MACLabel, MCLabel, SCLabel,
        })
            label.Visible = false;

        // 等级在右侧圆盘的中心；AC/DC 对齐圆盘下方原版的两个标识位。
        LevelLabel.Location = new Vector2I(665, 60);
        LevelLabel.Size = new Vector2I(70, 16);
        LevelLabel.Visible = true;

        ACLabel.Location = new Vector2I(580, 108);
        ACLabel.Size = new Vector2I(88, 16);
        ACLabel.Visible = true;
        DCLabel.Location = new Vector2I(680, 108);
        DCLabel.Size = new Vector2I(88, 16);
        DCLabel.Visible = true;

        // SetStats 会在收到服务器属性包后重新填值；这里先清掉旧版不应残留
        // 的新版文本，避免测试场/登录瞬间出现一帧错误字段。
        ClassLabel.Text = string.Empty;
        FPLabel.Text = string.Empty;
        CPLabel.Text = string.Empty;
        MACLabel.Text = string.Empty;
        MCLabel.Text = string.Empty;
        SCLabel.Text = string.Empty;
    }

    /// <summary>Apply the original EI's three-state caption rendering to all 16 HUD hit targets.</summary>
    public void ApplyLegacyEiHudCaptions()
    {
        SetLegacyCaption(ExchangeButton, "交易栏(Ctrl+C, C)");
        SetLegacyCaption(MiniMapButton, "小地图(Ctrl+V, V)");
        SetLegacyCaption(SkillEntryButton, "技能图鉴(Ctrl+B, B)");
        SetLegacyCaption(ExitButton, "退出游戏(Alt+Q)");
        SetLegacyCaption(LogoutButton, "注销人物(Alt+X)");
        SetLegacyCaption(PartyButton, "组队(Ctrl+G, G)");
        SetLegacyCaption(GuildButton, "行会(Ctrl+F, F)");
        SetLegacyCaption(BeltButton, "腰带(Ctrl+Z, Z)");
        SetLegacyCaption(SpellButton, "技能书(Ctrl+E, E)");
        SetLegacyCaption(MailButton, "聊天记录(Ctrl+R, R)");
        SetLegacyCaption(QuestButton, "信息窗口(Ctrl+D, D)");
        SetLegacyCaption(MenuButton, "设置栏(Ctrl+N, N)");
        SetLegacyCaption(GroupButton, "도움말창(지원예정)");
        SetLegacyCaption(CharacterButton, "坐骑(Ctrl+S, S)");
        SetLegacyCaption(InventoryButton, "包袱栏(Ctrl+Q, Q)");
        SetLegacyCaption(CashShopButton, "状态栏(Ctrl+W, W)");
    }

    private static void SetLegacyCaption(DXButton button, string text)
    {
        button.LegacyHudCaption = true;
        button.LegacyHudCaptionText = text;
        button.TooltipText = string.Empty;
    }

    private static float PercentOf(int current, int max)
    {
        if (current > 0 && max <= 0) max = current;
        if (max <= 0) return 0;
        return Math.Clamp(current / (float)max, 0f, 1f);
    }

    // ---- 条: 容器尺寸取背景图, 填充在 BeforeDraw 里按百分比缩放绘制 ----

    private DXControl CreateBar(int x, int y, int width, int height, int fillIndex, Func<float> percent, int glowIndex = -1)
    {
        var bar = new DXControl
        {
            Location = new Vector2I(x, y),
            Size = new Vector2I(width, height),
            Clip = true,
        };
        bar.BeforeDraw += (o, e) => DrawBarFill(bar, fillIndex, percent, glowIndex);
        AddControl(bar);
        return bar;
    }

    private void DrawBarFill(DXControl bar, int fillIndex, Func<float> percent, int glowIndex)
    {
        float p = percent();
        if (p <= 0) return;

        int idx = fillIndex;
        if (glowIndex >= 0 && p >= 1f && DateTime.Now.Second % 2 == 0)
            idx = glowIndex;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, idx);
        if (tex == null) return;

        var imgSize = tex.GetSize();
        // 原版 PresentTexture 按 HealthBar 左上对齐；高度以条容器为准，避免图高
        // 与 GetSize(52) 不一致时上下溢出入槽。
        // 球体从底部向上填充；父控件 Clip 负责裁掉尚未达到的上半部。
        float h = bar.Size.Y > 0 ? Math.Min(imgSize.Y, bar.Size.Y) : imgSize.Y;
        float y = bar.Size.Y - h;
        float visible = h * p;
        bar.DrawTextureRect(tex, new Rect2(0, y + h - visible, bar.Size.X, h), false);
    }

    private void DrawExperienceFill(object sender, EventArgs e)
    {
        if (sender is not DXControl bar) return;
        if (_maxExperience <= 0) return;
        float p = Math.Clamp((float)(_experience / _maxExperience), 0f, 1f);
        if (p <= 0) return;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, 63);
        if (tex == null) return;

        var imgSize = tex.GetSize();
        float y = (ExperienceBar.Size.Y - imgSize.Y) / 2f;
        bar.DrawTextureRect(tex, new Rect2(0, y, ExperienceBar.Size.X * p, imgSize.Y), false);
    }

    private void DrawPlayerOrb(object sender, EventArgs e)
    {
        if (sender is not DXControl orb) return;
        // 是否拥有蓝球由最大 Mana 决定；当前 MP 归零时仍应保留蓝球，
        // 不能因为法师暂时没蓝就切回整颗红球。
        if (_stats[Stat.Mana] <= 0)
        {
            var full = MirSkin.GetTexture(LibraryFile.GameInter, 62);
            if (full != null) orb.DrawTextureRect(full, new Rect2(0, 0, 112, 110), false);
            return;
        }

        var red = MirSkin.GetTexture(LibraryFile.GameInter, 60);
        var blue = MirSkin.GetTexture(LibraryFile.GameInter, 61);
        if (red != null) orb.DrawTextureRect(red, new Rect2(0, 0, 56, 110), false);
        if (blue != null) orb.DrawTextureRect(blue, new Rect2(56, 0, 56, 110), false);
    }

    private DXButton CreateButton(int index, int hoverIndex, int x, int y, int width, int height)
    {
        var b = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = index,
            HoverIndex = hoverIndex,
            PressedIndex = hoverIndex,
            Location = new Vector2I(x, y),
            FixedSize = true,
            Size = new Vector2I(width, height),
        };
        AddControl(b);
        return b;
    }

    private DXImageControl CreateStatImage(int index, int x, int y)
    {
        var img = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = index,
            Location = new Vector2I(x, y),
            IsControl = false,
        };
        AddControl(img);
        return img;
    }

    private DXLabel CreateStatLabel(int x, int y)
    {
        var label = new DXLabel
        {
            AutoSize = false,
            Location = new Vector2I(x, y),
            Size = new Vector2I(60, 16),
            FontSize = 8,
            TextColour = Colors.White,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            IsControl = false,
        };
        AddControl(label);
        return label;
    }

    private DXLabel CreateBarLabel()
    {
        var label = new DXLabel
        {
            TextColour = Colors.White,
            DrawOutline = true,
            OutlineColour = Colors.Black,
            IsControl = false,
        };
        AddControl(label);
        return label;
    }

    // 条上文字居中 (原版 SizeChanged 里做)。描边字略偏高，垂直用 +1 贴凹槽中线。
    private void CenterBarLabel(DXLabel label, DXControl bar)
    {
        if (label == null || bar == null) return;
        var size = MirSkin.MeasureText(label.Text ?? string.Empty, label.FontSize);
        label.Location = new Vector2I(
            bar.Location.X + (int)((bar.Size.X - size.X) / 2f),
            bar.Location.Y + (int)((bar.Size.Y - size.Y) / 2f) + 1);
    }

    // ---- GameScene 数据注入 (对应原版 GameScene 的 Changed 方法) ----

    public void SetLevel(int level)
    {
        LevelLabel.Text = level.ToString();
    }

    public void SetClass(MirClass cls)
    {
        ClassLabel.Text = cls.Local();
        bool showMC = cls == MirClass.Wizard || cls == MirClass.Warrior;
        bool showSC = cls == MirClass.Taoist || cls == MirClass.Assassin;
        if (_legacyEiStats)
        {
            MCLabel.Visible = false;
            MCImage.Visible = false;
            SCLabel.Visible = false;
            SCImage.Visible = false;
            return;
        }
        MCLabel.Visible = showMC;
        MCImage.Visible = false;
        SCLabel.Visible = showSC;
        SCImage.Visible = showSC;
    }

    public void SetStats(Stats stats)
    {
        _stats = stats ?? new Stats();
        string ac = _stats.GetFormat(Stat.MaxAC) ?? "";
        string dc = _stats.GetFormat(Stat.MaxDC) ?? "";
        ACLabel.Text = _legacyEiStats ? $"AC {ac}" : ac;
        MACLabel.Text = _stats.GetFormat(Stat.MaxMR) ?? "";
        DCLabel.Text = _legacyEiStats ? $"DC {dc}" : dc;
        SCLabel.Text = _stats.GetFormat(Stat.MaxSC) ?? "";
        MCLabel.Text = _stats.GetFormat(Stat.MaxMC) ?? "";
        RefreshBars();
    }

    public void SetHealth(int currentHP)
    {
        _currentHP = currentHP;
        HealthLabel.Text = $"{currentHP}/{_stats[Stat.Health]}";
        CenterBarLabel(HealthLabel, HealthBar);
    }

    public void SetMana(int currentMP)
    {
        _currentMP = currentMP;
        ManaLabel.Text = $"{currentMP}/{_stats[Stat.Mana]}";
        CenterBarLabel(ManaLabel, ManaBar);
        _playerOrb?.QueueRedraw();
    }

    public void SetFocus(int currentFP)
    {
        _currentFP = currentFP;
        FocusLabel.Visible = _stats[Stat.Focus] > 0;
        FocusLabel.Text = $"{currentFP}/{_stats[Stat.Focus]}";
        CenterBarLabel(FocusLabel, FocusBar);
    }

    public void SetExperience(decimal experience, decimal maxExperience)
    {
        _experience = experience;
        _maxExperience = maxExperience;
        ExperienceBar.QueueRedraw();
    }

    public void SetQuestIndicators(bool hasAvailable, bool hasCompleted)
    {
        AvailableQuestIcon.Visible = hasAvailable;
        CompletedQuestIcon.Visible = hasCompleted;
        CompletedQuestIcon.Location = hasAvailable ? new Vector2I(2, Math.Max(2, (int)QuestButton.Size.Y - (int)CompletedQuestIcon.Size.Y)) : new Vector2I(2, 2);
    }

    public void SetMailIndicator(bool visible)
    {
        if (NewMailIcon != null) NewMailIcon.Visible = visible;
    }

    /// <summary>旧版 HUD 球体审计：红球与红蓝球必须共用左侧同一控件。</summary>
    public bool AuditLegacyOrb(out string details)
    {
        Vector2I location = _playerOrb?.Location ?? new Vector2I(-1, -1);
        SetStats(new Stats());
        SetMana(80);
        bool noManaRedState = _stats[Stat.Mana] <= 0 && _playerOrb?.Location == location;
        SetStats(new Stats { [Stat.Mana] = 100 });
        SetMana(0);
        bool emptyManaSplitState = _stats[Stat.Mana] > 0 && _playerOrb?.Location == location;
        SetMana(80);
        bool splitState = _stats[Stat.Mana] > 0 && _playerOrb?.Location == location;
        SetClass(MirClass.Warrior);
        bool oneOrb = _playerOrb != null
            && _playerOrb.Visible
            && location == LegacyHudLayout.PlayerOrbLocation
            && _playerOrb.Size == LegacyHudLayout.PlayerOrbSize
            && !HealthBar.Visible
            && !ManaBar.Visible
            && !MCImage.Visible
            && noManaRedState
            && emptyManaSplitState
            && splitState;
        bool duplicateIcon = MCImage?.Visible == true;
        details = $"orb={_playerOrb?.Location}/{_playerOrb?.Size} visible={_playerOrb?.Visible} duplicateIcon={duplicateIcon} hiddenAttributeIcon={!MCImage.Visible} states={noManaRedState}/{emptyManaSplitState}/{splitState} single={oneOrb}";
        return oneOrb;
    }

    /// <summary>正式场景与独立测试场共用的旧版 EI HUD 几何审计。</summary>
    public bool AuditLegacyHud(out string details)
    {
        bool panel = Index == 50 && Size == new Vector2I(LegacyHudLayout.LogicalWidth, 136);
        bool buttons = ExchangeButton?.Location == new Vector2I(204, 2)
            && MiniMapButton?.Location == new Vector2I(228, 2)
            && SkillEntryButton?.Location == new Vector2I(252, 2)
            && ExitButton?.Location == new Vector2I(161, 46)
            && CharacterButton?.Location == new Vector2I(648, 70)
            && InventoryButton?.Location == new Vector2I(648, 32)
            && SpellButton?.Location == new Vector2I(703, 16)
            && MenuButton?.Location == new Vector2I(703, 85);
        bool legacyStats = _legacyEiStats
            && LevelLabel.Visible
            && !ClassLabel.Visible
            && !FPLabel.Visible
            && !CPLabel.Visible
            && !MACLabel.Visible
            && !MCLabel.Visible
            && !SCLabel.Visible
            && ACLabel.Visible
            && DCLabel.Visible
            && LevelLabel.Location == new Vector2I(665, 60)
            && ACLabel.Location == new Vector2I(580, 108)
            && DCLabel.Location == new Vector2I(680, 108);
        bool orb = AuditLegacyOrb(out string orbDetails);
        details = $"panel={Index}/{Size} buttons={buttons} legacyStats={legacyStats} "
            + $"vis(level/class/fp/cp/ac/dc/mac/mc/sc)={LevelLabel.Visible}/{ClassLabel.Visible}/{FPLabel.Visible}/{CPLabel.Visible}/{ACLabel.Visible}/{DCLabel.Visible}/{MACLabel.Visible}/{MCLabel.Visible}/{SCLabel.Visible} "
            + $"level={LevelLabel.Text}@{LevelLabel.Location} ac={ACLabel.Text}@{ACLabel.Location} dc={DCLabel.Text}@{DCLabel.Location} {orbDetails}";
        return panel && buttons && legacyStats && orb;
    }

    public void SetAttackMode(AttackMode mode)
    {
        // 原版 (Client/Scenes/Views/MainPanel.cs:489-501): 标签构造 Visible=false,
        // 全仓库无任何代码置 Visible=true → 永不渲染。模式反馈走聊天
        // (CConnection.Process(S.ChangeAttackMode) 打 ReceiveChat)。这里只更新
        // Text 供聊天使用, 不再显示。
        AttackModeLabel.Text = GetDescription(mode) ?? mode.ToString();
    }

    public void SetPetMode(PetMode mode)
    {
        PetModeLabel.Text = GetDescription(mode) ?? mode.ToString();
    }

    public void SetPetModeEnabled(bool enabled)
    {
        PetModeLabel.Visible = enabled;
    }

    private static string GetDescription<T>(T value) where T : Enum
    {
        MemberInfo[] infos = typeof(T).GetMember(value.ToString());
        if (infos.Length == 0) return null;
        return infos[0].GetCustomAttribute<DescriptionAttribute>()?.Description;
    }

    private void RefreshBars()
    {
        HealthBar.QueueRedraw();
        ManaBar.QueueRedraw();
        FocusBar.QueueRedraw();
        ExperienceBar.QueueRedraw();
        SetHealth(_currentHP);
        SetMana(_currentMP);
        SetFocus(_currentFP);
    }
}
