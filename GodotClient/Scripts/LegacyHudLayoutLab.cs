using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using S = Library.Network.ServerPackets;

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
        _hud.ApplyLegacyEiStatsLayout();
        _canvas.AddChild(_hud);
        _hud.SetLevel(70);
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
        _belt.ApplyLegacyEiPotionBeltLayout();
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
        bool expandedRequested = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            expandedRequested |= arg == "--legacy-character-expanded";
        if (expandedRequested)
        {
            WindowManager.Open(_character, _canvas);
            _character.ApplyLegacyEiLayout();
            _character.SetLegacyExpandedForAudit();
        }
        bool auditRequested = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            auditRequested |= arg == "--legacy-audit";
        if (auditRequested)
            RunLegacyAudit();
        bool npcSelfTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            npcSelfTest |= arg == "--legacy-npc-selftest";
        if (npcSelfTest)
            _ = RunNpcSelfTest();
    }

    // ------------------------------------------------------------------
    // F1100 NPC 对话窗 真实运行验收（独立测试场，不连服务器）。
    // 用一个"13 行正文 + 颜色 + 内嵌选项"的样例页驱动真实控件输入链：
    //   几何审计 -> 打开 -> 逐行下滚 -> 触底禁用 -> 上滚 -> 点关闭。
    // 截图存 .artifacts/npc-f1100-acceptance-2026-09-25/，结果以
    // [NpcF1100SelfTest] PASS/FAIL 打印并按 0/1 退出。
    // 触发: --legacy-npc-selftest
    // ------------------------------------------------------------------
    private async Task AwaitFrames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>用控件本地坐标构造左键事件，喂给控件真实的 _GuiInput 处理链。</summary>
    private static InputEventMouseButton LeftClick(Control c, bool pressed)
    {
        Vector2 center = new(c.Size.X / 2f, c.Size.Y / 2f);
        return new InputEventMouseButton
        {
            Position = center,
            GlobalPosition = center,
            ButtonIndex = MouseButton.Left,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
            Pressed = pressed,
        };
    }

    /// <summary>在控件上完成一次"按下-抬起"点击，走 DXButton 真实 MouseClick 链。
    /// 注意: press/release 必须在同一帧内发出 —— DXControl._Process 一旦检测到
    /// OS 级左键未物理按住 (headless 测试场没有真实鼠标) 就会复位 IsPressed,
    /// 中间 await 帧会导致 release 被判为无效事件、MouseClick 不触发。</summary>
    private async Task ClickControl(Control c)
    {
        c._GuiInput(LeftClick(c, true));
        c._GuiInput(LeftClick(c, false));
        await AwaitFrames(1);
    }

    private async Task RunNpcSelfTest()
    {
        string shotDir = Path.Combine(ProjectSettings.GlobalizePath("res://"), "..",
            ".artifacts", "npc-f1100-acceptance-2026-09-25");
        Directory.CreateDirectory(shotDir);
        string Shot(string name)
        {
            string p = Path.Combine(shotDir, name);
            GetViewport().GetTexture().GetImage().SavePng(p);
            return p;
        }
        var fail = new List<string>();
        void Check(bool ok, string what)
        {
            GD.Print($"[NpcF1100SelfTest] {(ok ? "ok" : "FAIL")} {what}");
            if (!ok) fail.Add(what);
        }

        try
        {
            // 数据源: 优先取客户端 System.db 里的真实 NPC 页 (Globals.NPCPageList,
            // 与服务端 0x515 包同源的 DBBindingList), 对象已绑定 Collection,
            // 属性 setter 的 OnChanged 安全; 正文不足 13 行时追加样张正文。
            // 仅内存修改 —— 客户端 DB 会话只读加载, 不写回 System.db。
            string sampleText = string.Join("\n", new[]
            {
                "尊敬的顾客，欢迎光临本店。",
                "{本店经营：} 武器、防具、药水。",
                "您可以选择以下服务：",
                "[购买物品:1]",
                "[出售物品:2]",
                "[修理装备:3]",
                "[升级武器:4]",
                "今日特价：金创药半价。",
                "库存充足，数量有限，售完即止。",
                "{温馨提示：} 交易前请先确认。",
                "请勿离开发售柜台太远。",
                "营业时间：全天开放。",
                "祝您游戏愉快，再见！",
                // FCOLOR 行 token（npc-dialog-family-evidence.json type4_0x43FF92）：
                // 之后的正文行改用调色板 [eax*4 + 0x47C4A8] 的颜色。
                // 这里取索引 10 = 0x00FF00 → RGB(0,255,0) 亮绿，便于肉眼与日志核对。
                "FCOLOR 10",
                "本行应使用 FCOLOR 指定的亮绿色。",
            });
            NPCPage page = null;
            try
            {
                var real = Globals.NPCPageList?.Binding
                    .Where(p => !string.IsNullOrWhiteSpace(p.Say))
                    .OrderByDescending(p => p.Say.Length)
                    .FirstOrDefault();
                if (real != null)
                {
                    // 保留真实正文前 3 行 (DB 长正文会把 maxScroll 推到不可控),
                    // 再补样张正文, 保证总行数 13~16, 必然触发行级滚动。
                    var headLines = real.Say.Replace("\r\n", "\n").Split('\n').Take(3);
                    string head = string.Join("\n", headLines).Trim();
                    real.Say = string.IsNullOrEmpty(head) ? sampleText : head + "\n" + sampleText;
                    real.DialogType = NPCDialogType.None;
                    page = real;
                }
            }
            catch (Exception ex)
            {
                GD.Print($"[NpcF1100SelfTest] 真实 NPC 页不可用, 回退合成页: {ex.Message}");
            }
            if (page == null)
            {
                // 回退: 直接写私有字段 _Say, 绕过 OnChanged (独立对象无 Collection 会 NRE)
                page = new NPCPage();
                typeof(NPCPage).GetField("_Say", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(page, sampleText);
            }

            var resp = new S.NPCResponse { ObjectID = 1, Index = -1, Page = page };

            WindowManager.Open(_npc, _canvas);
            _npc.ShowPage(resp);
            await AwaitFrames(3);
            string s1 = Shot("npc-f1100-self-01-open-top.png");
            GD.Print($"[NpcF1100SelfTest] shot {s1}");

            var st = _npc.LegacyEiSelfState();
            Check(_npc.Visible, "dialog visible after open");
            Check(st.Ok, $"geometry audit {st.Details}");
            Check(st.MaxLine >= 5, $"maxScroll lines={st.MaxLine} (body >=13 lines, 6 visible)");
            Check(st.Line == 0 && Math.Abs(st.TextOffsetY) < 0.5, $"start at line 0 offset {st.TextOffsetY}");
            // 注意: ButtonAreas 是逐字命中区 (选项每个字一个命中区, 点哪个字都算点中),
            // 所以按 distinct id 集合核对, 不按总条数。
            var areas = _npc.LegacyText.ButtonAreas;
            var ids = areas.Select(a => a.Id).Distinct().ToList();
            Check(ids.Contains(1) && ids.Contains(2) && ids.Contains(3) && ids.Contains(4),
                $"inline option ids={string.Join(",", ids)} (sample options 1..4 present)");

            // 选项悬停: 把鼠标移到选项 1 的命中区上 → 高亮变红 (与真实点击同一命中判定);
            // 再移开 → 高亮清除。
            var opt1 = areas.First(a => a.Id == 1);
            _npc.LegacyText._GuiInput(new InputEventMouseMotion { Position = opt1.Rect.Position + opt1.Rect.Size / 2f });
            await AwaitFrames(1);
            Check(_npc.LegacyText.HoveredButtonId == 1, "hover over option 1 -> hover state 1");
            string s1b = Shot("npc-f1100-self-01b-option-hover.png");
            GD.Print($"[NpcF1100SelfTest] shot {s1b}");
            _npc.LegacyText._GuiInput(new InputEventMouseMotion { Position = new Vector2(2, 2) });
            await AwaitFrames(1);
            Check(_npc.LegacyText.HoveredButtonId != 1, "leave option 1 area -> hover cleared");

            // 下滚一行
            await ClickControl(_npc.LegacyScrollDownButton);
            st = _npc.LegacyEiSelfState();
            Check(st.Line == 1 && Math.Abs(st.TextOffsetY - (-21)) < 0.5, $"after 1 down: line={st.Line} offsetY={st.TextOffsetY}");
            string s2 = Shot("npc-f1100-self-02-scrolled-1.png");
            GD.Print($"[NpcF1100SelfTest] shot {s2}");

            // 继续下滚到触底
            int guard = 0;
            while (_npc.LegacyEiSelfState().Line < _npc.LegacyEiSelfState().MaxLine && guard < 20)
            {
                await ClickControl(_npc.LegacyScrollDownButton);
                guard++;
            }
            st = _npc.LegacyEiSelfState();
            bool atBottom = st.Line == st.MaxLine;
            Check(atBottom, $"reached bottom line={st.Line}/{st.MaxLine}");
            Check(!_npc.LegacyScrollDownButton.Enabled, "down arrow disabled at bottom");
            Check(_npc.LegacyScrollUpButton.Enabled, "up arrow enabled at bottom");
            Check(Math.Abs(st.TextOffsetY - (-21.0 * st.MaxLine)) < 0.5, $"offsetY={st.TextOffsetY} == -21*{st.MaxLine}");
            string s3 = Shot("npc-f1100-self-03-scrolled-max.png");
            GD.Print($"[NpcF1100SelfTest] shot {s3}");

            // 触底后再点禁用下箭头：状态不得变化（溢出门）
            await ClickControl(_npc.LegacyScrollDownButton);
            st = _npc.LegacyEiSelfState();
            Check(st.Line == _npc.LegacyEiSelfState().MaxLine, $"disabled down ignored, line still {st.Line}");

            // 上滚两行
            await ClickControl(_npc.LegacyScrollUpButton);
            await ClickControl(_npc.LegacyScrollUpButton);
            st = _npc.LegacyEiSelfState();
            Check(st.Line == Math.Max(0, _npc.LegacyEiSelfState().MaxLine - 2),
                $"after 2 up: line={st.Line}");
            string s4 = Shot("npc-f1100-self-04-scrolled-up2.png");
            GD.Print($"[NpcF1100SelfTest] shot {s4}");

            // 点关闭
            await ClickControl(_npc.LegacyCloseButton);
            await AwaitFrames(2);
            Check(!_npc.Visible, "dialog closed after close-button click");
            string s5 = Shot("npc-f1100-self-05-closed.png");
            GD.Print($"[NpcF1100SelfTest] shot {s5}");

            bool pass = fail.Count == 0;
            GD.Print($"[NpcF1100SelfTest] {(pass ? "PASS" : "FAIL")} failures={fail.Count} {string.Join(" | ", fail)}");
            GetTree().Quit(pass ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcF1100SelfTest] ERROR {ex}");
            GetTree().Quit(2);
        }
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
            // G6：CommunicationDialog 不再套 F350 聊天窗外壳，回归自身几何
            // （Interface 200 / 296x424）。
            && _chat.Size == new Vector2I(296, 424)
            && _config.Size == new Vector2I(248, 264);
        bool roots2 = _trade.Size == new Vector2I(484, 330) && _guild.Size == new Vector2I(446, 596);
        bool roots3 = _storage.Size == new Vector2I(205, 205) && _config.Size == new Vector2I(248, 264) && _notice.Size == new Vector2I(584, 252);
        bool orb = _hud.AuditLegacyOrb(out string orbDetails);
        bool hud = _hud.AuditLegacyHud(out string hudDetails);
        bool pass = character && inventory && magic && horse && npc && chat && quest && trade && guild && storage && config && notice && minimap && lifecycle && orb && hud && roots && roots2 && roots3;
        GD.Print($"[LegacyAudit] {(pass ? "PASS" : "FAIL")} character={character} inventory={inventory} magic={magic} horse={horse} npc={npc} chat={chat} quest={quest} trade={trade} guild={guild} storage={storage} config={config} notice={notice} minimap={minimap} lifecycle={lifecycle} orb={orb} hud={hud} roots={roots && roots2 && roots3}");
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
        GD.Print($"[LegacyAudit] hud {hudDetails}");
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
