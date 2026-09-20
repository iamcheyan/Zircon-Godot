using Godot;
using Library;
using System;
using System.Collections.Generic;
using System.Linq;
using ZirconClient.Scripts;

namespace ZirconClient.Controls;

/// <summary>原版 DXConfigWindow 的 Godot 外框与分类页。</summary>
public partial class ConfigDialog : DXWindow
{
    private readonly DXControl _page;
    private DXControl _content;
    private DXVScrollBar _scroll;
    private readonly DXButton[] _tabs;
    private int _currentTab;
    private bool _allowObservable = true;
    private KeyBindDialog _keyBind;
    private bool _dragging;
    private Vector2 _dragOffset;

    public ConfigDialog()
    {
        ClientSettings.Load();
        HasTitle = false;
        HasFooter = false;
        Size = new Vector2I(364, 416); // 原版 Interface 282
        AddControl(new DXImageControl { LibraryFile = LibraryFile.Interface, Index = 282, FixedSize = true, Size = Size, MouseFilter = MouseFilterEnum.Ignore });

        var close = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15 };
        close.Location = new Vector2I((int)Size.X - (int)close.Size.X - 3, 3);
        close.MouseClick += (o, e) => WindowManager.Close(this);
        AddControl(close);
        AddControl(new DXLabel { Text = Lang.CommonControlConfigWindowTitle, FontSize = 10, TextColour = new Color(1f, 0.85f, 0.3f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, VAlign = VerticalAlignment.Center, AutoSize = false, Location = new Vector2I(0, 8), Size = new Vector2I((int)Size.X, 18), IsControl = false });

        _tabs = new DXButton[5];
        string[] names = { Lang.CommonControlConfigWindowGraphicsTabLabel, Lang.CommonControlConfigWindowSoundTabLabel, Lang.CommonControlConfigWindowGameTabLabel, Lang.CommonControlConfigWindowNetworkTabLabel, Lang.CommonControlConfigWindowUITabLabel };
        for (int i = 0; i < names.Length; i++)
        {
            int tab = i;
            _tabs[i] = new DXButton { Text = names[i], FontSize = 10, TextColour = new Color(0.9f, 0.8f, 0.45f), Size = new Vector2I(68, 25), Location = new Vector2I(8 + i * 70, 37), LibraryFile = LibraryFile.Interface, Index = -1 };
            _tabs[i].MouseClick += (o, e) => SelectTab(tab);
            AddControl(_tabs[i]);
        }
        _page = new DXControl { Location = new Vector2I(8, 62), Size = new Vector2I(348, 340), Clip = true };
        AddControl(_page);
        SelectTab(0);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsEnabled)
        {
            if (e is InputEventMouseButton or InputEventMouseMotion) AcceptEvent();
            return;
        }
        // HasTitle=false 时 DXWindow 不支持拖拽；这里允许在标题栏区域
        // （y < 37，即标签栏上方的装饰框区域）拖拽移动窗口。
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && !_dragging)
            {
                // 排除关闭按钮区域（右上角）
                var closeRect = new Rect2((int)Size.X - 30, 0, 30, 25);
                if (mb.Position.Y < 37 && !closeRect.HasPoint(mb.Position))
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                    return;
                }
            }
            else if (!mb.Pressed && _dragging)
            {
                _dragging = false;
            }
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            Vector2 target = Position + mm.Relative;
            Vector2 vp = GetViewport().GetVisibleRect().Size / GameScene.UiScale;
            target.X = Mathf.Clamp(target.X, 0, Mathf.Max(0, vp.X - Size.X));
            target.Y = Mathf.Clamp(target.Y, 0, Mathf.Max(0, vp.Y - Size.Y));
            Position = target;
            AcceptEvent();
            return;
        }
        base._GuiInput(e);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Left))
            _dragging = false;
    }

    private void SelectTab(int tab)
    {
        _currentTab = tab;
        _scroll = null;
        foreach (var child in _page.GetChildren())
        {
            if (child is not Node node) continue;
            if (node is DXControl control) _page.RemoveControl(control);
            else _page.RemoveChild(node);
            node.QueueFree();
        }
        _content = new DXControl { Size = new Vector2I(348, 0), IsControl = false };
        _page.AddControl(_content);
        if (tab == 0) BuildGraphicsPage();
        else if (tab == 1) BuildSoundPage();
        else if (tab == 2) BuildGamePage();
        else if (tab == 3) BuildNetworkPage();
        else BuildUiPage();
        // 内容超过 _page(340) 时接滚动条（原版 DXConfigWindow 图形页即滚动式）
        int bottom = 0;
        foreach (var child in _content.GetChildren())
            if (child is DXControl c && c.Visible) bottom = Math.Max(bottom, (int)c.Location.Y + (int)c.Size.Y);
        if (bottom > 340)
        {
            _scroll = new DXVScrollBar { Location = new Vector2I(334, 0), Size = new Vector2I(14, 340), VisibleSize = 340, Change = 20 };
            _scroll.MaxValue = Math.Max(1, bottom);
            _scroll.ValueChanged += (s, e) => _content.Location = new Vector2I(0, -_scroll.Value);
            _page.AddControl(_scroll);
        }
        // 鼠标滚轮在内容区任意位置都能滚动（不只限于滚动条本身）
        _page.MouseWheel += (s, e) => { if (_scroll != null) _scroll.DoMouseWheel(s, e); };
    }
    private ConfigCheckBox Check(string text, bool value, Action<bool> changed)
    {
        var check = new ConfigCheckBox(text) { Checked = value };
        check.CheckedChanged += (s, e) => changed?.Invoke(check.Checked);
        return check;
    }

    private ConfigSoundBar SoundBar(Func<int> getValue, Action<int> setValue, Func<bool> getMuted, Action<bool> setMuted)
    {
        var bar = new ConfigSoundBar { Value = getValue(), Muted = getMuted() };
        bar.ValueChanged += (s, e) => { setValue(bar.Value); ClientSettings.Save(); };
        bar.MutedChanged += (s, e) => { setMuted(bar.Muted); ClientSettings.Save(); };
        return bar;
    }

    private DXColourControl Colour(Func<Color> getValue, Action<Color> setValue)
    {
        var control = new DXColourControl { BackColour = getValue() };
        control.BackColourChanged += (s, e) => { setValue(control.BackColour); ClientSettings.Save(); };
        return control;
    }

    private DXColourControlPair ColourPair(Func<Color> getFore, Action<Color> setFore, Func<Color> getBack, Action<Color> setBack)
    {
        var pair = new DXColourControlPair();
        pair.ForeColourControl.BackColour = getFore();
        pair.BackColourControl.BackColour = getBack();
        pair.ForeColourControl.BackColourChanged += (s, e) => { setFore(pair.ForeColourControl.BackColour); ClientSettings.Save(); };
        pair.BackColourControl.BackColourChanged += (s, e) => { setBack(pair.BackColourControl.BackColour); ClientSettings.Save(); };
        return pair;
    }

    private void AddSection(ConfigSectionPanel section, int y)
    {
        section.Location = new Vector2I(0, y);
        _content.AddControl(section);
    }

    private void BuildGraphicsPage()
    {
        var display = new ConfigSectionPanel("显示", 7);
        display.AddOption("全屏显示", Check("全屏显示", ClientSettings.FullScreen, value =>
        {
            ClientSettings.FullScreen = value;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        }));
        display.AddOption("无边框窗口", Check("无边框窗口", ClientSettings.Borderless, value =>
        {
            ClientSettings.Borderless = value;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        }));

        var pipeline = new ConfigSelect();
        pipeline.AddItem("Forward Plus");
        pipeline.Enabled = false; // Godot 4 Forward Plus 是项目固定渲染器，无运行时切换。
        display.AddSelect($"{Lang.CommonControlConfigWindowGraphicsTabRenderingPipelineLabel}（固定）", pipeline);

        var resolution = new ConfigSelect();
        // 档位 = 常用分辨率 + 当前窗口尺寸（去重排序），保证当前值总能匹配显示
        var resolutions = new List<Vector2I> { new(1024, 768), new(1280, 720), new(1280, 800), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
        var current = (Vector2I)DisplayServer.WindowGetSize();
        if (!resolutions.Contains(current)) resolutions.Add(current);
        foreach (var size in resolutions.OrderBy(r => r.X).ThenBy(r => r.Y))
            resolution.AddItem($"{size.X} x {size.Y}");
        // 当前窗口尺寸是实际生效值；GameSize 可能仍是启动参数/ini 的旧值。
        resolution.SelectItem($"{current.X} x {current.Y}");
        resolution.SelectedChanged += (s, e) =>
        {
            string[] parts = resolution.SelectedItem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[0], out int width) || !int.TryParse(parts[2], out int height)) return;
            ClientSettings.GameSize = new Vector2I(width, height);
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        };
        display.AddSelect("游戏分辨率", resolution);

        var monitor = new ConfigSelect();
        int monitorCount = Math.Max(1, DisplayServer.GetScreenCount());
        for (int i = 0; i < monitorCount; i++)
        {
            Vector2I monitorSize = DisplayServer.GetName() == "headless" ? Vector2I.Zero : DisplayServer.ScreenGetSize(i);
            monitor.AddItem(monitorSize == Vector2I.Zero
                ? $"显示器 {i + 1}"
                : $"显示器 {i + 1}（物理 {monitorSize.X} x {monitorSize.Y}）");
        }
        monitor.SelectedIndex = Mathf.Clamp(ClientSettings.DefaultMonitor, 0, monitorCount - 1);
        monitor.SelectedChanged += (s, e) =>
        {
            ClientSettings.DefaultMonitor = monitor.SelectedIndex;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        };
        display.AddSelect("默认显示器", monitor);

        display.AddOption("垂直同步", Check("垂直同步", ClientSettings.VSync, value =>
        {
            ClientSettings.VSync = value;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        }));
        display.AddOption("限制帧率", Check("限制帧率", ClientSettings.LimitFPS, value =>
        {
            ClientSettings.LimitFPS = value;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        }));
        AddSection(display, 0);
        var usability = new ConfigSectionPanel("可用性", 4);
        usability.AddOption(Lang.CommonControlConfigWindowGraphicsTabSmoothMoveLabel, Check(Lang.CommonControlConfigWindowGraphicsTabSmoothMoveLabel, ClientSettings.SmoothMove, value => { ClientSettings.SmoothMove = value; ClientSettings.Save(); }));
        usability.AddOption("限制鼠标", Check("限制鼠标", ClientSettings.ClipMouse, value =>
        {
            ClientSettings.ClipMouse = value;
            ClientSettings.Save();
            ClientSettings.ApplyDisplaySettings();
        }));
        usability.AddOption(Lang.CommonControlConfigWindowGraphicsTabDebugLabelLabel, Check(Lang.CommonControlConfigWindowGraphicsTabDebugLabelLabel, ClientSettings.DebugLabel, value =>
        {
            ClientSettings.DebugLabel = value;
            ClientSettings.Save();
            // GameScene 每帧会同步 _debugLabel/_statusLabel 可见性
        }));
        var language = new ConfigSelect();
        language.AddItem("中文");
        language.AddItem("English");
        language.AddItem("日本語");
        language.SelectedIndex = ClientSettings.Language?.ToUpperInvariant() switch
        {
            "ENGLISH" => 1,
            "JAPANESE" => 2,
            _ => 0,
        };
        language.SelectedChanged += (s, e) =>
        {
            ClientSettings.Language = language.SelectedIndex switch { 1 => "ENGLISH", 2 => "JAPANESE", _ => "CHINESE" };
            ClientSettings.Save();
            Lang.Reload();
            GameScene.Game?.SendSelectLanguage(ClientSettings.Language);
            // 重建当前页：建页时取 Lang 快照的文本（标签页标题、分区标题、选项标签）
            // 不会随 Lang.Reload 自动刷新，必须重调 SelectTab 重建整个页面。
            // 同时刷新标签页按钮文字。
            string[] tabNames = { Lang.CommonControlConfigWindowGraphicsTabLabel, Lang.CommonControlConfigWindowSoundTabLabel, Lang.CommonControlConfigWindowGameTabLabel, Lang.CommonControlConfigWindowNetworkTabLabel, Lang.CommonControlConfigWindowUITabLabel };
            for (int i = 0; i < _tabs.Length && i < tabNames.Length; i++)
                _tabs[i].Text = tabNames[i];
            SelectTab(_currentTab);
        };
        usability.AddSelect(Lang.CommonControlConfigWindowGraphicsTabLanguageLabel, language);
        AddSection(usability, Mathf.RoundToInt(display.Size.Y) + 4);
        var effects = new ConfigSectionPanel("特效", 4, 2);
        effects.AddOption("显示粒子", Check("显示粒子", ClientSettings.DrawParticles, value => { ClientSettings.DrawParticles = value; ClientSettings.Save(); }), 2);
        effects.AddOption("显示特效", Check("显示特效", ClientSettings.DrawEffects, value => { ClientSettings.DrawEffects = value; ClientSettings.Save(); }), 2);
        effects.AddOption("显示天气与特效", Check("显示天气与特效", GameScene.Game?.DrawWeather ?? true, value => GameScene.Game?.SetDrawWeather(value)), 2);
        effects.AddOption("隐藏头盔", Check("隐藏头盔", GameScene.Game?.PlayerHideHead ?? false, value => GameScene.Game?.SendHelmetToggle(value)), 2);
        AddSection(effects, usability.Location.Y + Mathf.RoundToInt(usability.Size.Y) + 4);
    }

    private void BuildSoundPage()
    {
        var options = new ConfigSectionPanel("选项", 1);
        options.AddOption("后台播放声音", Check("后台播放声音", ClientSettings.SoundInBackground, value => { ClientSettings.SoundInBackground = value; ClientSettings.Save(); }));
        AddSection(options, 0);
        var volume = new ConfigSectionPanel("音量", 5);
        volume.AddSound(Lang.CommonControlConfigWindowSoundTabSystemVolumeLabel, SoundBar(() => ClientSettings.SystemVolume, value => { ClientSettings.SystemVolume = value; ClientSettings.ApplyAudioSettings(); }, () => ClientSettings.SystemVolumeMuted, value => { ClientSettings.SystemVolumeMuted = value; ClientSettings.ApplyAudioSettings(); }));
        volume.AddSound(Lang.CommonControlConfigWindowSoundTabMusicVolumeLabel, SoundBar(() => ClientSettings.MusicVolume, value => { ClientSettings.MusicVolume = value; ClientSettings.ApplyAudioSettings(); }, () => ClientSettings.MusicVolumeMuted, value => { ClientSettings.MusicVolumeMuted = value; ClientSettings.ApplyAudioSettings(); }));
        volume.AddSound("人物音量", SoundBar(() => ClientSettings.PlayerVolume, value => { ClientSettings.PlayerVolume = value; ClientSettings.ApplyAudioSettings(); }, () => ClientSettings.PlayerVolumeMuted, value => { ClientSettings.PlayerVolumeMuted = value; ClientSettings.ApplyAudioSettings(); }));
        volume.AddSound(Lang.CommonControlConfigWindowSoundTabMonsterVolumeLabel, SoundBar(() => ClientSettings.MonsterVolume, value => { ClientSettings.MonsterVolume = value; ClientSettings.ApplyAudioSettings(); }, () => ClientSettings.MonsterVolumeMuted, value => { ClientSettings.MonsterVolumeMuted = value; ClientSettings.ApplyAudioSettings(); }));
        volume.AddSound(Lang.CommonControlConfigWindowSoundTabMagicVolumeLabel, SoundBar(() => ClientSettings.MagicVolume, value => { ClientSettings.MagicVolume = value; ClientSettings.ApplyAudioSettings(); }, () => ClientSettings.MagicVolumeMuted, value => { ClientSettings.MagicVolumeMuted = value; ClientSettings.ApplyAudioSettings(); }));
        AddSection(volume, Mathf.RoundToInt(options.Size.Y) + 4);
    }

    private void BuildGamePage()
    {
        var game = new ConfigSectionPanel("游戏设置", 7, 2);
        game.AddOption("显示物品名称", Check("显示物品名称", ClientSettings.ShowItemNames, value => { ClientSettings.ShowItemNames = value; ClientSettings.Save(); }), 2);
        game.AddOption("显示怪物名称", Check("显示怪物名称", ClientSettings.ShowMonsterNames, value => { ClientSettings.ShowMonsterNames = value; ClientSettings.Save(); }), 2);
        game.AddOption("显示人物名称", Check("显示人物名称", ClientSettings.ShowPlayerNames, value => { ClientSettings.ShowPlayerNames = value; ClientSettings.Save(); }), 2);
        game.AddOption("显示生命条", Check("显示生命条", ClientSettings.ShowUserHealth, value => { ClientSettings.ShowUserHealth = value; ClientSettings.Save(); }), 2);
        game.AddOption("显示伤害数字", Check("显示伤害数字", ClientSettings.ShowDamageNumbers, value => { ClientSettings.ShowDamageNumbers = value; ClientSettings.Save(); }), 2);
        game.AddOption("右键取消目标", Check("右键取消目标", GameScene.Game?.RightClickDeTarget ?? true, value => GameScene.Game?.SetRightClickDeTarget(value)), 2);
        game.AddOption(Lang.BuffAllowLabel, Check(Lang.BuffAllowLabel, _allowObservable, value => { _allowObservable = value; GameScene.Game?.SendObservable(value); }), 2);
        AddSection(game, 0);

        var targetColours = new ConfigSectionPanel("目标颜色", 7, 2);
        targetColours.AddColour("怪物：低等级", Colour(() => ClientSettings.TargetMonsterLowLevelColour, value => ClientSettings.TargetMonsterLowLevelColour = value));
        targetColours.AddColour("怪物：同等级", Colour(() => ClientSettings.TargetMonsterSameLevelColour, value => ClientSettings.TargetMonsterSameLevelColour = value));
        targetColours.AddColour("怪物：高等级", Colour(() => ClientSettings.TargetMonsterHighLevelColour, value => ClientSettings.TargetMonsterHighLevelColour = value));
        targetColours.AddColour("怪物：友好", Colour(() => ClientSettings.TargetMonsterFriendlyColour, value => ClientSettings.TargetMonsterFriendlyColour = value));
        targetColours.AddColour("玩家：友好", Colour(() => ClientSettings.TargetPlayerFriendlyColour, value => ClientSettings.TargetPlayerFriendlyColour = value));
        targetColours.AddColour("玩家：敌对", Colour(() => ClientSettings.TargetPlayerEnemyColour, value => ClientSettings.TargetPlayerEnemyColour = value));
        targetColours.AddColour("NPC", Colour(() => ClientSettings.TargetNPCColour, value => ClientSettings.TargetNPCColour = value));
        AddSection(targetColours, Mathf.RoundToInt(game.Size.Y) + 4);
    }

    private void BuildNetworkPage()
    {
        var network = new ConfigSectionPanel("网络设置", 3);
        network.AddOption("使用网络配置", Check("使用网络配置", ClientSettings.UseNetworkConfig, value => { ClientSettings.UseNetworkConfig = value; ClientSettings.Save(); }));
        var address = new DXTextInput { Text = ClientSettings.IPAddress, MaxLength = 128 };
        address.TextChanged += value => { ClientSettings.IPAddress = value.Trim(); ClientSettings.Save(); };
        network.AddInput("服务器地址", address);
        var port = new DXTextInput { Text = ClientSettings.Port.ToString(), MaxLength = 6 };
        port.TextChanged += valueText =>
        {
            if (int.TryParse(valueText, out int value) && value > 0 && value <= 65535)
            {
                ClientSettings.Port = value;
                ClientSettings.Save();
            }
        };
        network.AddInput("服务器端口", port);
        AddSection(network, 0);
    }

    private void BuildUiPage()
    {
        var ui = new ConfigSectionPanel("界面设置", 6);
        ui.AddOption("隐藏聊天栏", Check("隐藏聊天栏", ClientSettings.HideChatBar, value => GameScene.Game?.SetHideChatBar(value)));
        ui.AddOption("按 Shift 打开聊天", Check("按 Shift 打开聊天", ClientSettings.ShiftOpenChat, value => { ClientSettings.ShiftOpenChat = value; ClientSettings.Save(); }));
        ui.AddOption("Esc 关闭所有窗口", Check("Esc 关闭所有窗口", GameScene.Game?.EscapeCloseAll ?? false, value => GameScene.Game?.SetEscapeCloseAll(value)));
        ui.AddOption("记录聊天", Check("记录聊天", ClientSettings.LogChat, value => { ClientSettings.LogChat = value; ClientSettings.Save(); }));
        var keyButton = new DXButton { Text = "快捷键设置", FontSize = 9, Size = new Vector2I(120, 18), LibraryFile = LibraryFile.Interface, Index = -1 };
        keyButton.MouseClick += (o, e) => { _keyBind ??= new KeyBindDialog(); WindowManager.Open(_keyBind, GetParent()); };
        ui.AddButton(keyButton);
        AddSection(ui, 0);

        var colours = new ConfigSectionPanel("聊天颜色", 13, 2);
        string[] colourNames = { Lang.CommonControlConfigWindowColoursTabLocalChatLabel, "GM 密语", "收到密语", "发送密语", "组队聊天", "行会聊天", "喊话", "世界聊天", "观察者", "提示", Lang.GameSystemLabel, Lang.GameUi591Label, Lang.CommonControlConfigWindowColoursTabAnnouncementsLabel };
        colours.AddColourPair(colourNames[0], ColourPair(() => ClientSettings.LocalTextForeColour, value => ClientSettings.LocalTextForeColour = value, () => ClientSettings.LocalTextBackColour, value => ClientSettings.LocalTextBackColour = value));
        colours.AddColourPair(colourNames[1], ColourPair(() => ClientSettings.GMWhisperInTextForeColour, value => ClientSettings.GMWhisperInTextForeColour = value, () => ClientSettings.GMWhisperInTextBackColour, value => ClientSettings.GMWhisperInTextBackColour = value));
        colours.AddColourPair(colourNames[2], ColourPair(() => ClientSettings.WhisperInTextForeColour, value => ClientSettings.WhisperInTextForeColour = value, () => ClientSettings.WhisperInTextBackColour, value => ClientSettings.WhisperInTextBackColour = value));
        colours.AddColourPair(colourNames[3], ColourPair(() => ClientSettings.WhisperOutTextForeColour, value => ClientSettings.WhisperOutTextForeColour = value, () => ClientSettings.WhisperOutTextBackColour, value => ClientSettings.WhisperOutTextBackColour = value));
        colours.AddColourPair(colourNames[4], ColourPair(() => ClientSettings.GroupTextForeColour, value => ClientSettings.GroupTextForeColour = value, () => ClientSettings.GroupTextBackColour, value => ClientSettings.GroupTextBackColour = value));
        colours.AddColourPair(colourNames[5], ColourPair(() => ClientSettings.GuildTextForeColour, value => ClientSettings.GuildTextForeColour = value, () => ClientSettings.GuildTextBackColour, value => ClientSettings.GuildTextBackColour = value));
        colours.AddColourPair(colourNames[6], ColourPair(() => ClientSettings.ShoutTextForeColour, value => ClientSettings.ShoutTextForeColour = value, () => ClientSettings.ShoutTextBackColour, value => ClientSettings.ShoutTextBackColour = value));
        colours.AddColourPair(colourNames[7], ColourPair(() => ClientSettings.GlobalTextForeColour, value => ClientSettings.GlobalTextForeColour = value, () => ClientSettings.GlobalTextBackColour, value => ClientSettings.GlobalTextBackColour = value));
        colours.AddColourPair(colourNames[8], ColourPair(() => ClientSettings.ObserverTextForeColour, value => ClientSettings.ObserverTextForeColour = value, () => ClientSettings.ObserverTextBackColour, value => ClientSettings.ObserverTextBackColour = value));
        colours.AddColourPair(colourNames[9], ColourPair(() => ClientSettings.HintTextForeColour, value => ClientSettings.HintTextForeColour = value, () => ClientSettings.HintTextBackColour, value => ClientSettings.HintTextBackColour = value));
        colours.AddColourPair(colourNames[10], ColourPair(() => ClientSettings.SystemTextForeColour, value => ClientSettings.SystemTextForeColour = value, () => ClientSettings.SystemTextBackColour, value => ClientSettings.SystemTextBackColour = value));
        colours.AddColourPair(colourNames[11], ColourPair(() => ClientSettings.GainsTextForeColour, value => ClientSettings.GainsTextForeColour = value, () => ClientSettings.GainsTextBackColour, value => ClientSettings.GainsTextBackColour = value));
        colours.AddColourPair(colourNames[12], ColourPair(() => ClientSettings.AnnouncementTextForeColour, value => ClientSettings.AnnouncementTextForeColour = value, () => ClientSettings.AnnouncementTextBackColour, value => ClientSettings.AnnouncementTextBackColour = value));
        AddSection(colours, Mathf.RoundToInt(ui.Size.Y) + 4);
    }

    public bool AuditLayout(out string details)
    {
        bool tabs = _tabs.Length == 5
            && _tabs[0].Location == new Vector2I(8, 37)
            && _tabs[4].Location == new Vector2I(288, 37);
        // 分区挂在 _content 下（滚动容器），用递归查找
        IEnumerable<ConfigSectionPanel> Sections() => _page.GetChildren().OfType<DXControl>().SelectMany(x => x.GetChildren().OfType<ConfigSectionPanel>());
        SelectTab(0);
        int graphicsSections = Sections().Count();
        SelectTab(1);
        int soundSections = Sections().Count();
        bool soundBars = Sections().SelectMany(x => x.GetChildren()).OfType<ConfigSoundBar>().Count() == 5;
        SelectTab(2);
        int gameSections = Sections().Count();
        SelectTab(3);
        int networkSections = Sections().Count();
        SelectTab(4);
        int uiSections = Sections().Count();
        details = $"size={Size} tabs={_tabs.Length} tab0={_tabs[0].Location}/{_tabs[0].Size} page={_page.Location}/{_page.Size} sections=g{graphicsSections}/s{soundSections}/game{gameSections}/net{networkSections}/ui{uiSections} soundBars={soundBars}";
        return Size == new Vector2I(364, 416) && tabs && _page.Location == new Vector2I(8, 62) && _page.Size == new Vector2I(348, 340)
            && graphicsSections == 3 && soundSections == 2 && soundBars && gameSections == 2 && networkSections == 1 && uiSections == 2;
    }
}
