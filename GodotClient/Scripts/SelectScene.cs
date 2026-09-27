using System;
using System.Collections.Generic;
using DrawingColor = System.Drawing.Color;
using Godot;
using Library;
using Library.Network;
using S = Library.Network.ServerPackets;
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
    // EI 选角屏是 **2 个角色槽**（base +0xCB8/+0x10BC，stride 0x40，idx 0..1），
    // 角色直接站在 F50 洞窟背景里，不是列表面板。这里补第 2 槽与两个名称标签。
    private DXAnimatedControl _characterAnimation2;
    private DXLabel _slotName0, _slotName1;
    private DXControl _slotHit0, _slotHit1;
    // EI 进入游戏前的公告框（F602）。原版流程：选中角色 -> 点进入 -> 服务端下发公告 ->
    // 玩家确认后才真正进游戏。我方此前公告只在 GameScene 里处理（进游戏之后），
    // 选角屏完全没有这条链。
    private NoticeDialog _noticeDialog;
    // EI 选角屏的 5 阶段状态机（证据 login-flow-evidence.json::screens.parent.phase，
    // 阶段表 0x457778 = [0x4575F3, 0x457615, 0x457604, 0x4576FA, 0x45773C]）：
    //   0 = 4 按钮角色列表（创建角色/删除角色/开始游戏/结束）
    //   1 = 创建角色中（+0x780 泵，CreateChr.dat）
    //   2 = 动画角色列表 + **5 底部按钮 F92/F95/F98/F86/F89** + 密码框
    //   3 = 等待（F89 0x459D48 与服务端 case 0x209 写入）
    //   4 = 进游戏（服务端 case 0x20D 写入）
    // **按钮是分阶段显示的**：phase 0 显示那 4 个、phase 2 显示 F92/95/98/86/89。
    private int _selectPhase;
    private DXButton _skinConfigButton;
    private ConfigDialog _selectConfig;
    // EI 两个角色槽的屏幕位置。原版以 640x480 中心 (320,240) 为基准
    // （0x4570D0 的 X 用 `delta*0.5 - 320.0`、Y 用 `240.0 - ...`），
    // 槽位在拱门左右；具体 X 偏移在静态证据里未给出，此处按 F50 背景构图取
    // 拱门两侧（**推导值**，非原版常量）。
    private const int Slot0X = 205;
    private const int Slot1X = 435;
    private const int SlotY = 250;

    private DXTextInput _skinName;
    private DXTextInput _skinCreateName;
    private DXButton _skinStart, _skinCreate, _skinDelete;
    // EI 选角屏（640x480）的其余原版按钮与背景。
    private DXButton _skinExit, _skinConfirmYes, _skinConfirmNo, _skinIconWeapon, _skinIconFace, _skinIconScroll;
    private DXImageControl _selectBackground;
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

    private static SelectScene _activeInstance;

    public override void _EnterTree()
    {
        if (_activeInstance != null && IsInstanceValid(_activeInstance))
        {
            GD.Print("[Select] 丢弃重复 SelectScene 实例");
            QueueFree();
            return;
        }
        _activeInstance = this;
    }

    public override void _Ready()
    {
        ClientSettings.Load();
        ClientSettings.ApplyDisplaySettings();
        ClientSettings.UpdateWindowTitle();
        ClientSettings.BindWindowTitle(GetViewport());
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
            // 选角屏也要收公告：原版在进入游戏前用 F602 公告框拦住流程。
            _net.Connection.ChatEvent += OnSelectChat;
            _unsubscribers.Add(() => _net.Connection.ChatEvent -= OnSelectChat);
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
        if (ReferenceEquals(_activeInstance, this)) _activeInstance = null;
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
    private bool _gameTransitionStarted;
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
        // EI 证据 0x4591D8-0x459240：服务端 case **0x209** 分支的三条路是
        //   无角色   -> 提示「请先建立至少一个角色才能进行游戏.」(0x47D818)
        //   认证失效 -> 提示「服务器认证已不可用,请重新登录.」(0x47D7F8)
        //   否则     -> 设 phase=3 + 播 SelChr.wav(0x459240)
        // 即 0x209 是**角色列表/认证响应**，`SelChr.wav` 在**拿到角色列表时**播。
        // 我方对应"登录成功 -> 注入角色列表 -> 进 SelectScene"这一刻，即本方法。
        if (AutoLoginArgs.LegacyUi) SoundPlayback.Play(this, SoundIndex.LegacySelChr);
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
        // EI 模式：**不建列表面板行**，改为把角色渲染到 F50 洞窟里的 2 个槽位。
        // 原版选角屏没有居中面板，角色是直接站在背景中的 3D 模型
        // （槽 base +0xCB8，stride 0x40，idx 0..1）。
        if (AutoLoginArgs.LegacyUi)
        {
            UpdateCaveSlots();
        }
        else if (_skinPanel != null)
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
        // EI 模式没有列表行，选中态体现在洞窟槽的名称标签上
        // （原版选中态是槽字段 +0x1168，详情由 0x458150 渲染）。
        if (AutoLoginArgs.LegacyUi)
        {
            var sel = new Color(1f, .92f, .6f);
            var dim = new Color(.62f, .58f, .48f);
            if (_slotName0 != null) _slotName0.TextColour = index == 0 ? sel : dim;
            if (_slotName1 != null) _slotName1.TextColour = index == 1 ? sel : dim;
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

        // **帧基址按反汇编证据重写**。旧表是猜测值：8 组里有 5 组（240/300/940/1240/
        // 1440/1740）在 Interface1c.wil 里落在**空帧**上，导致部分职业/性别的角色
        // 在选角屏渲染不出来。
        //
        // 证据 1（索引公式）：0x458EC0(arg1, arg2, flags) 计算
        //     index = (arg1 + arg2*2) * 5 + flags，要求 index < 30、flags < 5，
        //   即 arg1 + arg2*2 ∈ 0..5 —— 正是「职业 + 2*性别」的 6 种组合。
        //   （arg1/arg2 来自角色槽 [+4]/[+5]，由 0x458B20 传入。）
        // 证据 2（块识别）：逐帧渲染 Interface1c 候选块，真角色块只有 6 个，
        //   外观顺序与上面的组合顺序一致：
        //     0 战士男 = 440（铠甲+剑男性）   连续有效 18 帧
        //     1 战士女 = 740（红衣+剑女性）   连续有效 16 帧
        //     2 法师男 = 1040（红袍+帽男性）  连续有效 15 帧
        //     3 法师女 = 1340（红衣+法杖女性）连续有效 17 帧
        //     4 道士男 = 1640（白衣+剑男性）  连续有效 17 帧
        //     5 道士女 = 1940（绿衣+剑女性）  连续有效 15 帧
        //   其余候选块是技能特效（840/900/1080 火球、1202 光效、1260 雷电、1860 冰）。
        //
        // 帧数用**实测连续有效帧数**，不用旧表的猜测值；旧表的 intro/idle 分段
        // 在原版没有对应物（原版每块是一段连续动画，帧数由 anim 对象首字给出）。
        int baseFrame = (info.Class, info.Gender) switch
        {
            (MirClass.Warrior, MirGender.Male) => 440,
            (MirClass.Warrior, MirGender.Female) => 740,
            (MirClass.Wizard, MirGender.Male) => 1040,
            (MirClass.Wizard, MirGender.Female) => 1340,
            (MirClass.Taoist, MirGender.Male) => 1640,
            _ => 1940,   // 道士女；其余（含刺客）本库无对应块，退回女性角色帧
        };
        int baseFrames = baseFrame switch
        {
            440 => 18, 740 => 16, 1040 => 15, 1340 => 17, 1640 => 17, _ => 15,
        };
        GD.Print($"[LegacySelect] 角色帧: class={info.Class} gender={info.Gender} "
            + $"base={baseFrame} frames={baseFrames}");

        _characterAnimation.BaseIndex = baseFrame;
        _characterAnimation.FrameCount = baseFrames;
        _characterAnimation.AnimationDelay = TimeSpan.FromMilliseconds(2400);
        _characterAnimation.Loop = true;
        _characterAnimation.Restart(false);
    }

    /// <summary>
    /// 把已选中的角色列表渲染到洞窟里的 **2 个槽位**（EI 结构）。
    /// 每槽：角色动画（按职业/性别取 Interface1c 角色块）+ 槽下名称标签；
    /// 槽位命中区不可见（图形由角色本身承担）。
    /// 位置与偏移见 Slot0X/Slot1X/SlotY 的注释（**推导值**）。
    /// </summary>
    private void UpdateCaveSlots()
    {
        var slots = new (DXAnimatedControl anim, DXLabel label, DXControl hit, int x)[]
        {
            (_characterAnimation,  _slotName0, _slotHit0, Slot0X),
            (_characterAnimation2, _slotName1, _slotHit1, Slot1X),
        };
        for (int i = 0; i < slots.Length; i++)
        {
            var (anim, label, hit, x) = slots[i];
            bool has = i < _characters.Count;
            if (anim != null) anim.Visible = has;
            if (label != null) label.Visible = has;
            if (hit != null) hit.Visible = has;
            if (!has) continue;

            var c = _characters[i];
            int baseFrame = CharacterBaseFrame(c.Class, c.Gender);
            int frames = CharacterFrameCount(baseFrame);
            if (anim != null)
            {
                anim.BaseIndex = baseFrame;
                anim.FrameCount = frames;
                anim.AnimationDelay = TimeSpan.FromMilliseconds(2400);
                anim.Loop = true;
                anim.Location = new Vector2I(x, SlotY);
                anim.Restart(false);
            }
            if (label != null)
            {
                label.Text = $"{c.CharacterName}  Lv{c.Level} {c.Class.Local()}";
                label.Location = new Vector2I(x - 80, SlotY + 6);
            }
            if (hit != null) hit.Location = new Vector2I(x - 60, SlotY - 190);
        }
        GD.Print($"[LegacySelect] 洞窟槽位: 角色数={_characters.Count} "
            + $"slot0=({Slot0X},{SlotY}) slot1=({Slot1X},{SlotY})");
    }

    /// <summary>EI 角色帧基址：index = 职业 + 2*性别（反汇编 0x458EC0 的索引公式）。</summary>
    private static int CharacterBaseFrame(MirClass cls, MirGender gender)
    {
        int index = (int)cls + 2 * (int)gender;
        return index switch
        {
            0 => 440,   // 战士男
            1 => 740,   // 战士女
            2 => 1040,  // 法师男
            3 => 1340,  // 法师女
            4 => 1640,  // 道士男
            _ => 1940,  // 道士女（刺客本库无对应块，退回女性帧）
        };
    }

    /// <summary>实测连续有效帧数（见审计文档的角色块识别表）。</summary>
    private static int CharacterFrameCount(int baseFrame) => baseFrame switch
    {
        440 => 18, 740 => 16, 1040 => 15, 1340 => 17, 1640 => 17, _ => 15,
    };

    /// <summary>
    /// 切换选角屏阶段并按阶段显示对应按钮组（EI 是**分阶段换按钮**的，不是全部同时可见）。
    /// 证据：phase 0 用 4 按钮（+0x9E8/+0xA9C/+0xB50/+0xC04），
    ///       phase 2 用 5 按钮（+0xD38/+0xDEC/+0xEA0/+0xF54/+0x1008 = F92/F95/F98/F86/F89）。
    /// </summary>
    public void SetSelectPhase(int phase)
    {
        _selectPhase = phase;
        bool p0 = phase == 0;
        bool p2 = phase == 2;
        if (_skinCreate != null) _skinCreate.Visible = p0;
        if (_skinDelete != null) _skinDelete.Visible = p0;
        if (_skinStart != null) _skinStart.Visible = p0;
        if (_skinExit != null) _skinExit.Visible = p0;
        foreach (var b in new[] { _skinConfirmYes, _skinConfirmNo, _skinIconWeapon, _skinIconFace, _skinIconScroll })
            if (b != null) b.Visible = p2;
        GD.Print($"[LegacySelect] phase={phase} (0=列表/1=创建中/2=动画列表/3=等待/4=进游戏)");
    }

    /// <summary>
    /// 选角屏的**全屏过场动画**（640x480）。两段都来自 EI：
    ///   `CreateChr.dat` 39 帧  -> phase 1「创建角色中」的镜头移动过场
    ///   `StartGame.dat` 41 帧  -> 进入游戏前的过场（后段淡入黑）
    /// 两者都是 Intel Indeo 5.0 AVI，Godot 不能解码，
    /// 由 `Tools/convert_legacy_login_video.sh` 转成同名 .ogv。
    /// </summary>
    private void PlayLegacyTransition(string name)
    {
        var path = System.IO.Path.Combine(MirSkin.UiDataPath, name + ".ogv");
        if (!System.IO.File.Exists(path))
        {
            GD.PrintErr($"[LegacySelect] 缺少过场视频 {path}，"
                + "请运行 Tools/convert_legacy_login_video.sh");
            return;
        }
        var video = new VideoStreamPlayer
        {
            Stream = new VideoStreamTheora { File = path },
            Position = Vector2.Zero,
            Size = new Vector2(640, 480),
            Loop = false,
            VolumeDb = -80f,
        };
        _uiLayer.AddChild(video);
        video.Play();
        video.Finished += () => { if (IsInstanceValid(video)) video.QueueFree(); };
        GD.Print($"[LegacySelect] 过场动画 {name}.ogv 开始播放");
    }

    private void HideCreateCharacterPanel()
    {
        if (_skinCreatePanel != null) _skinCreatePanel.Visible = false;
        if (AutoLoginArgs.LegacyUi) SetSelectPhase(0);
        else if (_skinPanel != null) _skinPanel.Visible = true;
        if (_characterAnimation != null) _characterAnimation.Visible = true;
    }

    private void ShowCreateCharacterPanel()
    {
        if (_skinPanel != null) _skinPanel.Visible = !AutoLoginArgs.LegacyUi;
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
        // **背景必须按 EI 原生 640x480 绘制在左上**，不能拉伸到 1024x768。
        // 证据 login-flow-evidence.json::screens.parent.background：
        //   Interface1c.wil **F50**，w=640 h=480。
        // 旧实现把 640x480 的图拉伸铺满 1024x768 并居中，正是审计文档 PRE-04 记录的
        // 「贴图按原生 640x480 绘制在左上」不符。
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 50,
            FixedSize = true,
            Size = new Vector2I(640, 480),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
        };
        _selectBackground = background;
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

        // **移除左右光晕动画**：原实现用 Interface1c BaseIndex 2800/2900 各 17 帧，
        // 但独立解码该库确认 **F2800..F2816 与 F2900..F2916 全是空帧**（alpha 全零），
        // 等于在选角屏上叠两个不可见控件。EI 此屏也没有这两个光晕。

        _characterAnimation = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            FrameCount = 1,
            AnimationDelay = TimeSpan.FromMilliseconds(1),
            UseOffSet = true,
            // EI 以 **640x480 屏幕中心 (320,240)** 为基准定位角色：
            // 0x4570D0 里 X 用 `[edi] += delta*0.5 - 320.0`、Y 用 `240.0 - ...`
            // （常量 0x476394=240.0、0x476398=320.0，实测 float）。
            // 旧值 (450,200) 无证据来源。
            Location = new Vector2I(320, 240),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(_characterAnimation);
        _characterOverlay1 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(320, 240), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _characterOverlay2 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(320, 240), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        background.AddControl(_characterOverlay1);
        background.AddControl(_characterOverlay2);

        // 第 2 个角色槽（EI 有 2 槽）。两个槽分列洞窟拱门左右。
        _characterAnimation2 = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            FrameCount = 1,
            AnimationDelay = TimeSpan.FromMilliseconds(1),
            UseOffSet = true,
            Location = new Vector2I(320, 240),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(_characterAnimation2);

        // 每槽一个不可见命中区 + 一个名称标签（原版把名字画在角色附近）。
        _slotHit0 = new DXControl { Size = new Vector2I(120, 200), Location = new Vector2I(Slot0X - 60, SlotY - 190) };
        _slotHit1 = new DXControl { Size = new Vector2I(120, 200), Location = new Vector2I(Slot1X - 60, SlotY - 190) };
        _slotHit0.MouseClick += (o, e) => SelectSkinCharacter(0);
        _slotHit1.MouseClick += (o, e) => SelectSkinCharacter(1);
        background.AddControl(_slotHit0);
        background.AddControl(_slotHit1);
        _slotName0 = new DXLabel { FontSize = 9, TextColour = new Color(1f, .92f, .6f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, Size = new Vector2I(160, 16), Location = new Vector2I(Slot0X - 80, SlotY + 6), IsControl = false };
        _slotName1 = new DXLabel { FontSize = 9, TextColour = new Color(1f, .92f, .6f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, Size = new Vector2I(160, 16), Location = new Vector2I(Slot1X - 80, SlotY + 6), IsControl = false };
        background.AddControl(_slotName0);
        background.AddControl(_slotName1);

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
        // 原版 F51（创建角色）证据写入者 0x459AC5 把 phase 写成 1（创建中），
        // 再由 0x45763D 转到 phase 2。我方创建面板是自制的，这里只做阶段推进。
        _skinCreate.MouseClick += (o, e) =>
        {
            if (_characters.Count >= 4) return;
            SetSelectPhase(1);
            // 原版 phase 1（0x457615）载入 CreateChr.dat 到 +0x780 并 pump。
            if (AutoLoginArgs.LegacyUi)
            {
                PlayLegacyTransition("CreateChr");
                // 证据 0x459AB6（紧邻 F51 处理器 0x459AC5）读 +0x113C = CreateChr.wav
                // -> 点「创建角色」时播一次性音效。
                SoundPlayback.Play(this, SoundIndex.LegacyCreateChr);
            }
            ShowCreateCharacterPanel();
        };
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
        if (AutoLoginArgs.LegacyUi)
        {
            ApplyLegacyEiSelectLayout();
            // 验证用：--legacy-phase2 直接进入 phase 2（动画列表 + 5 个图形钮），
            // 便于截图核对 phase 2 的贴图/hover，不需要真走一遍创建流程。
            bool forceP2 = false;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--legacy-phase2") forceP2 = true;
            SetSelectPhase(forceP2 ? 2 : 0);
        }
        GetNode<Control>("VBox").Visible = false;
    }

    /// <summary>
    /// EI 原版选角屏（`login-flow-evidence.json::screens.parent`，screen obj 0x8A7140，
    /// ctor 0x456CB0）。**640x480 基准**，背景 F50，控件散点摆放、没有居中面板：
    ///
    ///   +0x9E8  F51  @ **(440, 93)**  96x26  「创建角色」 (create)
    ///   +0xA9C  F53  @ **(79, 243)**  96x26  「删除角色」 (evidence 标 unlabeled，实为删除)
    ///   +0xB50  F55  @ **(259, 49)**  96x24  「开始游戏」 (enter game)
    ///   +0xC04  F57  @ **(28, 438)**  48x26  「结束」     (exit -> WM_DESTROY)
    ///   +0xF54  F86  @ **(450, 444)** 28x28  ✔ 确认
    ///   +0x1008 F89  @ **(491, 444)** 28x28  ✘ 取消     (confirm -> phase 3)
    ///   +0xD38  F92  @ **(266, 419)** 40x38  武器图标圆钮
    ///   +0xDEC  F95  @ **(308, 419)** 40x38  人脸图标圆钮
    ///   +0xEA0  F98  @ **(352, 419)** 40x38  卷轴图标圆钮
    ///
    /// 帧内含文字/图形，故这些按钮清空 Text。原版角色槽是 2 个（base +0xCB8/+0x10BC，
    /// stride 0x40，idx 0..1），渲染链另列。
    /// </summary>
    private void ApplyLegacyEiSelectLayout()
    {
        // **先把三个主按钮从 _skinPanel 摘到 _uiLayer，再设坐标** ——
        // 顺序不能反：Reparent 默认**保留全局变换**，会把局部坐标换算成补偿值
        // （实测先设坐标再 reparent，create 从 (440,93) 变成 (792,264)）。
        // 它们是面板的子控件（`_skinPanel.AddControl(...)`），不摘出来就会被
        // 后面隐藏面板时一起藏掉（与登录页那次同一个坑）。
        // 用 Reparent 而不是 RemoveChild+AddChild：RemoveChild 是延迟移除，
        // 紧接着 AddChild 会失败（实测按钮 parent 仍是 DXControl、屏幕上不出现）。
        foreach (var b in new[] { _skinStart, _skinCreate, _skinDelete })
        {
            if (b == null || b.GetParent() == _uiLayer) continue;
            b.Reparent(_uiLayer);
        }

        // 主按钮：改成 Interface1c 文字精灵帧（帧内含字）。
        SkinSelectButton(_skinCreate, 51, 52, new Vector2I(440, 93), new Vector2I(96, 26));
        SkinSelectButton(_skinDelete, 53, 54, new Vector2I(79, 243), new Vector2I(96, 26));
        SkinSelectButton(_skinStart, 55, 56, new Vector2I(259, 49), new Vector2I(96, 24));

        // 「结束」按钮（原实现没有）。
        _skinExit = new DXButton { LibraryFile = LibraryFile.Interface1c, Index = 57, HoverIndex = 58,
            PressedIndex = 58, FixedSize = true, Size = new Vector2I(48, 26), Location = new Vector2I(28, 438) };
        _skinExit.MouseClick += (o, e) => GetTree().Quit();
        _uiLayer.AddChild(_skinExit);

        // 右下两个圆形确认钮（✔ / ✘）。
        // phase 2 的 ✔(F86) / ✘(F89)：F89 的证据写入者是 0x459D48 -> phase 3 并
        // 发 msgid 0x64 '%s/%d'。F86 的门控读 0x459879/0x459A29（phase 相关），
        // 语义未闭合，暂接"开始游戏"。
        _skinConfirmYes = MakeSelectIconButton(86, new Vector2I(450, 444), () => OnStartPressed());
        _skinConfirmNo = MakeSelectIconButton(89, new Vector2I(491, 444), () => SetSelectPhase(3));
        // 三个圆形图标钮（武器/人脸/卷轴）。
        _skinIconWeapon = MakeSelectIconButton(92, new Vector2I(266, 419), () => { });
        _skinIconFace = MakeSelectIconButton(95, new Vector2I(308, 419), () => { });
        _skinIconScroll = MakeSelectIconButton(98, new Vector2I(352, 419), () => { });

        // **EI 此屏没有居中面板** —— 原 _skinPanel 是自制列表容器（320x425 带窗口框）。
        // 角色改由洞窟里的 2 个槽位渲染（见 UpdateCaveSlots），面板整块隐藏，
        // 否则它会盖住 F50 的洞窟画面。
        if (_skinPanel != null) _skinPanel.Visible = false;
        GD.Print($"[LegacySelect] 按钮状态: create={_skinCreate?.Location}/{_skinCreate?.Size} vis={_skinCreate?.Visible} "
            + $"parent={_skinCreate?.GetParent()?.GetType().Name} delete={_skinDelete?.Location} start={_skinStart?.Location} "
            + $"panelVis={_skinPanel?.Visible}");
        GD.Print("[LegacySelect] EI 布局已应用: 背景 F50@(0,0) 640x480; "
            + "创建(440,93) 删除(79,243) 开始(259,49) 结束(28,438) "
            + "✔(450,444) ✘(491,444) 武器(266,419) 人脸(308,419) 卷轴(352,419)");
    }

    private DXButton MakeSelectIconButton(int frame, Vector2I location, Action action)
    {
        var size = MirSkin.GetSize(LibraryFile.Interface1c, frame);
        if (size == Vector2I.Zero) size = new Vector2I(28, 28);
        var button = new DXButton
        {
            // EI 证据：ctor 实参布局为 (ebx, f1, f2, X, Y, 0, 1, **hover**, 1)。
            // phase 2 的 hover 帧比 normal **小 1**（frame-1）：
            //   0x56(86)->hover 0x55(85)；0x59(89)->0x58(88)；
            //   0x5C(92)->0x5B(91)；0x5F(95)->0x5E(94)；0x62(98)->0x61(97)
            // 注意 phase 0 的方向相反（hover = normal+1，如 51->52），
            // 两组来自同一 ctor 但传入值不同，按各自实参照抄，不要统一处理。
            LibraryFile = LibraryFile.Interface1c, Index = frame,
            HoverIndex = frame - 1, PressedIndex = frame,
            FixedSize = true, Size = size, Location = location,
        };
        button.MouseClick += (o, e) => action();
        _uiLayer.AddChild(button);
        return button;
    }

    private static void SkinSelectButton(DXButton button, int normalFrame, int hoverFrame,
        Vector2I location, Vector2I size)
    {
        if (button == null) return;
        button.LibraryFile = LibraryFile.Interface1c;
        button.Index = normalFrame;
        button.HoverIndex = hoverFrame;
        button.PressedIndex = hoverFrame;
        button.FixedSize = true;
        button.Text = string.Empty;
        button.Location = location;
        button.Size = size;
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
            // EI 证据：创建流程完成时 `0x45763D mov byte [esi+0x930], 2`
            // —— **转 phase 2**（动画角色列表 + 5 底部按钮 F92/F95/F98/F86/F89 + 密码框）。
            // 我方此前创建成功后没有阶段推进。
            if (AutoLoginArgs.LegacyUi) SetSelectPhase(2);
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

    /// <summary>
    /// 选角屏的公告处理（EI 进入游戏前的 F602 公告框）。
    /// 证据边界：原版 F602 的**触发源**尚未闭合（审计文档 NOTICE-01 记录
    /// "当前还把公告聊天消息直接当作打开此窗的触发" 属我方猜测）。
    /// 此处按用户描述与现有网络链实现：收到 Announcement 类消息即弹 F602，
    /// 玩家确认后关闭；角色进入游戏仍由服务端的 StartGameResult 驱动。
    /// </summary>
    private void OnSelectChat(S.Chat p)
    {
        if (p == null) return;
        if (p.Type != MessageType.Announcement) return;
        if (!AutoLoginArgs.LegacyUi) return;
        ShowLegacyNotice(p.Text);
    }

    /// <summary>在选角屏弹出 F602 公告框（EI 进入游戏前的公告环节）。</summary>
    public void ShowLegacyNotice(string text)
    {
        _noticeDialog ??= new NoticeDialog();
        _noticeDialog.SetNotice(text);
        WindowManager.Open(_noticeDialog, _uiLayer);
        GD.Print($"[LegacySelect] 公告框 F602 已弹出: len={text?.Length ?? 0}");
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
        if (_gameTransitionStarted) return;
        if (_pendingStartResult == StartGameResult.Success)
        {
            _gameTransitionStarted = true;
            // EI phase **4 = 进游戏**，写入者是服务端 case **0x20D**
            // （login-flow-evidence.json::screens.parent.phase.writers）。
            // 对应我方 StartGameResult.Success。
            if (AutoLoginArgs.LegacyUi)
            {
                SetSelectPhase(4);
                // 原版 phase 4 = 进游戏，伴随 StartGame.dat 过场（后段淡入黑）。
                PlayLegacyTransition("StartGame");
                // 证据 0x459456（紧邻服务端 case 0x20D 处理器 0x459465）读 +0x1144
                // = StartGame.wav -> 进游戏时播一次性音效。
                SoundPlayback.Play(this, SoundIndex.LegacyStartGame);
            }
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
            // EI phase **3 = 等待**，写入者是服务端 case **0x209** 与 F89(0x459D48)。
            // 我方 StartGameResult.Delayed（冷却中、稍后重试）正是"等待"语义。
            if (AutoLoginArgs.LegacyUi) SetSelectPhase(3);
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
