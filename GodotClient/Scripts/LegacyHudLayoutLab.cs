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
    private LogoutConfirmDialog _logout;
    private ConfirmDialog _confirmLegacy;
    private LogoutConfirmDialog _f950Input;
    private ExitGameDialog _exitGame;

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
        // 小地图是 HUD 固定矩形，不在 LegacyUiSkin 的窗口 profile 表里：
        // 游戏内由 GameScene 显式调用（GameScene.cs:5192-5193），测试场同样显式调用。
        // 不调的话截到的是现代小地图（卷轴美术 / LargeMiniMapSize 300），
        // 会让人误以为 legacy 布局坏了，而实际上它此前根本没人验证。
        _miniMap.ApplyLegacyEiLayout();
        _miniMap.Location = new Vector2I(Math.Max(0, 800 - 128), 0);
        _exit = AddWindow(new ExitDialog(), new Vector2I(274, 230));
        // EI 原版「注销人物」F950 确认框：位置由原版证据给出 (220,151)。
        _logout = AddWindow(new LogoutConfirmDialog("返回游戏人物选择界面？", null), LogoutConfirmDialog.LegacyLocation);
        // 通用 ConfirmDialog 的 legacy 外观（应等同 F950，验证它不再用 F281）。
        _confirmLegacy = AddWindow(new ConfirmDialog("您确定要执行此操作吗？", "Confirm", null),
            LogoutConfirmDialog.LegacyLocation);
        // 输入型调用点（原版：转账金额 / 丢金币 / 建行会名称）——F950 + 输入框。
        _f950Input = AddWindow(new LogoutConfirmDialog("您要付给对方多少金币?", null,
            LogoutConfirmDialog.ButtonMode.YesNo, _ => { }), LogoutConfirmDialog.LegacyLocation);
        // EI 原版「退出游戏」F800 窗：位置由原版证据给出 (218,176)，不再由测试场摆放。
        _exitGame = AddWindow(new ExitGameDialog(), ExitGameDialog.LegacyLocation);
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

    /// <summary>
    /// 测试场用：打开 NPC 窗并强制显示商品面板（商店窗 id2 购买态），
    /// 便于对 F1000 外框/购买按钮做截图验证。
    /// </summary>
    private DXWindow OpenGoodsForTest()
    {
        _npc.ShowGoodsForTest();
        return _npc;
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
                "goods" or "shop" => OpenGoodsForTest(),
                "trade" => _trade,
                "guild" => _guild,
                "storage" => _storage,
                "config" or "settings" => _config,
                "notice" or "prompt" => _notice,
                "minimap" or "map" => _miniMap,
                "exit" or "logout" => _exit,
                "logoutconfirm" or "f950" => _logout,
                "confirm" or "confirmdialog" => _confirmLegacy,
                "f950input" or "goldinput" => _f950Input,
                "exitgame" or "quit" => _exitGame,
                _ => null,
            };
            if (window != null) WindowManager.Open(window, _canvas);
            break;
        }

        // 行会 legacy 列表截图取证：装载假行会并打开窗口（不退出，供 scrot 截图）。
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg != "--legacy-guild-sample") continue;
            WindowManager.Open(_guild, _canvas);
            _guild.LoadSampleGuildForTest();
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

        bool inventoryOverTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            inventoryOverTest |= arg == "--legacy-inventory-overtest";
        if (inventoryOverTest)
        {
            RunLegacyInventoryOverflowTest();
            return;
        }

        bool bagSamples = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            bagSamples |= arg == "--legacy-bag-samples";
        if (bagSamples)
        {
            RunLegacyBagSampleShot();
            return;
        }
        bool npcSelfTest = false;
        bool charSelfTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            npcSelfTest |= arg == "--legacy-npc-selftest";
            charSelfTest |= arg == "--legacy-character-selftest";
        }
        if (charSelfTest)
        {
            var (cok, cdetails) = CharacterDialog.RunLegacyAttributeFormatSelfTest();
            GD.Print($"[LegacyCharacterSelfTest] {(cok ? "PASS" : "FAIL")} {cdetails}");
            GetTree().Quit(cok ? 0 : 1);
            return;
        }
        bool tipSelfTest = false;
        bool keySelfTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            tipSelfTest |= arg == "--legacy-tooltip-selftest";
            keySelfTest |= arg == "--legacy-keychain-selftest";
        }
        if (keySelfTest)
        {
            var (kok, kdetails) = LogoutConfirmDialog.RunKeyboardChainSelfTest();
            GD.Print($"[LegacyKeyChainSelfTest] {(kok ? "PASS" : "FAIL")} {kdetails}");
            GetTree().Quit(kok ? 0 : 1);
            return;
        }
        if (tipSelfTest)
        {
            var (tok, tdetails) = GameScene.RunLegacyHoverRectSelfTest();
            GD.Print($"[LegacyTooltipSelfTest] {(tok ? "PASS" : "FAIL")} {tdetails}");
            GetTree().Quit(tok ? 0 : 1);
            return;
        }
        if (npcSelfTest)
            _ = RunNpcSelfTest();

        // 技能书右页（Magic.exp 段落）实机验收：打开窗口 -> 选中首行技能 ->
        // 右页应取到该技能 id 的段落。段落内容由 [LegacyMagicDetail] 日志输出。
        bool questRowsSelfTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            questRowsSelfTest |= arg == "--legacy-quest-rows-selftest";
        if (questRowsSelfTest)
        {
            // Q-2：用**真实** System.db 的 QuestInfo（独立 new QuestInfo 会触碰 MirDB
            // 集合而在 DBObject.OnChanged 抛 NRE），断言 legacy 列表行的原版几何/配色。
            // ClientUserQuest.IsComplete 会解引用 Tasks / Quest.Tasks（Globals.cs:1035），
            // 故只取 Tasks 非空的任务，并给合成实例填空 Tasks。
            var all = (Globals.QuestInfoList?.Binding ?? Enumerable.Empty<QuestInfo>())
                .Where(q => q?.Tasks != null)
                .ToList();
            if (all.Count == 0)
            {
                GD.Print("[LegacyQuestRowsSelfTest] SKIP 客户端 DB 无任务数据");
            }
            else
            {
                var samples = all.Take(3)
                    .Select((q, i) => new ClientUserQuest
                    {
                        Index = i,
                        Quest = q,
                        Track = i == 1,
                        Tasks = new List<ClientUserQuestTask>(),
                    })
                    .ToList();
                _quest.SetQuestsForTest(samples);
                var rows = _quest.LegacyListRowsForTest;
                var selected = new Color(25 / 255f, 25 / 255f, 200 / 255f);
                var normal = new Color(25 / 255f, 25 / 255f, 125 / 255f);
                bool ok = rows.Count == samples.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    var wantPos = new Vector2I(65, 90 + 15 * i);
                    bool posOk = rows[i].Position == wantPos;
                    bool colOk = Mathf.Abs(rows[i].Colour.R - normal.R) < 0.01f
                        && Mathf.Abs(rows[i].Colour.G - normal.G) < 0.01f
                        && Mathf.Abs(rows[i].Colour.B - normal.B) < 0.01f;
                    GD.Print($"[LegacyQuestRowsSelfTest] normal row{i} pos={rows[i].Position} want={wantPos} "
                        + $"posOk={posOk} colour={rows[i].Colour} colOk={colOk} text='{rows[i].Text}'");
                    ok &= posOk && colOk;
                }
                _quest.SelectFirstQuestForTest();
                var selRows = _quest.LegacyListRowsForTest;
                bool selOk = selRows.Count > 0
                    && Mathf.Abs(selRows[0].Colour.B - selected.B) < 0.01f
                    && Mathf.Abs(selRows[0].Colour.R - selected.R) < 0.01f;
                GD.Print($"[LegacyQuestRowsSelfTest] selected row0 pos={selRows[0].Position} colour={selRows[0].Colour} "
                    + $"want={selected} selOk={selOk}");
                GD.Print($"[LegacyQuestRowsSelfTest] {(ok && selOk ? "PASS" : "FAIL")} rows={rows.Count} data={all.Count}");
            }
        }

        bool magicSelfTest = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
            magicSelfTest |= arg == "--legacy-magic-selftest";
        if (magicSelfTest)
        {
            WindowManager.Open(_magic, _canvas);
            int id = _magic.SelectFirstLegacySkillForTest();
            GD.Print($"[LegacyMagicSelfTest] selectedSkillId={id} "
                + $"paragraph={(LegacySkillRowView.LegacyMagicExpParagraph(_magic?.SelectedSkillNameForTest) == null ? "null" : "ok")}");
        }
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
                // NPCIMG 行 token：真实语法是 {NPCIMG/<n>}（斜杠分隔）。
                // 证据：Mud3 服务端脚本集合 grep -rho "NPCIMG[^ ]*" 统计
                // NPCIMG/110} 43 次、NPCIMG/50} 33 次、NPCIMG/0} 31 次 …
                "{NPCIMG/110}",
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

    /// <summary>
    /// 行为自测（--legacy-inventory-overtest）：EI 背包的占用网格行数必须随
    /// 实际内容长高，F280 才有可滚范围。用真实 System.db 的物品（Image 1042
    /// = 木剑，zlsdk 实测帧 16x102 → 1x3）放 60 条记录（**超过旧的 48 槽上限**），
    /// first-fit 后应为 30 行、可视 6 行、滚轮跨度 24。期望值来自独立的 zlsdk
    /// 帧尺寸换算，不复用被测的 C# footprint 逻辑。
    /// </summary>
    private void RunLegacyInventoryOverflowTest()
    {
        var sword = Globals.ItemInfoList?.Binding?.FirstOrDefault(x => x?.Image == 1042);
        if (sword == null)
        {
            GD.Print("[LegacyInventoryOverflow] SKIP 客户端 DB 无 Image=1042 物品");
            GetTree().Quit(0);
            return;
        }

        const int count = 60; // > Globals.InventorySize (48)
        var items = new ClientUserItem[count];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ClientUserItem { Info = sword, Slot = i, Count = 1 };

        _inventory.Grid.ItemGrid = items;
        _inventory.ConfigureLegacyInventoryGrid();

        var grid = _inventory.Grid;
        int cols = grid.GridSize.X;
        int rows = grid.GridSize.Y;
        int cells = grid.Cells?.Length ?? -1;
        int range = _inventory.LegacyScrollRange;
        bool barEnabled = _inventory.LegacyScrollEnabled;
        grid.ScrollValue = range;
        int scrolled = grid.ScrollValue;

        // 木剑 1x3 竖排：cell0 是记录 0 的**原点格**（只有它画图标），
        // cell6/cell12 是同一条记录的占位格（不重复绘制，操作落回槽 0）；
        // cell1 是记录 1 的原点格。注意 anchor 槽号 != 格子号，不能用二者相等判原点。
        bool footprintOk = !grid.IsLegacyFootprintPlaceholder(0)
            && grid.IsLegacyFootprintPlaceholder(6)
            && ReferenceEquals(grid.GetItemForCell(6), items[0])
            && grid.ResolveOperationSlot(6) == 0
            && !grid.IsLegacyFootprintPlaceholder(1)
            && ReferenceEquals(grid.GetItemForCell(1), items[1])
            && grid.ResolveOperationSlot(1) == 1
            // 记录 6 的 first-fit 原点在第 3 行第 0 列（cell 18），槽号 6 != 18；
            // 旧实现会把它误判成占位格而不画，正是「大量物品不显示」的根因。
            && !grid.IsLegacyFootprintPlaceholder(18)
            && ReferenceEquals(grid.GetItemForCell(18), items[6])
            && grid.ResolveOperationSlot(18) == 6
            && grid.IsLegacyFootprintPlaceholder(24);

        int origins = 0, covered = 0;
        for (int c = 0; c < cells; c++)
        {
            if (grid.LegacyFootprintAnchor(c) < 0) continue;
            covered++;
            if (!grid.IsLegacyFootprintPlaceholder(c))
            {
                origins++;
                if (grid.GetItemForCell(c) == null) footprintOk = false;
            }
        }
        grid.GetLegacyFootprintSize(0, out int fpW, out int fpH);
        bool placementOk = origins == count && covered == count * 3 && fpW == 1 && fpH == 3;

        // 原地改内容（数组引用与长度都不变）后再 Configure，占用表必须重建；
        // 旧锚点会让移动/使用后的物品被截成单格、高亮缩成 1x1。
        var restore0 = items[0];
        items[0] = null;
        _inventory.ConfigureLegacyInventoryGrid();
        bool rebuilt = grid.LegacyFootprintAnchor(0) == 1
            && ReferenceEquals(grid.GetItemForCell(0), items[1]);
        items[0] = restore0;
        _inventory.ConfigureLegacyInventoryGrid();

        // 独立期望：60 件 1x3、6 列 → 每层 6 件占 3 行 → 10 层 = 30 行；跨度 30-6=24。
        bool ok = cols == 6 && rows == 30 && grid.VisibleHeight == 6
            && cells == 6 * rows && range == 24 && barEnabled && scrolled == 24
            && footprintOk && placementOk && rebuilt;

        GD.Print($"[LegacyInventoryOverflow] {(ok ? "PASS" : "FAIL")} cols={cols} rows={rows} cells={cells} range={range} enabled={barEnabled} scroll={scrolled} footprint={footprintOk} rebuilt={rebuilt} origins={origins}/{count} covered={covered} fp={fpW}x{fpH}");

        // --legacy-shot[=N]：不退出，打开背包供 scrot 截图目视验收；
        // 可选 N 先把滚动值设到 N（验证滚动后的内容与右侧指示块）。
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--legacy-shot")) continue;
            if (arg.Length > "--legacy-shot".Length && arg["--legacy-shot".Length] == '='
                && int.TryParse(arg[("--legacy-shot=".Length)..], out int sv))
            {
                grid.ScrollValue = Math.Clamp(sv, 0, Math.Max(0, range));
                _inventory.ConfigureLegacyInventoryGrid();
            }
            WindowManager.Open(_inventory, _canvas);
            return;
        }
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// 目视验收（--legacy-bag-samples [--legacy-hover=N]）：混合 2x3 布衣与 1x3 木剑，
    /// 打开背包供 scrot 截图，确认多列 footprint、滚动与整块高亮。
    /// </summary>
    private void RunLegacyBagSampleShot()
    {
        // 真实物品：Moonlight 武器 Image=2510（Inventory.Zl 56x190 → 2x6 大件）、
        // 男布衣 941（2x3）、木剑 1042（1x3）、金创药 5、魔法药 15。
        ItemInfo ByImage(int img) => Globals.ItemInfoList?.Binding?.FirstOrDefault(x => x?.Image == img);
        var weapon = ByImage(2510);
        var armour = ByImage(941);
        var sword = ByImage(1042);
        var potion = ByImage(5);
        var mana = ByImage(15);
        var pool = new[] { armour, sword, weapon, potion, mana, armour, sword }.Where(x => x != null).ToArray();
        if (weapon == null || pool.Length == 0)
        {
            GD.Print("[LegacyBagSample] SKIP 客户端 DB 缺样例物品");
            GetTree().Quit(0);
            return;
        }

        var items = new ClientUserItem[48];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ClientUserItem { Info = i == 0 ? weapon : pool[i % pool.Length], Slot = i, Count = i % 5 == 0 ? 7 : 1 };

        // --legacy-move=N：把 slot0 的大件原地换到 N（数组引用/长度不变），
        // 复现「移动后其他人避让」并验证占用表重建。
        int moveTo = -1;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--legacy-move=")
                && int.TryParse(arg["--legacy-move=".Length..], out int m) && m > 0 && m < items.Length)
            {
                (items[0], items[m]) = (items[m], items[0]);
                items[0].Slot = 0;
                items[m].Slot = m;
                moveTo = m;
            }

        _inventory.Grid.ItemGrid = items;
        _inventory.ConfigureLegacyInventoryGrid();
        // 预览用负重/总量文字（原版 mode-0 画在 (0x86,0x18)-(0xF0,0x26)）。
        _inventory.SetLegacyWeightPreview(44, 675);
        if (moveTo >= 0 && _inventory.Grid.GetLegacyPlacement(0, out int ms, out int moc, out int mor, out int mw, out int mh))
            GD.Print($"[LegacyBagSample] moved-to={moveTo} cell0 slot={ms} origin=({moc},{mor}) fp={mw}x{mh}");

        // --legacy-scroll=N：验证大件跨视口边界的切片绘制。
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--legacy-scroll=")
                && int.TryParse(arg["--legacy-scroll=".Length..], out int sv0))
            {
                _inventory.Grid.ScrollValue = Math.Max(0, sv0);
                _inventory.ConfigureLegacyInventoryGrid();
                GD.Print($"[LegacyBagSample] scroll={_inventory.Grid.ScrollValue}");
            }

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--legacy-hover=")
                && int.TryParse(arg["--legacy-hover=".Length..], out int h))
            {
                _inventory.Grid.NotifyLegacyHover(h, true);
                if (_inventory.Grid.GetLegacyPlacement(h, out int hs, out int hoc, out int hor, out int hw, out int hh))
                    GD.Print($"[LegacyBagSample] hover={h} slot={hs} origin=({hoc},{hor}) fp={hw}x{hh}");
                else
                    GD.Print($"[LegacyBagSample] hover={h} no-placement");
            }
            if (arg.StartsWith("--legacy-select=")
                && int.TryParse(arg["--legacy-select=".Length..], out int s)
                && s >= 0 && s < _inventory.Grid.Cells.Length && _inventory.Grid.Cells[s] != null)
            {
                _inventory.Grid.Cells[s].Selected = true;
                GD.Print($"[LegacyBagSample] select={s}");
            }
        }

        GD.Print($"[LegacyBagSample] weapon={(weapon != null)} rows={_inventory.Grid.GridSize.Y} cells={_inventory.Grid.Cells?.Length}");
        WindowManager.Open(_inventory, _canvas);
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
        // 小地图：legacy 布局 + 屏幕位置（原版固定矩形贴屏幕右上角 (672,0)-(800,128)）。
        bool minimap = _miniMap.AuditLegacyEiLayout(out string minimapDetails)
            && _miniMap.Location == new Vector2I(800 - 128, 0);
        bool lifecycle = AuditWindowLifecycle(out string lifecycleDetails);
        // GROUP-07：组队窗的邀请输入框与提交按钮必须同行（legacy 下两者一起移动）。
        bool group = _group.AuditLayout(out string groupDetails);
        bool roots = _group.Size == new Vector2I(256, 244)
            && _quest.Size == new Vector2I(340, 440)
            // G6：CommunicationDialog 不再套 F350 聊天窗外壳，回归自身几何
            // （Interface 200 / 296x424）。
            && _chat.Size == new Vector2I(296, 424)
            && _config.Size == new Vector2I(248, 264);
        bool roots2 = _trade.Size == new Vector2I(484, 330) && _guild.Size == new Vector2I(596, 446);
        bool roots3 = _storage.Size == new Vector2I(205, 205) && _config.Size == new Vector2I(248, 264) && _notice.Size == new Vector2I(584, 252);
        bool orb = _hud.AuditLegacyOrb(out string orbDetails);
        bool hud = _hud.AuditLegacyHud(out string hudDetails);
        // 商店窗 id2 的购买态面板（NPCGoodsPanel）legacy 布局。
        bool goods = _npc.AuditLegacyGoods(out string goodsDetails);
        // 关闭钮遮挡回归：legacy 布局常把内容容器放大到铺满整窗，而内容容器
        // 在构造期是**后于**关闭钮添加的 → 会盖住关闭钮并吃掉点击
        // （真机缺陷：行会窗关闭钮点不到，探针 top=_content）。此处对每个
        // legacy 窗口断言「关闭钮中心点上的最上层控件就是关闭钮本身」。
        bool closeHit = AuditCloseButtonReachability(out string closeHitDetails);
        // 行会成员列表（legacy）：原版 18 行上限 + (35,60) 原点 + 字体度量行距。
        bool guildList = _guild.RunLegacyGuildListSelfTest(out string guildListDetails);
        bool pass = character && inventory && magic && horse && npc && chat && quest && trade && guild && storage && config && notice && minimap && lifecycle && orb && hud && roots && roots2 && roots3 && goods && group && guildList && closeHit;
        GD.Print($"[LegacyAudit] {(pass ? "PASS" : "FAIL")} character={character} inventory={inventory} magic={magic} horse={horse} npc={npc} chat={chat} quest={quest} trade={trade} guild={guild} storage={storage} config={config} notice={notice} minimap={minimap} lifecycle={lifecycle} orb={orb} hud={hud} roots={roots && roots2 && roots3} goods={goods} guildList={guildList} closeHit={closeHit}");
        GD.Print($"[LegacyAudit] goods {goodsDetails}");
        GD.Print($"[LegacyAudit] character {characterDetails}");
        GD.Print($"[LegacyAudit] inventory {inventoryDetails}");
        GD.Print($"[LegacyAudit] magic {magicDetails}");
        GD.Print($"[LegacyAudit] horse {horseDetails}");
        GD.Print($"[LegacyAudit] npc {npcDetails}");
        GD.Print($"[LegacyAudit] chat {chatDetails}");
        GD.Print($"[LegacyAudit] quest {questDetails}");
        GD.Print($"[LegacyAudit] trade {tradeDetails}");
        GD.Print($"[LegacyAudit] guild {guildDetails}");
        GD.Print($"[LegacyAudit] guildList {guildListDetails}");
        GD.Print($"[LegacyAudit] storage {storageDetails}");
        GD.Print($"[LegacyAudit] group {groupDetails}");
        GD.Print($"[LegacyAudit] config {configDetails}");
        GD.Print($"[LegacyAudit] notice {noticeDetails}");
        GD.Print($"[LegacyAudit] minimap {minimapDetails}");
        GD.Print($"[LegacyAudit] lifecycle {lifecycleDetails}");
        GD.Print($"[LegacyAudit] orb {orbDetails}");
        GD.Print($"[LegacyAudit] hud {hudDetails}");
        GD.Print($"[LegacyAudit] closeHit {closeHitDetails}");
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

    /// <summary>
    /// 关闭钮可达性回归：对每个 legacy 窗口，取关闭钮矩形中心点，断言该点上
    /// **最上层**的 DXControl 就是关闭钮本身（而不是被后添加的内容容器盖住）。
    ///
    /// 为什么需要：legacy 布局常把内容容器放大到铺满整窗（如 GuildDialog 的
    /// `_content.Size = Size`），而内容容器在构造期后于关闭钮 AddControl →
    /// Godot 里后添加的兄弟节点在上层，会吃掉关闭钮的点击。真机缺陷复现：
    /// 点行会窗关闭钮无反应，命中探针 top=_content。
    /// </summary>
    private bool AuditCloseButtonReachability(out string details)
    {
        var checkedNames = new List<string>();
        var blocked = new List<string>();
        var windows = new (string Name, DXWindow Window)[]
        {
            ("character", _character), ("inventory", _inventory), ("magic", _magic),
            ("quest", _quest), ("group", _group), ("guild", _guild),
            ("config", _config), ("horse", _horse), ("notice", _notice),
            ("storage", _storage), ("trade", _trade), ("npc", _npc),
        };
        while (WindowManager.CloseTop()) { }
        foreach (var entry in windows)
        {
            // 关闭钮不一定是 DefaultCloseButton：legacy 窗口普遍自建
            // `_closeButton`（GuildDialog/CharacterDialog/...）并在 legacy 布局里
            // 重定位到 EI 实参位置。这里直接扫描窗口的直接子控件，挑出「可见的
            // DXButton 且 Index/HoverIndex/PressedIndex 用到 161/162」的那一个。
            var button = FindCloseButton(entry.Window);
            if (button == null) continue;
            WindowManager.Open(entry.Window, _canvas);
            checkedNames.Add(entry.Name);
            // 关闭钮中心点在**窗口坐标**中的位置
            Vector2I centre = button.Location + new Vector2I((int)button.Size.X / 2, (int)button.Size.Y / 2);
            if (!IsTopmostAt(entry.Window, centre, button))
                blocked.Add($"{entry.Name}@{centre}->{LastHit} order={DumpOrder(entry.Window)}");
            WindowManager.Close(entry.Window);
        }
        details = $"checked={checkedNames.Count} blocked=[{string.Join(",", blocked)}] ";
        return blocked.Count == 0;
    }

    /// <summary>
    /// 找出窗口的关闭钮：优先 <see cref="DXWindow.DefaultCloseButton"/>，
    /// 否则在直接子控件里找「可见的 DXButton 且三态帧用到 161/162」的那个
    /// （legacy 各窗口自建 `_closeButton` 后重定位到 EI 实参坐标）。
    /// </summary>
    private static DXButton FindCloseButton(DXWindow window)
    {
        if (window?.DefaultCloseButton != null) return window.DefaultCloseButton;
        DXButton fallback = null;
        foreach (var control in window.Controls)
        {
            if (control is not DXButton button || !button.Visible) continue;
            if (button.Index is 161 or 162 || button.HoverIndex is 161 or 162
                || button.PressedIndex is 161 or 162)
                return button;
            if (button.TooltipText == Lang.CommonControlClose) fallback ??= button;
        }
        return fallback;
    }

    /// <summary>
    /// 判断窗口坐标 point 处最上层的控件是否就是 <paramref name="expected"/>。
    /// 按 Controls 列表**倒序**（Godot 子节点顺序 = 绘制/命中顺序，后者在上）逐个
    /// 做矩形命中，返回第一个命中的控件。
    /// </summary>
    private static string LastHit = "-";

    private static string DumpOrder(DXWindow window)
    {
        var parts = new List<string>();
        for (int i = 0; i < window.GetChildCount(); i++)
            if (window.GetChild(i) is DXControl c) parts.Add($"{i}:{c.GetType().Name}@{c.Location}");
        return string.Join("|", parts);
    }

    private static bool IsTopmostAt(DXWindow window, Vector2I point, DXControl expected)
    {
        LastHit = "<none>";
        // 必须按 **Godot 子节点顺序**倒序：绘制与命中都遵循节点顺序，
        // 而 `DXControl.Controls` 只是记账列表 —— `BringToFront()` 走
        // `MoveChild` 只改节点顺序，不同步该列表，用它做命中判定会得出
        // 与实际点击相反的结论。
        for (int i = window.GetChildCount() - 1; i >= 0; i--)
        {
            if (window.GetChild(i) is not DXControl control) continue;
            if (!GodotObject.IsInstanceValid(control)) continue;
            if (!control.Visible || !control.IsEnabled) continue;
            if (control.MouseFilter != Control.MouseFilterEnum.Stop) continue;
            if (point.X < control.Location.X || point.Y < control.Location.Y) continue;
            if (point.X >= control.Location.X + control.Size.X) continue;
            if (point.Y >= control.Location.Y + control.Size.Y) continue;
            LastHit = $"{control.GetType().Name}@{control.Location}/{control.Size}";
            return ReferenceEquals(control, expected);
        }
        return false;
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
