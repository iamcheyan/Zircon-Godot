using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 旧版 EI 主 HUD 的独立测试场。
/// 画布固定采用原版 800x600，运行时只做等比缩放，不改变旧版坐标。
/// </summary>
public partial class LegacyHudLayoutLab : Control
{
    private const float LegacyWidth = 800f;
    private const float LegacyHeight = 600f;
    private const float MainPanelY = 465f;
    private CanvasLayer _canvas;
    private MainPanel _hud;
    private InventoryDialog _inventory;
    private CharacterDialog _character;
    private GroupDialog _group;
    private MenuDialog _menu;
    private QuestDialog _quest;
    private CommunicationDialog _chat;
    private MagicDialog _magic;
    private BeltDialog _belt;

    public override void _Ready()
    {
        _canvas = new CanvasLayer { Layer = 10 };
        AddChild(_canvas);

        var background = new ColorRect
        {
            Color = new Color("11161b"),
            Size = new Vector2(LegacyWidth, LegacyHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(background);

        _hud = new MainPanel
        {
            Location = new Vector2I(0, (int)MainPanelY),
        };
        _canvas.AddChild(_hud);
        _hud.SetHealth(100);
        _hud.SetMana(80);
        _hud.SetFocus(30);

        CreateInteractiveWindows();
        BindHudButtons();
        OpenRequestedWindow();

        var caption = new Label
        {
            Text = "Legacy EI HUD Layout Lab · 800×600 · GameInter[50]",
            Position = new Vector2(12, 10),
            Size = new Vector2(500, 22),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        caption.AddThemeColorOverride("font_color", new Color("d8bb76"));
        _canvas.AddChild(caption);

        UpdateScale();
        GetViewport().SizeChanged += UpdateScale;
    }

    private void CreateInteractiveWindows()
    {
        _inventory = AddWindow(new InventoryDialog(), new Vector2I(518, 8));
        _character = AddWindow(new CharacterDialog(), new Vector2I(8, 8));
        _group = AddWindow(new GroupDialog(), new Vector2I(272, 105));
        _menu = AddWindow(new MenuDialog(), new Vector2I(324, 180));
        _quest = AddWindow(new QuestDialog(), new Vector2I(34, 50));
        _chat = AddWindow(new CommunicationDialog(), new Vector2I(252, 80));
        _magic = AddWindow(new MagicDialog(), new Vector2I(348, 10));
        _belt = AddWindow(new BeltDialog(), new Vector2I(280, 330));
    }

    private T AddWindow<T>(T window, Vector2I location) where T : DXWindow
    {
        LegacyUiSkin.ApplyLegacyTestWindow(window, location);
        _canvas.AddChild(window);
        return window;
    }

    private void BindHudButtons()
    {
        _hud.CharacterButton.MouseClick += (o, e) => Toggle(_character);
        _hud.InventoryButton.MouseClick += (o, e) => Toggle(_inventory);
        _hud.SpellButton.MouseClick += (o, e) => Toggle(_magic);
        _hud.QuestButton.MouseClick += (o, e) => Toggle(_quest);
        _hud.MailButton.MouseClick += (o, e) => Toggle(_chat);
        _hud.BeltButton.MouseClick += (o, e) => Toggle(_belt);
        _hud.GroupButton.MouseClick += (o, e) => Toggle(_group);
        _hud.MenuButton.MouseClick += (o, e) => Toggle(_menu);
        _hud.MiniMapButton.MouseClick += (o, e) => ShowNotice("小地图入口已接入旧版 HUD");
        _hud.SkillEntryButton.MouseClick += (o, e) => Toggle(_magic);
        _hud.ExitButton.MouseClick += (o, e) => ShowNotice("退出/登出入口已接入");
        _hud.LogoutButton.MouseClick += (o, e) => ShowNotice("登出入口已接入");
        _hud.PartyButton.MouseClick += (o, e) => Toggle(_group);
        _hud.GuildButton.MouseClick += (o, e) => ShowNotice("行会窗口将在正式游戏中打开");
        _hud.ExchangeButton.MouseClick += (o, e) => ShowNotice("交易窗口需要先选中其他玩家");
    }

    private void OpenRequestedWindow()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--legacy-open=")) continue;
            string name = arg["--legacy-open=".Length..].ToLowerInvariant();
            DXWindow window = name switch
            {
                "character" => _character,
                "inventory" => _inventory,
                "magic" => _magic,
                "quest" => _quest,
                "chat" => _chat,
                "group" => _group,
                "menu" => _menu,
                "belt" => _belt,
                _ => null,
            };
            if (window != null) WindowManager.Open(window, _canvas);
            break;
        }
    }

    private void Toggle(DXWindow window) => WindowManager.Toggle(window, _canvas);

    private void ShowNotice(string text)
    {
        GD.Print($"[LegacyHudLayoutLab] {text}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.Keycode)
        {
            case Key.F2: Toggle(_character); break;
            case Key.F3: Toggle(_inventory); break;
            case Key.F4: Toggle(_magic); break;
            case Key.F5: Toggle(_quest); break;
            case Key.F6: Toggle(_chat); break;
            case Key.F7: Toggle(_group); break;
            case Key.F8: Toggle(_menu); break;
            case Key.Escape: WindowManager.CloseTop(); break;
        }
    }

    private void UpdateScale()
    {
        if (_canvas == null) return;
        Vector2 viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float scale = Mathf.Min(viewport.X / LegacyWidth, viewport.Y / LegacyHeight);
        _canvas.Transform = Transform2D.Identity
            .Translated((viewport - new Vector2(LegacyWidth, LegacyHeight) * scale) / 2f)
            .Scaled(Vector2.One * scale);
    }
}
