using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.Network;
using S = Library.Network.ServerPackets;

namespace ZirconClient.Scripts;

public partial class LoginScene : Control
{
    private Network.NetworkManager _net;
    private CanvasLayer _uiLayer;
    private LineEdit _emailEdit;
    private LineEdit _passwordEdit;
    private Button _loginBtn;
    private Button _registerBtn;
    private Label _statusLabel;
    private LineEdit _keyEdit;
    private LineEdit _newPasswordEdit;
    private List<SelectInfo> _pendingCharacters;
    private DXTextInput _skinEmail, _skinPassword;
    private DXButton _skinLogin, _skinRegister, _skinChange, _skinRanking, _skinOptions, _skinExit, _skinActivation;
    private DXLabel _skinForgot;
    private DXCheckBox _skinRemember;
    private DXLabel _skinStatus;
    // EI 登录页：640x480 基准，视频背景 + 4 个文字按钮，没有居中对话框。
    private DXImageControl _loginDialogFrame;
    private VideoStreamPlayer _loginVideo;
    private RankingDialog _loginRanking;
    private ConfigDialog _loginConfig;
    private LegacyLoginDialog _accountDialog, _changeDialog, _requestResetDialog, _resetDialog, _activationDialog, _requestActivationDialog;
    private readonly List<Action> _unsubscribers = new();
    private bool _selectTransitionStarted;

    private static LoginScene _activeInstance;

    public override void _EnterTree()
    {
        if (_activeInstance != null && IsInstanceValid(_activeInstance))
        {
            GD.Print("[Login] 丢弃重复 LoginScene 实例");
            QueueFree();
            return;
        }
        _activeInstance = this;
    }

    public override void _Ready()
    {
        ClientSettings.Load();
        ClientSettings.ApplyDisplaySettings();
        if (AutoLoginArgs.LegacyUi) ClientSettings.ApplyLegacyPregameWindow();
        ClientSettings.UpdateWindowTitle();
        ClientSettings.BindWindowTitle(GetViewport());
        ClientSettings.ApplyAudioSettings();
        SoundPlayback.Play(this, SoundIndex.LoginScene);
        _net = GetNode<Network.NetworkManager>("/root/NetworkManager");

        _emailEdit = GetNode<LineEdit>("VBox/EmailRow/EmailEdit");
        _passwordEdit = GetNode<LineEdit>("VBox/PasswordRow/PasswordEdit");
        _loginBtn = GetNode<Button>("VBox/LoginBtn");
        _registerBtn = GetNode<Button>("VBox/RegisterBtn");
        _statusLabel = GetNode<Label>("VBox/StatusLabel");
        var vbox = GetNode<VBoxContainer>("VBox");
        _keyEdit = new LineEdit { PlaceholderText = Lang.LoginResetLabel };
        _newPasswordEdit = new LineEdit { PlaceholderText = Lang.LoginPasswordLabel, Secret = true };
        vbox.AddChild(_keyEdit);
        vbox.AddChild(_newPasswordEdit);
        AddAccountButton(vbox, Lang.LoginDialogChangePasswordButtonLabel, () => _net.Connection?.SendChangePassword(_emailEdit.Text, _passwordEdit.Text, _newPasswordEdit.Text));
        AddAccountButton(vbox, Lang.LoginPasswordLabel2, () => _net.Connection?.SendRequestPasswordReset(_emailEdit.Text));
        AddAccountButton(vbox, Lang.LoginPasswordLabel3, () => _net.Connection?.SendResetPassword(_keyEdit.Text, _newPasswordEdit.Text));
        AddAccountButton(vbox, Lang.ActivationTitle, () => _net.Connection?.SendActivation(_keyEdit.Text));
        AddAccountButton(vbox, Lang.LoginUi446Label, () => _net.Connection?.SendRequestActivationKey(_emailEdit.Text));

        _loginBtn.Pressed += OnLoginPressed;
        _registerBtn.Pressed += OnRegisterPressed;
        // 2 倍 UI 缩放：DX 旧版 UI 按 1024x768 逻辑坐标布局，直接挂到
        // CanvasLayer 缩放层（与 GameScene 的 _uiLayer 一致），窗口放大时
        // 跟随缩放。不用 Control 中转——Control 的 anchors 会干扰 Transform。
        _uiLayer = new CanvasLayer { Name = "UiScaleLayer" };
        AddChild(_uiLayer);
        BuildLegacyLoginUi();
        UiScaler.UpdateScale(_uiLayer, GetViewport());
        // 调试审计：ZIRCON_UI_AUDIT=1 时列出所有超出逻辑画布的控件
        if (System.Environment.GetEnvironmentVariable("ZIRCON_UI_AUDIT") == "1")
            UiScaler.AuditOverflow(_uiLayer, "LoginScene");
        // 窗口大小变化后视口才更新，Resized（Control）可能错过时序，
        // 用 Viewport.SizeChanged 确保窗口变化时重新应用缩放。
        GetViewport().SizeChanged += () => UiScaler.UpdateScale(_uiLayer, GetViewport());

        // 连接服务端
        _net.Log += OnNetLog;
        _unsubscribers.Add(() => _net.Log -= OnNetLog);
        // 自动登录: --auto-login 固定测试账号, 或 --user/--pass 指定账号
        bool autoLogin = AutoLoginArgs.AutoLogin;
        // 命令行服务器参数优先于持久化配置，方便在本地/远程服务器之间快速切换：
        // --server 127.0.0.1 --port 7000 或 --server 192.168.3.82 --port 7000。
        string host = AutoLoginArgs.ServerAddress
                      ?? (ClientSettings.UseNetworkConfig ? ClientSettings.IPAddress : "127.0.0.1");
        int port = AutoLoginArgs.ServerPort
                   ?? (ClientSettings.UseNetworkConfig ? ClientSettings.Port : 7000);
        GD.Print($"[Login] 目标服务器: {host}:{port}");
        foreach (var a in OS.GetCmdlineUserArgs()) GD.Print($"[Login] arg: {a}");
        // 单机模式：目标端口无监听时自动拉起本地 ServerCore（进程生命周期绑定，
        // 客户端退出时由 Shutdown 关闭）。远程 --server 参数指定时不触发。
        var launcher = GetNodeOrNull<SinglePlayerLauncher>("/root/SinglePlayerLauncher");
        if (launcher != null)
        {
            launcher.EnsureServerRunning(host, port);
            if (launcher.IsSpawned && !launcher.WaitForServer(host, port))
            {
                SetStatus(Lang.LoginUi447Label);
                return;
            }
        }
        if (!_net.Connect(host, port))
        {
            SetStatus(Lang.LoginNoneLabel);
            return;
        }

        // 订阅网络事件（_ExitTree 统一退订，避免场景释放后回调已销毁对象）
        _net.Connection.ConnectedEvent += OnConnected;
        _unsubscribers.Add(() => _net.Connection.ConnectedEvent -= OnConnected);
        _net.Connection.VersionOK += OnVersionOK;
        _unsubscribers.Add(() => _net.Connection.VersionOK -= OnVersionOK);
        _net.Connection.LoginResultEvent += OnLoginResult;
        _unsubscribers.Add(() => _net.Connection.LoginResultEvent -= OnLoginResult);
        _net.Connection.ChangePasswordResultEvent += OnChangePasswordResult;
        _unsubscribers.Add(() => _net.Connection.ChangePasswordResultEvent -= OnChangePasswordResult);
        _net.Connection.RequestPasswordResetResultEvent += OnRequestPasswordResetResult;
        _unsubscribers.Add(() => _net.Connection.RequestPasswordResetResultEvent -= OnRequestPasswordResetResult);
        _net.Connection.ResetPasswordResultEvent += OnResetPasswordResult;
        _unsubscribers.Add(() => _net.Connection.ResetPasswordResultEvent -= OnResetPasswordResult);
        _net.Connection.ActivationResultEvent += OnActivationResult;
        _unsubscribers.Add(() => _net.Connection.ActivationResultEvent -= OnActivationResult);
        _net.Connection.RequestActivationKeyResultEvent += OnRequestActivationKeyResult;
        _unsubscribers.Add(() => _net.Connection.RequestActivationKeyResultEvent -= OnRequestActivationKeyResult);
        _net.Connection.NewAccountResultEvent += OnNewAccountResult;
        _unsubscribers.Add(() => _net.Connection.NewAccountResultEvent -= OnNewAccountResult);
        _net.Connection.RankingsEvent += OnRankings;
        _unsubscribers.Add(() => _net.Connection.RankingsEvent -= OnRankings);
        _net.Connection.DisconnectedEvent += OnDisconnected;
        _unsubscribers.Add(() => _net.Connection.DisconnectedEvent -= OnDisconnected);
    }

    public override void _ExitTree()
    {
        foreach (var unsubscribe in _unsubscribers)
            unsubscribe();
        _unsubscribers.Clear();
        base._ExitTree();
        if (ReferenceEquals(_activeInstance, this)) _activeInstance = null;
    }

    private void AddAccountButton(VBoxContainer parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += () => action();
        parent.AddChild(button);
    }

    private void OnNetLog(string msg) => GD.Print(msg);

    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.Text = text;
        if (_skinStatus != null) _skinStatus.Text = text;
    }

    private void OnConnected()
    {
        GD.Print("[Login] 服务端确认连接");
    }

    private void ShowVersionOK(string version)
    {
        SetStatus(string.Format(Lang.LoginLoginLabel, version));
        if (_loginBtn != null && IsInstanceValid(_loginBtn)) _loginBtn.Disabled = false;
        if (_registerBtn != null && IsInstanceValid(_registerBtn)) _registerBtn.Disabled = false;
        if (_skinLogin != null) _skinLogin.Enabled = true;
        if (_skinRegister != null) _skinRegister.Enabled = true;
    }

    private void OnLoginResult(LoginResult result, string message, List<SelectInfo> characters, string address)
    {
        _pendingCharacters = characters ?? new List<SelectInfo>();
        _pendingLoginResult = result;
        _pendingLoginMessage = message;
        _net.BuyAddress = address;
        CallDeferred(nameof(ShowLoginResult));
    }
    private LoginResult _pendingLoginResult;
    private string _pendingLoginMessage;
    private void ShowLoginResult()
    {
        if (_selectTransitionStarted) return;
        if (_pendingLoginResult == LoginResult.Success)
        {
            _selectTransitionStarted = true;
            SoundPlayback.Stop(SoundIndex.LoginScene);
            SetStatus(string.Format(Lang.LoginCharacterLabel, _pendingCharacters.Count));
            GD.Print($"[Login] 登录成功, 角色数 {_pendingCharacters.Count}");
            // **2000ms 淡出到黑**（原版 phase 3 的过渡）。
            // 证据 login-flow-evidence.json::screens.char_select.phase：
            //   phase 3 = transition fade (tick 0x403560)，时长 **0x7D0 = 2000ms**，
            //   结束后 0x402970 -> parent（选角屏）。
            // 该过渡是 0x45FD50 画的**纯色覆盖层**，不是贴图动画 ——
            // 记录的 "draw frame 0x3C via 0x466130(+0x5B0,0)" 里帧 60 经独立解码
            // 确认是**空帧**（Interface1c F59-F65 全 alpha 全零），所以不必找素材。
            // 非 legacy 路径保持原来的立即切换。
            if (AutoLoginArgs.LegacyUi)
            {
                var fade = new ColorRect
                {
                    Color = new Color(0f, 0f, 0f, 0f),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _uiLayer.AddChild(fade);
                var tween = CreateTween();
                tween.TweenProperty(fade, "color:a", 1.0, 2.0);   // 2000ms
                tween.TweenCallback(Callable.From(() =>
                {
                    var sc = ResourceLoader.Load<PackedScene>("res://Scenes/SelectScene.tscn");
                    var ss = sc.Instantiate<SelectScene>();
                    ss.SetCharacters(_pendingCharacters);
                    GetTree().Root.AddChild(ss);
                    QueueFree();
                }));
                GD.Print("[LegacyLogin] phase 3 淡出开始 (2000ms)");
            }
            else
            {
                var selectScene = ResourceLoader.Load<PackedScene>("res://Scenes/SelectScene.tscn");
                var selectScript = selectScene.Instantiate<SelectScene>();
                selectScript.SetCharacters(_pendingCharacters);
                GetTree().Root.AddChild(selectScript);
                QueueFree();
            }
        }
        else
        {
            SetStatus(string.Format(Lang.LoginLoginLabel2, _pendingLoginResult, _pendingLoginMessage));
            if (_loginBtn != null && IsInstanceValid(_loginBtn)) _loginBtn.Disabled = false;
        }
    }

    private void OnNewAccountResult(NewAccountResult result)
    {
        CallDeferred(nameof(ShowNewAccountResult), (int)result);
    }

    private void OnVersionOK(string version, string dbKey)
    {
        GD.Print($"[Login] 版本校验通过, version={version}, dbKeyLen={dbKey}");
        CallDeferred(nameof(ShowVersionOK), version);
        if (AutoLoginArgs.AutoLogin)
        {
            GD.Print($"[Login] 自动登录: {AutoLoginArgs.User}, 发送 Login 包...");
            _net.Connection.SendLogin(AutoLoginArgs.User, AutoLoginArgs.Password);
            GD.Print("[Login] Login 包已入发送队列");
        }
    }

    private void OnChangePasswordResult(ChangePasswordResult result)
        => SetStatus(string.Format(Lang.LoginPasswordLabel4, result));
    private void OnRequestPasswordResetResult(RequestPasswordResetResult result)
        => SetStatus(string.Format(Lang.LoginResetLabel2, result));
    private void OnResetPasswordResult(ResetPasswordResult result)
        => SetStatus(string.Format(Lang.LoginPasswordLabel5, result));
    private void OnActivationResult(ActivationResult result)
        => SetStatus(string.Format(Lang.LoginUi455Label, result));
    private void OnRequestActivationKeyResult(RequestActivationKeyResult result)
        => SetStatus(string.Format(Lang.LoginUi456Label, result));
    private void OnRankings(S.Rankings rankings)
        => _loginRanking?.ApplyRankings(rankings);
    private void ShowNewAccountResult(int resultInt)
    {
        var result = (NewAccountResult)resultInt;
        if (result == NewAccountResult.Success || result == NewAccountResult.AlreadyExists)
            SetStatus(string.Format(Lang.LoginLoginLabel3, result));
        else
            SetStatus(string.Format(Lang.LoginRegisterLabel, result));
        if (_registerBtn != null && IsInstanceValid(_registerBtn)) _registerBtn.Disabled = false;
    }

    private void OnDisconnected()
    {
        CallDeferred(nameof(ShowDisconnected));
    }
    private void ShowDisconnected()
    {
        SetStatus(Lang.LoginUi459Label);
        if (_loginBtn != null && IsInstanceValid(_loginBtn)) _loginBtn.Disabled = true;
        if (_registerBtn != null && IsInstanceValid(_registerBtn)) _registerBtn.Disabled = true;
    }

    private void OnLoginPressed()
    {
        if (_loginBtn != null && IsInstanceValid(_loginBtn)) _loginBtn.Disabled = true;
        SetStatus(Lang.LoginLoginLabel4);
        string email = _skinEmail?.Text ?? _emailEdit.Text;
        string password = _skinPassword?.Text ?? _passwordEdit.Text;
        if (_skinRemember?.Checked == true)
        {
            ClientSettings.RememberDetails = true;
            ClientSettings.RememberedEMail = email;
            ClientSettings.RememberedPassword = password;
        }
        else
        {
            ClientSettings.RememberDetails = false;
            ClientSettings.RememberedEMail = string.Empty;
            ClientSettings.RememberedPassword = string.Empty;
        }
        ClientSettings.Save();
        _net.Connection?.SendLogin(email, password);
    }

    private void OnRegisterPressed()
    {
        _registerBtn.Disabled = true;
        SetStatus(Lang.LoginRegisterLabel2);
        _net.Connection?.SendNewAccount(_skinEmail?.Text ?? _emailEdit.Text, _skinPassword?.Text ?? _passwordEdit.Text);
    }

    private void ToggleLoginConfig()
    {
        if (_loginConfig == null) return;
        WindowManager.Toggle(_loginConfig, _uiLayer);
    }

    private void ToggleLoginRanking()
    {
        if (_loginRanking == null) return;
        WindowManager.Toggle(_loginRanking, _uiLayer);
        if (_loginRanking.Visible) _net.Connection?.SendRankings();
    }

    private void OpenAccountDialog()
    {
        _accountDialog ??= CreateAccountDialog();
        WindowManager.Open(_accountDialog, _uiLayer);
    }

    private void OpenChangeDialog()
    {
        _changeDialog ??= CreateChangeDialog();
        WindowManager.Open(_changeDialog, _uiLayer);
    }

    private void OpenRequestResetDialog()
    {
        _requestResetDialog ??= CreateRequestResetDialog();
        WindowManager.Open(_requestResetDialog, _uiLayer);
    }

    private LegacyLoginDialog CreateAccountDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.LoginRegisterLabel3, new Vector2I(300, 255),
            new[] { Lang.LoginEmailLabel, Lang.LoginPasswordLabel6, Lang.LoginConfirmLabel, Lang.LoginUi466Label, Lang.LoginDateLabel, Lang.LoginUi468Label },
            new[] { false, true, true, false, false, false });
        dialog.Submitted += values =>
        {
            if (values[0].Length < 3 || values[1].Length < 1 || values[1] != values[2]) { SetStatus(Lang.LoginRegisterLabel4); return; }
            DateTime.TryParse(values[4], out var birth);
            if (birth == default) birth = new DateTime(1990, 1, 1);
            _net.Connection?.SendNewAccount(values[0], values[1], string.IsNullOrWhiteSpace(values[3]) ? "Player" : values[3], birth, values[5]);
            WindowManager.Close(dialog);
        };
        return dialog;
    }

    private LegacyLoginDialog CreateChangeDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.LoginDialogChangePasswordButtonLabel, new Vector2I(330, 205),
            new[] { Lang.LoginEmailLabel, Lang.LoginPasswordLabel7, Lang.LoginPasswordLabel, Lang.LoginConfirmLabel2 }, new[] { false, true, true, true });
        dialog.Submitted += values =>
        {
            if (values[0].Length < 3 || values[2] != values[3]) { SetStatus(Lang.LoginPasswordLabel9); return; }
            _net.Connection?.SendChangePassword(values[0], values[1], values[2]);
            WindowManager.Close(dialog);
        };
        return dialog;
    }

    private LegacyLoginDialog CreateRequestResetDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.LoginPasswordLabel2, new Vector2I(330, 150), new[] { Lang.LoginEmailLabel }, secondary: Lang.LoginResetLabel3);
        dialog.Submitted += values => { if (!string.IsNullOrWhiteSpace(values[0])) _net.Connection?.SendRequestPasswordReset(values[0]); };
        dialog.SecondaryClicked += () => { _resetDialog ??= CreateResetDialog(); WindowManager.Close(dialog); WindowManager.Open(_resetDialog, _uiLayer); };
        return dialog;
    }

    private LegacyLoginDialog CreateResetDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.LoginPasswordLabel3, new Vector2I(330, 180), new[] { Lang.LoginResetLabel4, Lang.LoginPasswordLabel, Lang.LoginConfirmLabel }, new[] { false, true, true });
        dialog.Submitted += values =>
        {
            if (values[1] != values[2]) { SetStatus(Lang.LoginPasswordLabel13); return; }
            _net.Connection?.SendResetPassword(values[0], values[1]);
            WindowManager.Close(dialog);
        };
        return dialog;
    }

    private LegacyLoginDialog CreateActivationDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.ActivationTitle, new Vector2I(330, 155), new[] { Lang.LoginUi483Label }, secondary: Lang.LoginUi484Label);
        dialog.Submitted += values => { if (!string.IsNullOrWhiteSpace(values[0])) _net.Connection?.SendActivation(values[0]); };
        dialog.SecondaryClicked += () => { _requestActivationDialog ??= CreateRequestActivationDialog(); WindowManager.Close(dialog); WindowManager.Open(_requestActivationDialog, _uiLayer); };
        return dialog;
    }

    private LegacyLoginDialog CreateRequestActivationDialog()
    {
        var dialog = new LegacyLoginDialog(Lang.LoginUi446Label, new Vector2I(330, 150), new[] { Lang.LoginEmailLabel });
        dialog.Submitted += values => { if (!string.IsNullOrWhiteSpace(values[0])) _net.Connection?.SendRequestActivationKey(values[0]); };
        return dialog;
    }

    private void BuildLegacyLoginUi()
    {
        // 布局基准 = 逻辑画布 1024x768（与 GameScene HUD 同机制）。
        // 不要用真实视口尺寸定位：UiScaler 会把整个画布缩放并居中到视口，
        // 若按真实视口坐标布局再叠加缩放 Transform，4K 下元素会超出屏幕。
        Vector2 viewport = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight);
        // **帧号修正**：Interface1c 实测只有 3 帧（wilsdk 独立解码）：
        //   F0 = 640x360 沙漠背景（含右下 "WEMADE ENTERTAINMENT (C) 2002"）
        //   F1 = 640x48  底部条
        //   F2 = 327x20  「ID」「PASSWORD」标签条
        // 旧代码用的 F20/F23/F22 **在该库中不存在**，viewer 对三帧都返回 blank ——
        // 这正是审计文档 PRE-05 记录的"登录美术帧选择不符合目标素材"。
        // 尺寸按**原生 640x360**绘制在左上，不再拉伸到 1024x768（PRE-04）。
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 0,
            FixedSize = true,
            Size = new Vector2I(640, 360),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
        };
        _uiLayer.AddChild(background);
        GD.Print($"[LegacyLogin] 背景: Interface1c[0] size={background.Size} "
            + $"legacyUi={AutoLoginArgs.LegacyUi} legacyHud={AutoLoginArgs.LegacyHud} "
            + $"tex={(MirSkin.GetTexture(LibraryFile.Interface1c, 0) != null)}");

        // **移除 4 个不存在的动画层与 logo 层**：它们引用 Interface1c 的
        // 2200/2400/2300/2500（动画）与 23/22（logo），而该库**只有 0/1/2 三帧**。
        // 保留它们等于在登录页上叠 5 个空控件。
        //
        // 原版登录的动画 logo 来自 **Data/ei_Login.dat**（`file` 独立识别：
        // RIFF AVI 640x360 ~30fps，视频 **Intel Indeo 5.0** + PCM 立体声 32kHz，38MB）。
        // Indeo 5.0 不在 Godot 的 VideoStream 解码范围内，**本轮不实现视频播放**，
        // 故 logo 区暂缺 —— 这一点如实记录，不假装已还原。

        // 底部条：Interface1c F1（实测 640x48，含版权行）。
        _uiLayer.AddChild(new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 1,
            FixedSize = true,
            Size = new Vector2I(640, 48),
            Position = new Vector2(0, 360),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // 主登录框容器 (使用 Interface[151] 贴图)
        var dialog = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 151,
        };
        _loginDialogFrame = dialog;
        _uiLayer.AddChild(dialog);

        // 原版 LoginDialog 的底框位置 (居中偏下)
        Vector2I dialogSize = MirSkin.GetSize(LibraryFile.Interface, 151);
        if (dialogSize.X <= 0 || dialogSize.Y <= 0) dialogSize = new Vector2I(780, 115);
        dialog.Position = new Vector2((viewport.X - dialogSize.X) / 2f, viewport.Y - dialogSize.Y - 20f);

        // 标题提示文字
        dialog.AddControl(new DXLabel
        {
            Text = Lang.LoginPasswordLabel14,
            TextColour = new Color(214f / 255f, 190f / 255f, 148f / 255f),
            Location = new Vector2I(280, 38),
            Size = new Vector2I(220, 18),
            IsControl = false,
        });

        // 邮箱和密码输入框 (精准放置在金属框插槽内)
        _skinEmail = new DXTextInput
        {
            Location = new Vector2I(70, 65),
            Size = new Vector2I(170, 14),
            Text = ClientSettings.RememberDetails ? ClientSettings.RememberedEMail : _emailEdit.Text,
            Border = false,
            FontSize = 8,
            TextOffsetY = -2
        };
        _skinPassword = new DXTextInput
        {
            Location = new Vector2I(357, 65),
            Size = new Vector2I(170, 14),
            Text = ClientSettings.RememberDetails ? ClientSettings.RememberedPassword : _passwordEdit.Text,
            Border = false,
            Secret = true,
            FontSize = 8,
            TextOffsetY = -2
        };
        dialog.AddControl(_skinEmail);
        dialog.AddControl(_skinPassword);
        // The legacy skin input can outlive the hidden native LineEdit during a
        // scene transition. Do not forward events into a disposed control.
        _skinEmail.TextChanged += value =>
        {
            if (_emailEdit != null && GodotObject.IsInstanceValid(_emailEdit))
                _emailEdit.Text = value;
        };
        _skinPassword.TextChanged += value =>
        {
            if (_passwordEdit != null && GodotObject.IsInstanceValid(_passwordEdit))
                _passwordEdit.Text = value;
        };

        int defaultButtonHeight = MirSkin.GetSize(LibraryFile.Interface, 16).Y;
        if (defaultButtonHeight <= 0) defaultButtonHeight = 21;

        // 登录/退出 主按钮
        _skinLogin = new DXButton { Text = Lang.LoginDialogLoginButtonLabel, FontSize = 10, TextColour = new Color(1f, .88f, .55f), TextOffsetY = -1, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(550, 60), Size = new Vector2I(100, defaultButtonHeight), Enabled = false };
        _skinExit = new DXButton { Text = Lang.CommonControlExit, FontSize = 10, TextColour = new Color(1f, .88f, .55f), TextOffsetY = -1, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(660, 60), Size = new Vector2I(100, defaultButtonHeight) };

        // 顶部功能页签按钮 (排行榜、选项、注册账号、修改密码)
        _skinRanking = new DXButton { Text = Lang.RankingRankingLabel, FontSize = 9, TextColour = new Color(1f, .88f, .55f), LibraryFile = LibraryFile.Interface, Index = 153, Location = new Vector2I(20, 0), Size = new Vector2I(68, 32) };
        _skinOptions = new DXButton { Text = Lang.LoginDialogOptionButtonLabel, FontSize = 9, TextColour = new Color(1f, .88f, .55f), LibraryFile = LibraryFile.Interface, Index = 153, Location = new Vector2I(93, 0), Size = new Vector2I(68, 32) };
        _skinRegister = new DXButton { Text = Lang.LoginRegisterLabel3, FontSize = 10, TextColour = new Color(1f, .88f, .55f), TextOffsetY = -2, LibraryFile = LibraryFile.Interface, Index = 152, Location = new Vector2I(485, 0), Size = new Vector2I(136, 32), Enabled = false };
        _skinChange = new DXButton { Text = Lang.LoginDialogChangePasswordButtonLabel, FontSize = 10, TextColour = new Color(1f, .88f, .55f), TextOffsetY = -2, LibraryFile = LibraryFile.Interface, Index = 152, Location = new Vector2I(625, 0), Size = new Vector2I(136, 32) };

        _skinLogin.MouseClick += (o, e) => OnLoginPressed();
        _skinRegister.MouseClick += (o, e) => OpenAccountDialog();
        _skinChange.MouseClick += (o, e) => OpenChangeDialog();
        _skinRanking.MouseClick += (o, e) => ToggleLoginRanking();
        _skinOptions.MouseClick += (o, e) => ToggleLoginConfig();
        _skinExit.MouseClick += (o, e) =>
        {
            MirSkin.DisposeAll();
            GetTree().Quit();
        };

        dialog.AddControl(_skinLogin);
        dialog.AddControl(_skinRegister);
        dialog.AddControl(_skinChange);
        dialog.AddControl(_skinRanking);
        dialog.AddControl(_skinOptions);
        dialog.AddControl(_skinExit);

        // 忘记密码 链接
        _skinForgot = new DXLabel { Text = Lang.LoginPasswordLabel15, FontSize = 9, TextColour = new Color(1f, .75f, .25f), TextOffsetY = -2, Location = new Vector2I(640, 38), Size = new Vector2I(100, 16), IsControl = true };
        _skinForgot.MouseEnter += (o, e) => _skinForgot.TextColour = Colors.White;
        _skinForgot.MouseLeave += (o, e) => _skinForgot.TextColour = new Color(1f, .75f, .25f);
        _skinForgot.MouseClick += (o, e) => OpenRequestResetDialog();
        dialog.AddControl(_skinForgot);

        // 记住账号 复选框
        _skinRemember = new DXCheckBox { Location = new Vector2I(490, 38), LabelBoxPadding = 4, Checked = ClientSettings.RememberDetails };
        _skinRemember.Label.Text = Lang.LoginAccountLabel;
        _skinRemember.Label.FontSize = 9;
        _skinRemember.Label.TextOffsetY = -2;
        _skinRemember.Label.TextColour = new Color(1f, .75f, .25f);
        dialog.AddControl(_skinRemember);

        // 激活账号 按钮
        _skinActivation = new DXButton { Text = Lang.ActivationTitle, FontSize = 9, TextColour = new Color(1f, .75f, .25f), TextOffsetY = -1, LibraryFile = LibraryFile.Interface, Index = -1, Location = new Vector2I(20, 36), Size = new Vector2I(72, 20) };
        _skinActivation.MouseClick += (o, e) => { _activationDialog ??= CreateActivationDialog(); WindowManager.Open(_activationDialog, _uiLayer); };
        dialog.AddControl(_skinActivation);

        // 状态提示 Label（审计实测底边 772 → 上移至 84，底边 764 留 4px 余量）
        _skinStatus = new DXLabel { Text = Lang.LoginUi492Label, FontSize = 9, TextColour = new Color(1f, .85f, .45f), DrawOutline = true, Size = new Vector2I(500, 36), Location = new Vector2I(20, 84) };
        // EI 原版登录布局（--legacy-ui 时生效）：视频背景 + 原生 640x480 坐标。
        if (AutoLoginArgs.LegacyUi)
        {
            ApplyLegacyEiLoginLayout();
            PlayLegacyBootLogo();
        }
        dialog.AddControl(_skinStatus);

        // 初始隐藏弹出的对话框（排行榜和选项配置）
        _loginRanking = new RankingDialog(false) { Position = new Vector2((viewport.X - 330) / 2f, (viewport.Y - 456) / 2f), Visible = false };
        _loginConfig = new ConfigDialog { Position = new Vector2((viewport.X - 380) / 2f, (viewport.Y - 430) / 2f), Visible = false };

        // 保留原生控件树但隐藏它。不能 QueueFree：DXTextInput 的 TextChanged
        // 仍会同步到这些字段，销毁后输入会触发 ObjectDisposedException。
        var vbox = GetNodeOrNull<VBoxContainer>("VBox");
        if (vbox != null)
            vbox.Visible = false;
        dialog.Position = new Vector2((viewport.X - dialog.Size.X) / 2f, viewport.Y - dialog.Size.Y - 20f);
    }

    /// <summary>
    /// EI 原版登录页布局（640x480 基准）。证据 `login-flow-evidence.json`
    /// 的 `screens.char_select`（screen obj 0x8A9520，ctor 0x4026E0）：
    ///
    ///   背景动画  Data/ei_Login.dat  AVI 640x360，draw rect **SetRect(+0x740, 0, 0x3C, 0x280, 0x1A4)**
    ///             = (0, 60) - (640, 420)。原版是 Indeo 5.0 AVI，Godot 只认 Ogg Theora，
    ///             故用 Tools/convert_legacy_login_video.sh 转出的 ei_Login.ogv（画面不变）。
    ///   账号输入  SetRect(+0xF44, 0x80, 0x1B8, 0xE3, 0x1C6) = **(128, 440) - (227, 454)**，99x14
    ///   密码输入  SetRect(+0xF54, 0x146, 0x1B8, 0x1A9, 0x1C6) = **(326, 440) - (425, 454)**，99x14
    ///   按钮（frame/hover_frame，全部取自 Interface1c.wil 文字精灵帧）：
    ///     连接游戏 F11 @ **(459, 436)**  96x24   —— 原版 label "选择角色 (select character)"
    ///     创建账号 F12/F13 @ **(139, 379)**  96x26
    ///     修改密码 F14/F15 @ **(279, 379)**  96x26
    ///     结束     F16/F17 @ **(439, 379)**  48x26
    ///
    /// **帧号纠偏**：证据里写的是 11/12、13/14、15/16、17/18。独立解码 Interface1c.wil
    /// 逐帧渲染确认实际配对是 **(F12,F13) (F14,F15) (F16,F17)** —— F11 是「连接游戏」单帧，
    /// F18 是空帧；F13/F15/F17 是**绿色 hover** 变体（F12/F14/F16 是白色 normal）。
    /// 故按资源实测值实现，而不是照抄证据的 +1 偏移。
    ///
    /// 原版此屏还有 phase 状态机（1=登录表单 -> 2=服务器列表 -> 3=2 秒淡出 -> parent 选角屏），
    /// 本轮先还原**静态布局与交互**，phase 过渡另列。
    /// </summary>
    private void ApplyLegacyEiLoginLayout()
    {
        // 1) EI 没有居中对话框底框。**不能设 Visible=false** —— 输入框和按钮
        // 都是这个容器的子控件，隐藏容器会把它们一起藏掉（实测第一版就是这样，
        // 截图里只有视频、没有表单）。改为清空底图纹理，并把容器移到原点，
        // 使子控件的 Location 就是 EI 的 640x480 屏幕坐标。
        if (_loginDialogFrame != null)
        {
            _loginDialogFrame.LibraryFile = LibraryFile.None;
            _loginDialogFrame.Index = -1;
            _loginDialogFrame.Position = Vector2.Zero;
            // **容器必须放大**：原 dialog 尺寸是 780x115（现代布局的居中底框），
            // 子控件在 y=440 会被容器自身裁掉 —— 实测第一版就是"位置日志全对、
            // 屏幕上什么都看不见"。EI 是整屏 640x480 布局，容器按此放宽。
            _loginDialogFrame.Size = new Vector2I(640, 480);
            _loginDialogFrame.Clip = false;
        }

        // 2) 背景动画：ei_Login.ogv @ (0,60) 640x360，循环播放。
        var videoPath = System.IO.Path.Combine(MirSkin.UiDataPath, "ei_Login.ogv");
        if (System.IO.File.Exists(videoPath))
        {
            var stream = new VideoStreamTheora { File = videoPath };
            _loginVideo = new VideoStreamPlayer
            {
                Stream = stream,
                Position = new Vector2(0, 60),
                Size = new Vector2(640, 360),
                Loop = true,
                VolumeDb = -80f,   // 原版此视频的音轨未被播放（转换时已去音轨）
            };
            _uiLayer.AddChild(_loginVideo);
            _loginVideo.Play();
            GD.Print($"[LegacyLogin] 背景视频 ei_Login.ogv @ (0,60) 640x360 已播放");
        }
        else
        {
            GD.PrintErr($"[LegacyLogin] 缺少背景视频 {videoPath}，"
                + "请运行 Tools/convert_legacy_login_video.sh");
        }

        // 3) 输入框：EI 原生 640x480 坐标。
        if (_skinEmail != null)
        {
            _skinEmail.Location = new Vector2I(128, 440);
            _skinEmail.Size = new Vector2I(99, 14);
        }
        if (_skinPassword != null)
        {
            _skinPassword.Location = new Vector2I(326, 440);
            _skinPassword.Size = new Vector2I(99, 14);
        }

        // 4) 四个按钮换成 Interface1c 的文字精灵帧（帧内含文字，故清空 Text）。
        SkinLegacyLoginButton(_skinLogin, 11, 11, new Vector2I(459, 436), new Vector2I(96, 24));
        SkinLegacyLoginButton(_skinRegister, 12, 13, new Vector2I(139, 379), new Vector2I(96, 26));
        SkinLegacyLoginButton(_skinChange, 14, 15, new Vector2I(279, 379), new Vector2I(96, 26));
        SkinLegacyLoginButton(_skinExit, 16, 17, new Vector2I(439, 379), new Vector2I(48, 26));

        // 5) EI 此屏没有的现代入口：隐藏（保留接线，避免影响其它路径）。
        foreach (var extra in new Control[] { _skinRanking, _skinOptions, _skinForgot, _skinRemember, _skinActivation })
            if (extra != null) extra.Visible = false;
        // 状态文字保留（登录失败/连接状态要显示），移到屏幕左下空白处。
        if (_skinStatus != null)
        {
            _skinStatus.Location = new Vector2I(8, 460);
            _skinStatus.Size = new Vector2I(620, 16);
        }

        GD.Print($"[LegacyLogin] EI 布局: email={_skinEmail?.Location}/{_skinEmail?.Size} vis={_skinEmail?.Visible} "
            + $"pwd={_skinPassword?.Location} login={_skinLogin?.Location}/{_skinLogin?.Size} idx={_skinLogin?.Index} vis={_skinLogin?.Visible} "
            + $"reg={_skinRegister?.Location} chg={_skinChange?.Location} exit={_skinExit?.Location} "
            + $"frame={_loginDialogFrame?.Location} frameVis={_loginDialogFrame?.Visible} canvas={_uiLayer?.GetChildCount()}");
    }

    /// <summary>
    /// EI 的开场 logo：`Data/wemade.dat`（RIFF AVI 640x360 ~30fps **149 帧 / 4.97 秒**，
    /// 视频编码 Intel Indeo 5.0）—— 山影 + "WeMade ENTERTAINMENT" 标志浮现，
    /// 播放于登录界面出现之前。
    ///
    /// Godot 的 VideoStreamPlayer 不支持 Indeo 5.0，故用
    /// `Tools/convert_legacy_login_video.sh` 转出的 `wemade.ogv`（画面不变，仅换编码）。
    ///
    /// 实现：叠在登录 UI 之上铺满，**不循环**，播完自动移除。
    /// （原版是独立 boot 阶段；这里用覆盖层达到同样的可观察效果。）
    /// </summary>
    private void PlayLegacyBootLogo()
    {
        var path = System.IO.Path.Combine(MirSkin.UiDataPath, "wemade.ogv");
        if (!System.IO.File.Exists(path))
        {
            GD.PrintErr($"[LegacyLogin] 缺少开场 logo 视频 {path}，"
                + "请运行 Tools/convert_legacy_login_video.sh");
            return;
        }
        var logo = new VideoStreamPlayer
        {
            Stream = new VideoStreamTheora { File = path },
            Position = new Vector2(0, 60),
            Size = new Vector2(640, 360),
            Loop = false,
            VolumeDb = -80f,
        };
        _uiLayer.AddChild(logo);
        logo.Play();
        logo.Finished += () => { if (IsInstanceValid(logo)) logo.QueueFree(); };
        GD.Print("[LegacyLogin] 开场 WeMade logo 开始播放 (wemade.ogv, 4.97s)");
    }

    /// <summary>把按钮换成 EI 的 Interface1c 文字精灵帧（帧内已含文字）。</summary>
    private static void SkinLegacyLoginButton(DXButton button, int normalFrame, int hoverFrame,
        Vector2I location, Vector2I size)
    {
        if (button == null) return;
        button.LibraryFile = LibraryFile.Interface1c;
        button.Index = normalFrame;
        button.HoverIndex = hoverFrame;
        button.PressedIndex = hoverFrame;
        button.FixedSize = true;
        button.Text = string.Empty;   // 文字在帧里
        button.Location = location;
        button.Size = size;
    }

    private static void AddLoginAnimation(DXControl parent, int baseIndex, int frameCount, int seconds, bool loop, bool offset, bool blend)
    {
        parent.AddControl(new DXAnimatedControl
        {
            LibraryFile = LibraryFile.Interface1c,
            BaseIndex = baseIndex,
            FrameCount = frameCount,
            AnimationDelay = TimeSpan.FromSeconds(seconds),
            Animated = true,
            Loop = loop,
            UseOffSet = offset,
            Blend = blend,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }
}
