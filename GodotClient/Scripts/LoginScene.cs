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
    private RankingDialog _loginRanking;
    private ConfigDialog _loginConfig;
    private LegacyLoginDialog _accountDialog, _changeDialog, _requestResetDialog, _resetDialog, _activationDialog, _requestActivationDialog;
    private readonly List<Action> _unsubscribers = new();

    public override void _Ready()
    {
        ClientSettings.Load();
        ClientSettings.ApplyDisplaySettings();
        ClientSettings.UpdateWindowTitle();
        GetViewport().SizeChanged += ClientSettings.UpdateWindowTitle;
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
        if (_pendingLoginResult == LoginResult.Success)
        {
            SoundPlayback.Stop(SoundIndex.LoginScene);
            SetStatus(string.Format(Lang.LoginCharacterLabel, _pendingCharacters.Count));
            GD.Print($"[Login] 登录成功, 角色数 {_pendingCharacters.Count}");
            var selectScene = ResourceLoader.Load<PackedScene>("res://Scenes/SelectScene.tscn");
            var selectScript = selectScene.Instantiate<SelectScene>();
            selectScript.SetCharacters(_pendingCharacters);
            GetTree().Root.AddChild(selectScript);
            QueueFree();
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
        GD.Print($"[Login] 版本校验通过, version={version}, dbKey={dbKey}");
        CallDeferred(nameof(ShowVersionOK), version);
        if (AutoLoginArgs.AutoLogin)
        {
            GD.Print($"[Login] 自动登录: {AutoLoginArgs.User}");
            _net.Connection.SendLogin(AutoLoginArgs.User, AutoLoginArgs.Password);
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
        var background = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 20,
            FixedSize = true,
            Size = new Vector2I(1024, 768),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
        };
        _uiLayer.AddChild(background);

        AddLoginAnimation(background, 2200, 100, 10, true, true, false);
        AddLoginAnimation(background, 2400, 30, 5, true, true, false);
        AddLoginAnimation(background, 2300, 30, 10, true, false, true);
        AddLoginAnimation(background, 2500, 30, 8, true, true, false);

        var logoBackground = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 23,
            Position = new Vector2((viewport.X - 564) / 2f, 25),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _uiLayer.AddChild(logoBackground);
        var logo = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface1c,
            Index = 22,
            FixedSize = true,
            Size = new Vector2I(564, 300),
            Position = new Vector2(-35, -35),
            Blend = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        logoBackground.AddControl(logo);

        // 主登录框容器 (使用 Interface[151] 贴图)
        var dialog = new DXImageControl
        {
            LibraryFile = LibraryFile.Interface,
            Index = 151,
        };
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
