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
    private const float LegacyWidth = LegacyHudLayout.LogicalWidth;
    private const float LegacyHeight = LegacyHudLayout.LogicalHeight;
    private CanvasLayer _canvas;
    private MainPanel _hud;
    private InventoryDialog _inventory;
    private CharacterDialog _character;
    private GroupDialog _group;
    private QuestDialog _quest;
    private CommunicationDialog _chat;
    private MagicDialog _magic;
    private BeltDialog _belt;
    private HorseDialog _horse;
    private NPCDialog _npc;
    private TradeDialog _trade;
    private GuildDialog _guild;
    private StorageDialog _storage;
    private ConfigDialog _config;
    private NoticeDialog _notice;
    private MiniMapDialog _miniMap;
    private ExitDialog _exit;

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
            Location = LegacyHudLayout.MainPanelLocation,
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
        _quest = AddWindow(new QuestDialog(), new Vector2I(34, 50));
        _chat = AddWindow(new CommunicationDialog(), new Vector2I(252, 80));
        _magic = AddWindow(new MagicDialog(), new Vector2I(348, 10));
        _belt = AddWindow(new BeltDialog(), new Vector2I(280, 330));
        _horse = AddWindow(new HorseDialog(), new Vector2I(250, 130));
        _npc = AddWindow(new NPCDialog(), new Vector2I(120, 180));
        _trade = AddWindow(new TradeDialog(), new Vector2I(158, 110));
        _guild = AddWindow(new GuildDialog(), new Vector2I(12, 2));
        _storage = AddWindow(new StorageDialog(), new Vector2I(580, 180));
        _config = AddWindow(new ConfigDialog(), new Vector2I(280, 160));
        _notice = AddWindow(new NoticeDialog(), new Vector2I(107, 110));
        _miniMap = AddWindow(new MiniMapDialog(), new Vector2I(580, 20));
        _exit = AddWindow(new ExitDialog(), new Vector2I(274, 230));
        _notice.SetNotice("旧版公告窗口测试正文\n正文区域使用 F602 原版坐标。");
    }

    private T AddWindow<T>(T window, Vector2I location) where T : DXWindow
    {
        LegacyUiSkin.ApplyLegacyTestWindow(window, location);
        // 测试场默认只显示 HUD；由 --legacy-open 或 HUD 按钮显式打开窗口。
        // 若让 DXWindow 的默认 Visible 状态漏出，会把多个旧版窗口叠在
        // 截图里，误导后续的贴图/尺寸验收。
        window.Visible = false;
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
        _hud.MenuButton.MouseClick += (o, e) => Toggle(_config);
        _hud.MiniMapButton.MouseClick += (o, e) => Toggle(_miniMap);
        _hud.SkillEntryButton.MouseClick += (o, e) => Toggle(_magic);
        _hud.ExitButton.MouseClick += (o, e) => Toggle(_exit);
        _hud.LogoutButton.MouseClick += (o, e) => Toggle(_exit);
        _hud.PartyButton.MouseClick += (o, e) => Toggle(_group);
        _hud.GuildButton.MouseClick += (o, e) => Toggle(_guild);
        _hud.ExchangeButton.MouseClick += (o, e) => Toggle(_trade);
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
                "menu" => _config,
                "belt" => _belt,
                "horse" => _horse,
                "npc" => _npc,
                "trade" => _trade,
                "guild" => _guild,
                "storage" => _storage,
                "config" or "settings" => _config,
                "notice" or "prompt" => _notice,
                "minimap" or "map" => _miniMap,
                "exit" or "logout" => _exit,
                _ => null,
            };
            if (window != null) WindowManager.Open(window, _canvas);
            break;
        }
        bool auditRequested = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            auditRequested |= arg == "--legacy-audit";
        if (auditRequested)
            RunLegacyAudit();
    }

    private void RunLegacyAudit()
    {
        bool character = _character.AuditLegacyEiLayout(out string characterDetails);
        bool inventory = _inventory.AuditLegacyEiLayout(out string inventoryDetails);
        bool magic = _magic.AuditLegacyEiLayout(out string magicDetails);
        bool horse = _horse.AuditLegacyEiLayout(out string horseDetails);
        bool npc = _npc.AuditLegacyEiLayout(out string npcDetails);
        bool chat = _chat.AuditLegacyEiLayout(out string chatDetails);
        bool quest = _quest.AuditLegacyEiLayout(out string questDetails);
        bool trade = _trade.AuditLegacyEiLayout(out string tradeDetails);
        bool guild = _guild.AuditLegacyEiLayout(out string guildDetails);
        bool storage = _storage.AuditLegacyEiLayout(out string storageDetails);
        bool config = _config.AuditLegacyEiLayout(out string configDetails);
        bool notice = _notice.AuditLegacyEiLayout(out string noticeDetails);
        bool minimap = _miniMap.AuditLayout(out string minimapDetails);
        bool lifecycle = AuditWindowLifecycle(out string lifecycleDetails);
        bool roots = _group.Size == new Vector2I(256, 244)
            && _quest.Size == new Vector2I(340, 440)
            && _chat.Size == new Vector2I(572, 388)
            && _config.Size == new Vector2I(248, 264);
        bool roots2 = _trade.Size == new Vector2I(484, 330) && _guild.Size == new Vector2I(446, 596);
        bool roots3 = _storage.Size == new Vector2I(205, 205) && _config.Size == new Vector2I(248, 264) && _notice.Size == new Vector2I(584, 252);
        bool orb = _hud.AuditLegacyOrb(out string orbDetails);
        bool pass = character && inventory && magic && horse && npc && chat && quest && trade && guild && storage && config && notice && minimap && lifecycle && orb && roots && roots2 && roots3;
        GD.Print($"[LegacyAudit] {(pass ? "PASS" : "FAIL")} character={character} inventory={inventory} magic={magic} horse={horse} npc={npc} chat={chat} quest={quest} trade={trade} guild={guild} storage={storage} config={config} notice={notice} minimap={minimap} lifecycle={lifecycle} orb={orb} roots={roots && roots2 && roots3}");
        GD.Print($"[LegacyAudit] character {characterDetails}");
        GD.Print($"[LegacyAudit] inventory {inventoryDetails}");
        GD.Print($"[LegacyAudit] magic {magicDetails}");
        GD.Print($"[LegacyAudit] horse {horseDetails}");
        GD.Print($"[LegacyAudit] npc {npcDetails}");
        GD.Print($"[LegacyAudit] chat {chatDetails}");
        GD.Print($"[LegacyAudit] quest {questDetails}");
        GD.Print($"[LegacyAudit] trade {tradeDetails}");
        GD.Print($"[LegacyAudit] guild {guildDetails}");
        GD.Print($"[LegacyAudit] storage {storageDetails}");
        GD.Print($"[LegacyAudit] config {configDetails}");
        GD.Print($"[LegacyAudit] notice {noticeDetails}");
        GD.Print($"[LegacyAudit] minimap {minimapDetails}");
        GD.Print($"[LegacyAudit] lifecycle {lifecycleDetails}");
        GD.Print($"[LegacyAudit] orb {orbDetails}");
        GetTree().Quit(pass ? 0 : 1);
    }

    private bool AuditWindowLifecycle(out string details)
    {
        var windows = new[]
        {
            (Name: "character", Window: (DXWindow)_character),
            (Name: "inventory", Window: (DXWindow)_inventory),
            (Name: "magic", Window: (DXWindow)_magic),
            (Name: "quest", Window: (DXWindow)_quest),
            (Name: "chat", Window: (DXWindow)_chat),
            (Name: "group", Window: (DXWindow)_group),
            (Name: "config", Window: (DXWindow)_config),
            (Name: "horse", Window: (DXWindow)_horse),
            (Name: "npc", Window: (DXWindow)_npc),
            (Name: "trade", Window: (DXWindow)_trade),
            (Name: "guild", Window: (DXWindow)_guild),
            (Name: "storage", Window: (DXWindow)_storage),
            (Name: "notice", Window: (DXWindow)_notice),
            (Name: "minimap", Window: (DXWindow)_miniMap),
            (Name: "exit", Window: (DXWindow)_exit),
        };

        while (WindowManager.CloseTop()) { }
        int opened = 0;
        bool valid = true;
        foreach (var entry in windows)
        {
            WindowManager.Open(entry.Window, _canvas);
            bool openedOnce = entry.Window.Visible
                && WindowManager.OpenWindows.Count == 1
                && ReferenceEquals(WindowManager.OpenWindows[^1], entry.Window);
            WindowManager.Open(entry.Window, _canvas);
            bool duplicateSuppressed = WindowManager.OpenWindows.Count == 1;
            valid &= openedOnce && duplicateSuppressed;
            opened++;
            valid &= WindowManager.CloseTop() && !entry.Window.Visible && WindowManager.OpenWindows.Count == 0;
        }
        details = $"windows={opened} open-close={valid} stack={WindowManager.OpenWindows.Count}";
        return valid;
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
            case Key.F8: Toggle(_config); break;
            case Key.F9: Toggle(_horse); break;
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
