using Godot;
using Library;

namespace ZirconClient.Scripts;

/// <summary>原版 Client.Envir.Config 的 Godot 持久化镜像。</summary>
public static class ClientSettings
{
    private const string FilePath = "user://Zircon.ini";
    public static readonly Vector2I FixedDebugLogicalSize = new(1024, 768);
    private const string WindowTitle = "ZirconClient";
    private static bool _loaded;
    private static bool _windowArgsApplied;
    private static Viewport _titleViewport;

    public static bool DrawEffects { get; set; } = true;
    public static bool DrawParticles { get; set; } = true;
    public static bool DrawWeather { get; set; } = true;
    public static bool ShowTargetOutline { get; set; } = true;
    public static bool ShowItemNames { get; set; } = true;
    // 地面物品标签默认关闭；游戏内按 Alt 可切换显示/隐藏，不写入配置，
    // 保证每次进入游戏默认都是不遮挡场景的状态。
    public static bool ShowGroundItemNames { get; set; }
    public static bool ShowMonsterNames { get; set; } = true;
    public static bool ShowPlayerNames { get; set; } = true;
    public static bool ShowUserHealth { get; set; } = true;
    public static bool ShowMonsterHealth { get; set; } = true;
    public static bool ShowDamageNumbers { get; set; } = true;
    public static bool EscapeCloseAll { get; set; }
    public static bool ShiftOpenChat { get; set; } = true;
    public static bool RightClickDeTarget { get; set; } = true;
    public static bool HideChatBar { get; set; } = true;
    public static bool ShowMagicBarFrames { get; set; } = true;
    /// <summary>快捷技能栏的逻辑像素位置；负数表示首次使用默认锚点。</summary>
    public static Vector2I MagicBarPosition { get; set; } = new(-1, -1);
    /// <summary>腰带栏的逻辑像素位置；负数表示首次使用默认锚点。</summary>
    public static Vector2I BeltDialogLocation { get; set; } = new(-1, -1);
    /// <summary>技能栏坐标格式版本；用于一次性迁移旧版错误坐标。</summary>
    public static int MagicBarPositionVersion { get; set; } = 1;
    public static bool MonsterBoxVisible { get; set; } = true;
    public static bool QuestTrackerVisible { get; set; } = true;
    public static bool LogChat { get; set; } = true;
    public static bool SoundInBackground { get; set; } = true;
    public static bool FullScreen { get; set; } = true;
    public static bool Borderless { get; set; }
    public static bool VSync { get; set; }
    public static bool LimitFPS { get; set; }
    public static Vector2I GameSize { get; set; } = new(1280, 1024);
    public static int DefaultMonitor { get; set; }
    public static string RenderingPipeline { get; set; } = "Forward Plus";
    // 原版移动是按走/跑帧时长连续回拉的；默认关闭会退化成按帧阶梯位移，
    // 在高刷新率窗口中明显感觉“卡步”。保留设置项，但新配置默认开启。
    public static bool SmoothMove { get; set; } = true;
    public static bool ClipMouse { get; set; }
    public static bool DebugLabel { get; set; }
    public static int SystemVolume { get; set; } = 25;
    public static int MusicVolume { get; set; } = 25;
    public static int PlayerVolume { get; set; } = 25;
    public static int MonsterVolume { get; set; } = 25;
    public static int MagicVolume { get; set; } = 25;
    public static bool SystemVolumeMuted { get; set; }
    public static bool MusicVolumeMuted { get; set; }
    public static bool PlayerVolumeMuted { get; set; }
    public static bool MonsterVolumeMuted { get; set; }
    public static bool MagicVolumeMuted { get; set; }
    // EI F750 uses two master switches and two shared dB sliders. Keep these
    // values separate from the modern per-bus controls so legacy mode can
    // reproduce the original 0..160 slider travel and Config.ini semantics.
    public static bool LegacyBgmEnabled { get; set; } = true;
    public static bool LegacyEffectSoundEnabled { get; set; } = true;
    public static bool LegacyAmbienceEnabled { get; set; }
    public static bool LegacyShadowBlendEnabled { get; set; }
    public static int LegacyBgmLevel { get; set; }
    public static int LegacyEffectSoundLevel { get; set; }
    public static bool UseNetworkConfig { get; set; }
    public static string IPAddress { get; set; } = "127.0.0.1";
    public static int Port { get; set; } = 7000;
    public static string Language { get; set; } = "CHINESE";
    public static bool RememberDetails { get; set; }
    public static string RememberedEMail { get; set; } = string.Empty;
    public static string RememberedPassword { get; set; } = string.Empty;

    public static Color LocalTextForeColour { get; set; } = Colors.White;
    public static Color GMWhisperInTextForeColour { get; set; } = Colors.Red;
    public static Color WhisperInTextForeColour { get; set; } = Colors.Cyan;
    public static Color WhisperOutTextForeColour { get; set; } = Colors.Aquamarine;
    public static Color GroupTextForeColour { get; set; } = Colors.Plum;
    public static Color GuildTextForeColour { get; set; } = Colors.LightPink;
    public static Color ShoutTextForeColour { get; set; } = Colors.Yellow;
    public static Color GlobalTextForeColour { get; set; } = Colors.Lime;
    public static Color ObserverTextForeColour { get; set; } = Colors.Silver;
    public static Color HintTextForeColour { get; set; } = Colors.AntiqueWhite;
    public static Color SystemTextForeColour { get; set; } = Colors.Red;
    public static Color GainsTextForeColour { get; set; } = Colors.GreenYellow;
    public static Color AnnouncementTextForeColour { get; set; } = Colors.DarkBlue;
    public static Color LocalTextBackColour { get; set; } = Colors.Transparent;
    public static Color GMWhisperInTextBackColour { get; set; } = new(1f, 1f, 1f, 200f / 255f);
    public static Color WhisperInTextBackColour { get; set; } = Colors.Transparent;
    public static Color WhisperOutTextBackColour { get; set; } = Colors.Transparent;
    public static Color GroupTextBackColour { get; set; } = Colors.Transparent;
    public static Color GuildTextBackColour { get; set; } = Colors.Transparent;
    public static Color ShoutTextBackColour { get; set; } = Colors.Transparent;
    public static Color GlobalTextBackColour { get; set; } = Colors.Transparent;
    public static Color ObserverTextBackColour { get; set; } = Colors.Transparent;
    public static Color HintTextBackColour { get; set; } = Colors.Transparent;
    public static Color SystemTextBackColour { get; set; } = new(1f, 1f, 1f, 200f / 255f);
    public static Color GainsTextBackColour { get; set; } = Colors.Transparent;
    public static Color AnnouncementTextBackColour { get; set; } = new(1f, 1f, 1f, 200f / 255f);
    public static Color TargetMonsterLowLevelColour { get; set; } = new(50f / 255f, 205f / 255f, 50f / 255f);
    public static Color TargetMonsterSameLevelColour { get; set; } = Colors.Yellow;
    public static Color TargetMonsterHighLevelColour { get; set; } = Colors.Red;
    public static Color TargetMonsterFriendlyColour { get; set; } = Colors.Cyan;
    public static Color TargetPlayerFriendlyColour { get; set; } = Colors.Cyan;
    public static Color TargetPlayerEnemyColour { get; set; } = Colors.Red;
    public static Color TargetNPCColour { get; set; } = Colors.Cyan;

    public static Color ChatForeColour(MessageType type) => type switch
    {
        MessageType.GMWhisperIn => GMWhisperInTextForeColour,
        MessageType.WhisperIn => WhisperInTextForeColour,
        MessageType.WhisperOut => WhisperOutTextForeColour,
        MessageType.Group => GroupTextForeColour,
        MessageType.Guild => GuildTextForeColour,
        MessageType.Shout => ShoutTextForeColour,
        MessageType.Global => GlobalTextForeColour,
        MessageType.ObserverChat => ObserverTextForeColour,
        MessageType.Hint => HintTextForeColour,
        MessageType.System => SystemTextForeColour,
        MessageType.Combat => GainsTextForeColour,
        MessageType.Announcement => AnnouncementTextForeColour,
        _ => LocalTextForeColour,
    };

    public static Color ChatBackColour(MessageType type) => type switch
    {
        MessageType.GMWhisperIn => GMWhisperInTextBackColour,
        MessageType.WhisperIn => WhisperInTextBackColour,
        MessageType.WhisperOut => WhisperOutTextBackColour,
        MessageType.Group => GroupTextBackColour,
        MessageType.Guild => GuildTextBackColour,
        MessageType.Shout => ShoutTextBackColour,
        MessageType.Global => GlobalTextBackColour,
        MessageType.ObserverChat => ObserverTextBackColour,
        MessageType.Hint => HintTextBackColour,
        MessageType.System => SystemTextBackColour,
        MessageType.Combat => GainsTextBackColour,
        MessageType.Announcement => AnnouncementTextBackColour,
        _ => LocalTextBackColour,
    };

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        // UI 翻译：必须放在 file.Load 之前——ini 不存在时 Load 提前 return，
        // 放在末尾会导致语言永远不初始化（Lang.Reload 不执行）。
        Lang.Reload();

        var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        DrawEffects = Read(file, "Game", nameof(DrawEffects), DrawEffects);
        DrawParticles = Read(file, "Game", nameof(DrawParticles), DrawParticles);
        DrawWeather = Read(file, "Game", nameof(DrawWeather), DrawWeather);
        ShowTargetOutline = Read(file, "Game", nameof(ShowTargetOutline), ShowTargetOutline);
        ShowItemNames = Read(file, "Game", nameof(ShowItemNames), ShowItemNames);
        ShowMonsterNames = Read(file, "Game", nameof(ShowMonsterNames), ShowMonsterNames);
        ShowPlayerNames = Read(file, "Game", nameof(ShowPlayerNames), ShowPlayerNames);
        ShowUserHealth = Read(file, "Game", nameof(ShowUserHealth), ShowUserHealth);
        ShowMonsterHealth = Read(file, "Game", nameof(ShowMonsterHealth), ShowMonsterHealth);
        ShowDamageNumbers = Read(file, "Game", nameof(ShowDamageNumbers), ShowDamageNumbers);
        EscapeCloseAll = Read(file, "Game", nameof(EscapeCloseAll), EscapeCloseAll);
        ShiftOpenChat = Read(file, "Game", nameof(ShiftOpenChat), ShiftOpenChat);
        RightClickDeTarget = Read(file, "Game", nameof(RightClickDeTarget), RightClickDeTarget);
        HideChatBar = Read(file, "Game", nameof(HideChatBar), HideChatBar);
        ShowMagicBarFrames = Read(file, "Game", nameof(ShowMagicBarFrames), ShowMagicBarFrames);
        MagicBarPositionVersion = Read(file, "Game", nameof(MagicBarPositionVersion), 0);
        MagicBarPosition = ReadPoint(file, "Game", nameof(MagicBarPosition), MagicBarPosition);
        BeltDialogLocation = ReadPoint(file, "Game", nameof(BeltDialogLocation), BeltDialogLocation);
        // v1: 旧客户端保存的是未经过最终 Canvas/视口布局的临时坐标，一律丢弃。
        // v2: ReadPoint 修复前，配置里的小坐标会被 ReadVector2I 的 320x240 下限
        // 抬到屏幕中部；修复后这些坐标恢复原值，但其中贴顶带 (Y<48) 的位置是
        // 早期「技能栏贴顶」bug 残留，不是用户刻意摆放 —— 一次性丢弃，让 LayoutHud
        // 重新锚到主面板左下侧。
        bool migrateMagicBarPosition = MagicBarPositionVersion < 1;
        if (migrateMagicBarPosition)
        {
            MagicBarPosition = new Vector2I(-1, -1);
            MagicBarPositionVersion = 1;
        }
        bool migrateMagicBarPositionV2 = MagicBarPositionVersion < 2;
        if (migrateMagicBarPositionV2)
        {
            if (MagicBarPosition.Y >= 0 && MagicBarPosition.Y < 48)
                MagicBarPosition = new Vector2I(-1, -1);
            MagicBarPositionVersion = 2;
        }
        MonsterBoxVisible = Read(file, "Game", nameof(MonsterBoxVisible), MonsterBoxVisible);
        QuestTrackerVisible = Read(file, "Game", nameof(QuestTrackerVisible), QuestTrackerVisible);
        LogChat = Read(file, "Game", nameof(LogChat), LogChat);
        SoundInBackground = Read(file, "Sound", nameof(SoundInBackground), SoundInBackground);
        FullScreen = Read(file, "Graphics", nameof(FullScreen), FullScreen);
        Borderless = Read(file, "Graphics", nameof(Borderless), Borderless);
        VSync = Read(file, "Graphics", nameof(VSync), VSync);
        LimitFPS = Read(file, "Graphics", nameof(LimitFPS), LimitFPS);
        GameSize = ReadVector2I(file, "Graphics", nameof(GameSize), GameSize);
        DefaultMonitor = 0;
        DefaultMonitor = Read(file, "Graphics", nameof(DefaultMonitor), DefaultMonitor);
        RenderingPipeline = Read(file, "Graphics", nameof(RenderingPipeline), RenderingPipeline);
        SmoothMove = Read(file, "Graphics", nameof(SmoothMove), SmoothMove);
        ClipMouse = Read(file, "Graphics", nameof(ClipMouse), ClipMouse);
        DebugLabel = Read(file, "Graphics", nameof(DebugLabel), DebugLabel);
        SystemVolume = Read(file, "Sound", nameof(SystemVolume), SystemVolume);
        MusicVolume = Read(file, "Sound", nameof(MusicVolume), MusicVolume);
        PlayerVolume = Read(file, "Sound", nameof(PlayerVolume), PlayerVolume);
        MonsterVolume = Read(file, "Sound", nameof(MonsterVolume), MonsterVolume);
        MagicVolume = Read(file, "Sound", nameof(MagicVolume), MagicVolume);
        SystemVolumeMuted = Read(file, "Sound", nameof(SystemVolumeMuted), SystemVolumeMuted);
        MusicVolumeMuted = Read(file, "Sound", nameof(MusicVolumeMuted), MusicVolumeMuted);
        PlayerVolumeMuted = Read(file, "Sound", nameof(PlayerVolumeMuted), PlayerVolumeMuted);
        MonsterVolumeMuted = Read(file, "Sound", nameof(MonsterVolumeMuted), MonsterVolumeMuted);
        MagicVolumeMuted = Read(file, "Sound", nameof(MagicVolumeMuted), MagicVolumeMuted);
        LegacyBgmEnabled = Read(file, "LegacyEI", nameof(LegacyBgmEnabled), LegacyBgmEnabled);
        LegacyEffectSoundEnabled = Read(file, "LegacyEI", nameof(LegacyEffectSoundEnabled), LegacyEffectSoundEnabled);
        LegacyAmbienceEnabled = Read(file, "LegacyEI", nameof(LegacyAmbienceEnabled), LegacyAmbienceEnabled);
        LegacyShadowBlendEnabled = Read(file, "LegacyEI", nameof(LegacyShadowBlendEnabled), LegacyShadowBlendEnabled);
        LegacyBgmLevel = Mathf.Clamp(Read(file, "LegacyEI", nameof(LegacyBgmLevel), LegacyBgmLevel), -100, 0);
        LegacyEffectSoundLevel = Mathf.Clamp(Read(file, "LegacyEI", nameof(LegacyEffectSoundLevel), LegacyEffectSoundLevel), -100, 0);
        UseNetworkConfig = Read(file, "Network", nameof(UseNetworkConfig), UseNetworkConfig);
        IPAddress = Read(file, "Network", nameof(IPAddress), IPAddress);
        Port = Read(file, "Network", nameof(Port), Port);
        Language = Read(file, "UI", nameof(Language), Language);
        RememberDetails = Read(file, "Login", nameof(RememberDetails), RememberDetails);
        RememberedEMail = Read(file, "Login", nameof(RememberedEMail), RememberedEMail);
        RememberedPassword = Read(file, "Login", nameof(RememberedPassword), RememberedPassword);
        LoadColours(file);
        if (migrateMagicBarPosition || migrateMagicBarPositionV2)
            Save();

        // ini 存在时 Language 已从文件读出：重载一次让 UI 用持久化语言
        //（首次 Reload 在 file.Load 前，读到的还是默认值）。
        Lang.Reload();
    }

    public static void Save()
    {
        Load();
        var file = new ConfigFile();
        Write(file, "Game", nameof(DrawEffects), DrawEffects);
        Write(file, "Game", nameof(DrawParticles), DrawParticles);
        Write(file, "Game", nameof(DrawWeather), DrawWeather);
        Write(file, "Game", nameof(ShowTargetOutline), ShowTargetOutline);
        Write(file, "Game", nameof(ShowItemNames), ShowItemNames);
        Write(file, "Game", nameof(ShowMonsterNames), ShowMonsterNames);
        Write(file, "Game", nameof(ShowPlayerNames), ShowPlayerNames);
        Write(file, "Game", nameof(ShowUserHealth), ShowUserHealth);
        Write(file, "Game", nameof(ShowMonsterHealth), ShowMonsterHealth);
        Write(file, "Game", nameof(ShowDamageNumbers), ShowDamageNumbers);
        Write(file, "Game", nameof(EscapeCloseAll), EscapeCloseAll);
        Write(file, "Game", nameof(ShiftOpenChat), ShiftOpenChat);
        Write(file, "Game", nameof(RightClickDeTarget), RightClickDeTarget);
        Write(file, "Game", nameof(HideChatBar), HideChatBar);
        Write(file, "Game", nameof(ShowMagicBarFrames), ShowMagicBarFrames);
        Write(file, "Game", nameof(MagicBarPositionVersion), MagicBarPositionVersion);
        Write(file, "Game", nameof(MagicBarPosition), MagicBarPosition);
        Write(file, "Game", nameof(BeltDialogLocation), BeltDialogLocation);
        Write(file, "Game", nameof(MonsterBoxVisible), MonsterBoxVisible);
        Write(file, "Game", nameof(QuestTrackerVisible), QuestTrackerVisible);
        Write(file, "Game", nameof(LogChat), LogChat);
        Write(file, "Sound", nameof(SoundInBackground), SoundInBackground);
        Write(file, "Graphics", nameof(FullScreen), FullScreen);
        Write(file, "Graphics", nameof(Borderless), Borderless);
        Write(file, "Graphics", nameof(VSync), VSync);
        Write(file, "Graphics", nameof(LimitFPS), LimitFPS);
        Write(file, "Graphics", nameof(GameSize), GameSize);
        Write(file, "Graphics", nameof(DefaultMonitor), DefaultMonitor);
        Write(file, "Graphics", nameof(RenderingPipeline), RenderingPipeline);
        Write(file, "Graphics", nameof(SmoothMove), SmoothMove);
        Write(file, "Graphics", nameof(ClipMouse), ClipMouse);
        Write(file, "Graphics", nameof(DebugLabel), DebugLabel);
        Write(file, "Sound", nameof(SystemVolume), SystemVolume);
        Write(file, "Sound", nameof(MusicVolume), MusicVolume);
        Write(file, "Sound", nameof(PlayerVolume), PlayerVolume);
        Write(file, "Sound", nameof(MonsterVolume), MonsterVolume);
        Write(file, "Sound", nameof(MagicVolume), MagicVolume);
        Write(file, "Sound", nameof(SystemVolumeMuted), SystemVolumeMuted);
        Write(file, "Sound", nameof(MusicVolumeMuted), MusicVolumeMuted);
        Write(file, "Sound", nameof(PlayerVolumeMuted), PlayerVolumeMuted);
        Write(file, "Sound", nameof(MonsterVolumeMuted), MonsterVolumeMuted);
        Write(file, "Sound", nameof(MagicVolumeMuted), MagicVolumeMuted);
        Write(file, "LegacyEI", nameof(LegacyBgmEnabled), LegacyBgmEnabled);
        Write(file, "LegacyEI", nameof(LegacyEffectSoundEnabled), LegacyEffectSoundEnabled);
        Write(file, "LegacyEI", nameof(LegacyAmbienceEnabled), LegacyAmbienceEnabled);
        Write(file, "LegacyEI", nameof(LegacyShadowBlendEnabled), LegacyShadowBlendEnabled);
        Write(file, "LegacyEI", nameof(LegacyBgmLevel), LegacyBgmLevel);
        Write(file, "LegacyEI", nameof(LegacyEffectSoundLevel), LegacyEffectSoundLevel);
        Write(file, "Network", nameof(UseNetworkConfig), UseNetworkConfig);
        Write(file, "Network", nameof(IPAddress), IPAddress);
        Write(file, "Network", nameof(Port), Port);
        Write(file, "UI", nameof(Language), Language);
        Write(file, "Login", nameof(RememberDetails), RememberDetails);
        Write(file, "Login", nameof(RememberedEMail), RememberedEMail);
        Write(file, "Login", nameof(RememberedPassword), RememberedPassword);
        SaveColours(file);
        file.Save(FilePath);
    }

    /// <summary>音效分类 → 音频总线名（default_bus_layout.tres 中定义）。</summary>
    public static string BusFor(SoundCategory category) => category switch
    {
        SoundCategory.Music => "Music",
        SoundCategory.Player => "Player",
        SoundCategory.Magic => "Magic",
        SoundCategory.Monster => "Monster",
        _ => "System",
    };

    /// <summary>应用 5 类音量/静音到音频总线（设置页音量条与启动时调用）。</summary>
    public static void ApplyAudioSettings()
    {
        void Apply(int idx, string bus, int volume, bool muted)
        {
            if (idx < 0 || idx >= AudioServer.BusCount) return;
            if (AudioServer.GetBusName(idx) != bus) return;
            if (AutoLoginArgs.LegacyUi)
            {
                float legacyDb = bus == "Music" ? LegacyBgmLevel : LegacyEffectSoundLevel;
                bool legacyMuted = bus == "Music" ? !LegacyBgmEnabled : !LegacyEffectSoundEnabled;
                AudioServer.SetBusVolumeDb(idx, legacyDb);
                AudioServer.SetBusMute(idx, legacyMuted);
                return;
            }
            AudioServer.SetBusVolumeDb(idx, volume <= 0 || muted ? -80f : Mathf.LinearToDb(volume / 100f));
            AudioServer.SetBusMute(idx, muted || volume <= 0);
        }
        for (int i = 0; i < AudioServer.BusCount; i++)
        {
            string name = AudioServer.GetBusName(i);
            switch (name)
            {
                case "System": Apply(i, name, SystemVolume, SystemVolumeMuted); break;
                case "Music": Apply(i, name, MusicVolume, MusicVolumeMuted); break;
                case "Player": Apply(i, name, PlayerVolume, PlayerVolumeMuted); break;
                case "Monster": Apply(i, name, MonsterVolume, MonsterVolumeMuted); break;
                case "Magic": Apply(i, name, MagicVolume, MagicVolumeMuted); break;
            }
        }
    }

    /// <summary>将原版 Graphics 页的窗口选项映射到 Godot 当前窗口。</summary>
    public static void ApplyDisplaySettings()    {
        // --window 只覆盖启动阶段的窗口模式和初始尺寸；之后设置页可以
        // 通过 GameSize 真正调整窗口，不能每次 Apply 都重写用户刚选的尺寸。
        if (AutoLoginArgs.Window)
        {
            FullScreen = false;
            Borderless = false;
            if (!_windowArgsApplied)
            {
                GameSize = AutoLoginArgs.WindowSize;
                if (GameSize == Vector2I.Zero)
                {
                    Vector2I screen = DisplayServer.GetName() == "headless"
                        ? new Vector2I(1024, 768)
                        : DisplayServer.ScreenGetSize(DefaultMonitor);
                    GameSize = new Vector2I(Mathf.Max(1024, Mathf.RoundToInt(screen.X * .75f)),
                        Mathf.Max(768, Mathf.RoundToInt(screen.Y * .75f)));
                }
                _windowArgsApplied = true;
                GD.Print($"[Display] --window 初始尺寸: {GameSize.X}x{GameSize.Y}");
            }
        }

        if (DisplayServer.GetName() == "headless") return;

        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, Borderless);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        if (DefaultMonitor >= 0 && DefaultMonitor < DisplayServer.GetScreenCount())
            DisplayServer.WindowSetCurrentScreen(DefaultMonitor);

        DisplayServer.WindowSetMode(FullScreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        if (!FullScreen)
        {
            GameSize = new Vector2I(Mathf.Max(1024, GameSize.X), Mathf.Max(768, GameSize.Y));
            DisplayServer.WindowSetSize(GameSize);
        }
        var actualSize = DisplayServer.WindowGetSize();
        float uiScale = Mathf.Clamp(Mathf.Min(actualSize.X / 1024f, actualSize.Y / 768f), 1f, 2f);
        ZirconClient.Controls.MirSkin.SetUiScale(uiScale);
        UpdateWindowTitle();

        Engine.MaxFps = LimitFPS ? 60 : 0;
        Input.MouseMode = ClipMouse ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
    }

    /// <summary>在系统标题栏显示当前窗口的实际像素尺寸，便于调试缩放。</summary>
    public static void UpdateWindowTitle()
    {
        if (DisplayServer.GetName() == "headless") return;
        Vector2I size = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetTitle($"{WindowTitle} - {size.X}x{size.Y}");
    }

    /// <summary>为当前视口绑定一次标题刷新，避免登录/选人/游戏场景交接时重复连接。</summary>
    public static void BindWindowTitle(Viewport viewport)
    {
        if (viewport == null || _titleViewport == viewport) return;
        _titleViewport = viewport;
        viewport.SizeChanged += UpdateWindowTitle;
    }

    private static void LoadColours(ConfigFile file)
    {
        LocalTextForeColour = ReadColour(file, nameof(LocalTextForeColour), LocalTextForeColour);
        GMWhisperInTextForeColour = ReadColour(file, nameof(GMWhisperInTextForeColour), GMWhisperInTextForeColour);
        WhisperInTextForeColour = ReadColour(file, nameof(WhisperInTextForeColour), WhisperInTextForeColour);
        WhisperOutTextForeColour = ReadColour(file, nameof(WhisperOutTextForeColour), WhisperOutTextForeColour);
        GroupTextForeColour = ReadColour(file, nameof(GroupTextForeColour), GroupTextForeColour);
        GuildTextForeColour = ReadColour(file, nameof(GuildTextForeColour), GuildTextForeColour);
        ShoutTextForeColour = ReadColour(file, nameof(ShoutTextForeColour), ShoutTextForeColour);
        GlobalTextForeColour = ReadColour(file, nameof(GlobalTextForeColour), GlobalTextForeColour);
        ObserverTextForeColour = ReadColour(file, nameof(ObserverTextForeColour), ObserverTextForeColour);
        HintTextForeColour = ReadColour(file, nameof(HintTextForeColour), HintTextForeColour);
        SystemTextForeColour = ReadColour(file, nameof(SystemTextForeColour), SystemTextForeColour);
        GainsTextForeColour = ReadColour(file, nameof(GainsTextForeColour), GainsTextForeColour);
        AnnouncementTextForeColour = ReadColour(file, nameof(AnnouncementTextForeColour), AnnouncementTextForeColour);
        LocalTextBackColour = ReadColour(file, nameof(LocalTextBackColour), LocalTextBackColour);
        GMWhisperInTextBackColour = ReadColour(file, nameof(GMWhisperInTextBackColour), GMWhisperInTextBackColour);
        WhisperInTextBackColour = ReadColour(file, nameof(WhisperInTextBackColour), WhisperInTextBackColour);
        WhisperOutTextBackColour = ReadColour(file, nameof(WhisperOutTextBackColour), WhisperOutTextBackColour);
        GroupTextBackColour = ReadColour(file, nameof(GroupTextBackColour), GroupTextBackColour);
        GuildTextBackColour = ReadColour(file, nameof(GuildTextBackColour), GuildTextBackColour);
        ShoutTextBackColour = ReadColour(file, nameof(ShoutTextBackColour), ShoutTextBackColour);
        GlobalTextBackColour = ReadColour(file, nameof(GlobalTextBackColour), GlobalTextBackColour);
        ObserverTextBackColour = ReadColour(file, nameof(ObserverTextBackColour), ObserverTextBackColour);
        HintTextBackColour = ReadColour(file, nameof(HintTextBackColour), HintTextBackColour);
        SystemTextBackColour = ReadColour(file, nameof(SystemTextBackColour), SystemTextBackColour);
        GainsTextBackColour = ReadColour(file, nameof(GainsTextBackColour), GainsTextBackColour);
        AnnouncementTextBackColour = ReadColour(file, nameof(AnnouncementTextBackColour), AnnouncementTextBackColour);
        TargetMonsterLowLevelColour = ReadColour(file, nameof(TargetMonsterLowLevelColour), TargetMonsterLowLevelColour);
        TargetMonsterSameLevelColour = ReadColour(file, nameof(TargetMonsterSameLevelColour), TargetMonsterSameLevelColour);
        TargetMonsterHighLevelColour = ReadColour(file, nameof(TargetMonsterHighLevelColour), TargetMonsterHighLevelColour);
        TargetMonsterFriendlyColour = ReadColour(file, nameof(TargetMonsterFriendlyColour), TargetMonsterFriendlyColour);
        TargetPlayerFriendlyColour = ReadColour(file, nameof(TargetPlayerFriendlyColour), TargetPlayerFriendlyColour);
        TargetPlayerEnemyColour = ReadColour(file, nameof(TargetPlayerEnemyColour), TargetPlayerEnemyColour);
        TargetNPCColour = ReadColour(file, nameof(TargetNPCColour), TargetNPCColour);
    }

    private static void SaveColours(ConfigFile file)
    {
        Write(file, "Colours", nameof(LocalTextForeColour), LocalTextForeColour);
        Write(file, "Colours", nameof(GMWhisperInTextForeColour), GMWhisperInTextForeColour);
        Write(file, "Colours", nameof(WhisperInTextForeColour), WhisperInTextForeColour);
        Write(file, "Colours", nameof(WhisperOutTextForeColour), WhisperOutTextForeColour);
        Write(file, "Colours", nameof(GroupTextForeColour), GroupTextForeColour);
        Write(file, "Colours", nameof(GuildTextForeColour), GuildTextForeColour);
        Write(file, "Colours", nameof(ShoutTextForeColour), ShoutTextForeColour);
        Write(file, "Colours", nameof(GlobalTextForeColour), GlobalTextForeColour);
        Write(file, "Colours", nameof(ObserverTextForeColour), ObserverTextForeColour);
        Write(file, "Colours", nameof(HintTextForeColour), HintTextForeColour);
        Write(file, "Colours", nameof(SystemTextForeColour), SystemTextForeColour);
        Write(file, "Colours", nameof(GainsTextForeColour), GainsTextForeColour);
        Write(file, "Colours", nameof(AnnouncementTextForeColour), AnnouncementTextForeColour);
        Write(file, "Colours", nameof(LocalTextBackColour), LocalTextBackColour);
        Write(file, "Colours", nameof(GMWhisperInTextBackColour), GMWhisperInTextBackColour);
        Write(file, "Colours", nameof(WhisperInTextBackColour), WhisperInTextBackColour);
        Write(file, "Colours", nameof(WhisperOutTextBackColour), WhisperOutTextBackColour);
        Write(file, "Colours", nameof(GroupTextBackColour), GroupTextBackColour);
        Write(file, "Colours", nameof(GuildTextBackColour), GuildTextBackColour);
        Write(file, "Colours", nameof(ShoutTextBackColour), ShoutTextBackColour);
        Write(file, "Colours", nameof(GlobalTextBackColour), GlobalTextBackColour);
        Write(file, "Colours", nameof(ObserverTextBackColour), ObserverTextBackColour);
        Write(file, "Colours", nameof(HintTextBackColour), HintTextBackColour);
        Write(file, "Colours", nameof(SystemTextBackColour), SystemTextBackColour);
        Write(file, "Colours", nameof(GainsTextBackColour), GainsTextBackColour);
        Write(file, "Colours", nameof(AnnouncementTextBackColour), AnnouncementTextBackColour);
        Write(file, "Colours", nameof(TargetMonsterLowLevelColour), TargetMonsterLowLevelColour);
        Write(file, "Colours", nameof(TargetMonsterSameLevelColour), TargetMonsterSameLevelColour);
        Write(file, "Colours", nameof(TargetMonsterHighLevelColour), TargetMonsterHighLevelColour);
        Write(file, "Colours", nameof(TargetMonsterFriendlyColour), TargetMonsterFriendlyColour);
        Write(file, "Colours", nameof(TargetPlayerFriendlyColour), TargetPlayerFriendlyColour);
        Write(file, "Colours", nameof(TargetPlayerEnemyColour), TargetPlayerEnemyColour);
        Write(file, "Colours", nameof(TargetNPCColour), TargetNPCColour);
    }

    private static Color ReadColour(ConfigFile file, string key, Color fallback)
    {
        if (!file.HasSectionKey("Colours", key)) return fallback;
        return file.GetValue("Colours", key).AsColor();
    }

    private static T Read<T>(ConfigFile file, string section, string key, T fallback)
    {
        if (!file.HasSectionKey(section, key)) return fallback;
        Variant value = file.GetValue(section, key);
        if (typeof(T) == typeof(bool)) return (T)(object)value.AsBool();
        if (typeof(T) == typeof(int)) return (T)(object)value.AsInt32();
        if (typeof(T) == typeof(string)) return (T)(object)value.AsString();
        return fallback;
    }

    private static Vector2I ReadVector2I(ConfigFile file, string section, string key, Vector2I fallback)
    {
        if (!file.HasSectionKey(section, key)) return fallback;
        Variant value = file.GetValue(section, key);
        Vector2I size = value.AsVector2I();
        return new Vector2I(Mathf.Max(320, size.X), Mathf.Max(240, size.Y));
    }
    private static Vector2I ReadPoint(ConfigFile file, string section, string key, Vector2I fallback)
    {
        // UI 控件坐标 (MagicBar/Belt)：允许 (-1,-1) 表示未设置，不能套用
        // ReadVector2I 的 320x240 最小尺寸下限 (那是给窗口分辨率 GameSize 用的)，
        // 否则保存的小坐标会被强制抬到屏幕中部，控件永远飘在中间。
        if (!file.HasSectionKey(section, key)) return fallback;
        Variant value = file.GetValue(section, key);
        if (value.VariantType == Variant.Type.Vector2I) return value.AsVector2I();
        return fallback;
    }
    private static void Write(ConfigFile file, string section, string key, Variant value)
        => file.SetValue(section, key, value);
}
