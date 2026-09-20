using System;
using System.Collections.Generic;
using DrawingColor = System.Drawing.Color;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class SelectScene : Control
{
    private CanvasLayer _uiLayer;
    private Network.NetworkManager _net;
    private List<SelectInfo> _characters = new();
    private ItemList _charList;
    private LineEdit _nameEdit;
    private OptionButton _classBtn;
    private OptionButton _genderBtn;
    private Button _createBtn;
    private Button _startBtn;
    private Button _deleteBtn;
    private Label _statusLabel;
    private DXControl _skinPanel;
    private DXControl _skinCreatePanel;
    private DXAnimatedControl _characterAnimation;
    private DXImageControl _characterOverlay1, _characterOverlay2;
    private DXButton _skinConfigButton;
    private ConfigDialog _selectConfig;
    private DXTextInput _skinName;
    private DXTextInput _skinCreateName;
    private DXButton _skinStart, _skinCreate, _skinDelete;
    private DXButton _skinCreateConfirm, _skinCreateCancel;
    private DXNumberField _skinHairNumber;
    private DXAnimatedControl _createPreview;
    private DXLabel _selectedClassLabel, _selectedGenderLabel;
    private MirClass _skinCreateClass = MirClass.Warrior;
    private MirGender _skinCreateGender = MirGender.Male;
    private int _skinHairType = 1;
    private DrawingColor _skinHairColour = DrawingColor.Black;
    private DrawingColor _skinArmourColour = DrawingColor.White;
    private readonly List<DXButton> _skinCharacters = new();
    private readonly List<DXButton> _createClassButtons = new();
    private readonly List<DXButton> _createGenderButtons = new();
    private readonly List<Action> _unsubscribers = new();

    public override void _Ready()
    {
        ClientSettings.Load();
        ClientSettings.ApplyDisplaySettings();
        ClientSettings.ApplyAudioSettings();
        SoundPlayback.Stop(SoundIndex.LoginScene);
        SoundPlayback.Play(this, SoundIndex.SelectScene);
        _net = GetNode<Network.NetworkManager>("/root/NetworkManager");

        _charList = GetNode<ItemList>("VBox/CharList");
        _nameEdit = GetNode<LineEdit>("VBox/CreateRow/NameEdit");
        _classBtn = GetNode<OptionButton>("VBox/CreateRow/ClassBtn");
        _genderBtn = GetNode<OptionButton>("VBox/CreateRow/GenderBtn");
        _createBtn = GetNode<Button>("VBox/CreateBtn");
        _startBtn = GetNode<Button>("VBox/StartBtn");
        _deleteBtn = new Button { Text = Lang.SelectCharacterLabel, Disabled = true };
        GetNode<Control>("VBox").AddChild(_deleteBtn);
        _statusLabel = GetNode<Label>("VBox/StatusLabel");
        // 2 倍 UI 缩放：DX 旧版 UI 挂到缩放层，窗口放大时跟随缩放。
        _uiLayer = new CanvasLayer { Name = "UiScaleLayer" };
        AddChild(_uiLayer);
        BuildLegacySelectUi();
        UiScaler.UpdateScale(_uiLayer, GetViewport());
        // 调试审计：ZIRCON_UI_AUDIT=1 时列出所有超出逻辑画布的控件
        if (System.Environment.GetEnvironmentVariable("ZIRCON_UI_AUDIT") == "1")
            UiScaler.AuditOverflow(_uiLayer, "SelectScene");
        // 窗口大小变化后视口才更新，用 Viewport.SizeChanged 确保缩放跟随。
        GetViewport().SizeChanged += () => UiScaler.UpdateScale(_uiLayer, GetViewport());

        // 填充职业/性别选项
        _classBtn.AddItem("战士", (int)MirClass.Warrior);
        _classBtn.AddItem("法师", (int)MirClass.Wizard);
        _classBtn.AddItem("道士", (int)MirClass.Taoist);
        _classBtn.AddItem("刺客", (int)MirClass.Assassin);
        _genderBtn.AddItem("男", (int)MirGender.Male);
        _genderBtn.AddItem("女", (int)MirGender.Female);

        if (_createBtn != null) _createBtn.Pressed += OnCreatePressed;
        if (_startBtn != null) _startBtn.Pressed += OnStartPressed;
        if (_charList != null) _charList.ItemSelected += idx => { if (_startBtn != null) _startBtn.Disabled = false; _deleteBtn.Disabled = false; };
        if (_deleteBtn != null) _deleteBtn.Pressed += OnDeletePressed;

        // 订阅网络事件（_ExitTree 统一退订，避免场景释放后回调已销毁对象）
        if (_net?.Connection != null)
        {
            _net.Connection.NewCharacterResultEvent += OnNewCharacterResult;
            _unsubscribers.Add(() => _net.Connection.NewCharacterResultEvent -= OnNewCharacterResult);
            _net.Connection.DeleteCharacterResultEvent += OnDeleteCharacterResult;
            _unsubscribers.Add(() => _net.Connection.DeleteCharacterResultEvent -= OnDeleteCharacterResult);
            _net.Connection.StartGameResultEvent += OnStartGameResult;
            _unsubscribers.Add(() => _net.Connection.StartGameResultEvent -= OnStartGameResult);
        }

        RefreshList();

        // headless 自动测试: --auto-login / --user 时自动进游戏; --char 指定角色名
        if (AutoLoginArgs.AutoLogin)
        {
            var wantChar = AutoLoginArgs.Character;
            if (wantChar.Length > 0)
            {
                var target = _characters.Find(c => c.CharacterName == wantChar);
                if (target != null)
                {
                    GD.Print($"[Select] 自动进入指定角色: {wantChar} (idx={target.CharacterIndex})");
                    _autoCharIndex = target.CharacterIndex;
                    CallDeferred(nameof(AutoStartGame));
                }
                else
                {
                    GD.Print($"[Select] 指定角色 {wantChar} 不存在, 现有: [{string.Join(", ", _characters.ConvertAll(c => c.CharacterName))}]");
                    _statusLabel.Text = string.Format(Lang.SelectCharacterLabel2, wantChar);
                }
            }
            else if (_characters.Count == 0)
            {
                GD.Print("[Select] 自动建角色 TestHero...");
                CallDeferred(nameof(AutoCreateCharacter));
            }
            else
            {
                GD.Print($"[Select] 自动进入游戏, 角色: {_characters[0].CharacterName}");
                CallDeferred(nameof(AutoStartGame));
            }
        }
    }

    public override void _ExitTree()
    {
        foreach (var unsubscribe in _unsubscribers)
            unsubscribe();
        _unsubscribers.Clear();
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_characterAnimation == null || !_characterAnimation.Visible) return;

        bool showOverlays = !_characterAnimation.Loop && _characterAnimation.Animated;
        Vector2I baseOffset = MirSkin.GetOffset(LibraryFile.Interface1c, _characterAnimation.Index);
        if (_characterOverlay1 != null)
        {
            _characterOverlay1.Visible = showOverlays;
            _characterOverlay1.Index = showOverlays ? _characterAnimation.Index + 100 : -1;
            if (showOverlays)
                _characterOverlay1.Location = new Vector2I(450, 200) + baseOffset - MirSkin.GetOffset(LibraryFile.Interface1c, _characterOverlay1.Index);
        }
        if (_characterOverlay2 != null)
        {
            _characterOverlay2.Visible = showOverlays;
            _characterOverlay2.Index = showOverlays ? _characterAnimation.Index + 130 : -1;
            if (showOverlays)
                _characterOverlay2.Location = new Vector2I(450, 200) + baseOffset - MirSkin.GetOffset(LibraryFile.Interface1c, _characterOverlay2.Index);
        }
    }

    private int _autoCharIndex = -1;
    private int _lastStartIndex = -1;

    private void AutoCreateCharacter()
    {
        _net.Connection?.SendNewCharacter("TestHero", MirClass.Warrior, MirGender.Male);
    }
    private void AutoStartGame()
    {
        int idx = _autoCharIndex >= 0 && _autoCharIndex < _characters.Count
            ? _autoCharIndex
            : _characters[0].CharacterIndex;
        _lastStartIndex = idx;
        GD.Print($"[Select] AutoStartGame: 发送 StartGame, charIndex={idx}");
        _net.Connection?.SendStartGame(idx);
    }

    public void SetCharacters(List<SelectInfo> chars)
    {
        _characters = chars ?? new List<SelectInfo>();
        RefreshList();
    }

    private void RefreshList()
    {
        // LoginScene 在 SelectScene._Ready 之前注入角色列表；此时场景控件尚未绑定。
        // _Ready 会再次调用 RefreshList，因此这里只需延后刷新。
        if (_charList == null || _skinPanel == null) return;
        _charList.Clear();
        foreach (var c in _characters)
            _charList.AddItem($"#{c.CharacterIndex} {c.CharacterName} Lv{c.Level} {c.Class}");
        foreach (var button in _skinCharacters) { _skinPanel.RemoveControl(button); button.QueueFree(); }
        _skinCharacters.Clear();
        if (_skinPanel != null)
        {
            for (int i = 0; i < _characters.Count && i < 4; i++)
            {
                var c = _characters[i];
                var button = new DXButton
                {
                    Text = string.Empty,
                    BackColour = new Color(.095f, .047f, .047f),
                    Border = true,
                    BorderColour = new Color(.72f, .52f, .24f),
                    Location = new Vector2I(20, 45 + i * 78),
                    Size = new Vector2I(280, 75),
                };
                int selected = i;
                button.MouseClick += (o, e) => SelectSkinCharacter(selected);
                button.AddControl(new DXImageControl
                {
                    LibraryFile = LibraryFile.Interface,
                    Index = 27 + (int)c.Class,
                    FixedSize = true,
                    Size = new Vector2I(64, 64),
                    Location = new Vector2I(6, 4),
                    IsControl = false,
                });
                button.AddControl(new DXLabel { Text = Lang.SelectNameLabel, FontSize = 8, TextColour = new Color(.8f, .7f, .5f), Location = new Vector2I(77, 7), IsControl = false });
                button.AddControl(new DXLabel { Text = c.CharacterName, FontSize = 10, TextColour = Colors.White, Border = true, BorderColour = new Color(.5f, .35f, .18f), BackColour = new Color(.04f, .02f, .02f, .75f), Location = new Vector2I(135, 8), Size = new Vector2I(130, 15), IsControl = false });
                button.AddControl(new DXLabel { Text = Lang.SelectClassLabel, FontSize = 8, TextColour = new Color(.8f, .7f, .5f), Location = new Vector2I(77, 29), IsControl = false });
                button.AddControl(new DXLabel { Text = c.Class.Local(), FontSize = 9, TextColour = Colors.White, Border = true, BorderColour = new Color(.5f, .35f, .18f), BackColour = new Color(.04f, .02f, .02f, .75f), Location = new Vector2I(135, 28), Size = new Vector2I(53, 15), IsControl = false });
                button.AddControl(new DXLabel { Text = c.Level.ToString(), FontSize = 9, TextColour = Colors.White, Border = true, BorderColour = new Color(.5f, .35f, .18f), BackColour = new Color(.04f, .02f, .02f, .75f), Location = new Vector2I(235, 28), Size = new Vector2I(30, 15), IsControl = false });
                button.AddControl(new DXLabel { Text = Lang.StatusWindowUi496Label, FontSize = 8, TextColour = new Color(.8f, .7f, .5f), Location = new Vector2I(77, 51), IsControl = false });
                button.AddControl(new DXLabel { Text = GetLocationName(c.Location), FontSize = 8, TextColour = Colors.White, Location = new Vector2I(135, 48), Size = new Vector2I(130, 15), IsControl = false });
                _skinPanel.AddControl(button); _skinCharacters.Add(button);
            }
        }
        if (_characters.Count == 0)
        {
            _statusLabel.Text = Lang.SelectCharacterLabel3;
            _characterAnimation.Visible = false;
            _skinStart.Enabled = false;
            _skinDelete.Enabled = false;
        }
        else
        {
            _statusLabel.Text = Lang.SelectCharacterLabel4;
            SelectSkinCharacter(0);
        }
        _skinCreate.Enabled = _characters.Count < 4;
    }

    private void SelectSkinCharacter(int index)
    {
        if (index < 0 || index >= _characters.Count) return;
        _charList.Select(index);
        _startBtn.Disabled = false;
        _deleteBtn.Disabled = false;
        _skinStart.Enabled = true;
        _skinDelete.Enabled = true;
        for (int i = 0; i < _skinCharacters.Count; i++)
        {
            _skinCharacters[i].BackColour = i == index
                ? new Color(.28f, .14f, .14f)
                : new Color(.095f, .047f, .047f);
            _skinCharacters[i].Border = i != index;
        }
        UpdateCharacterDisplay(_characters[index]);
    }

    private string GetLocationName(int index)
    {
        if (Globals.MapInfoList?.Binding == null) return "New Character";
        foreach (var map in Globals.MapInfoList.Binding)
            if (map.Index == index) return map.Local() ?? "New Character";
        return "New Character";
    }

    private void UpdateCharacterDisplay(SelectInfo info)
    {
        if (_characterAnimation == null || info == null) return;

        _characterAnimation.ClearAnimationHandlers();
        _characterAnimation.Visible = true;
        _characterAnimation.UseOffSet = true;
        _characterAnimation.Loop = false;
        _characterAnimation.AnimationStart = DateTime.MinValue;

        (int intro, int introFrames, int idle, int idleFrames, int introMs, int idleMs) = (info.Class, info.Gender) switch
        {
            (MirClass.Warrior, MirGender.Male) => (240, 22, 300, 13, 2200, 1900),
            (MirClass.Warrior, MirGender.Female) => (440, 28, 500, 13, 2800, 1900),
            (MirClass.Wizard, MirGender.Male) => (740, 20, 800, 10, 2000, 1500),
            (MirClass.Wizard, MirGender.Female) => (940, 26, 1000, 15, 2600, 2250),
            (MirClass.Taoist, MirGender.Male) => (1240, 27, 1300, 15, 2700, 2250),
            (MirClass.Taoist, MirGender.Female) => (1440, 20, 1500, 10, 2000, 1500),
            (MirClass.Assassin, MirGender.Male) => (1740, 25, 1800, 16, 2500, 2400),
            _ => (1940, 20, 2000, 10, 2000, 1500),
        };

        _characterAnimation.BaseIndex = intro;
        _characterAnimation.FrameCount = introFrames;
        _characterAnimation.AnimationDelay = TimeSpan.FromMilliseconds(introMs);
        _characterAnimation.AfterAnimation += (sender, args) =>
        {
            _characterAnimation.BaseIndex = idle;
            _characterAnimation.FrameCount = idleFrames;
            _characterAnimation.AnimationDelay = TimeSpan.FromMilliseconds(idleMs);
            _characterAnimation.Restart(true);
        };
        _characterAnimation.Restart(false);
    }

    private void HideCreateCharacterPanel()
    {
        if (_skinCreatePanel != null) _skinCreatePanel.Visible = false;
        if (_skinPanel != null) _skinPanel.Visible = true;
        if (_characterAnimation != null) _characterAnimation.Visible = true;
    }

    private void ShowCreateCharacterPanel()
    {
        if (_skinPanel != null) _skinPanel.Visible = false;
        if (_skinCreatePanel != null) _skinCreatePanel.Visible = true;
        if (_characterAnimation != null) _characterAnimation.Visible = false;
    }

    private void SelectCreateClass(MirClass value)
    {
        _skinCreateClass = value;
        UpdateCreateButtonStates();
        UpdateCreatePreview();
    }

    private void SelectCreateGender(MirGender value)
    {
        _skinCreateGender = value;
        UpdateCreateButtonStates();
        UpdateCreatePreview();
    }

    private void UpdateCreateButtonStates()
    {
        if (_skinCreateConfirm != null)
            _skinCreateConfirm.Enabled = !string.IsNullOrWhiteSpace(_skinCreateName?.Text);

        int[] normalClass = { 121, 126, 131, 136 };
        int[] pressedClass = { 120, 125, 130, 135 };
        for (int i = 0; i < _createClassButtons.Count && i < normalClass.Length; i++)
            _createClassButtons[i].Index = (int)_skinCreateClass == i ? pressedClass[i] : normalClass[i];
        for (int i = 0; i < _createGenderButtons.Count && i < 2; i++)
            _createGenderButtons[i].Index = (int)_skinCreateGender == i ? (i == 0 ? 115 : 110) : (i == 0 ? 116 : 111);
        if (_selectedClassLabel != null) _selectedClassLabel.Text = _skinCreateClass.Local();
        if (_selectedGenderLabel != null) _selectedGenderLabel.Text = _skinCreateGender.Local();
    }

    private void SubmitSkinCharacter()
    {
        if (_skinCreateName == null || string.IsNullOrWhiteSpace(_skinCreateName.Text)) return;
        _skinCreateConfirm.Enabled = false;
        _statusLabel.Text = Lang.SelectCreateLabel;
        _net.Connection?.SendNewCharacter(_skinCreateName.Text.Trim(), _skinCreateClass, _skinCreateGender, _skinHairType, _skinHairColour, _skinArmourColour);
    }

    private void BuildLegacySelectUi()
    {
        // 布局基准 = 逻辑画布 1024x768，UiScaler 负责缩放 + 居中（同 LoginScene）。
        var viewport = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight);
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 50,
            FixedSize = true,
            Size = new Vector2I(1024, 768),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = (viewport - new Vector2(1024, 768)) / 2f,
        };
        _uiLayer.AddChild(background);

        _skinConfigButton = new DXButton
        {
            LibraryFile = LibraryFile.GameInter,
            Index = 116,
            Position = new Vector2(viewport.X - 58, 10),
        };
        _skinConfigButton.Position = new Vector2(viewport.X - _skinConfigButton.Size.X - 10f, 10);
        _skinConfigButton.MouseClick += (o, e) =>
        {
            _selectConfig ??= new ConfigDialog { Position = new Vector2((viewport.X - 380) / 2f, (viewport.Y - 430) / 2f) };
            WindowManager.Toggle(_selectConfig, _uiLayer);
        };
        _uiLayer.AddChild(_skinConfigButton);

        var leftGlow = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            BaseIndex = 2800,
            FrameCount = 17,
            AnimationDelay = TimeSpan.FromSeconds(3),
            Animated = true,
            Loop = true,
            Blend = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(leftGlow);
        var rightGlow = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            BaseIndex = 2900,
            FrameCount = 17,
            AnimationDelay = TimeSpan.FromSeconds(3),
            Animated = true,
            Loop = true,
            Blend = true,
            UseOffSet = true,
            Location = new Vector2I(20, 25),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(rightGlow);

        _characterAnimation = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            FrameCount = 1,
            AnimationDelay = TimeSpan.FromMilliseconds(1),
            UseOffSet = true,
            Location = new Vector2I(450, 200),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(_characterAnimation);
        _characterOverlay1 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(450, 200), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _characterOverlay2 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(450, 200), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        background.AddControl(_characterOverlay1);
        background.AddControl(_characterOverlay2);

        _skinPanel = new DXControl
        {
            Size = new Vector2I(320, 425),
            // 角色列表面板按逻辑画布居中；旧公式又除了一次 2，
            // 导致 1024 宽画布下 x=96 而不是 x=352，视觉和点击区域整体偏左。
            Position = new Vector2((viewport.X - 320f) / 2f, (viewport.Y - 425) / 2f),
        };
        // 角色列表面板也要挂到缩放层，否则窗口放大时它不跟随缩放。
        _uiLayer.AddChild(_skinPanel);
        _skinPanel.AddControl(new LegacyWindowFrame
        {
            Size = new Vector2I(320, 425),
            HasTitle = true,
            HasFooter = true,
        });
        _skinPanel.AddControl(new DXLabel { Text = Lang.SelectCharacterLabel5, FontSize = 12, TextColour = new Color(1f, .85f, .35f), DrawOutline = true, Size = new Vector2I(320, 28), Align = HorizontalAlignment.Center, IsControl = false });
        int defaultButtonHeight = MirSkin.GetSize(LibraryFile.Interface, 16).Y;
        if (defaultButtonHeight <= 0) defaultButtonHeight = 21;
        _skinStart = new DXButton { Text = Lang.SelectGameLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(25, 382), Size = new Vector2I(80, defaultButtonHeight), Enabled = false };
        _skinCreate = new DXButton { Text = Lang.NewCharacterTitle, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(120, 382), Size = new Vector2I(80, defaultButtonHeight) };
        _skinDelete = new DXButton { Text = Lang.SelectCharacterLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(215, 382), Size = new Vector2I(80, defaultButtonHeight), Enabled = false };
        _skinStart.MouseClick += (o, e) => OnStartPressed();
        _skinDelete.MouseClick += (o, e) => OnDeletePressed();
        _skinCreate.MouseClick += (o, e) => { if (_characters.Count < 4) ShowCreateCharacterPanel(); };
        _skinPanel.AddControl(_skinStart); _skinPanel.AddControl(_skinCreate); _skinPanel.AddControl(_skinDelete);

        // 原版 NewCharacterDialog: 260x650，职业、性别、外观和底部创建按钮均保留原坐标。
        _skinCreatePanel = new DXControl
        {
            Size = new Vector2I(260, 650),
            Position = new Vector2((viewport.X - 260) / 2f, (viewport.Y - 650) / 2f),
            Visible = false,
        };
        _uiLayer.AddChild(_skinCreatePanel);
        _skinCreatePanel.AddControl(new LegacyWindowFrame
        {
            Size = new Vector2I(260, 650),
            HasTitle = true,
            HasFooter = true,
        });
        _skinCreatePanel.AddControl(new DXLabel { Text = Lang.NewCharacterTitle, FontSize = 12, TextColour = new Color(1f, .85f, .35f), DrawOutline = true, Align = HorizontalAlignment.Center, Size = new Vector2I(260, 30), IsControl = false });

        var classBox = CreateOptionBox(Lang.SelectClassLabel2, new Vector2I(30, 40));
        _selectedClassLabel = new DXLabel { Text = "战士", FontSize = 8, Align = HorizontalAlignment.Center, Location = new Vector2I(60, 65), Size = new Vector2I(80, 15), IsControl = false };
        classBox.AddControl(_selectedClassLabel);
        _createClassButtons.Add(AddCreateOption(classBox, 0, Lang.NewCharacterSelectedClassLabel, 120, () => SelectCreateClass(MirClass.Warrior)));
        _createClassButtons.Add(AddCreateOption(classBox, 1, Lang.RankingUi145Label, 126, () => SelectCreateClass(MirClass.Wizard)));
        _createClassButtons.Add(AddCreateOption(classBox, 2, Lang.RankingUi146Label, 131, () => SelectCreateClass(MirClass.Taoist)));
        _createClassButtons.Add(AddCreateOption(classBox, 3, Lang.RankingUi147Label, 136, () => SelectCreateClass(MirClass.Assassin)));

        var genderBox = CreateOptionBox(Lang.SelectGenderLabel, new Vector2I(30, 135));
        _selectedGenderLabel = new DXLabel { Text = "男", FontSize = 8, Align = HorizontalAlignment.Center, Location = new Vector2I(60, 65), Size = new Vector2I(80, 15), IsControl = false };
        genderBox.AddControl(_selectedGenderLabel);
        _createGenderButtons.Add(AddCreateOption(genderBox, 1, Lang.NewCharacterSelectedGenderLabel, 115, () => SelectCreateGender(MirGender.Male)));
        _createGenderButtons.Add(AddCreateOption(genderBox, 2, Lang.SelectUi524Label, 111, () => SelectCreateGender(MirGender.Female)));

        var appearance = new DXControl { Size = new Vector2I(200, 330), Location = new Vector2I(30, 230), BackColour = new Color(.28f, .14f, .14f), Border = true, BorderColour = new Color(.75f, .55f, .2f) };
        _skinCreatePanel.AddControl(appearance);
        appearance.AddControl(new DXLabel { Text = Lang.SelectCharacterLabel7, FontSize = 9, TextColour = new Color(1f, .85f, .55f), Align = HorizontalAlignment.Center, Size = new Vector2I(200, 22), IsControl = false });
        appearance.AddControl(new DXLabel { Text = Lang.SelectUi526Label, FontSize = 9, Location = new Vector2I(35, 28), IsControl = false });
        _skinHairNumber = new DXNumberField("", 0, 11) { Location = new Vector2I(90, 25) };
        _skinHairNumber.Value = 1;
        _skinHairNumber.ValueChanged += (o, e) => { _skinHairType = _skinHairNumber.Value; UpdateCreatePreview(); };
        appearance.AddControl(_skinHairNumber);
        appearance.AddControl(new DXLabel { Text = Lang.SelectUi527Label, FontSize = 9, Location = new Vector2I(35, 53), IsControl = false });
        AddColourChoice(appearance, new Vector2I(90, 50), DrawingColor.Black, false);
        appearance.AddControl(new DXLabel { Text = Lang.SelectColoursLabel, FontSize = 9, Location = new Vector2I(18, 78), IsControl = false });
        AddColourChoice(appearance, new Vector2I(90, 75), DrawingColor.White, true);
        var previewPanel = new DXControl { Size = new Vector2I(190, 225), Location = new Vector2I(5, 100), BackColour = new Color(.19f, .16f, .09f), Border = true, BorderColour = new Color(.75f, .55f, .2f) };
        appearance.AddControl(previewPanel);
        previewPanel.AddControl(new DXLabel { Text = Lang.NewCharacterPreviewLabel, FontSize = 9, TextColour = new Color(1f, .85f, .55f), Align = HorizontalAlignment.Center, Size = new Vector2I(190, 20), IsControl = false });
        _createPreview = new DXAnimatedControl { LibraryFile = LibraryFile.Interface1c, BaseIndex = 300, FrameCount = 13, AnimationDelay = TimeSpan.FromMilliseconds(1900), Animated = true, Loop = true, UseOffSet = true, Location = new Vector2I(70, 145), MouseFilter = MouseFilterEnum.Ignore };
        previewPanel.AddControl(_createPreview);
        _skinCreateName = new DXTextInput { Location = new Vector2I(75, 570), Size = new Vector2I(155, 20), Text = "TestHero" };
        _skinCreateName.TextChanged += value => UpdateCreateButtonStates();
        _skinCreatePanel.AddControl(_skinCreateName);
        _skinCreatePanel.AddControl(new DXLabel { Text = Lang.SelectCharacterLabel8, FontSize = 9, Location = new Vector2I(20, 572), IsControl = false });

        _skinCreateConfirm = new DXButton { Text = Lang.SelectCreateButtonLabel, FontSize = 10, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(90, 607), Size = new Vector2I(80, defaultButtonHeight), Enabled = true };
        _skinCreateCancel = new DXButton { LibraryFile = LibraryFile.Interface, Index = 15, Location = new Vector2I(230, 3) };
        _skinCreateConfirm.MouseClick += (o, e) => SubmitSkinCharacter();
        _skinCreateCancel.MouseClick += (o, e) => HideCreateCharacterPanel();
        _skinCreatePanel.AddControl(_skinCreateConfirm);
        _skinCreatePanel.AddControl(_skinCreateCancel);
        UpdateCreateButtonStates();
        UpdateCreatePreview();
        GetNode<Control>("VBox").Visible = false;
    }

    private DXControl CreateOptionBox(string title, Vector2I location)
    {
        var box = new DXControl { Size = new Vector2I(200, 85), Location = location, BackColour = new Color(.28f, .14f, .14f), Border = true, BorderColour = new Color(.75f, .55f, .2f) };
        box.AddControl(new DXLabel { Text = title, FontSize = 9, TextColour = new Color(1f, .85f, .55f), Align = HorizontalAlignment.Center, Size = new Vector2I(200, 20), IsControl = false });
        _skinCreatePanel.AddControl(box);
        return box;
    }

    private DXButton AddCreateOption(DXControl box, int slot, string text, int index, Action action)
    {
        var button = new DXButton { Text = text, FontSize = 8, LibraryFile = LibraryFile.Interface1c, Index = index, Location = new Vector2I(12 + slot * 45, 25), Size = new Vector2I(40, 38) };
        button.MouseClick += (o, e) => action();
        box.AddControl(button);
        return button;
    }

    private void AddColourChoice(DXControl parent, Vector2I location, DrawingColor colour, bool armour)
    {
        var button = new DXColourControl { Location = location, Size = new Vector2I(40, 20), BackColour = new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f) };
        button.BackColourChanged += (o, e) =>
        {
            var selected = button.BackColour;
            var drawing = DrawingColor.FromArgb(Mathf.RoundToInt(selected.R * 255f), Mathf.RoundToInt(selected.G * 255f), Mathf.RoundToInt(selected.B * 255f));
            if (armour) _skinArmourColour = drawing; else _skinHairColour = drawing;
            UpdateCreatePreview();
        };
        parent.AddControl(button);
    }

    private void UpdateCreatePreview()
    {
        if (_createPreview == null) return;
        int baseIndex = (_skinCreateClass, _skinCreateGender) switch
        {
            (MirClass.Warrior, MirGender.Male) => 300,
            (MirClass.Warrior, MirGender.Female) => 500,
            (MirClass.Wizard, MirGender.Male) => 800,
            (MirClass.Wizard, MirGender.Female) => 1000,
            (MirClass.Taoist, MirGender.Male) => 1300,
            (MirClass.Taoist, MirGender.Female) => 1500,
            (MirClass.Assassin, MirGender.Male) => 1800,
            _ => 2000,
        };
        int frames = (_skinCreateClass, _skinCreateGender) switch
        {
            (MirClass.Warrior, MirGender.Male) or (MirClass.Warrior, MirGender.Female) => 13,
            (MirClass.Wizard, MirGender.Male) or (MirClass.Wizard, MirGender.Female) => 10,
            (MirClass.Taoist, MirGender.Male) or (MirClass.Taoist, MirGender.Female) => 15,
            _ => 16,
        };
        _createPreview.BaseIndex = baseIndex;
        _createPreview.FrameCount = frames;
        _createPreview.AnimationDelay = TimeSpan.FromMilliseconds(1900);
        _createPreview.Restart(true);
    }

    private void OnCreatePressed()
    {
        if (string.IsNullOrEmpty(_nameEdit.Text))
        {
            _statusLabel.Text = Lang.SelectCharacterLabel9;
            return;
        }
        _createBtn.Disabled = true;
        _statusLabel.Text = Lang.SelectCreateLabel;
        _net.Connection?.SendNewCharacter(
            _nameEdit.Text,
            (MirClass)_classBtn.Selected,
            (MirGender)_genderBtn.Selected
        );
    }

    private NewCharacterResult _pendingNewCharResult;
    private SelectInfo _pendingNewCharInfo;
    private void OnNewCharacterResult(NewCharacterResult result, SelectInfo info)
    {
        _pendingNewCharResult = result;
        _pendingNewCharInfo = info;
        CallDeferred(nameof(ShowNewCharacterResult));
    }
    private void ShowNewCharacterResult()
    {
        _createBtn.Disabled = false;
        if (_pendingNewCharResult == NewCharacterResult.Success)
        {
            GD.Print($"[Select] 建角色成功: {_pendingNewCharInfo?.CharacterName}");
            if (_pendingNewCharInfo != null)
                _characters.Add(_pendingNewCharInfo);
            RefreshList();
            _statusLabel.Text = Lang.SelectCharacterLabel10;
            // headless 自动测试: 建完直接进游戏
            if (AutoLoginArgs.AutoLogin && _characters.Count > 0)
            {
                GD.Print("[Select] 自动进入游戏...");
                CallDeferred(nameof(AutoStartGame));
            }
        }
        else
        {
            GD.Print($"[Select] 建角色失败: {_pendingNewCharResult}");
            _statusLabel.Text = string.Format(Lang.SelectCreateLabel3, _pendingNewCharResult);
        }
    }

    private void OnStartPressed()
    {
        if (_charList.GetSelectedItems().Length == 0) return;
        int idx = _charList.GetSelectedItems()[0];
        if (idx >= _characters.Count) return;
        _startBtn.Disabled = true;
        _skinStart.Enabled = false;
        _statusLabel.Text = Lang.SelectGameLabel2;
        _lastStartIndex = _characters[idx].CharacterIndex;
        _net.Connection?.SendStartGame(_characters[idx].CharacterIndex);
    }

    private void OnDeletePressed()
    {
        var selected = _charList.GetSelectedItems();
        if (selected.Length == 0 || selected[0] >= _characters.Count) return;
        int listIndex = selected[0];
        var character = _characters[listIndex];
        var confirm = new ConfirmationDialog { Title = Lang.SelectCharacterLabel, DialogText = string.Format(Lang.SelectCharacterLabel12, character.CharacterName) };
        AddChild(confirm);
        confirm.Confirmed += () =>
        {
            _deleteBtn.Disabled = true;
            _skinDelete.Enabled = false;
            _statusLabel.Text = Lang.SelectDeleteLabel;
            _net.Connection?.SendDeleteCharacter(character.CharacterIndex);
            confirm.QueueFree();
        };
        confirm.Canceled += () => confirm.QueueFree();
        confirm.PopupCentered();
    }

    private void OnDeleteCharacterResult(DeleteCharacterResult result, int deletedIndex)
    {
        if (result == DeleteCharacterResult.Success)
        {
            _characters.RemoveAll(c => c.CharacterIndex == deletedIndex);
            RefreshList();
            _startBtn.Disabled = true;
            _deleteBtn.Disabled = true;
            _statusLabel.Text = Lang.SelectDeleteLabel2;
        }
        else
        {
            _deleteBtn.Disabled = false;
            _statusLabel.Text = string.Format(Lang.SelectDeleteLabel3, result);
        }
    }

    private StartGameResult _pendingStartResult;
    private StartInformation _pendingStartInfo;
    private void OnStartGameResult(StartGameResult result, StartInformation info)
    {
        _pendingStartResult = result;
        _pendingStartInfo = info;
        CallDeferred(nameof(ShowStartGameResult));
    }
    private void ShowStartGameResult()
    {
        if (_pendingStartResult == StartGameResult.Success)
        {
            SoundPlayback.Stop(SoundIndex.SelectScene);
            GD.Print($"[Select] *** StartGame 成功! 进入游戏 ***");
            var gameScene = ResourceLoader.Load<PackedScene>("res://Scenes/GameScene.tscn");
            var game = gameScene.Instantiate<GameScene>();
            game.StartInfo = _pendingStartInfo;
            GetTree().Root.AddChild(game);
            QueueFree();
        }
        else if (_pendingStartResult == StartGameResult.Delayed)
        {
            GD.Print("[Select] StartGame 冷却中, 3秒后重试...");
            _statusLabel.Text = Lang.SelectUi540Label;
            var timer = new Timer();
            timer.WaitTime = 3.0;
            timer.OneShot = true;
            AddChild(timer);
            timer.Timeout += () =>
            {
                GD.Print("[Select] 重试 StartGame");
                int retryIdx = _lastStartIndex >= 0 ? _lastStartIndex : _characters[0].CharacterIndex;
                GD.Print($"[Select] AutoStartGame: 发送 StartGame, charIndex={retryIdx}");
                _net.Connection?.SendStartGame(retryIdx);
            };
            timer.Start();
        }
        else
        {
            _statusLabel.Text = string.Format(Lang.SelectGameLabel3, _pendingStartResult);
            GD.Print($"[Select] StartGame 失败: {_pendingStartResult}");
            _startBtn.Disabled = false;
        }
    }
}
