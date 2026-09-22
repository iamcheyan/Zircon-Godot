using System;
using System.ComponentModel;
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
    public DXImageControl NewMailIcon, AvailableQuestIcon, CompletedQuestIcon;
    public DXImageControl ClassImage, LevelImage, FPImage, CPImage, ACImage, DCImage, MACImage, MCImage, SCImage;
    public DXLabel ClassLabel, LevelLabel, FPLabel, CPLabel, ACLabel, DCLabel, MACLabel, MCLabel, SCLabel,
        HealthLabel, ManaLabel, FocusLabel, AttackModeLabel, PetModeLabel;

    // 数据状态 (GameScene 注入)
    private int _currentHP, _currentMP, _currentFP;
    private decimal _experience, _maxExperience;
    private Stats _stats = new Stats();

    public MainPanel()
    {
        LibraryFile = LibraryFile.GameInter;
        Index = 50; // 底图, Size 自动

        ExperienceBar = new DXImageControl { LibraryFile = LibraryFile.GameInter, Index = 51, Clip = true };
        ExperienceBar.Location = new Vector2I((int)(Size.X - ExperienceBar.Size.X) / 2 + 1, 3);
        ExperienceBar.BeforeDraw += DrawExperienceFill;
        AddControl(ExperienceBar);

        HealthBar = CreateBar(35, 22, 52, 52, () => PercentOf(_currentHP, _stats[Stat.Health]));
        ManaBar = CreateBar(35, 36, 52, 54, () => PercentOf(_currentMP, _stats[Stat.Mana]));
        FocusBar = CreateBar(35, 50, 58, 58, () => PercentOf(_currentFP, _stats[Stat.Focus]), glowIndex: 59);

        // CreateButton 的参数顺序是 (图标索引, X, Y)；X/Y 保持旧客户端
        // GameInter 50 底图上的逻辑坐标，CanvasLayer 再统一放大 2 倍。
        CharacterButton = CreateButton(82, 650, 23);
        InventoryButton = CreateButton(87, 689, 23);
        SpellButton = CreateButton(92, 728, 23);
        QuestButton = CreateButton(112, 767, 23);
        MailButton = CreateButton(97, 806, 23);
        BeltButton = CreateButton(107, 845, 23);
        GroupButton = CreateButton(102, 884, 23);
        MenuButton = CreateButton(117, 923, 23);
        CashShopButton = CreateButton(122, 972, 16);

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

    private static float PercentOf(int current, int max)
    {
        if (current > 0 && max <= 0) max = current;
        if (max <= 0) return 0;
        return Math.Clamp(current / (float)max, 0f, 1f);
    }

    // ---- 条: 容器尺寸取背景图, 填充在 BeforeDraw 里按百分比缩放绘制 ----

    private DXControl CreateBar(int x, int y, int sizeIndex, int fillIndex, Func<float> percent, int glowIndex = -1)
    {
        var bar = new DXControl
        {
            Location = new Vector2I(x, y),
            Size = MirSkin.GetSize(LibraryFile.GameInter, sizeIndex),
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
        float h = bar.Size.Y > 0 ? Math.Min(imgSize.Y, bar.Size.Y) : imgSize.Y;
        float y = bar.Size.Y > h ? (bar.Size.Y - h) / 2f : 0f;
        float w = imgSize.X * p;
        bar.DrawTextureRect(tex, new Rect2(0, y, w, h), false);
    }

    private void DrawExperienceFill(object sender, EventArgs e)
    {
        if (sender is not DXControl bar) return;
        if (_maxExperience <= 0) return;
        float p = Math.Clamp((float)(_experience / _maxExperience), 0f, 1f);
        if (p <= 0) return;

        var tex = MirSkin.GetTexture(LibraryFile.GameInter, 56);
        if (tex == null) return;

        var imgSize = tex.GetSize();
        // 原版: 填充在经验条内水平居中
        float x = (ExperienceBar.Size.X - imgSize.X) / 2f;
        float y = (ExperienceBar.Size.Y - imgSize.Y) / 2f - 1;
        bar.DrawTextureRect(tex, new Rect2(x, y, imgSize.X * p, imgSize.Y), false);
    }

    private DXButton CreateButton(int index, int x, int y)
    {
        var b = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = index,
            Location = new Vector2I(x, y),
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
        MCLabel.Visible = showMC;
        MCImage.Visible = showMC;
        SCLabel.Visible = showSC;
        SCImage.Visible = showSC;
    }

    public void SetStats(Stats stats)
    {
        _stats = stats ?? new Stats();
        ACLabel.Text = _stats.GetFormat(Stat.MaxAC) ?? "";
        MACLabel.Text = _stats.GetFormat(Stat.MaxMR) ?? "";
        DCLabel.Text = _stats.GetFormat(Stat.MaxDC) ?? "";
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
