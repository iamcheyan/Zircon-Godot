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
    // 洞窟槽位地面阴影：WIL 中每个角色块后有 +20 的阴影块（帧数与角色块一致），
    // 阴影帧 = 角色当前帧 + 20，在 _Process 里同步 Index。
    private DXImageControl _caveShadow0, _caveShadow1;
    private DXLabel _slotName0, _slotName1;
    private DXControl _slotHit0, _slotHit1;
    // 开始游戏过场结束后显示公告确认窗；公告文字目前允许为空。
    private CanvasLayer _legacyStartNoticeLayer;
    private ColorRect _legacyStartNoticeBackdrop;
    private NoticeDialog _legacyStartNoticeDialog;
    private string _pendingLegacyStartNotice = string.Empty;
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
    // ---- EI 原版槽位锚点（primary-bytes：msg 0x208 角色列表解析器 0x458FBD）----
    // 原版**不是**"按居中/脚底摆放"：槽位绘制用 (锚点 + 帧头 offsetX/offsetY)，
    // 尺寸取帧头 width/height（1:1 原生，无缩放）。见
    //   0x4578DC-0x4578F5  阴影：x = [slot+0x18] + 帧头 offsetX, y = [slot+0x1C] + offsetY
    //   0x45791C-0x457945  身体：同一表达式，尺寸取帧头 w/h
    //   0x458A70          命中框 = 同一表达式算出的 bbox（画到 [slot+0x28] 的 RECT）
    // 锚点常量（0x458FBD 解析循环）：
    //   0x459120  mov dword [ebx+0xCD0], 0x12C   ; 300 → 当前槽 X
    //   0x459149  mov dword [ebp+0xCD0], 0x0FA   ; 250 → 槽 0 X
    //   0x459158  mov dword [ebp+0xCD4], 0x0D2   ; 210 → 槽 0 Y
    //   0x45912E  mov dword [ebx+0xCD4], 0x0D2   ; 210 → 当前槽 Y
    //   0x45916E  mov dword [ebx+0xCD4], 0x0FA   ; 250 → 三号槽 Y（(350,250)，2 槽上限下不可达）
    // 槽 0 的 X 取值取决于 0x4590F8 读的局部 [esp+0x1C]：该局部在本函数内**先读后写**
    // （0x459112 才清零），故静态上无法排除 300。若两槽都取 300 则完全重叠
    // （0x459117 分支对 idx=0/1 都写"当前槽"），故取唯一非退化解 250/210 + 300/210。
    private const int Slot0AnchorX = 250;
    private const int Slot1AnchorX = 300;
    private const int SlotAnchorY = 210;
    private static readonly Vector2I Slot0Anchor = new(Slot0AnchorX, SlotAnchorY);
    private static readonly Vector2I Slot1Anchor = new(Slot1AnchorX, SlotAnchorY);
    // 槽位名称标签的水平中心（**移植版 UI 元素**，非原版坐标）：保持改动前的
    // 350/490 不变，避免把"锚点修正"扩散成标签布局改动。原版此屏没有逐槽标签。
    private const int Slot0LabelX = 350;
    private const int Slot1LabelX = 490;

    private DXTextInput _skinName;
    private DXTextInput _skinCreateName;
    private DXButton _skinStart, _skinCreate, _skinDelete;
    // EI 选角屏（640x480）的其余原版按钮与背景。
    private DXButton _skinExit, _skinConfirmYes, _skinConfirmNo, _skinClassWarrior, _skinClassWizard, _skinClassTaoist;

    // ---- 相位 BGM 计时（镜像 EI 的 +0x1160 / +0x1164 机制）----
    // 证据 0x4577A0（phase 0/3）与 0x457AB0（phase 2）同构：
    //   cmp eax, 0x3E8        ; 累计 > 1000 ms
    //   push <该阶段的 mp3 名>
    //   mov ecx, 0x8AB130 / call 0x45B390    ; 音频管理器
    //   mov dword [本阶段+0x1160], 0         ; 复位开关
    //   mov dword [本阶段+0x1164], 0         ; 复位累计
    // 语义 = **进入该相位后播一次该相位的 BGM**（开关复位表示"已播过"）。
    // 我方此前没有按相位跑的每帧更新，故本组字段与 _Process 是新增的承载点。
    private bool _phaseBgmArmed;
    private double _phaseBgmAccumMs;
    private DXImageControl _selectBackground;
    private DXButton _skinCreateConfirm, _skinCreateCancel;
    private DXNumberField _skinHairNumber;
    private DXCreatePreviewControl _createPreview;
    private DXLabel _selectedClassLabel, _selectedGenderLabel;

    // ---- EI phase 2「新建人物」原生构图（legacy 专用，见 BuildLegacyCreateLayer） ----
    private DXControl _legacyCreateLayer;
    private DXImageControl _legacyCreateStrip;        // Interface1c F82（半透明，alpha 50）
    private DXImageControl _legacyCreatePlate;        // Interface1c F81（名字牌）
    private DXImageControl _legacyCreateShadow0, _legacyCreateShadow1;
    private DXImageControl _legacyCreateBody0, _legacyCreateBody1;
    private DXControl _legacyCreateHit0, _legacyCreateHit1, _legacyCreatePlateHit;
    private DXTextInput _legacyCreateName;
    private int _legacyCreateSelected;                // 原版 [obj+0x1488]，0=男槽 1=女槽
    private int _legacyCreateClassIndex;              // 0=武士 1=法师 2=道士（两槽共用）
    private readonly int[] _legacyCreateFrame = new int[2];
    private readonly double[] _legacyCreateTimer = new double[2];
    private bool _legacyCreateSlotsReady;
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
        // EI 选角屏（mode 0 / mode 2）屏幕区 640×480；进游戏后由 GameScene 改回 800×600。
        if (AutoLoginArgs.LegacyUi) ClientSettings.ApplyLegacyPregameWindow(640, 480);
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
            // 暂存选角期间到达的公告，等 StartGame 过场结束后再显示。
            _net.Connection.ChatEvent += OnSelectChat;
            _unsubscribers.Add(() => _net.Connection.ChatEvent -= OnSelectChat);
        }

        RefreshList();

        // headless 自动测试: --auto-login / --user 时自动进游戏; --char 指定角色名
        // --stay-select：不自动进游戏（空账号仍会自动建角），供选角屏截图验证。
        if (AutoLoginArgs.AutoLogin)
        {
            var wantChar = AutoLoginArgs.Character;
            if (wantChar.Length > 0 && !AutoLoginArgs.StayInSelect)
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
            else if (!AutoLoginArgs.StayInSelect)
            {
                GD.Print($"[Select] 自动进入游戏, 角色: {_characters[0].CharacterName}");
                CallDeferred(nameof(AutoStartGame));
            }
            else
            {
                GD.Print("[Select] --stay-select: 角色列表就绪，停在选角屏验证");
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
        TickPhaseBgm(delta);
        // EI stage 2 两个预览人物的帧推进（variant 4、120ms/帧、选中槽动/未选中槽定格首帧）
        ProcessLegacyCreatePreview(delta);
        // 洞窟槽位几何同步（阴影帧 = 角色帧 + 20；命中框/标签 = 锚点 + 帧头 offset）
        SyncLegacySlotGeometry(_caveShadow0, _characterAnimation, _slotHit0, _slotName0, Slot0AnchorX, Slot0LabelX);
        SyncLegacySlotGeometry(_caveShadow1, _characterAnimation2, _slotHit1, _slotName1, Slot1AnchorX, Slot1LabelX);
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
        // **此处不播 SelChr**（我此前接在这里，是错的）：
        //   0x459220 读的 +0x1140 不是 SelChr；真正的 SelChr 证据在
        //   0x4577C0 -> push 0x47D624 = '.\Sound\SelChr.mp3'，
        //   那是 **phase 0/3 更新路径**里的 1000ms 周期音效（与 phase 2 的
        //   CreateChr.mp3 同构：同一对字段 +0x1160/+0x1164、同一门限 0x3E8、
        //   同一音频管理器 0x8AB130、同一复位方式）。
        // 我方目前没有"按 phase 跑每帧更新"的循环，故该音效**暂不播放**，
        // 待实现 phase 更新循环时再接（见审计文档 2026-09-27 音效归属表）。
        // 不在此处保留任何 SelChr 播放：位置错误比缺失更糟。
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
            if (AutoLoginArgs.LegacyUi) ApplyLegacySlotSelection(-1);
        }
        else if (AutoLoginArgs.LegacyUi)
        {
            // 原版列表刷新后**不默认选中任何角色**：0x458FCC-0x458FD0 把 [0x1168] 写成
            // 0xFFFFFFFF，0x458310 见 -1 直接返回（不画详情）。选中只能由槽位点击产生
            // （0x4598BF 命中槽 → [0x1168]=idx）。此前移植版自动选中槽 0，属偏差。
            _statusLabel.Text = Lang.SelectCharacterLabel4;
            ApplyLegacySlotSelection(-1);
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
        // EI 模式没有列表行，选中态体现在洞窟槽的名称标签上 + 两槽的动画段
        // （原版选中态是槽字段 +0x1168，命中处理 0x4598BF-0x459902）。
        if (AutoLoginArgs.LegacyUi)
        {
            if (_legacySelectedIndex != index)
            {
                // 命中槽：0x4598C7 0x458B20(idx,1) → 重播 variant 1 → 播完 loop variant 2
                StartLegacySlotAnimation(LegacySlotAnim(index), _characters[index],
                    LegacySlotIntroVariant, LegacySlotIdleVariant, index);
                int other = index == 0 ? 1 : 0;
                // 另一个"已占用"的槽：0x4598DF/0x4598EF 0x458B20(other,3)
                // → 播 variant 3 → 播完 loop variant 0（站立待机）
                if (other < _characters.Count && LegacySlotAnim(other) != null)
                    StartLegacySlotAnimation(LegacySlotAnim(other), _characters[other], 3, 0, other);
            }
            _legacySelectedIndex = index;
            ApplyLegacySlotSelection(index);
            // 不调用 UpdateCharacterDisplay —— 那会把 _characterAnimation（= 槽 0）
            // 拽到屏幕中心 (320,240) 且 Loop=false，选中后槽 0 角色就离开洞窟、
            // 停止动画，与原版"角色常驻槽位"不符。
            return;
        }
        UpdateCharacterDisplay(_characters[index]);
    }

    private int _legacySelectedIndex = -1;

    /// <summary>槽 idx 的角色动画控件（0 = _characterAnimation，1 = _characterAnimation2）。</summary>
    private DXAnimatedControl LegacySlotAnim(int slot) => slot == 0 ? _characterAnimation : _characterAnimation2;

    /// <summary>
    /// 选角槽位选中态（原版 = 槽字段 +0x1168）。列表刷新时初值为 -1（未选中，
    /// 0x458FCC），选中只能由槽位点击产生（0x4598BF），点击只改选中态与动画段，
    /// 不触发进游戏/建角/删角。这里把它映射到移植版的两条名称标签颜色 +
    /// 开始/删除按钮可用性（原版对应 0x458310 的详情块与 F55 的 sel 门）。
    /// </summary>
    private void ApplyLegacySlotSelection(int index)
    {
        var sel = new Color(1f, .92f, .6f);
        var dim = new Color(.62f, .58f, .48f);
        if (_slotName0 != null) _slotName0.TextColour = index == 0 ? sel : dim;
        if (_slotName1 != null) _slotName1.TextColour = index == 1 ? sel : dim;
        bool has = index >= 0 && index < _characters.Count;
        if (_skinStart != null) _skinStart.Enabled = has;
        if (_skinDelete != null) _skinDelete.Enabled = has;
        GD.Print($"[LegacySelect] 槽位选中: index={index} 开始={_skinStart?.Enabled} "
            + $"删除={_skinDelete?.Enabled}（原版 [0x1168] 语义，点击只改选中态）");
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
        //   即 arg1 + arg2*2 ∈ 0..5。arg1=性别(0/1)、arg2=职业(0..2)，
        //   即组合编号 = 职业*2 + 性别，与 WIL 块顺序一致（逐帧解码验证：
        //   奇数块为女性、偶数块为男性）。
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
            (MirClass.Taoist, MirGender.Female) => 1940,
            // **原版只有三职业（武士/法师/道士）**，故这里显式列全 3×2=6 种组合。
            // 我方移植版还有其它职业（如 Assassin），但**不在 legacy 选角屏的契约内**，
            // 按用户决定"多余职业直接无视"，此处不做其映射；若被传进来属越界调用，
            // 退回战士男帧（而非静默套用某职业的帧）。
            _ => 440,
        };
        int baseFrames = baseFrame switch
        {
            440 => 18, 740 => 16, 1040 => 15, 1340 => 17, 1640 => 17, 1940 => 15, _ => 15,
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
    /// 把已选中的角色列表渲染到洞窟里的 **2 个槽位**（EI phase 0 角色列表）。
    /// 每槽：地面阴影（帧 = 角色帧 + 20）+ 角色动画 + 槽位名称标签 + 命中区。
    ///
    /// 帧块 / 时序 / 坐标全部来自 primary-bytes：
    ///   • 帧块 = 父对象 0x8A7140 +0x932 的 30 条 6 字节记录（0x458BB0..0x458EB8 用
    ///     0x449C50 逐条写入；0x458EC0 查表）：{firstFrame, lastFrame, frameMs}，
    ///     frameMs 30 条全为 0x78 = 120；索引 = (gender + class*2)*5 + variant。
    ///   • phase 0 用 variant 1（解析器 0x459195 → 0x458B20(slot,1)），播完由状态机
    ///     0x45786F 转 variant 2 并无限循环（0x458B20 会把帧复位到记录首帧）。
    ///   • 每帧 120ms（记录第 3 字），不是"整段总时长"。
    ///   • 坐标 = 槽锚点 + 帧头 offsetX/offsetY，尺寸取帧头 w/h（1:1，无缩放）。
    /// </summary>
    private void UpdateCaveSlots()
    {
        var slots = new (DXAnimatedControl anim, DXImageControl shadow, DXLabel label, DXControl hit, int anchorX)[]
        {
            (_characterAnimation,  _caveShadow0, _slotName0, _slotHit0, Slot0AnchorX),
            (_characterAnimation2, _caveShadow1, _slotName1, _slotHit1, Slot1AnchorX),
        };
        for (int i = 0; i < slots.Length; i++)
        {
            var (anim, shadow, label, hit, anchorX) = slots[i];
            bool has = i < _characters.Count;
            if (anim != null) anim.Visible = has;
            if (shadow != null) shadow.Visible = has;
            if (label != null) label.Visible = has;
            if (hit != null) hit.Visible = has;
            if (!has) continue;

            var c = _characters[i];
            if (anim != null)
            {
                // 解析器 0x459195 对两个槽都传 variant 1（0x459112 清零后两分支均置 1）
                StartLegacySlotAnimation(anim, c, LegacySlotIntroVariant, LegacySlotIdleVariant, i);
            }
            if (label != null)
                label.Text = $"{c.CharacterName}  Lv{c.Level} {c.Class.Local()}";
        }
        SyncLegacySlotGeometry(_caveShadow0, _characterAnimation, _slotHit0, _slotName0, Slot0AnchorX, Slot0LabelX);
        SyncLegacySlotGeometry(_caveShadow1, _characterAnimation2, _slotHit1, _slotName1, Slot1AnchorX, Slot1LabelX);
        // **phase 2（新建人物）不画洞窟槽位**：原版 stage 2 的 tick 只画创建界面
        // （背景换成 F80 + 2 个预览槽 + F82/F81 + 5 按钮），不画列表槽位。
        if (_legacyCreateLayer is { Visible: true }) SetLegacyCaveSlotsVisible(false);
        GD.Print($"[LegacySelect] 洞窟槽位: 角色数={_characters.Count} "
            + $"锚点 slot0=({Slot0AnchorX},{SlotAnchorY}) slot1=({Slot1AnchorX},{SlotAnchorY}) "
            + $"帧时长={LegacyFrameMs}ms variant={LegacySlotIntroVariant}→{LegacySlotIdleVariant}");
    }

    /// <summary>洞窟槽位（角色/阴影/命中区/名称标签）整体显隐（legacy 相位切换用）。</summary>
    private void SetLegacyCaveSlotsVisible(bool visible)
    {
        foreach (var ctl in new DXControl[]
                 {
                     _characterAnimation, _caveShadow0, _slotHit0, _slotName0,
                     _characterAnimation2, _caveShadow1, _slotHit1, _slotName1,
                 })
            if (ctl != null) ctl.Visible = visible;
    }

    /// <summary>
    /// 逐帧同步槽位的 bbox 派生几何：阴影帧号（= 角色帧 + 20）、命中框、名称标签。
    /// 阴影/命中框用**同一表达式**：左上 = 锚点 + 帧头 offset，尺寸 = 帧头 w/h
    /// （原版 0x4578B0-0x4578F5 绘制，0x458A70 命中框 = 同一 bbox 写进 RECT）。
    /// 旧实现用 UseOffSet=false 并把阴影按帧宽重新居中，会与身体（各自 offset 不同）错位。
    /// 标签 X 保持改动前的位置常量（labelX）；原版没有逐槽名称标签——选中角色的详情是
    /// 0x458150 在 0x466800 算出的 rect 上画的三行文本（0x96C8FF/0x96F096/0xAFFFFF），
    /// 该 rect 坐标本移植版未闭合，故沿用既有标签位置，只让 Y 随当前帧顶边走。
    /// </summary>
    private static void SyncLegacySlotGeometry(DXImageControl shadow, DXAnimatedControl anim,
        DXControl hit, DXLabel label, int anchorX, int labelX)
    {
        if (anim == null || !anim.Visible) return;
        int idx = anim.Index;
        Vector2I off = MirSkin.GetOffset(LibraryFile.Interface1c, idx);
        Vector2I size = MirSkin.GetSize(LibraryFile.Interface1c, idx);
        if (shadow != null && shadow.Visible)
        {
            int shadowIndex = idx + 20;   // 阴影块 = 角色块 + 20
            if (shadowIndex != shadow.Index) shadow.Index = shadowIndex;
        }
        if (hit != null && hit.Visible)
        {
            hit.Location = new Vector2I(anchorX + off.X, SlotAnchorY + off.Y);
            hit.Size = size;
        }
        if (label != null && label.Visible)
            label.Location = new Vector2I(labelX - 80, SlotAnchorY + off.Y - 18);
    }
    /// <summary>
    /// EI 原版选角帧表（primary-bytes：父对象 +0x932，30 条 6 字节记录，
    /// 由 0x458BB0..0x458EB8 逐条经 0x449C50 写入）。每项 = (首帧, 末帧)；
    /// 第 3 字（每帧毫秒）30 条全为 0x78 = 120。
    /// 索引 = (gender + class*2)*5 + variant（0x458EC0：idx=(arg1+arg2*2)*5+flags，
    /// 要求 0 ≤ idx &lt; 0x1E）。
    ///   class  = [slot+4]（0x458310 读 0xCBC 分派 0x47D6A8/B8/C8「职 业   武 士/法 师/道 士」）
    ///   gender = [slot+5]（0x458F80 解析器写入；phase 2 音效索引 [slot+5]*3+[slot+4] 同日键）
    /// 逐帧解码独立复核（wilsdk）：30 段首帧 = 200 + 300*group + 60*variant，
    /// 每段内全部为有效帧、首末帧与表值逐段吻合。
    /// </summary>
    private static readonly (int First, int Last)[] LegacySlotFrameTable =
    {
        (200, 210), (260, 279), (320, 331), (380, 387), (440, 457),           // 武士男 v0..v4
        (500, 510), (560, 570), (620, 630), (680, 691), (740, 755),           // 武士女
        (800, 810), (860, 871), (920, 930), (980, 990), (1040, 1054),         // 法师男
        (1100, 1110), (1160, 1177), (1220, 1230), (1280, 1288), (1340, 1356), // 法师女
        (1400, 1410), (1460, 1476), (1520, 1539), (1580, 1591), (1640, 1656), // 道士男
        (1700, 1710), (1760, 1776), (1820, 1830), (1880, 1889), (1940, 1954), // 道士女
    };
    private const int LegacyFrameMs = 120;        // 记录第 3 字（0x78），30 条全同值
    private const int LegacySlotIntroVariant = 1; // phase 0 解析器传入（0x459195）
    private const int LegacySlotIdleVariant = 2;  // 状态机 0x45786F 转到的循环段

    /// <summary>组合编号 = class*2 + gender（0..5）。原版只有 3 职业，越界职业退回武士男。</summary>
    private static int LegacyGroup(MirClass cls, MirGender gender)
    {
        int group = (int)cls * 2 + (int)gender;
        return group is >= 0 and < 6 ? group : 0;
    }

    private static (int First, int Last) LegacySlotBlock(MirClass cls, MirGender gender, int variant)
        => LegacySlotFrameTable[LegacyGroup(cls, gender) * 5 + ((variant % 5 + 5) % 5)];

    /// <summary>
    /// 让某槽按"intro 段播一次 → idle 段无限循环"跑。原版状态机
    /// （0x457835-0x457891）：帧计数超过记录末帧时，state==1 → 0x458B20(slot,2)，
    /// state==3 → 0x458B20(slot,0)，两者都会把帧复位到新段的记录首帧、计时清零。
    /// 槽位点击（0x4598C7/0x4598DF）正是用这个机制把"命中槽"重播 variant 1、
    /// 把另一个已占用槽切到 variant 3。
    /// </summary>
    private static void StartLegacySlotAnimation(DXAnimatedControl anim, SelectInfo c,
        int introVariant, int idleVariant, int slot)
    {
        if (anim == null) return;
        anim.ClearAnimationHandlers();
        ApplyLegacySlotVariant(anim, c, introVariant, loop: false);
        anim.AfterAnimation += (o, e) =>
        {
            ApplyLegacySlotVariant(anim, c, idleVariant, loop: true);
            GD.Print($"[LegacySelect] 槽 {slot} 动画: variant {introVariant} → {idleVariant}（循环）");
        };
    }

    /// <summary>
    /// 把槽位动画设到某个 variant 帧段。AnimationDelay = 帧数 × 120ms
    /// （DXAnimatedControl 的 AnimationDelay 是**整轮总时长**，见其 _Process 换算）。
    /// </summary>
    private static void ApplyLegacySlotVariant(DXAnimatedControl anim, SelectInfo c, int variant, bool loop)
    {
        var (first, last) = LegacySlotBlock(c.Class, c.Gender, variant);
        anim.BaseIndex = first;
        anim.FrameCount = last - first + 1;
        anim.AnimationDelay = TimeSpan.FromMilliseconds((last - first + 1) * (long)LegacyFrameMs);
        anim.Restart(loop);
    }

    /// <summary>
    /// 非 legacy（现代 UI）单角色展示块 = 原版 variant 4（大体型站立待机 250-268px；
    /// phase 2 创建预览用同一段，见 0x459DB6/0x459E42/0x459ECE 的 0x458B20(slot,4)）。
    /// </summary>
    private static int CharacterBaseFrame(MirClass cls, MirGender gender)
        => LegacySlotBlock(cls, gender, 4).First;

    /// <summary>帧数 = 末帧 - 首帧 + 1（同表）。</summary>
    private static int CharacterFrameCount(int baseFrame)
    {
        foreach (var (first, last) in LegacySlotFrameTable)
            if (first == baseFrame) return last - first + 1;
        return 15;
    }

    /// <summary>
    /// 切换选角屏阶段并按阶段显示对应按钮组（EI 是**分阶段换按钮**的，不是全部同时可见）。
    /// 证据：phase 0 用 4 按钮（+0x9E8/+0xA9C/+0xB50/+0xC04），
    ///       phase 2 用 5 按钮（+0xD38/+0xDEC/+0xEA0/+0xF54/+0x1008 = F92/F95/F98/F86/F89）。
    /// </summary>
    /// <summary>
    /// 选角屏 9 个按钮的**属性**自检（对应 EI 反汇编出的 ctor 实参）。
    /// 触发：--legacy-select-selftest。断言控件属性而非渲染像素 ——
    /// 像素差分受窗口缩放/视口原点干扰（本日已三次失败），属性断言不受影响。
    ///
    /// 期望值来自 0x456DBB-0x456EC2 各调用点实参，布局：
    ///   (ebx, f1, f2, X, Y, 0, 1, hover, 1)
    /// </summary>
    private void RunSelectButtonSelfTestIfRequested()
    {
        bool want = false;
        foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--legacy-select-selftest") want = true;
        if (!want) return;

        var expect = new (string name, DXButton btn, int idx, int hov, int x, int y, int w, int h, bool vis)[]
        {
            ("p0-1 创建角色", _skinCreate, 51, 52, 440, 93, 96, 26, true),
            ("p0-2 删除角色", _skinDelete, 53, 54, 79, 243, 96, 26, true),
            ("p0-3 开始游戏", _skinStart, 55, 56, 259, 49, 96, 24, true),
            ("p0-4 结束",     _skinExit,   57, 58, 28, 438, 48, 26, true),
            ("p2-1",          _skinClassWarrior, 92, 91, 266, 419, 40, 38, false),
            ("p2-2",          _skinClassWizard,   95, 94, 308, 419, 40, 38, false),
            ("p2-3",          _skinClassTaoist, 98, 97, 352, 419, 40, 38, false),
            // p2-4/p2-5 的帧 0x56(86)/0x59(89) 实测是 28x28（圆钮），
            // 而 p2-1/p2-2/p2-3 的 0x5C/0x5F/0x62 是 40x38。尺寸随帧走，不是统一值。
            ("p2-4",          _skinConfirmYes, 86, 85, 450, 444, 28, 28, false),
            ("p2-5",          _skinConfirmNo,  89, 88, 491, 444, 28, 28, false),
        };

        bool ok = true;
        var bad = new System.Collections.Generic.List<string>();
        foreach (var e in expect)
        {
            if (e.btn == null) { ok = false; bad.Add($"{e.name}: null"); continue; }
            var b = e.btn;
            bool m = b.Index == e.idx && b.HoverIndex == e.hov
                     && b.Location.X == e.x && b.Location.Y == e.y
                     && b.Size.X == e.w && b.Size.Y == e.h;
            if (!m)
            {
                ok = false;
                bad.Add($"{e.name}: got idx={b.Index} hov={b.HoverIndex} "
                        + $"loc={b.Location} size={b.Size} | want idx={e.idx} hov={e.hov} "
                        + $"loc=({e.x},{e.y}) size=({e.w},{e.h})");
            }
        }
        // --- 职业三钮的行为断言（帧 92/95/98 -> class Warrior/Wizard/Taoist）---
        // 依据：0x459DAE/0x459E19/0x459EA5 三格级联 + 0x458440(slot,gender,class,?,str)
        //       的 arg3 依次为 0/1/2；0x4584C0 随后刷新显示串。
        // 说明：三钮的实际动作通过 lambda 绑在 DXButton.MouseClick 上，事件无法从外部触发，
        //       故此处断言**动作的目标方法行为**（SelectCreateClass）与**帧号对应关系**，
        //       而不是断言 lambda 本身。这是本轮能做到的最强断言，如实标注。
        // arg3 是 **0/1/2**，而帧号是 **92/95/98** —— 两者**没有 frame-92 这种关系**
        // （95-92=3≠1、98-92=6≠2）。故此处用**显式表**记录 帧->class->arg3 的对应，
        // 不写公式。（首版我写成 frame-92，被本自检当场判 FAIL —— 断言的价值正在于此。）
        var classMap = new (string name, DXButton btn, int frame, MirClass cls, int arg3)[]
        {
            ("帧 92 -> 武士", _skinClassWarrior, 92, MirClass.Warrior, 0),
            ("帧 95 -> 法师", _skinClassWizard,  95, MirClass.Wizard,  1),
            ("帧 98 -> 道士", _skinClassTaoist,  98, MirClass.Taoist,  2),
        };
        foreach (var (name, btn, frame, cls, arg3) in classMap)
        {
            if (btn == null) { ok = false; bad.Add($"{name}: null"); continue; }
            if (btn.Index != frame) { ok = false; bad.Add($"{name}: 帧号 {btn.Index} != {frame}"); }
            // 行为断言：设置后 _skinCreateClass 应等于该职业，且与 MirClass 数值一致
            SelectCreateClass(cls);
            if (_skinCreateClass != cls)
            {
                ok = false;
                bad.Add($"{name}: SelectCreateClass({cls}) 后 _skinCreateClass={_skinCreateClass}");
            }
            if ((int)cls != arg3)   // 原版 0x458440 的 arg3 = 0/1/2
            {
                ok = false;
                bad.Add($"{name}: MirClass 数值 {(int)cls} 与原版 arg3 {arg3} 不一致");
            }
        }
        SelectCreateClass(MirClass.Warrior);   // 复位

        string details = ok
            ? "9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参；"
              + "职业三钮(帧92/95/98->Warrior/Wizard/Taoist)的帧号与 MirClass 数值均匹配"
            : string.Join(" ; ", bad);
        GD.Print($"[LegacySelectButtonSelfTest] {(ok ? "PASS" : "FAIL")} {details}");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// 相位 BGM 计时。EI 在相位更新函数里累计 delta，超过 1000 ms 才播该相位的 BGM
    /// 并复位开关（见字段注释处的反汇编）。这里用 _Process 做同样的事。
    /// </summary>
    private void TickPhaseBgm(double delta)
    {
        if (!_phaseBgmArmed) return;
        _phaseBgmAccumMs += delta * 1000.0;
        if (_phaseBgmAccumMs <= 1000.0) return;

        // 复位开关（原版 mov [..+0x1160], 0 与 mov [..+0x1164], 0）
        _phaseBgmArmed = false;
        _phaseBgmAccumMs = 0;

        // 相位 -> BGM（证据：0x4577C0 phase0/3 用 SelChr.mp3；0x457AE6 phase2 用 CreateChr.mp3）
        SoundIndex? bgm = _selectPhase switch
        {
            0 or 3 => SoundIndex.LegacySelChrBgm,
            2      => SoundIndex.LegacyCreateChrBgm,
            _      => null,
        };
        if (bgm == null) return;
        GD.Print($"[LegacySelect] 相位 BGM: phase={_selectPhase} -> {bgm}");
        SoundPlayback.Play(this, bgm.Value);
    }

    public void SetSelectPhase(int phase)
    {
        _selectPhase = phase;
        // 换相位即重新武装 BGM 计时（对应原版进入相位时把 +0x1160 置 1、+0x1164 清 0）
        _phaseBgmArmed = true;
        _phaseBgmAccumMs = 0;
        // **phase 3 原版不绘制任何控件**（0x4576FA 只泵过场视频后就回 phase 0），
        // DirectDraw 下屏幕保留上一相位的画面。故此处不改按钮/图层显隐与背景，
        // 避免出现"控件全消失的空背景"闪帧。
        // 注意：Zircon 协议下等待回包期间输入未加锁（原版 stage 3 的鼠标派发不动作）；
        // 主要影响是清空后的名字框使 F86 无效果。见 parity 文档 §11。
        if (phase == 3)
        {
            GD.Print($"[LegacySelect] phase={phase} (0=列表/1=创建中/2=动画列表/3=等待/4=进游戏) 保留上一相位画面");
            return;
        }
        bool p0 = phase == 0;
        bool p2 = phase == 2;
        if (_skinCreate != null) _skinCreate.Visible = p0;
        if (_skinDelete != null) _skinDelete.Visible = p0;
        if (_skinStart != null) _skinStart.Visible = p0;
        if (_skinExit != null) _skinExit.Visible = p0;
        foreach (var b in new[] { _skinConfirmYes, _skinConfirmNo, _skinClassWarrior, _skinClassWizard, _skinClassTaoist })
            if (b != null) b.Visible = p2;
        // **背景按阶段换帧**：原版 stage 0（列表）用 Interface1c **F50**（0x4577E1 push 0x32），
        // stage 2（创建）用 **F80**（0x457B09 push 0x50），两者都是 640x480 且像素明显不同
        // （F50 = 洞窟大厅俯视；F80 = 近景石壁 + 浮雕/石门，见 wilsdk 离线对照）。
        // 只在 0/2 切：phase 1/4 由过场视频覆盖。
        if (_selectBackground != null && (phase == 0 || phase == 2))
            _selectBackground.Index = phase == 2 ? 80 : 50;
        // EI phase 2 的两个人物预览/名牌/名字框只在创建相位显示。
        SetLegacyCreateLayerVisible(p2);
        GD.Print($"[LegacySelect] phase={phase} (0=列表/1=创建中/2=动画列表/3=等待/4=进游戏) "
            + $"背景F={(phase == 2 ? 80 : 50)}");
    }

    /// <summary>
    /// 选角屏的**全屏过场动画**（640x480）。两段都来自 EI：
    /// <summary>
    /// EI 过场：CreateChr.dat / StartGame.dat（640×480, 29.97fps, ~1.3s，39/41 帧）。
    /// - CreateChr: phase 1「创建中」泵播，播完才进 phase 2（显示编辑面板）。
    /// - StartGame: phase 4「进游戏」泵播，播完才切 GameScene。
    /// 两者都是 Intel Indeo 5.0 AVI，Godot 不能解码，
    /// 由 `Tools/convert_legacy_login_video.sh` 转成同名 .ogv。
    /// </summary>
    private void PlayLegacyTransition(string name, Action onFinished = null, bool attachToRoot = false)
    {
        var path = System.IO.Path.Combine(MirSkin.UiDataPath, name + ".ogv");
        if (!System.IO.File.Exists(path))
        {
            GD.PrintErr($"[LegacySelect] 缺少过场视频 {path}，"
                + "请运行 Tools/convert_legacy_login_video.sh");
            onFinished?.Invoke();
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
        // StartGame 过场要在 SelectScene 销毁后继续播放，因此挂到根 Viewport。
        // CreateChr 过场保留在 _uiLayer，随 SelectScene 生命周期即可。
        Node parent = attachToRoot ? (Node)GetTree().Root : _uiLayer;
        parent.AddChild(video);
        video.Play();
        video.Finished += () =>
        {
            if (IsInstanceValid(video)) video.QueueFree();
            GD.Print($"[LegacySelect] 过场 {name}.ogv 播放完毕");
            onFinished?.Invoke();
        };
        GD.Print($"[LegacySelect] 过场动画 {name}.ogv 开始播放 (attachToRoot={attachToRoot})");
    }

    private void HideCreateCharacterPanel()
    {
        if (_skinCreatePanel != null) _skinCreatePanel.Visible = false;
        if (AutoLoginArgs.LegacyUi)
        {
            SetLegacyCreateLayerVisible(false);
            SetSelectPhase(0);
            // 恢复洞窟槽位（原版 stage 3 的 tick 把 phase 写回 0 后，stage 0 重新画列表槽位）
            for (int i = 0; i < 2; i++)
            {
                bool has = i < _characters.Count;
                var anim = i == 0 ? _characterAnimation : _characterAnimation2;
                var shadow = i == 0 ? _caveShadow0 : _caveShadow1;
                var label = i == 0 ? _slotName0 : _slotName1;
                var hit = i == 0 ? _slotHit0 : _slotHit1;
                if (anim != null) anim.Visible = has;
                if (shadow != null) shadow.Visible = has;
                if (label != null) label.Visible = has;
                if (hit != null) hit.Visible = has;
            }
        }
        else if (_skinPanel != null) _skinPanel.Visible = true;
        if (_characterAnimation != null) _characterAnimation.Visible = true;
        if (_characterAnimation2 != null) _characterAnimation2.Visible = true;
    }

    private void ShowCreateCharacterPanel()
    {
        if (AutoLoginArgs.LegacyUi)
        {
            // **EI 原版新建人物界面没有窗口面板**：F50 洞窟保持可见，
            // 两只预览人物（+0x10BC 男 / +0x10FC 女，variant 4 段，锚点查 0x458910）、
            // 半透明条 F82、名字牌 F81 与 5 个图形钮直接摆在 640x480 画布上。
            // 覆盖整个 260x650 的 Zircon 新建人物对话框（它的职业/性别/发型/染色
            // 控件与 Program/Equip 合成预览在原版 phase 2 都没有对应物）。
            if (_skinPanel != null) _skinPanel.Visible = false;
            SetLegacyCaveSlotsVisible(false);
            InitLegacyCreateSlots();
            SetLegacyCreateLayerVisible(true);
            return;
        }
        if (_skinPanel != null) _skinPanel.Visible = true;
        if (_skinCreatePanel != null) _skinCreatePanel.Visible = true;
        if (_characterAnimation != null) _characterAnimation.Visible = false;
    }

    private void SelectCreateClass(MirClass value)
    {
        _skinCreateClass = value;
        if (AutoLoginArgs.LegacyUi)
        {
            // 原版 F92/F95/F98 处理器（0x459D1D/0x459E19/0x459EA5）把 class 写进
            // **两个**槽（0x458440(&+0x10bc, gender, class) 与 (&+0x10fc, gender, class)），
            // 随后各自 0x458B20(slot,4) 重装 → 从 variant 4 段首帧重播 + 命中框重算。
            _legacyCreateClassIndex = Mathf.Clamp((int)value, 0, 2);
            RebuildLegacyCreateSlots();
        }
        UpdateCreateButtonStates();
        UpdateCreatePreview();
    }

    private void SelectCreateGender(MirGender value)
    {
        _skinCreateGender = value;
        UpdateCreateButtonStates();
        UpdateCreatePreview();
    }

    /// <summary>当前生效的角色名输入框（legacy = EI 原版那个 (288,405) 75x13 的 EDIT）。</summary>
    private DXTextInput ActiveCreateNameField
        => AutoLoginArgs.LegacyUi ? _legacyCreateName : _skinCreateName;

    private void UpdateCreateButtonStates()
    {
        if (_skinCreateConfirm != null)
            _skinCreateConfirm.Enabled = !string.IsNullOrWhiteSpace(ActiveCreateNameField?.Text);

        // **只保留三职业的帧**（121/126/131 与 120/125/130）。
        // 原为 4 个元素（第 4 对 136/135 是给多余职业预留的），按"原版只有三职业"
        // 的决定删除，避免代码里出现第四个职业的位置。
        int[] normalClass = { 121, 126, 131 };
        int[] pressedClass = { 120, 125, 130 };
        for (int i = 0; i < _createClassButtons.Count && i < normalClass.Length; i++)
            _createClassButtons[i].Index = (int)_skinCreateClass == i ? pressedClass[i] : normalClass[i];
        for (int i = 0; i < _createGenderButtons.Count && i < 2; i++)
            _createGenderButtons[i].Index = (int)_skinCreateGender == i ? (i == 0 ? 115 : 110) : (i == 0 ? 116 : 111);
        if (_selectedClassLabel != null) _selectedClassLabel.Text = _skinCreateClass.Local();
        if (_selectedGenderLabel != null) _selectedGenderLabel.Text = _skinCreateGender.Local();
    }

    private void SubmitSkinCharacter()
    {
        var field = ActiveCreateNameField;
        string name = field?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;

        // 原版 F86 处理器（0x459F56-0x45A02A，悬停标题 0x47D64C「确认人物创建」）：
        //   GetWindowTextA(edit) → **先清空输入框** → 空则不动 → 长度 > 14 弹提示框
        //   → 0x4589B0 名字校验 → 通过则发送 msgid 0x65 '%s/%s/%d/%d/%d'
        //     (账号/名字/职业/性别/flag)。
        // 目标 WIL 名限制 14 字符（0x459FBE cmp ecx,0xe）。
        if (name.Length > 14)
        {
            _statusLabel.Text = Lang.SelectCharacterLabel9;
            GD.Print($"[LegacyCreate] 名字过长({name.Length})，按原版 0x459FBE 上限 14 拒绝");
            return;
        }
        if (field != null) field.Text = string.Empty;
        if (_skinCreateConfirm != null) _skinCreateConfirm.Enabled = false;
        _statusLabel.Text = Lang.SelectCreateLabel;
        // **原版提交后不改阶段**：F86 处理器（0x459F56-0x45A02A）清空输入框、发
        // `CM_NEWCHR 0x65` 后直接返回，界面留在 stage 2（清空的输入框本身即防重入）。
        // 阶段 3 由**服务器回包**写入：0x209(SM_NEWCHR_SUCCESS) → 0x459216 →
        // SelChr.wav + CreateChr.dat + phase=3。
        // 性别：原版取**选中预览槽**的 [+4]/[+5]（0x45A000/0x45A009；槽 0 = 男、槽 1 = 女），
        // 不是独立性别控件。legacy 下用 _legacyCreateSelected 映射。
        MirGender gender = AutoLoginArgs.LegacyUi
            ? (_legacyCreateSelected == 1 ? MirGender.Female : MirGender.Male)
            : _skinCreateGender;
        _net.Connection?.SendNewCharacter(name, _skinCreateClass, gender, _skinHairType, _skinHairColour, _skinArmourColour);
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

        // 洞窟槽位的地面阴影（Z 序在角色之下）：WIL 帧 = 角色帧 + 20。
        // **UseOffSet=true**：原版阴影绘制（0x4578DC-0x4578F5）与身体同构——
        // x = [slot+0x18] + 帧头 offsetX，y = [slot+0x1C] + 帧头 offsetY，尺寸取帧头。
        // 旧实现用 UseOffSet=false 并按帧宽重新居中，会与身体错位（两者 offset 不同）。
        // 位置固定为槽锚点，帧号在 _Process 里跟随角色当前帧。
        _caveShadow0 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = Slot0Anchor, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _caveShadow1 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = Slot1Anchor, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        background.AddControl(_caveShadow0);
        background.AddControl(_caveShadow1);

        _characterAnimation = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            FrameCount = 1,
            AnimationDelay = TimeSpan.FromMilliseconds(1),
            // 原版身体绘制 = 锚点 + 帧头 offsetX/offsetY（0x45791C-0x457945）。
            UseOffSet = true,
            Location = Slot0Anchor,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(_characterAnimation);
        _characterOverlay1 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(320, 240), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _characterOverlay2 = new DXImageControl { LibraryFile = LibraryFile.Interface1c, UseOffSet = true, Location = new Vector2I(320, 240), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        background.AddControl(_characterOverlay1);
        background.AddControl(_characterOverlay2);

        // 第 2 个角色槽（EI 有 2 槽），锚点 (300,210)。
        _characterAnimation2 = new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            FrameCount = 1,
            AnimationDelay = TimeSpan.FromMilliseconds(1),
            UseOffSet = true,
            Location = Slot1Anchor,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.AddControl(_characterAnimation2);

        // 每槽一个不可见命中区 + 一个名称标签。原版命中框 = 当前帧 bbox
        // （0x458A70：锚点 + 帧头 offset 起、帧头 w/h 大），_Process 里逐帧刷新；
        // 名称标签放在 bbox 上方（原版详情文字由 0x458310/0x458150 单独绘制，
        // 这里保留移植版的槽位标签以便点击选择，位置随 bbox 走）。
        _slotHit0 = new DXControl { Size = new Vector2I(120, 200), Location = Slot0Anchor, Visible = false };
        _slotHit1 = new DXControl { Size = new Vector2I(120, 200), Location = Slot1Anchor, Visible = false };
        _slotHit0.MouseClick += (o, e) => SelectSkinCharacter(0);
        _slotHit1.MouseClick += (o, e) => SelectSkinCharacter(1);
        background.AddControl(_slotHit0);
        background.AddControl(_slotHit1);
        _slotName0 = new DXLabel { FontSize = 9, TextColour = new Color(1f, .92f, .6f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, Size = new Vector2I(160, 16), Location = new Vector2I(Slot0Anchor.X - 80, Slot0Anchor.Y), IsControl = false };
        _slotName1 = new DXLabel { FontSize = 9, TextColour = new Color(1f, .92f, .6f), DrawOutline = true, OutlineColour = Colors.Black, Align = HorizontalAlignment.Center, Size = new Vector2I(160, 16), Location = new Vector2I(Slot1Anchor.X - 80, Slot1Anchor.Y), IsControl = false };
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
        // 再由 0x45763D 在 CreateChr.dat 泵完时转到 phase 2。CreateChr 过场
        // 1.30s (39 帧) 期间没有任何输入；视频结束回调里才显示编辑面板。
        _skinCreate.MouseClick += (o, e) =>
        {
            if (_characters.Count >= 4) return;
            if (_selectPhase == 1) return; // 过场中防重入
            SetSelectPhase(1);
            // 原版 phase 1（0x457615）载入 CreateChr.dat 到 +0x780 并 pump。
            if (AutoLoginArgs.LegacyUi)
            {
                // 过场期间隐藏主按钮面板，避免用户点击穿透
                if (_skinPanel != null) _skinPanel.Visible = false;
                // 证据 0x459AB6（紧邻 F51 处理器 0x459AC5）读 +0x113C = CreateChr.wav
                // -> 点「创建角色」时播一次性音效。
                SoundPlayback.Play(this, SoundIndex.LegacyCreateChr);
                PlayLegacyTransition("CreateChr", onFinished: () =>
                {
                    // 视频播完，phase 2：显示创建编辑界面
                    SetSelectPhase(2);
                    if (_skinPanel != null) _skinPanel.Visible = !AutoLoginArgs.LegacyUi;
                    ShowCreateCharacterPanel();
                });
            }
            else
            {
                ShowCreateCharacterPanel();
            }
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
        // 创建预览：原版是静态分层合成（身体+盔甲+武器+染色+头发），不是动画帧。
        // 见 DXCreatePreviewControl，复刻 NewCharacterDialog.PreviewPanel_AfterDraw。
        _createPreview = new DXCreatePreviewControl { Location = new Vector2I(0, 0), Size = new Vector2I(190, 225), Clip = true };
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
            BuildLegacyCreateLayer();
            ApplyLegacyEiSelectLayout();
            // 验证用：--legacy-phase2 直接进入 phase 2（动画列表 + 5 个图形钮），
            // 便于截图核对 phase 2 的贴图/hover，不需要真走一遍创建流程。
            // --legacy-create 再进一步直接显示创建面板（含大人物预览）。
            bool forceP2 = false, forceCreate = false;
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                if (a == "--legacy-phase2") forceP2 = true;
                if (a == "--legacy-create") forceCreate = true;
            }
            SetSelectPhase(forceP2 || forceCreate ? 2 : 0);
            if (forceCreate) ShowCreateCharacterPanel();
            RunSelectButtonSelfTestIfRequested();
        }
        GetNode<Control>("VBox").Visible = false;
    }

    // =====================================================================================
    // EI phase 2「新建人物」原生构图
    //
    // 原版 stage 2 的 tick（0x457AB0-0x458130）按顺序画：
    //   1. 背景 Interface1c **F80**（0x457B09 push 0x50 → 0x466130(+0x14C,0x50) → 0x45FD50，(0,0)）
    //   2. 2 个预览槽循环（0x457B47-0x457D31，esi 从 +0x10E0 起、步长 0x40、ebp<2）：
    //        • 帧推进：计时 += delta，超过记录时长（0x78 = 120 ms）→ 帧++、计时清零，
    //          超过末帧回绕到首帧（0x457B63-0x457BD1）
    //        • 0x458A70 按**当前帧**重算命中框 [+0x28]
    //        • 阴影 = 帧+0x14(=20) → 0x460CB0（混合 blit），位置 = 锚点 + 帧头 offset
    //        • 身体：选中槽（ebp == [owner+0x1488]）= 当前帧 → 0x45FD50（动）；
    //                未选中槽 = 记录首帧 → 0x4645E0（**静止**）
    //        • 叠加：选中槽 帧+0x28(=40) → 0x457310（3D 贴图页，**无 2D 等价物，未移植**）
    //   3. F82 → 0x460CB0（混合 0x32 = 50）于 (201,434)，尺寸取帧头 256x32
    //   4. F81 → 0x45FD50 于 (247,384)，尺寸取帧头 164x88
    //   5. 5 个 phase-2 按钮（+0xD38/+0xDEC/+0xEA0/+0xF54/+0x1008）
    //   6. 名字框底：SetRect(287,404,364,419) + 0x45E570 填色 0x3C5A78（0x4580EF-0x458121）
    // 名字输入框本身是独立的 Win32 EDIT 子窗口：stage 1 结束时
    // MoveWindow(edit, win+0x120, win+0x195, 0x4B, 0xD) + ShowWindow(edit, SW_SHOW)
    // （0x4576A1/0x4576B0）→ 客户区坐标 (288,405)、尺寸 75x13；字符上限 14（EM_SETLIMITTEXT 0xE）。
    //
    // 帧段/锚点：
    //   • 帧段 = variant 4（0x4576F3 / 0x459E56 的 0x458B20(slot, 4)）= 大体型站立待机段
    //   • 锚点 = 0x458910 表，idx = 性别*3 + 职业，8 字节一项（primary-bytes）
    // 因此本屏与 phase 0 的列表槽位（variant 1→2、锚点 (250/300,210)）**不是同一套
    // 坐标/时序**（同属 Interface1c 同一张 30 条帧表，仅 variant 不同），不可混用。
    // =====================================================================================

    /// <summary>
    /// phase 2 两个预览槽的锚点（primary-bytes：0x458910-0x4589A2，逐项 8 字节）：
    /// 男武(110,110) 男法(110,120) 男道(110,120) 女武(400,160) 女法(420,115) 女道(425,118)。
    /// 索引 = 性别*3 + 职业；参数越界时默认 (110,100)。
    /// （写入位置逐个为 S+0…S+0x2c，其中 entry1.Y/entry2.Y 由 0x45891D/0x458921 在
    /// `push esi` **之前**写入 S+0xc/S+0x14，故 120 是真实值，不是未初始化。）
    /// 锚点同时是命中框/阴影的基准（0x458A70 与 0x457C58 都用 [+0x18]/[+0x1C]）。
    /// </summary>
    private static readonly Vector2I[] LegacyCreateAnchors =
    {
        new(110, 110), new(110, 120), new(110, 120),   // 槽 0 = 男：武 / 法 / 道
        new(400, 160), new(420, 115), new(425, 118),   // 槽 1 = 女：武 / 法 / 道
    };

    /// <summary>槽 0 = 男、槽 1 = 女（0x4576E3 给 +0x10FC 传 gender=1，即 0x458440 的 arg2）。</summary>
    private static MirGender LegacyCreateGender(int slot) => slot == 1 ? MirGender.Female : MirGender.Male;

    private static readonly MirClass[] LegacyCreateClasses =
        { MirClass.Warrior, MirClass.Wizard, MirClass.Taoist };

    private MirClass LegacyCreateClass => LegacyCreateClasses[Mathf.Clamp(_legacyCreateClassIndex, 0, 2)];

    private static Vector2I LegacyCreateAnchorFor(int slot, int classIndex)
        => LegacyCreateAnchors[Mathf.Clamp(slot, 0, 1) * 3 + Mathf.Clamp(classIndex, 0, 2)];

    private Vector2I LegacyCreateAnchor(int slot) => LegacyCreateAnchorFor(slot, _legacyCreateClassIndex);

    private (int First, int Last) LegacyCreateBlock(int slot, int variant)
        => LegacySlotBlock(LegacyCreateClass, LegacyCreateGender(slot), variant);

    /// <summary>
    /// EI stage 1 → stage 2 的槽初始化（primary-bytes：0x4576B6-0x4576F8）：
    ///   0x458440(&amp;+0x10BC, gender=0, class=0, level=0, name=NULL) + 0x458B20(0, 4)
    ///   0x458440(&amp;+0x10FC, gender=1, class=0, level=0, name=NULL) + 0x458B20(1, 4)
    /// 即：**每次进创建屏都重置为「武士 + 男/女两只」**，并从 variant 4 段首帧重播
    /// （0x458B20 把记录首帧写 [+0x22]、计时清零）。
    /// 选中槽 [+0x1488] 不在重置范围内（ctor 0x456C4B 只把它清 0，之后只有点击修改）。
    /// </summary>
    private void InitLegacyCreateSlots()
    {
        _legacyCreateClassIndex = 0;   // 0x458440 的 class 实参恒为 0（0x4576BA/0x4576DB 前的 push 0）
        _skinCreateClass = MirClass.Warrior;
        RebuildLegacyCreateSlots();
    }

    /// <summary>
    /// 按当前 [职业] 与固定 [槽0=男 / 槽1=女] 重建两个预览槽。职业按钮（0x459DAE/0x459E19/
    /// 0x459EA5）走这里：class 变了 → 帧段变、锚点可能变（0x458910 按 性别*3+职业 取值）。
    /// </summary>
    private void RebuildLegacyCreateSlots()
    {
        if (_legacyCreateBody0 == null) return;   // legacy 图层尚未构建
        for (int slot = 0; slot < 2; slot++)
        {
            var (first, _) = LegacyCreateBlock(slot, 4);
            _legacyCreateFrame[slot] = first;
            _legacyCreateTimer[slot] = 0;
            var body = slot == 0 ? _legacyCreateBody0 : _legacyCreateBody1;
            var shadow = slot == 0 ? _legacyCreateShadow0 : _legacyCreateShadow1;
            var hit = slot == 0 ? _legacyCreateHit0 : _legacyCreateHit1;
            Vector2I anchor = LegacyCreateAnchor(slot);
            if (body != null) body.Location = anchor;
            if (shadow != null) shadow.Location = anchor;
            if (hit != null) hit.Visible = true;
            SyncLegacyCreateHit(slot);
        }
        SyncLegacyCreateFrameIndexes();
        GD.Print($"[LegacyCreate] 槽重建: class={LegacyCreateClass} variant=4 "
            + $"锚点 slot0={LegacyCreateAnchorFor(0, _legacyCreateClassIndex)} "
            + $"slot1={LegacyCreateAnchorFor(1, _legacyCreateClassIndex)} "
            + $"帧段 slot0={LegacyCreateBlock(0, 4)} slot1={LegacyCreateBlock(1, 4)} "
            + $"帧时长={LegacyFrameMs}ms 选中槽={_legacyCreateSelected}");
    }

    /// <summary>
    /// 按选中槽决定身体显示哪一帧：选中槽显示推进中的当前帧（[+0x22]，0x457C42 分支），
    /// 未选中槽显示记录首帧（[record+0]，0x457C98 分支）。阴影两槽都跟随推进的帧号
    /// （0x457BE3 用 [slot+0x22]），原版如此，不做"未选中也静止"的想当然处理。
    /// </summary>
    private void SyncLegacyCreateFrameIndexes()
    {
        if (_legacyCreateBody0 == null) return;
        for (int slot = 0; slot < 2; slot++)
        {
            var body = slot == 0 ? _legacyCreateBody0 : _legacyCreateBody1;
            var shadow = slot == 0 ? _legacyCreateShadow0 : _legacyCreateShadow1;
            if (body == null) continue;
            var (first, last) = LegacyCreateBlock(slot, 4);
            int frame = Mathf.Clamp(_legacyCreateFrame[slot], first, last);
            int shown = _legacyCreateSelected == slot ? frame : first;
            if (body.Index != shown) body.Index = shown;
            if (shadow != null)
            {
                int shadowIndex = frame + 20;   // 0x457BE3 add eax, 0x14
                if (shadow.Index != shadowIndex) shadow.Index = shadowIndex;
            }
        }
    }

    /// <summary>命中框 = 锚点 + **当前帧**帧头 offset，尺寸取帧头 w/h（0x458A70）。</summary>
    private void SyncLegacyCreateHit(int slot)
    {
        var hit = slot == 0 ? _legacyCreateHit0 : _legacyCreateHit1;
        var body = slot == 0 ? _legacyCreateBody0 : _legacyCreateBody1;
        if (hit == null || body == null) return;
        int frame = _legacyCreateFrame[slot];
        Vector2I size = MirSkin.GetSize(LibraryFile.Interface1c, frame);
        if (size == Vector2I.Zero) return;
        hit.Location = LegacyCreateAnchor(slot) + MirSkin.GetOffset(LibraryFile.Interface1c, frame);
        hit.Size = size;
    }

    /// <summary>
    /// stage 2 每帧推进（对应 tick 0x457B47-0x457D31 的帧推进段）。
    /// 计时严格大于 120 ms 才进帧、进帧后计时清零（0x457B6B-0x457BD1）；
    /// **两个槽都在推进**，只有身体按选中态取帧。
    /// </summary>
    private void ProcessLegacyCreatePreview(double delta)
    {
        if (_legacyCreateLayer == null || !_legacyCreateLayer.Visible) return;
        double ms = delta * 1000.0;
        for (int slot = 0; slot < 2; slot++)
        {
            var (first, last) = LegacyCreateBlock(slot, 4);
            _legacyCreateTimer[slot] += ms;
            if (_legacyCreateTimer[slot] > LegacyFrameMs)
            {
                _legacyCreateTimer[slot] = 0;
                _legacyCreateFrame[slot]++;
            }
            if (_legacyCreateFrame[slot] > last || _legacyCreateFrame[slot] < first)
                _legacyCreateFrame[slot] = first;
            SyncLegacyCreateHit(slot);
        }
        SyncLegacyCreateFrameIndexes();
    }

    /// <summary>
    /// 点击预览人物 = 选择性别（0x459939-0x4599A1 的 stage 2 槽命中循环）：
    ///   PtInRect([slot+0x28]) 命中 → 已是选中槽则只写选中值；否则 0x458B20(slot, 4)
    ///   重播该槽 variant 4（帧复位到记录首帧）→ [+0x1488] = slot → 0x4584C0 刷新显示串。
    /// 槽 0 = 男、槽 1 = 女，故这一步就是性别选择；F86 提交时读 [+0x1488] 槽的 [+5]/[+4]。
    /// </summary>
    private void SelectLegacyCreateSlot(int slot)
    {
        if (slot < 0 || slot > 1) return;
        if (_legacyCreateSelected != slot)
        {
            var (first, _) = LegacyCreateBlock(slot, 4);
            _legacyCreateFrame[slot] = first;   // 0x458B20(slot, 4) 把帧复位到记录首帧
            _legacyCreateTimer[slot] = 0;
        }
        _legacyCreateSelected = slot;
        SyncLegacyCreateFrameIndexes();
        GD.Print($"[LegacyCreate] 选中预览槽 {slot}（性别 {LegacyCreateGender(slot)}，原版 [+0x1488]），"
            + $"职业 {LegacyCreateClass}");
    }

    /// <summary>F89「退出人物创建」：隐藏创建界面回到角色列表（见 MakeSelectIconButton 处注释）。</summary>
    private void ExitLegacyCreate()
    {
        GD.Print("[LegacyCreate] F89 退出人物创建 -> 回 phase 0 列表");
        HideCreateCharacterPanel();
    }

    private void SetLegacyCreateLayerVisible(bool visible)
    {
        if (_legacyCreateLayer == null) return;
        _legacyCreateLayer.Visible = visible;
        if (visible) SyncLegacyCreateFrameIndexes();
    }

    /// <summary>
    /// 构建 EI stage 2 的原生图层（挂在 F50/F80 背景之下，与 phase 0 槽位同一 640x480 画布）。
    /// 坐标/帧号/层级全部来自上面的 primary-bytes 结论；仅 legacy 模式使用。
    /// 绘制顺序 = 子节点顺序：预览槽（阴影→身体，槽 0 后槽 1）→ F82 → F81 →
    /// 名字框底 → 输入框（Win32 EDIT 是子窗口，永远在最上层）。
    /// </summary>
    private void BuildLegacyCreateLayer()
    {
        if (_selectBackground == null) return;

        _legacyCreateLayer = new DXControl
        {
            Size = new Vector2I(640, 480),
            Position = Vector2I.Zero,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _selectBackground.AddControl(_legacyCreateLayer);

        // 两只预览槽：阴影 + 身体（原版 tick 每槽依次画 阴影 → 身体 → 叠加）
        for (int slot = 0; slot < 2; slot++)
        {
            var shadow = new DXImageControl
            {
                LibraryFile = LibraryFile.Interface1c,
                UseOffSet = true,          // 0x457BF0：锚点 + 帧头 offset，尺寸取帧头
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var body = new DXImageControl
            {
                LibraryFile = LibraryFile.Interface1c,
                UseOffSet = true,          // 0x457C58：同上（1:1，无缩放）
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _legacyCreateLayer.AddControl(shadow);
            _legacyCreateLayer.AddControl(body);
            var hit = new DXControl { Visible = false };
            int captured = slot;
            hit.MouseClick += (o, e) => SelectLegacyCreateSlot(captured);
            _legacyCreateLayer.AddControl(hit);
            if (slot == 0) { _legacyCreateShadow0 = shadow; _legacyCreateBody0 = body; _legacyCreateHit0 = hit; }
            else { _legacyCreateShadow1 = shadow; _legacyCreateBody1 = body; _legacyCreateHit1 = hit; }
        }

        // F82：混合绘制的暗条，位置 (201,434)，尺寸取帧头 256x32，混合量 0x32/255
        // （0x457D37-0x457D85 → 0x460CB0）。
        _legacyCreateStrip = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 82,
            FixedSize = true,
            Size = new Vector2I(256, 32),
            Location = new Vector2I(201, 434),
            ImageOpacity = 50 / 255f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _legacyCreateLayer.AddControl(_legacyCreateStrip);

        // F81：石台/名字牌，实心 blit 于 (247,384)，尺寸取帧头 164x88（0x457D8A-0x457DC6）。
        // 该帧内已烘焙了三个职业图标（与 F91/F94/F97 逐像素吻合：x 完全对齐、y 差 4px），
        // 实机由三个职业按钮叠在其上 —— 这是原版就有的重叠，照实复现。
        _legacyCreatePlate = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 81,
            FixedSize = true,
            Size = new Vector2I(164, 88),
            Location = new Vector2I(247, 384),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _legacyCreateLayer.AddControl(_legacyCreatePlate);

        // 点击名字框区域（原版 0x459C8C 命中 SetRect(294,404,371,419)）→
        // SetFocus(edit) + ShowWindow(edit, SW_SHOW)。加在输入框之前，输入框在自己
        // 区域内先接管鼠标。
        _legacyCreatePlateHit = new DXControl
        {
            Location = new Vector2I(294, 404),
            Size = new Vector2I(77, 15),
        };
        _legacyCreatePlateHit.MouseClick += (o, e) => _legacyCreateName?.GrabFocus();
        _legacyCreateLayer.AddControl(_legacyCreatePlateHit);

        // 名字框底：SetRect(287,404,364,419)（77x15）+ 0x45E570 填 0x3C5A78
        // （0x00BBGGRR → RGB(120,90,60) 青铜色），在 F81 之上、输入框之下。
        var namePlate = new DXControl
        {
            Location = new Vector2I(287, 404),
            Size = new Vector2I(77, 15),
            BackColour = new Color(120 / 255f, 90 / 255f, 60 / 255f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _legacyCreateLayer.AddControl(namePlate);

        // 角色名输入框：原版 Win32 EDIT，客户区坐标 (288,405)、尺寸 75x13，
        // 字符上限 14（0x4511D0 → EM_SETLIMITTEXT 0xE）。DPTextInput 自带的边框不画：
        // 原版这里只有上面那块填色底 + 白色文字。
        _legacyCreateName = new DXTextInput
        {
            Location = new Vector2I(288, 405),
            Size = new Vector2I(75, 13),
            FontSize = 8,
            Border = false,
            MaxLength = 14,
            Text = string.Empty,
        };
        _legacyCreateName.TextChanged += _ => UpdateCreateButtonStates();
        _legacyCreateName.TextSubmitted += _ => SubmitSkinCharacter();
        _legacyCreateLayer.AddControl(_legacyCreateName);

        GD.Print("[LegacyCreate] EI 新建人物图层已构建: 2 预览槽(variant 4) + F82@(201,434) "
            + "+ F81@(247,384) + 名字框底@(287,404,77x15) + 名字框@(288,405,75x13,max14)");
    }

    /// <summary>EI 原版选角屏（`login-flow-evidence.json::screens.parent`，screen obj 0x8A7140，
    /// ctor 0x456CB0）。**640x480 基准**，背景按阶段换帧，控件散点摆放、没有居中面板：
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
        // **帧极性按 ctor 实参**（0x456DA7-0x456E1E）：arg8(+0x20)=常态、arg3(+0x1C)=hover
        // （绘制 0x417640：状态 0 → [+0x20]；状态 2 → [+0x1C]，逐字节实读）。
        // 故「创建角色」常态 = **52**、hover = 51（旧的 51/52 正好相反）。
        SkinSelectButton(_skinCreate, 52, 51, new Vector2I(440, 93), new Vector2I(96, 26));
        SkinSelectButton(_skinDelete, 54, 53, new Vector2I(79, 243), new Vector2I(96, 26));
        SkinSelectButton(_skinStart, 56, 55, new Vector2I(259, 49), new Vector2I(96, 24));

        // 「结束」按钮（原实现没有）。常态 58 / hover 57（ctor 0x456E13-0x456E1E）。
        _skinExit = new DXButton { LibraryFile = LibraryFile.Interface1c, Index = 58, HoverIndex = 57,
            PressedIndex = 57, FixedSize = true, Size = new Vector2I(48, 26), Location = new Vector2I(28, 438) };
        _skinExit.MouseClick += (o, e) => GetTree().Quit();
        _uiLayer.AddChild(_skinExit);

        // 右下两个圆形钮（✔ / ✘）。**悬停标题是决定性证据**（phase-2 tick 的
        // 5 按钮 hover 分支：跳表 0x458134 + 字符串表）：
        //   F86 @0x47D64C = 「确认人物创建」→ 处理链 0x459F31（发送 msgid 0x65 CM_NEWCHR）
        //   F89 @0x47D638 = 「退出人物创建」→ 处理链 0x459D1D
        //   F92/F95/F98 @0x47D680/70/60 = 「武士/法师/道士 职业 选择」
        // 据此把「确认」接 SubmitSkinCharacter、「退出」接 ExitLegacyCreate。
        //
        // **帧号按 ctor 实参改正**（0x456E89-0x456EC8）：+0x20 = 常态帧、+0x1C = hover 帧。
        //   确认钮：常态 **85**、hover 87；退出钮：常态 **88**、hover 90
        //   （旧代码用 86/89 作常态 —— 那是 +0x18「状态 1」帧，F85≠F86、F88≠F89）。
        _skinConfirmYes = MakeSelectIconButton(85, 87, 86, new Vector2I(450, 444), () => SubmitSkinCharacter(), "确认人物创建");
        // F89 = 退出人物创建（0x459D1D-0x459D8A）：
        //   -> 播 UI 音（0x45B3D0）-> phase=3 -> [+0x1160]=0（关相位 BGM 开关）
        //   -> SetFocus(主窗口) -> ShowWindow(edit, SW_HIDE) -> 网络发送 msgid 0x64
        //      '%s/%d'（0x451F90 = CM_QUERYCHR「刷新角色列表」；Zircon 协议无对应包，
        //      列表本就在内存中，故不发送并记录该差异）。
        // phase 3 的 tick（0x4576FA）在视频泵返回 0 后立即把 phase 写回 0，
        // 本移植版没有该过场视频 → 等价于「隐藏创建界面、回到角色列表」。
        _skinConfirmNo = MakeSelectIconButton(88, 90, 89, new Vector2I(491, 444), () => ExitLegacyCreate(), "退出人物创建");

        // 三枚图形钮 = 职业选择（ctor 0x456E23/0x456E45/0x456E67）：
        //   武士 常态 **91** / hover 93；法师 常态 **94** / hover 96；道士 常态 **97** / hover 98。
        // 处理链 0x459D1D(武士 class=0) → 0x459E19(法师 class=1) → 0x459EA5(道士 class=2)，
        // 每个都调 0x458440(slot, gender, **class**, 0, NULL) + 0x458B20(slot, 4)
        // **重建两个预览槽**（[slot+4]=class、[slot+5]=gender 正是帧号公式 0x458EC0 的键）。
        // 我方 MirClass 枚举 Warrior=0 / Wizard=1 / Taoist=2 —— 与原版 0/1/2 数值一致。
        // 悬停标题（0x47D680/70/60，GBK）＝「武士/法师/道士 职业 选择」，用移植版
        // tooltip 承载（原版是按钮旁的缩放 sprite + 文字条，渲染方式记为 candidate）。
        _skinClassWarrior = MakeSelectIconButton(91, 93, 92, new Vector2I(266, 419), () => SelectCreateClass(MirClass.Warrior), "武士 职业 选择");
        _skinClassWizard = MakeSelectIconButton(94, 96, 95, new Vector2I(308, 419), () => SelectCreateClass(MirClass.Wizard), "法师 职业 选择");
        _skinClassTaoist = MakeSelectIconButton(97, 98, 98, new Vector2I(352, 419), () => SelectCreateClass(MirClass.Taoist), "道士 职业 选择");


        // **EI 此屏没有居中面板** —— 原 _skinPanel 是自制列表容器（320x425 带窗口框）。
        // 角色改由洞窟里的 2 个槽位渲染（见 UpdateCaveSlots），面板整块隐藏，
        // 否则它会盖住 F50 的洞窟画面。
        if (_skinPanel != null) _skinPanel.Visible = false;
        GD.Print($"[LegacySelect] 按钮状态: create={_skinCreate?.Location}/{_skinCreate?.Size} vis={_skinCreate?.Visible} "
            + $"parent={_skinCreate?.GetParent()?.GetType().Name} delete={_skinDelete?.Location} start={_skinStart?.Location} "
            + $"panelVis={_skinPanel?.Visible}");
        GD.Print("[LegacySelect] EI 布局已应用: 背景 phase0=F50 / phase2=F80 @(0,0) 640x480; "
            + "创建(440,93) 删除(79,243) 开始(259,49) 结束(28,438) "
            + "✔(450,444) ✘(491,444) 武士(266,419) 法师(308,419) 道士(352,419)");
    }

    /// <summary>
    /// phase 2 的图形钮。帧号**按 ctor 实参逐个传入**（不要用 frame±1 的规则推导）：
    /// ctor 0x417550 的 `(ebx, +0x18帧, +0x1C帧, X, Y, 0, 1, +0x20帧, 1)`；
    /// 绘制 0x417640 的选帧是 **状态 0 → [+0x20]（常态）、状态 2 → [+0x1C]（hover）**
    /// （hover 由 vtable+0xC = 0x4177C0 写状态 2）。9 个按钮的实参见 0x456DA7-0x456EC8。
    /// </summary>
    private DXButton MakeSelectIconButton(int normalFrame, int hoverFrame, int pressedFrame,
        Vector2I location, Action action, string hint = null)
    {
        var size = MirSkin.GetSize(LibraryFile.Interface1c, normalFrame);
        if (size == Vector2I.Zero) size = new Vector2I(28, 28);
        var button = new DXButton
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = normalFrame,
            HoverIndex = hoverFrame,
            PressedIndex = pressedFrame,
            FixedSize = true,
            Size = size,
            Location = location,
            TooltipText = hint ?? string.Empty,
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
        // 原版 PreviewPanel_AfterDraw：ProgUse 身体 + Equip 盔甲/武器 + 染色 overlay 静态合成
        var hairC = new Color(_skinHairColour.R / 255f, _skinHairColour.G / 255f, _skinHairColour.B / 255f);
        var armourC = new Color(_skinArmourColour.R / 255f, _skinArmourColour.G / 255f, _skinArmourColour.B / 255f);
        _createPreview.SetAppearance(_skinCreateClass, _skinCreateGender, _skinHairType, hairC, armourC);
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
            // 建角成功：原版由**服务器回包**驱动（0x209 SM_NEWCHR_SUCCESS → 0x459216）：
            //   播 SelChr.wav（[+0x1140]）+ 载入并播放 .\Data\CreateChr.dat
            //   → [0x930]=3 + [+0x1160]=0 + SetFocus(主窗口) + ShowWindow(edit, SW_HIDE)
            // phase 3 的 tick 泵完该过场视频后才把 phase 写回 0（0x45770F），列表随之刷新
            // （0x459216 末尾再发 `CM_QUERYCHR 0x64` 让服务器重发角色列表）。
            // Zircon 协议 NewCharacter 一次往返即完成，故此处用本地列表刷新 + 过场视频等价。
            if (AutoLoginArgs.LegacyUi)
            {
                SetLegacyCreateLayerVisible(false);
                SetSelectPhase(3);
                SoundPlayback.Play(this, SoundIndex.LegacySelChrBgm);
                PlayLegacyTransition("CreateChr", onFinished: () =>
                {
                    SetSelectPhase(0);
                    AfterCreateSuccess();
                });
                _statusLabel.Text = Lang.SelectCharacterLabel10;
                return;
            }
            HideCreateCharacterPanel();
            SetSelectPhase(0);
            AfterCreateSuccess();
        }
        else
        {
            GD.Print($"[Select] 建角色失败: {_pendingNewCharResult}");
            _statusLabel.Text = string.Format(Lang.SelectCreateLabel3, _pendingNewCharResult);
            // 失败留在创建界面（原版 0x20A：弹 Mirmg 文案，不改阶段）
            if (AutoLoginArgs.LegacyUi) SetSelectPhase(2);
            if (_skinCreateConfirm != null) _skinCreateConfirm.Enabled = true;
        }
    }

    /// <summary>建角成功后的收尾：恢复洞窟槽位、刷新列表、按需自动进游戏。</summary>
    private void AfterCreateSuccess()
    {
        if (_characterAnimation != null) _characterAnimation.Visible = true;
        RefreshList();
        _statusLabel.Text = Lang.SelectCharacterLabel10;
        // headless 自动测试: 建完直接进游戏（--stay-select 时留在选角屏验证）
        if (AutoLoginArgs.AutoLogin && !AutoLoginArgs.StayInSelect && _characters.Count > 0)
        {
            GD.Print("[Select] 自动进入游戏...");
            CallDeferred(nameof(AutoStartGame));
        }
    }

    /// <summary>
    /// 缓存选角期间到达的公告；开始游戏过场结束前不弹窗。
    /// </summary>
    private void OnSelectChat(S.Chat p)
    {
        if (p == null) return;
        if (p.Type != MessageType.Announcement) return;
        if (!AutoLoginArgs.LegacyUi) return;
        _pendingLegacyStartNotice = p.Text ?? string.Empty;
        if (_legacyStartNoticeDialog != null && IsInstanceValid(_legacyStartNoticeDialog)
            && _legacyStartNoticeDialog.Visible)
            _legacyStartNoticeDialog.SetNotice(_pendingLegacyStartNotice);
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
        // EI F55 直接发起进入请求；成功回包后先播 StartGame 过场，结束后再显示公告确认。
        _net.Connection?.SendStartGame(_lastStartIndex);
    }

    private void ShowLegacyStartNotice()
    {
        if (_uiLayer != null) _uiLayer.Visible = false;
        _legacyStartNoticeLayer ??= new CanvasLayer
        {
            Name = "LegacyStartNoticeLayer",
            Layer = (_uiLayer?.Layer ?? 1) + 1,
        };
        if (_legacyStartNoticeLayer.GetParent() == null) AddChild(_legacyStartNoticeLayer);
        _legacyStartNoticeLayer.Visible = true;
        UiScaler.UpdateScale(_legacyStartNoticeLayer, GetViewport());

        _legacyStartNoticeBackdrop ??= new ColorRect
        {
            Name = "LegacyStartNoticeBlackout",
            Color = Colors.Black,
            Position = Vector2.Zero,
            Size = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        if (_legacyStartNoticeBackdrop.GetParent() == null)
            _legacyStartNoticeLayer.AddChild(_legacyStartNoticeBackdrop);

        _legacyStartNoticeDialog ??= new NoticeDialog();
        _legacyStartNoticeDialog.SetNotice(_pendingLegacyStartNotice);
        _legacyStartNoticeDialog.Confirmed += OnLegacyStartNoticeConfirmed;
        _legacyStartNoticeDialog.Cancelled += OnLegacyStartNoticeCancelled;
        WindowManager.Open(_legacyStartNoticeDialog, _legacyStartNoticeLayer);
        GD.Print($"[LegacySelect] StartGame 过场结束，黑屏上显示 F602 公告确认框: len={_pendingLegacyStartNotice.Length}");
    }

    private void OnLegacyStartNoticeConfirmed()
    {
        DetachLegacyStartNoticeHandlers();
        GD.Print("[LegacySelect] F602 勾选 -> 进入游戏");
        EnterGameScene();
    }

    private void OnLegacyStartNoticeCancelled()
    {
        DetachLegacyStartNoticeHandlers();
        GD.Print("[LegacySelect] F602 关闭；保持黑屏并等待勾选确认");
        CallDeferred(nameof(ReopenLegacyStartNotice));
    }

    private void DetachLegacyStartNoticeHandlers()
    {
        if (_legacyStartNoticeDialog == null) return;
        _legacyStartNoticeDialog.Confirmed -= OnLegacyStartNoticeConfirmed;
        _legacyStartNoticeDialog.Cancelled -= OnLegacyStartNoticeCancelled;
    }

    private void ReopenLegacyStartNotice()
    {
        if (_legacyStartNoticeDialog == null || _legacyStartNoticeLayer == null) return;
        _legacyStartNoticeDialog.Confirmed += OnLegacyStartNoticeConfirmed;
        _legacyStartNoticeDialog.Cancelled += OnLegacyStartNoticeCancelled;
        WindowManager.Open(_legacyStartNoticeDialog, _legacyStartNoticeLayer);
    }

    private void OnDeletePressed()
    {
        var selected = _charList.GetSelectedItems();
        if (selected.Length == 0 || selected[0] >= _characters.Count) return;
        int listIndex = selected[0];
        var character = _characters[listIndex];
        if (AutoLoginArgs.LegacyUi)
        {
            // 旧 Client/Scenes/SelectScene.cs L588-611（source-confirmed）：
            //   DeleteButton_MouseClick → DXMessageBox Yes/No；Yes 按钮默认禁用，
            //   倒计时 5 秒后才启用，同时动态更新文字显示剩余秒数。
            //   确认后发 C.DeleteCharacter{CharacterIndex, CheckSum=CEnvir.C}。
            // EI 反编目前未闭合 F53 具体 handler，此处保守沿用旧 C# 行为，
            // 避免即时误删角色（evidence level = source-confirmed）。
            ShowLegacyDeleteConfirm(character);
        }
        else
        {
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
    }

    private DXControl _legacyDeleteDialog;
    private DXLabel _legacyDeleteCountdownLabel;
    private DXButton _legacyDeleteYes, _legacyDeleteNo;
    private Timer _legacyDeleteTimer;
    private double _legacyDeleteCountdown;
    private SelectInfo _legacyDeleteTarget;

    private void ShowLegacyDeleteConfirm(SelectInfo character)
    {
        if (_legacyDeleteDialog != null)
        {
            _legacyDeleteDialog.QueueFree();
            _legacyDeleteDialog = null;
        }
        _legacyDeleteTarget = character;
        _legacyDeleteCountdown = 5.0;

        var dlg = new DXControl
        {
            Size = new Vector2I(360, 140),
            Position = new Vector2((UiScaler.BaseWidth - 360) / 2f, (UiScaler.BaseHeight - 140) / 2f),
        };
        _uiLayer.AddChild(dlg);
        _legacyDeleteDialog = dlg;

        dlg.AddControl(new LegacyWindowFrame { Size = new Vector2I(360, 140), HasTitle = true, HasFooter = false });
        dlg.AddControl(new DXLabel
        {
            Text = Lang.SelectCharacterLabel,
            FontSize = 12,
            TextColour = new Color(1f, .85f, .35f),
            DrawOutline = true,
            Align = HorizontalAlignment.Center,
            Size = new Vector2I(360, 28),
            Location = new Vector2I(0, 10),
            IsControl = false,
        });
        _legacyDeleteCountdownLabel = new DXLabel
        {
            Text = string.Format(Lang.SelectCharacterLabel12, character.CharacterName)
                   + $"\nPlease wait {_legacyDeleteCountdown:0.0} seconds before confirming.",
            FontSize = 9,
            TextColour = Colors.White,
            DrawOutline = true,
            Align = HorizontalAlignment.Center,
            Size = new Vector2I(340, 60),
            Location = new Vector2I(10, 38),
            IsControl = false,
        };
        dlg.AddControl(_legacyDeleteCountdownLabel);

        _legacyDeleteYes = new DXButton
        {
            Text = "Yes",
            FontSize = 10,
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Location = new Vector2I(90, 108),
            Size = new Vector2I(80, 24),
            Enabled = false,
        };
        _legacyDeleteNo = new DXButton
        {
            Text = "No",
            FontSize = 10,
            LibraryFile = LibraryFile.Interface,
            Index = -1,
            Location = new Vector2I(190, 108),
            Size = new Vector2I(80, 24),
        };
        _legacyDeleteYes.MouseClick += (o, e) =>
        {
            _deleteBtn.Disabled = true;
            _skinDelete.Enabled = false;
            _statusLabel.Text = Lang.SelectDeleteLabel;
            // 旧 C# 带 CheckSum=CEnvir.C；Zircon 的 SendDeleteCharacter 目前
            // 只发 CharacterIndex。CheckSum 是 EI 协议校验字段，Zircon 协议
            // 无需此字段；此处保留普通删除调用，证据标 source-confirmed 注释。
            _net.Connection?.SendDeleteCharacter(character.CharacterIndex);
            CloseLegacyDeleteConfirm();
        };
        _legacyDeleteNo.MouseClick += (o, e) => CloseLegacyDeleteConfirm();
        dlg.AddControl(_legacyDeleteYes);
        dlg.AddControl(_legacyDeleteNo);

        if (_legacyDeleteTimer == null)
        {
            _legacyDeleteTimer = new Timer { WaitTime = 0.1, OneShot = false };
            AddChild(_legacyDeleteTimer);
            _legacyDeleteTimer.Timeout += TickLegacyDeleteConfirm;
        }
        _legacyDeleteTimer.Start();
    }

    private void TickLegacyDeleteConfirm()
    {
        if (_legacyDeleteDialog == null) return;
        _legacyDeleteCountdown -= 0.1;
        if (_legacyDeleteCountdown <= 0)
        {
            _legacyDeleteCountdown = 0;
            if (_legacyDeleteYes != null) _legacyDeleteYes.Enabled = true;
            if (_legacyDeleteCountdownLabel != null)
                _legacyDeleteCountdownLabel.Text =
                    $"Are you sure you want to delete the character {_legacyDeleteTarget?.CharacterName}.";
            _legacyDeleteTimer?.Stop();
        }
        else if (_legacyDeleteCountdownLabel != null)
        {
            _legacyDeleteCountdownLabel.Text =
                string.Format(Lang.SelectCharacterLabel12, _legacyDeleteTarget?.CharacterName)
                + $"\nPlease wait {_legacyDeleteCountdown:0.0} seconds before confirming.";
        }
    }

    private void CloseLegacyDeleteConfirm()
    {
        _legacyDeleteTimer?.Stop();
        if (_legacyDeleteDialog != null)
        {
            _legacyDeleteDialog.QueueFree();
            _legacyDeleteDialog = null;
        }
        _legacyDeleteYes = null;
        _legacyDeleteNo = null;
        _legacyDeleteCountdownLabel = null;
        _legacyDeleteTarget = null;
    }

    private void OnDeleteCharacterResult(DeleteCharacterResult result, int deletedIndex)
    {
        if (result == DeleteCharacterResult.Success)
        {
            _characters.RemoveAll(c => c.CharacterIndex == deletedIndex);
            RefreshList();
            _startBtn.Disabled = true;
            _deleteBtn.Disabled = true;
            if (_skinStart != null) _skinStart.Enabled = false;
            if (_skinDelete != null) _skinDelete.Enabled = false;
            if (AutoLoginArgs.LegacyUi) SetSelectPhase(0);
            _statusLabel.Text = Lang.SelectDeleteLabel2;
        }
        else
        {
            _deleteBtn.Disabled = false;
            if (_skinDelete != null) _skinDelete.Enabled = true;
            _statusLabel.Text = string.Format(Lang.SelectDeleteLabel3, result);
        }
    }

    private void EnterGameScene()
    {
        GD.Print("[Select] StartGame 过场结束 -> 进入游戏世界");
        var gameScene = ResourceLoader.Load<PackedScene>("res://Scenes/GameScene.tscn");
        var game = gameScene.Instantiate<GameScene>();
        game.StartInfo = _pendingStartInfo;
        GetTree().Root.AddChild(game);
        QueueFree();
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
            SoundPlayback.Stop(SoundIndex.SelectScene);
            if (AutoLoginArgs.LegacyUi)
            {
                SetSelectPhase(4);
                // 原版 phase 4 = 进游戏，伴随 StartGame.dat 过场（后段淡入黑）。
                // 过场期间隐藏选角 UI；视频完整播完 (1.37s / 41 帧) 后，黑屏显示
                // F602 公告确认框；勾选后才创建 GameScene。视频挂到 Root。
                if (_uiLayer != null) _uiLayer.Visible = false;
                // F602 公告画在 **mode 3 = 800×600**（0x419377 →
                // 0x45D270(&0x8AB7A8, 0x320=800, 0x258=600, 0x10)），而选角屏是
                // mode 2 = 640×480。公告按 800×600 设计稿居中，窗口不先切大，
                // 弹框时视口还是 640×480，会整体偏右下 (80,60)。这里在过场开始
                // 时就切（过场后段本来就是黑屏，切换不可见），等 1.37s 结束后
                // Viewport.Size 必然已到位，ShowLegacyStartNotice 直接按 800×600 布局。
                ClientSettings.ApplyLegacyPregameWindow(800, 600);
                // 证据 0x459456（紧邻服务端 case 0x20D 处理器 0x459465）读 +0x1144
                // = StartGame.wav -> 进游戏时播一次性音效。
                SoundPlayback.Play(this, SoundIndex.LegacyStartGame);
                GD.Print("[Select] *** StartGame 成功! 播放 StartGame.ogv 过场后显示 F602 公告确认 ***");
                PlayLegacyTransition("StartGame", onFinished: ShowLegacyStartNotice, attachToRoot: true);
            }
            else
            {
                EnterGameScene();
            }
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
