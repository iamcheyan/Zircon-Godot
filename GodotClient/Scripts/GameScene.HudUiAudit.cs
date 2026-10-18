using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// HUD 全入口实机走查（--hud-ui-audit）：从 HUD 出发，把每一个入口都"真点"一遍，
/// 看有没有问题（灰块 / 打不开 / 打开错窗口 / 点了没反应 / 窗口跑出屏幕 / 抛异常）。
///
/// 与 --ui-mask-audit 的区别：
///   - ui-mask 是**程序化打开**每个窗口各截一张图，只证明"窗口画得出来"；
///   - 本走查是**从 HUD 与窗口内部走点击路径**：合成鼠标事件（motion → down → up）
///     交给 Godot 真实命中测试与 DXControl._GuiInput → MouseClick，
///     因此能抓"按钮没接处理器 / 处理器接错 / 控件被遮挡 / 禁用却看着像能点"这类问题。
///
/// 两个阶段（--hud-ui-audit-phase hud|windows|all）：
///   1) hud     ：主面板上逐个 HUD 按钮点击，记录它打开了哪个窗口 + 截图，然后全部关掉；
///   2) windows ：逐个窗口打开后，枚举窗口内所有可点控件逐个点击（每次点击前重开窗口，
///                保证状态干净），记录灰块增量 / 是否新开窗口 / 画面签名是否变化（0 变化 = 疑似死按钮）。
///
/// 安全：退出/注销/删除/解散类按钮**只记录不点**，避免关掉测试进程或删数据。
/// 输出：&lt;out&gt;/results.jsonl + 逐张 PNG；结束打印 SUMMARY 并（默认）退出进程。
/// </summary>
public partial class GameScene
{
    private bool _hudUiAuditStarted;

    internal void StartHudUiAudit() => _ = RunHudUiAuditAsync();

    /// <summary>点了会改变进程/数据状态的按钮关键字：只记录，不点。</summary>
    private static readonly string[] HudUiDangerWords =
    {
        "退出", "注销", "删除", "解散", "重启", "关服", "下线",
        "Exit", "Quit", "Logout", "Delete", "Shutdown", "Restart",
    };

    /// <summary>窗口清单：与 --ui-mask-audit 同一套（34 项，含背包两种模式）。</summary>
    private List<(string Label, Func<DXWindow> Get)> HudUiWindowTargets() => new()
    {
        ("mini_map", () => (DXWindow)_miniMap),
        ("buff", () => (DXWindow)_buffDialog),
        ("magic", () => (DXWindow)_magicDialog),
        ("inventory", () => (DXWindow)_inventoryDialog),
        ("character", () => (DXWindow)_characterDialog),
        ("quest_tracker", () => (DXWindow)_questTracker),
        ("quest", () => (DXWindow)_questDialog),
        ("group", () => (DXWindow)_groupDialog),
        ("guild", () => (DXWindow)_guildDialog),
        ("communication", () => (DXWindow)_communicationDialog),
        ("ranking", () => (DXWindow)_rankingDialog),
        ("help", () => (DXWindow)_helpDialog),
        ("config", () => (DXWindow)_configDialog),
        ("menu", () => (DXWindow)_menuDialog),
        ("chat_options", () => (DXWindow)_chatOptionsDialog),
        ("companion", () => (DXWindow)_companionDialog),
        ("horse", () => (DXWindow)_horseDialog),
        ("milestone", () => (DXWindow)_milestoneDialog),
        ("game_store", () => (DXWindow)_gameStoreDialog),
        ("consignment", () => (DXWindow)_consignmentDialog),
        ("market_history", () => (DXWindow)_marketHistoryDialog),
        ("monster", () => (DXWindow)_monsterDialog),
        ("trade", () => (DXWindow)_tradeDialog),
        ("fishing", () => (DXWindow)_fishingDialog),
        ("storage", () => (DXWindow)_storageDialog),
        ("companion_storage", () => (DXWindow)_npcCompanionStorageDialog),
        ("npc_dialog", () => (DXWindow)_npcDialog),
        ("npc_socket", () => (DXWindow)_npcSocketDialog),
        ("npc_quest", () => (DXWindow)_npcQuestDialog),
        ("npc_quest_list", () => (DXWindow)_npcQuestListDialog),
        ("legacy_chat", () => (DXWindow)_legacyChatDialog),
        ("big_map", () => (DXWindow)_bigMap),
    };

    /// <summary>HUD 主面板上的全部具名按钮入口（legacy 与现代都在同一块面板上）。</summary>
    private List<(string Label, DXButton Btn)> HudUiHudEntries() => new()
    {
        ("hud_inventory", _mainPanel.InventoryButton),
        ("hud_character", _mainPanel.CharacterButton),
        ("hud_spell", _mainPanel.SpellButton),
        ("hud_quest", _mainPanel.QuestButton),
        ("hud_mail", _mainPanel.MailButton),
        ("hud_group", _mainPanel.GroupButton),
        ("hud_guild", _mainPanel.GuildButton),
        ("hud_party", _mainPanel.PartyButton),
        ("hud_menu", _mainPanel.MenuButton),
        ("hud_cashshop", _mainPanel.CashShopButton),
        ("hud_exchange", _mainPanel.ExchangeButton),
        ("hud_minimap", _mainPanel.MiniMapButton),
        ("hud_skill_entry", _mainPanel.SkillEntryButton),
        ("hud_belt", _mainPanel.BeltButton),
        ("hud_exit", _mainPanel.ExitButton),      // 点它会开"退出确认"，本阶段只开框不确认
        ("hud_logout", _mainPanel.LogoutButton),  // 同上
    };

    private async Task RunHudUiAuditAsync()
    {
        string outDir = AutoLoginArgs.HudUiAuditOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[HudUi] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await HudUiFramesAsync(3);

        string phase = AutoLoginArgs.HudUiAuditPhase;
        var rows = new List<string>();
        int gray0 = DXButton.FallbackDrawReports.Count;
        GD.Print($"[HudUi] start mapIndex={_playerMapIndex} phase={phase} out={outDir} gray0={gray0}");

        try
        {
            if (phase is "hud" or "all") await HudUiPhaseHudAsync(outDir, rows);
            if (phase is "windows" or "all") await HudUiPhaseWindowsAsync(outDir, rows);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudUi] 走查异常终止: {ex.GetType().Name}: {ex.Message}");
            rows.Add(HudUiRow(new { phase = "fatal", note = ex.GetType().Name + ": " + ex.Message }));
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[HudUi] 写 results.jsonl 失败: {ex.Message}"); }

        int grays = DXButton.FallbackDrawReports.Count - gray0;
        bool pass = grays == 0;
        GD.Print($"[HudUi] SUMMARY clicks={rows.Count} gray_blocks={grays} {(pass ? "PASS" : "FAIL")}");
        if (AutoLoginArgs.HudUiAuditQuit) GetTree().Quit(pass ? 0 : 1);
    }

    // ---------- 阶段 1：HUD 入口 ----------


    /// <summary>
    /// 反射读控件的 MouseClick 事件订阅者数量。合成鼠标事件对 HUD 按钮不生效时（被上层控件
    /// 遮挡/不在同一画布层），这是判断"按钮到底有没有接处理器"最直接的证据：0 个订阅者 = 死按钮。
    /// </summary>
    private static int HudUiCountClickHandlers(DXControl c)
    {
        if (c == null) return -1;
        try
        {
            FieldInfo f = typeof(DXControl).GetField("MouseClick",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f?.GetValue(c) is Delegate d) return d.GetInvocationList().Length;
            return 0;
        }
        catch { return -1; }
    }

    private async Task HudUiPhaseHudAsync(string outDir, List<string> rows)
    {
        GD.Print("[HudUi] === 阶段 1：HUD 入口 ===");
        foreach (var (label, btn) in HudUiHudEntries())
        {
            if (btn == null || !IsInstanceValid(btn))
            {
                rows.Add(HudUiRow(new { phase = "hud", label, note = "button_missing" }));
                GD.Print($"[HudUi] {label}: 按钮不存在（该 UI 模式下无此入口）");
                continue;
            }

            bool visible = btn.IsVisibleInTree();
            int handlers = HudUiCountClickHandlers(btn);
            var before = WindowManager.OpenWindows.ToList();
            int grayBefore = DXButton.FallbackDrawReports.Count;
            string note = "";
            try { await HudUiClickAsync(btn); }
            catch (Exception ex) { note = "exception:" + ex.GetType().Name + ": " + ex.Message; }
            await HudUiFramesAsync(8);

            var opened = WindowManager.OpenWindows.Where(w => !before.Contains(w)).ToList();
            var closed = before.Where(w => !WindowManager.OpenWindows.Contains(w)).ToList();
            string shot = HudUiShot(outDir, label);
            int grayDelta = DXButton.FallbackDrawReports.Count - grayBefore;
            // legacy 模式下背包/状态等窗口是常显的，HUD 按钮走 Toggle -> 表现为"关闭"，
            // 所以"打开"与"关闭"都要记录，否则会把正常响应误判成没反应。
            bool responded = opened.Count > 0 || closed.Count > 0;
            string openedNames = string.Join(",", opened.Select(w => w.GetType().Name));
            string closedNames = string.Join(",", closed.Select(w => w.GetType().Name));
            rows.Add(HudUiRow(new
            {
                phase = "hud",
                label,
                text = btn.Text ?? "",
                visible,
                handlers,
                opened = opened.Select(w => w.GetType().Name).ToArray(),
                closed = closed.Select(w => w.GetType().Name).ToArray(),
                responded,
                gray = grayDelta,
                shot,
                note,
            }));
            GD.Print($"[HudUi] {label}: visible={visible} handlers={handlers} opened=[{openedNames}] "
                + $"closed=[{closedNames}] gray={grayDelta} shot={shot} {note}");

            // 回到干净状态：关掉本次打开的所有窗口（含退出/注销确认框，绝不在框内点确认）；
            // 本次被关掉的常显窗口（背包/状态）重新打开，避免影响后续入口的判断。
            foreach (DXWindow w in opened)
            {
                try { WindowManager.Close(w); } catch (Exception ex) { GD.PrintErr($"[HudUi] 关窗失败: {ex.Message}"); }
            }
            foreach (DXWindow w in closed)
            {
                try { WindowManager.Open(w, _uiLayer); } catch { }
            }
            await HudUiFramesAsync(4);
        }
    }

    // ---------- 阶段 2：窗口内控件 ----------

    private async Task HudUiPhaseWindowsAsync(string outDir, List<string> rows)
    {
        GD.Print("[HudUi] === 阶段 2：窗口内控件 ===");
        int perWindow = AutoLoginArgs.HudUiAuditLimit;

        List<string> onlyWindows = AutoLoginArgs.HudUiAuditOnly();
        foreach (var (wLabel, get) in HudUiWindowTargets()
                     .Where(t => onlyWindows.Count == 0 || onlyWindows.Contains(t.Label)))
        {
            DXWindow w = null;
            try { w = get(); }
            catch (Exception ex) { GD.PrintErr($"[HudUi] 取窗口 {wLabel} 异常: {ex.Message}"); }

            if (w == null || !IsInstanceValid(w))
            {
                rows.Add(HudUiRow(new { phase = "window", label = wLabel, note = "window_missing" }));
                continue;
            }

            try
            {
                WindowManager.Open(w, _uiLayer);
                await HudUiFramesAsync(6);
            }
            catch (Exception ex)
            {
                rows.Add(HudUiRow(new { phase = "window", label = wLabel, note = "open_failed:" + ex.Message }));
                continue;
            }

            string baseShot = HudUiShot(outDir, "win_" + wLabel);
            var rect = w.GetGlobalRect();
            var buttons = HudUiCollectButtons(w, perWindow, 4);
            bool offScreen = HudUiRectOffScreen(rect);
            rows.Add(HudUiRow(new
            {
                phase = "window",
                label = wLabel,
                buttons = buttons.Count,
                rect = new[] { rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y },
                off_screen = offScreen,
                shot = baseShot,
            }));
            GD.Print($"[HudUi] {wLabel}: 可点控件 {buttons.Count} 个 rect={rect} off_screen={offScreen} shot={baseShot}");
            WindowManager.Close(w);
            await HudUiFramesAsync(3);

            for (int i = 0; i < buttons.Count; i++)
            {
                DXButton b = buttons[i];
                if (b == null || !IsInstanceValid(b)) continue;
                string text = (b.Text ?? "").Trim();
                int clickHandlers = HudUiCountClickHandlers(b);
                string label = $"{wLabel}_b{i}" + (text.Length > 0 ? "_" + HudUiSanitize(text) : "");
                bool danger = HudUiIsDanger(text);

                try { WindowManager.Open(w, _uiLayer); await HudUiFramesAsync(5); }
                catch (Exception ex)
                {
                    rows.Add(HudUiRow(new { phase = "click", label = wLabel, index = i, text, note = "reopen_failed:" + ex.Message }));
                    continue;
                }

                if (danger)
                {
                    string skipShot = HudUiShot(outDir, label);
                    rows.Add(HudUiRow(new { phase = "click", label = wLabel, index = i, text, danger = true, shot = skipShot, note = "skipped_dangerous" }));
                    GD.Print($"[HudUi] {label}: 危险按钮（{text}），只记录不点");
                    WindowManager.Close(w);
                    await HudUiFramesAsync(3);
                    continue;
                }

                var before = WindowManager.OpenWindows.ToList();
                int grayBefore = DXButton.FallbackDrawReports.Count;
                // 先悬停一次并取"点击前"画面签名，用来判断这次点击是否产生了可见变化
                await HudUiHoverAsync(b);
                string sigBefore = HudUiSignature();
                string note = "";
                string receiver = "";
                try { receiver = await HudUiClickAsync(b); }
                catch (Exception ex) { note = "exception:" + ex.GetType().Name + ": " + ex.Message; }
                await HudUiFramesAsync(8);

                bool unreachable = receiver.StartsWith("unreachable");
                if (unreachable && note.Length == 0) note = "unreachable";
                var opened = WindowManager.OpenWindows.Where(x => !before.Contains(x) && x != w).ToList();
                string shot = HudUiShot(outDir, label);
                int grayDelta = DXButton.FallbackDrawReports.Count - grayBefore;
                string sigAfter = HudUiSignature();
                bool changed = sigBefore != sigAfter;
                rows.Add(HudUiRow(new
                {
                    phase = "click",
                    label = wLabel,
                    index = i,
                    text,
                    handlers = clickHandlers,
                    gray = grayDelta,
                    opened = opened.Select(x => x.GetType().Name).ToArray(),
                    changed,
                    receiver,
                    shot,
                    note = note.Length > 0 ? note : (changed || grayDelta != 0 || opened.Count > 0 ? "" : "no_visible_effect"),
                }));
                GD.Print($"[HudUi] {label}: text='{text}' gray={grayDelta} changed={changed} receiver={receiver} opened=[{string.Join(",", opened.Select(x => x.GetType().Name))}] shot={shot} {note}");

                foreach (DXWindow x in opened)
                {
                    try { WindowManager.Close(x); } catch { }
                }
                try { WindowManager.Close(w); } catch { }
                await HudUiFramesAsync(4);
            }
        }
    }

    // ---------- 基础设施 ----------

    private async Task HudUiFramesAsync(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (!IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>合成一次真实鼠标点击：motion（建立 hover/enter 状态）→ down → up。</summary>
    /// <summary>
    /// 悬停探测：返回屏幕点 p 上**真正会接收事件**的控件名。
    /// 走查此前只记录"打算点哪个控件"，不记录"实际谁收到了"——2026-10-18 技能窗页签
    /// 就会出现"控件被点到、0 灰块，但处理器一次没触发"的现象，无法定位截获者。
    /// </summary>
    private string HudUiReceiverAt()
    {
        try
        {
            Control hovered = GetViewport()?.GuiGetHoveredControl();
            if (hovered == null) return "(null)";
            string name = hovered.Name.ToString();
            Control parent = hovered.GetParent() as Control;
            return parent != null ? $"{parent.Name}/{name}" : name;
        }
        catch (Exception ex) { return $"err:{ex.GetType().Name}"; }
    }

    private async Task<string> HudUiClickAsync(DXControl c)
    {
        if (c == null || !IsInstanceValid(c) || !c.IsVisibleInTree()) return "not_visible";
        (Vector2 p, string method, bool hit) = await HudUiAimAsync(c);
        GD.Print($"[HudUi] 瞄准 {c.GetType().Name} 用 {method} hit={hit} at={p}");
        if (!hit) return $"unreachable:{method}";   // 三种坐标算法都没命中 → 不点，如实上报
        await HudUiFramesAsync(1);
        string receiver = HudUiReceiverAt();
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = p,
            GlobalPosition = p,
            ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left,
            Pressed = true,
        });
        await HudUiFramesAsync(1);
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = p,
            GlobalPosition = p,
            ButtonIndex = MouseButton.Left,
            Pressed = false,
        });
        return receiver;
    }

    private async Task HudUiHoverAsync(DXControl c)
    {
        if (c == null || !IsInstanceValid(c) || !c.IsVisibleInTree()) return;
        (Vector2 p, _, _) = await HudUiAimAsync(c);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        await HudUiFramesAsync(2);
    }

    /// <summary>
    /// 瞄准：算出"鼠标下方**确实就是目标控件**"的屏幕点。
    /// 起因（2026-10-18）：走查原本只按 `GetViewport().GetCanvasTransform() * 控件全局矩形中心`
    /// 换算，而那是**世界相机**的变换，对 UI 层（CanvasLayer 上的窗口控件）是错的——
    /// 实测 `GuiGetHoveredControl()` 在几乎每次点击位置都返回 null，即点根本没落在控件上。
    /// 后果：`176 击 0 灰块 PASS` 只证明了"画得出来"，没证明"点得到"。
    /// 现在改为多候选逐个试，并用悬停控件自校验；命中即用，全部未命中则退回最后一个候选并记录。
    /// </summary>
    private async Task<(Vector2 Point, string Method, bool Hit)> HudUiAimAsync(DXControl c)
    {
        var candidates = new List<(string Name, Vector2 Point)>();
        Vector2 center = c.Size * 0.5f;

        try { candidates.Add(("screen_transform", c.GetScreenTransform() * center)); }
        catch { /* 旧版 Godot 无此 API */ }
        try
        {
            Vector2 g = (c.GetGlobalTransform() * center);
            candidates.Add(("global_transform", g));
        }
        catch { }
        try
        {
            Rect2 r = c.GetGlobalRect();
            Vector2 canvasPoint = r.Position + r.Size * 0.5f;
            candidates.Add(("canvas_transform", GetViewport().GetCanvasTransform() * canvasPoint));
        }
        catch { }

        Vector2 last = candidates.Count > 0 ? candidates[candidates.Count - 1].Point : Vector2.Zero;
        foreach (var (name, point) in candidates)
        {
            last = point;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
            await HudUiFramesAsync(1);
            Control hovered = HubUiHoveredControl();
            if (hovered != null && (hovered == c || c.IsAncestorOf(hovered))) return (point, name, true);
            // 未命中时记录该候选坐标下**实际**悬停到什么：用于判断"是被别的控件挡住"还是"坐标空间不对"
            string hitName = hovered == null ? "(空)" : $"{hovered.GetType().Name}#{hovered.GetInstanceId()}";
            GD.Print($"[HudUi]   候选 {name} 未命中 at={point} 实际悬停={hitName}");
        }
        return (last, "none_hit", false);
    }

    private Control HubUiHoveredControl()
    {
        try { return GetViewport()?.GuiGetHoveredControl(); }
        catch { return null; }
    }

    private List<DXButton> HudUiCollectButtons(DXControl root, int max, int maxDepth)
    {
        var list = new List<DXButton>();
        void Walk(DXControl node, int depth)
        {
            if (node == null || depth > maxDepth || list.Count >= max) return;
            foreach (DXControl child in node.Controls)
            {
                if (list.Count >= max) return;
                if (child is DXButton b && b.IsEnabled && b.CanBePressed && b.IsVisibleInTree() && !list.Contains(b))
                    list.Add(b);
                Walk(child, depth + 1);
            }
        }
        Walk(root, 0);
        return list;
    }

    private static bool HudUiIsDanger(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string word in HudUiDangerWords)
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool HudUiRectOffScreen(Rect2 rect)
    {
        try
        {
            Rect2 vp = GetViewport().GetVisibleRect();
            return rect.Position.X < vp.Position.X - 1 || rect.Position.Y < vp.Position.Y - 1
                || rect.End.X > vp.End.X + 1 || rect.End.Y > vp.End.Y + 1;
        }
        catch { return false; }
    }

    /// <summary>画面签名：把视口缩到 32x32 取 MD5 —— 用来判断"这次点击有没有产生可见变化"（死按钮探测）。</summary>
    private string HudUiSignature()
    {
        try
        {
            Image img = GetViewport()?.GetTexture()?.GetImage();
            if (img == null) return "";
            img.Resize(32, 32, Image.Interpolation.Bilinear);
            byte[] data = img.GetData();
            return Convert.ToHexString(MD5.HashData(data));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudUi] 画面签名异常: {ex.Message}");
            return "";
        }
    }

    private string HudUiShot(string outDir, string label)
    {
        try
        {
            Image image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null) return "";
            string file = Path.Combine(outDir, label + ".png");
            image.SavePng(file);
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HudUi] 截图异常 {label}: {ex.Message}");
            return "";
        }
    }

    private static string HudUiSanitize(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '_' || ch == '-') sb.Append(ch);
            if (sb.Length >= 24) break;
        }
        return sb.Length > 0 ? sb.ToString() : "x";
    }

    private static string HudUiRow(object row)
    {
        try { return JsonSerializer.Serialize(row); }
        catch (Exception ex) { return "{\"error\":\"" + ex.GetType().Name + "\"}"; }
    }
}
