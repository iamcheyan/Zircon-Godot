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
    public DXImageControl WeightBar;
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
    private int _bagWeight, _maxBagWeight;
    private bool _expBarDiagnosed;
    private Stats _stats = new Stats();
    private DXControl _playerOrb;
    private DXControl _playerOrbHoverArea;
    private LegacyHudCaptionHint _playerOrbValueHint;
    private bool _playerOrbHovered;
    private bool _legacyEiStats;

    /// <summary>
    /// 原版主 HUD 的 AC/DC 数值颜色。Mir3.exe `0x0042A77D` 与 `0x0042A801` 各自
    /// 压栈 `0x0032C8FF`，按 Win32 COLORREF(0x00BBGGRR) 解出 RGB(255,200,50) 琥珀金。
    /// </summary>
    private static readonly Color LegacyEiAcDcColour = new(255 / 255f, 200 / 255f, 50 / 255f);

    public MainPanel()
    {
        LibraryFile = LibraryFile.GameInter;
        Index = 50; // 底图, Size 自动
        FixedSize = true;
        Size = new Vector2I(LegacyHudLayout.LogicalWidth, LegacyHudLayout.MainPanelHeight);

        // 位置不能照搬反编译的 SetRect (61,586,400,597)：那是 339 宽的整块区域。
        // F50 底图上真正给经验条留的凹槽由下边框亮线量出：y=131 处亮线
        // x 236..400（宽 165），内部暗段 x 233..399（宽 167），即凹槽
        // x 233..400、y 122..131（面板相对坐标）。
        // 凹槽宽 167 与 F63 的 164x6 几乎相等 —— 原版是**按原生尺寸**画，
        // 不能拉伸到 339，否则黄条会从球体下方一直伸到聊天框，位置与宽度全错。
        // 这里按 F63 原生 164 宽居中放进凹槽，左缘留约 2px 暗边（凹槽左边界本身
        // 是暗的，黄条不应贴到它）。
        ExperienceBar = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 63,
            FixedSize = true,
            Size = new Vector2I(164, 10),
            Location = new Vector2I(235, 122),
            Clip = true,
        };
        // F63 同时承担经验填充纹理；DXImageControl 默认 DrawImage 会在
        // BeforeDraw 后再次绘制整帧，覆盖按比例裁切的填充，因此只保留自定义绘制。
        ExperienceBar.DrawImage = false;
        ExperienceBar.BeforeDraw += DrawExperienceFill;
        AddControl(ExperienceBar);

        // 原版竖着的背包负重条 = GameInter F67 (4x70)。旧版屏幕矩形候选为
        // (206,499)-(215,574)，转主面板相对坐标即 (206,34)，高 75；
        // 这里按 F67 原生尺寸 4x70 居中放进该槽位，并按负重比例自下而上填充。
        WeightBar = new DXImageControl
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 67,
            FixedSize = true,
            Size = MirSkin.GetSize(LibraryFile.GameInter, 67),
            Location = new Vector2I(208, 36),
            Clip = true,
            Visible = false,
        };
        WeightBar.DrawImage = false;
        WeightBar.BeforeDraw += DrawWeightFill;
        AddControl(WeightBar);

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
        // Keep the hover target tall enough for the values immediately below
        // the orb, so moving from the orb to either number does not hide it.
        _playerOrbHoverArea = new DXControl
        {
            Location = LegacyHudLayout.PlayerOrbLocation,
            Size = new Vector2I(LegacyHudLayout.PlayerOrbSize.X, 126),
            IsControl = true,
        };
        _playerOrbHoverArea.MouseEntered += OnPlayerOrbMouseEntered;
        _playerOrbHoverArea.MouseExited += OnPlayerOrbMouseExited;
        AddControl(_playerOrbHoverArea);
        _playerOrbValueHint = new LegacyHudCaptionHint
        {
            Name = "PlayerOrbValueHint",
            Visible = false,
            ZIndex = 1000,
        };
        AddControl(_playerOrbValueHint);
        // 玩家球是**旧版 EI HUD 专有**元素，现代 Zircon UI 没有它。
        // 若在现代模式保持可见，DrawPlayerOrb 会用 `MirSkin.GetTexture(GameInter, 62)`
        // 取图；现代模式解析的是 Zircon 自己的 `GameInter.Zl`，其 F62 是 24x12 的
        // 属性小图标（EI 的 `GameInter.wil` F62 才是 112x110 完整红球），
        // 于是被拉伸成 112x110 的巨型乱码块压在 HP/MP 数值上。
        // 证据：`zlsdk` 读 `Debug/Client/Data/GameInter.Zl` → F50(1024x68)、F62(24,12)、
        // F60(20,12)、F61(32,12)；真机 --zircon-ui 1024x768 实测该控件
        // global=(161,645) size=112x110，渲染出 (161,645)-(273,755) 的巨型字形。
        _playerOrb.Visible = false;
        _playerOrbHoverArea.Visible = false;
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
        // 腰带按钮：layout.json 的 records.hud.belt 写 (393,2) 24x16（其 size.source
        // 还错写成 "frame 0x6A"=106），但三处独立来源（hud-label-evidence.json 的
        // records[0] 与 caption_ctor_table[7]、chat-window-control-map.json、
        // RESEARCH_LOG.md:4936）与 ctor 实参解码都给出 (393,13) 16x14。
        // 独立裁决：F50 底图在面板相对 y=0..10 完全空白（lum<25），内容从 y=11
        // 才开始；候选 (393,2) 落在空白处，(393,13) 才压在烘焙内容上。
        // 且 F159 实测就是 16x14 —— 用 24x16 会横向拉伸。
        BeltButton = CreateButton(159, 159, 393, 13, 16, 14);
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
        // HP/MP 数值字号必须在构造期就固定：现代模式走 SetHealth/SetMana 的
        // `CenterBarLabel` 分支，**不会**经过 UpdatePlayerOrbNumbers，若依赖那里的
        // 赋值就会保持 DXLabel 默认 `FontSize = 12`（DXLabel.cs:12）。
        // 原版用 CEnvir.FontSize(8F)（旧 Client/Scenes/Views/MainPanel.cs:450）。
        // 对齐方式同样显式声明：AutoSize 下 Size.X==0，若 Align 仍是默认的
        // Center，DrawControl 会按 0 宽居中，把文字推到 Location 左侧。
        foreach (DXLabel barLabel in new[] { HealthLabel, ManaLabel })
        {
            barLabel.FontSize = 8;
            barLabel.AutoSize = true;
            barLabel.Align = HorizontalAlignment.Left;
            barLabel.VAlign = VerticalAlignment.Top;
        }
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
        // 原版旧版主 HUD 有竖着的背包负重条（F67）；新版属性栏没有。
        WeightBar.Visible = true;
        // 旧版 EI 才有玩家球（红/蓝半球或完整红球）；现代 Zircon UI 不显示。
        _playerOrb.Visible = true;
        _playerOrbHoverArea.Visible = true;

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

        // 等级在右侧圆盘的中心。
        LevelLabel.Location = new Vector2I(665, 60);
        LevelLabel.Size = new Vector2I(70, 16);
        LevelLabel.Visible = true;

        // AC/DC 的**数值**矩形直接取自原版 Mir3.exe 的 SetRect 立即数（HUD 根 = (0,465)）：
        //   AC `0x0042A752`-`0x0042A76B`: SetRect(636, 586, 694, 597)
        //      → 面板相对 (636,121)，58x11
        //   DC `0x0042A7AA`-`0x0042A7C3`: SetRect(736, 586, 794, 598)
        //      → 面板相对 (736,121)，58x12
        // 两处文本随后经 `0x45DE50` 以 DrawTextA flags=0x25
        // （DT_SINGLELINE|DT_VCENTER|DT_CENTER）居中绘制，格式字面量 `0x0047BD28`
        // = "%d-%d"，**不含** "AC"/"DC" 前缀——两个前缀字形是 F50 底图的烘焙美术
        // （像素实测金色字形 AC 在 x607..620、DC 在 x705..717）。
        // 独立佐证：GameInter F50 在该行带有两个黑色值框，实测 AC x635..696、
        // DC x733..795，与上述 SetRect 立即数一致（相差 ≤2px 边框）。
        //
        // 此前是 (580,108)/(680,108) 且文本带 "AC "/"DC " 前缀 → 数字整体偏左上约
        // 41x10 px、压过金框并覆盖上方圆盘装饰（用户报告的「右下角 AC/DC 文本错位」）。
        ACLabel.Location = new Vector2I(636, 121);
        ACLabel.Size = new Vector2I(58, 11);
        ACLabel.TextColour = LegacyEiAcDcColour;
        ACLabel.Visible = true;
        DCLabel.Location = new Vector2I(736, 121);
        DCLabel.Size = new Vector2I(58, 12);
        DCLabel.TextColour = LegacyEiAcDcColour;
        DCLabel.Visible = true;

        // SetStats 会在收到服务器属性包后重新填值；这里先清掉旧版不应残留
        // 的新版文本，避免测试场/登录瞬间出现一帧错误字段。
        ClassLabel.Text = string.Empty;
        FPLabel.Text = string.Empty;
        CPLabel.Text = string.Empty;
        MACLabel.Text = string.Empty;
        MCLabel.Text = string.Empty;
        SCLabel.Text = string.Empty;

        _playerOrbHovered = false;
        UpdatePlayerOrbNumbers();
        UpdatePlayerOrbValueHint();
    }

    /// <summary>Apply the original EI's three-state caption rendering to all 16 HUD hit targets.</summary>
    public void ApplyLegacyEiHudCaptions()
    {
        SetLegacyCaption(ExchangeButton, "交易栏(Ctrl+C, C)");
        // 帧 82/83 在原版标题表里是「任务栏(Ctrl+V, V)」（hud-label-evidence.json
        // caption_ctor_table[1]），不是「小地图」——小地图是固定 HUD 控件、没有标题。
        // 此前按按钮的现代语义写成「小地图(Ctrl+V, V)」，与所用贴图的文字不符。
        SetLegacyCaption(MiniMapButton, "任务栏(Ctrl+V, V)");
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
        if (!_expBarDiagnosed)
        {
            _expBarDiagnosed = true;
            var t = MirSkin.GetTexture(LibraryFile.GameInter, 63);
            GD.Print($"[ExpBarDiag] exp={_experience} max={_maxExperience} tex={(t == null ? "null" : t.GetSize().ToString())} legacy={_legacyEiStats} visible={ExperienceBar.Visible} pos={ExperienceBar.Location} size={ExperienceBar.Size}");
        }
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

    /// <summary>
    /// HUD 键位条按钮。入参沿用原版 caption ctor 实参顺序：<paramref name="index"/> = arg2（+0x18，
    /// 悬停态帧）、<paramref name="hoverIndex"/> = arg3（+0x1C，**按下态**帧）。
    /// 原版 16 个 caption 的 arg8(+0x20) = -1、arg9(+0x30) = 0
    /// （`hud-label-evidence.json::paint_state_machine.hud_caption_result`：
    /// "normal draws nothing / hover draws TEXT ONLY / pressed draws the state_frame art"），
    /// 而按钮美术本身**已烘焙进 HUD 背景 F50**（本轮像素比对：F80 在 F50 内最佳匹配 (203,2)）
    /// —— 故普通态/悬停态都不应叠画帧，只有按下态画 arg3。
    /// </summary>
    private DXButton CreateButton(int index, int hoverIndex, int x, int y, int width, int height)
    {
        var b = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = -1,
            HoverIndex = -1,
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
        // 只填数值：原版 AC/DC 字样是 F50 烘焙美术，且 Mir3.exe 无对应格式串；
        // 旧 Client/Scenes/GameScene.cs:4136/4139 同样只赋 GetFormat(Stat.MaxAC/DC)。
        ACLabel.Text = ac;
        MACLabel.Text = _stats.GetFormat(Stat.MaxMR) ?? "";
        DCLabel.Text = dc;
        SCLabel.Text = _stats.GetFormat(Stat.MaxSC) ?? "";
        MCLabel.Text = _stats.GetFormat(Stat.MaxMC) ?? "";
        RefreshBars();
    }

    public void SetHealth(int currentHP)
    {
        _currentHP = currentHP;
        HealthLabel.Text = $"{currentHP}/{_stats[Stat.Health]}";
        if (_legacyEiStats) UpdatePlayerOrbNumbers();
        else CenterBarLabel(HealthLabel, HealthBar);
    }

    public void SetMana(int currentMP)
    {
        _currentMP = currentMP;
        ManaLabel.Text = $"{currentMP}/{_stats[Stat.Mana]}";
        if (_legacyEiStats) UpdatePlayerOrbNumbers();
        else CenterBarLabel(ManaLabel, ManaBar);
        _playerOrb?.QueueRedraw();
    }

    private void OnPlayerOrbMouseEntered() => SetPlayerOrbHovered(true);
    private void OnPlayerOrbMouseExited() => SetPlayerOrbHovered(false);

    private void SetPlayerOrbHovered(bool hovered)
    {
        _playerOrbHovered = hovered;
        UpdatePlayerOrbNumbers();
        if (!hovered && _playerOrbValueHint != null)
            _playerOrbValueHint.Visible = false;
    }

    private void UpdatePlayerOrbNumbers()
    {
        if (HealthLabel == null || ManaLabel == null) return;
        if (!_legacyEiStats)
        {
            // 现代 HUD 的血/蓝数值字号/对齐在构造期已固定（见 ctor 里的
            // HealthLabel/ManaLabel 设置），这里只负责刷新可见性与位置，
            // 不要再次改写 Align——否则会与构造期约定互相打架。
            // 位置由 CenterBarLabel 按 HealthBar(43x70)/ManaBar(42x70) 计算，
            // AutoSize 下 Size.X==0，Align 必须是 Left，否则按 0 宽居中会把
            // 文字推到 Location 左侧（真机 1024x768 --zircon-ui 实测）。
            foreach (var label in new[] { HealthLabel, ManaLabel })
                label.Visible = true;
            CenterBarLabel(HealthLabel, HealthBar);
            CenterBarLabel(ManaLabel, ManaBar);
            return;
        }

        // EI displays the value in a cursor-following yellow caption. The
        // labels below the orb belong to the newer HUD and must stay hidden.
        foreach (var label in new[] { HealthLabel, ManaLabel })
        {
            label.AutoSize = false;
            label.Size = new Vector2I(56, 16);
            label.FontSize = 8;
            label.Align = HorizontalAlignment.Center;
            label.VAlign = VerticalAlignment.Center;
            label.Visible = false;
        }
        HealthLabel.Location = new Vector2I(49, 123);
        ManaLabel.Location = new Vector2I(105, 123);
    }

    public override void Process()
    {
        base.Process();
        UpdatePlayerOrbValueHint();
    }

    public override void _Process(double delta) => Process();

    private void UpdatePlayerOrbValueHint()
    {
        if (_playerOrbValueHint == null) return;
        if (!_legacyEiStats || !_playerOrbHovered || _playerOrb == null)
        {
            _playerOrbValueHint.Visible = false;
            return;
        }

        float canvasScale = GetGlobalTransformWithCanvas().X.Length();
        if (canvasScale < 0.01f) canvasScale = 1f;

        Vector2 cursor = GetGlobalTransformWithCanvas().AffineInverse()
            * GetViewport().GetMousePosition();
        float orbX = cursor.X - _playerOrb.Location.X;
        bool overHealth = orbX < _playerOrb.Size.X / 2f;
        // 原版 hover formatter 字面量（layout.json hud_bars_render_evidence
        // .ratios[*].semantic_string_evidence，均 primary-static）：
        //   (血量)%d/%d   @0x0047BD70
        //   (魔法量)%d/%d @0x0047BD60
        // 此前只显示 "120/200"，缺少前缀。
        string value = overHealth
            ? $"(血量){_currentHP}/{_stats[Stat.Health]}"
            : $"(魔法量){_currentMP}/{_stats[Stat.Mana]}";

        _playerOrbValueHint.TextLabel.Text = value;
        _playerOrbValueHint.TextLabel.TextColour = overHealth
            ? new Color(1f, 0.22f, 0.22f)
            : new Color(0.35f, 0.55f, 1f);

        Vector2 measured = MirSkin.MeasureTextPhysical(value, 12);
        float widthPhysical = Mathf.Ceil(measured.X) + 12;
        float heightPhysical = Mathf.Ceil(measured.Y) + 4;
        float width = widthPhysical / canvasScale;
        float height = heightPhysical / canvasScale;
        _playerOrbValueHint.Size = new Vector2I(
            Mathf.CeilToInt(width), Mathf.CeilToInt(height));
        _playerOrbValueHint.Location = new Vector2I(
            Mathf.RoundToInt(cursor.X), Mathf.RoundToInt(cursor.Y - height));
        _playerOrbValueHint.TextLabel.Location = Vector2I.Zero;
        _playerOrbValueHint.TextLabel.Size = _playerOrbValueHint.Size;
        _playerOrbValueHint.Visible = true;
        _playerOrbValueHint.QueueRedraw();
        _playerOrbValueHint.TextLabel.QueueRedraw();

        // 原版 hover formatter 字面量（layout.json hud_bars_render_evidence
        // .ratios[*].semantic_string_evidence，均 primary-static）：
        //   (负重)%d/%d    @0x0047BD40
        //   (经验条)%.2f%s @0x0047BD4C / 0x0047BD5C
        // 证据未给触发区域，挂在各自条控件上是最自然的落点。
        if (WeightBar != null)
            WeightBar.TooltipText = $"(负重){_bagWeight}/{_maxBagWeight}";
        if (ExperienceBar != null && _maxExperience > 0)
            ExperienceBar.TooltipText = $"(经验条){_experience / _maxExperience * 100m:F2}%";
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

    /// <summary>旧版主 HUD 的竖条背包负重条（GameInter F67）。</summary>
    public void SetWeight(int bagWeight, int maxBagWeight)
    {
        _bagWeight = bagWeight;
        _maxBagWeight = maxBagWeight;
        WeightBar.QueueRedraw();
    }

    private void DrawWeightFill(object sender, EventArgs e)
    {
        if (sender is not DXControl bar) return;
        if (_maxBagWeight <= 0) return;
        float p = Math.Clamp((float)_bagWeight / _maxBagWeight, 0f, 1f);
        if (p <= 0) return;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, 67);
        if (tex == null) return;

        // 竖向 gauge：与球体同样自下而上填充，父控件 Clip 裁掉未达到的上半部。
        var imgSize = tex.GetSize();
        float h = bar.Size.Y > 0 ? Math.Min(imgSize.Y, bar.Size.Y) : imgSize.Y;
        float visible = h * p;
        bar.DrawTextureRect(tex, new Rect2(0, h - visible, imgSize.X, visible), false);
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
        SetPlayerOrbHovered(false);
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
            && _playerOrbHoverArea?.Location == LegacyHudLayout.PlayerOrbLocation
            && _playerOrbHoverArea?.Size == new Vector2I(LegacyHudLayout.PlayerOrbSize.X, 126)
            && !HealthBar.Visible
            && !ManaBar.Visible
            && !HealthLabel.Visible
            && !ManaLabel.Visible
            && HealthLabel.Location == new Vector2I(49, 123)
            && ManaLabel.Location == new Vector2I(105, 123)
            && !MCImage.Visible
            && noManaRedState
            && emptyManaSplitState
            && splitState;
        bool duplicateIcon = MCImage?.Visible == true;
        details = $"orb={_playerOrb?.Location}/{_playerOrb?.Size} visible={_playerOrb?.Visible} hover={_playerOrbHoverArea?.Location}/{_playerOrbHoverArea?.Size} valuesHidden={!HealthLabel.Visible && !ManaLabel.Visible} valuePositions={HealthLabel.Location}/{ManaLabel.Location} duplicateIcon={duplicateIcon} hiddenAttributeIcon={!MCImage.Visible} states={noManaRedState}/{emptyManaSplitState}/{splitState} single={oneOrb}";
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
            // AC/DC 数值必须落在原版 SetRect 的矩形内
            // （AC 636..694 / DC 736..794，行带 y 121..132），
            // 且文本**不带** "AC "/"DC " 前缀（字样是 F50 烘焙美术）。
            && ACLabel.Location == new Vector2I(636, 121)
            && DCLabel.Location == new Vector2I(736, 121)
            && !ACLabel.Text.StartsWith("AC")
            && !DCLabel.Text.StartsWith("DC");
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
