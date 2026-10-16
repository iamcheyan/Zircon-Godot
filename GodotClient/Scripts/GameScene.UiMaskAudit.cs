using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 「灰色遮罩」全 UI 巡检：把客户端所有能独立打开的窗口逐个打开、各截一张图，
/// 并汇总 DXButton 兜底灰块报告（DXButton.FallbackDrawReports）。
///
/// 背景（2026-10-16 用户报「有灰色遮罩的元素太多了，都有问题，依次修复、保证全部 UI 正常」）：
/// 灰块来自 DXButton.DrawFallbackButton() —— 按钮既没 Index 也没有可用的生成贴图时
/// 画一块灰底"保证可见"。在 legacy EI 皮肤下大量现代帧缺失/空帧，于是灰块到处出现。
/// 正确修法是给每个控件换成原版对应帧、或让它的美术由父窗承载（SuppressFallbackDraw），
/// **不是**统一切掉兜底 —— 兜底本身是"美术缺失"的信号，必须逐个消灭。
///
/// NPC 才带出来的窗口（商店/修理/强化/宠物仓库等）由 --npc-audit 覆盖（同一套灰块报告）。
///
/// 触发: --ui-mask-audit [--ui-mask-audit-out /tmp/ui_mask_audit]
/// 结果: 打印每个窗口的灰块数 + 汇总 PASS/FAIL（0 灰块为 PASS），然后退出进程。
/// </summary>
public partial class GameScene
{
    private bool _uiMaskAuditStarted;

    internal void StartUiMaskAudit() => _ = RunUiMaskAuditAsync();

    private async Task RunUiMaskAuditAsync()
    {
        string outDir = AutoLoginArgs.UiMaskAuditOut;
        try { System.IO.Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[UiMask] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await UiMaskFramesAsync(2);

        GD.Print($"[UiMask] start mapIndex={_playerMapIndex} out={outDir}");

        // 常显 HUD（主面板/腰带/魔法条/聊天条）不进列表：它们一直可见，
        // 有灰块会被下面的"常显控件灰块累计"统计到。
        var targets = new List<(string Label, Func<DXWindow> Get)>
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

        int totalGray = 0;
        foreach (var (label, get) in targets)
        {
            DXWindow window = null;
            try { window = get(); }
            catch (Exception ex) { GD.PrintErr($"[UiMask] 取窗口 {label} 异常: {ex.Message}"); }

            if (window == null || !IsInstanceValid(window))
            {
                GD.Print($"[UiMask] {label}: 不存在，跳过");
                continue;
            }

            int before = DXButton.FallbackDrawReports.Count;
            string shot = "";
            try
            {
                WindowManager.Open(window, _uiLayer);
                await UiMaskFramesAsync(4);           // 至少画一帧，灰块诊断才会记录
                shot = CaptureUiMaskShot(outDir, label);
                WindowManager.Close(window);
                await UiMaskFramesAsync(2);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[UiMask] {label}: 打开/截图异常 {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            int newGray = DXButton.FallbackDrawReports.Count - before;
            totalGray += newGray;
            GD.Print($"[UiMask] {label}: 新增灰块={newGray} shot={shot}");
            for (int i = before; i < DXButton.FallbackDrawReports.Count; i++)
                GD.Print($"[UiMask]    {label} -> {DXButton.FallbackDrawReports[i]}");
        }

        // 背包在"仓库/变卖/修补"上下文里的状态：用户截图那排动作钮（丢弃/分割/寄存/取出）
        // 只在这种状态下出现，默认状态覆盖不到。逐模式开关一次并截图。
        if (_inventoryDialog != null)
        {
            foreach (InventoryMode mode in new[] { InventoryMode.Normal, InventoryMode.Storage })
            {
                int before = DXButton.FallbackDrawReports.Count;
                try
                {
                    _inventoryDialog.SetLegacyMode(mode);
                    WindowManager.Open(_inventoryDialog, _uiLayer);
                    await UiMaskFramesAsync(4);
                    string shot = CaptureUiMaskShot(outDir, "inventory_" + mode.ToString().ToLowerInvariant());
                    int added = DXButton.FallbackDrawReports.Count - before;
                    totalGray += added;
                    GD.Print($"[UiMask] inventory[{mode}]: 新增灰块={added} shot={shot}");
                    for (int i = before; i < DXButton.FallbackDrawReports.Count; i++)
                        GD.Print($"[UiMask]    inventory[{mode}] -> {DXButton.FallbackDrawReports[i]}");
                    WindowManager.Close(_inventoryDialog);
                    await UiMaskFramesAsync(2);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[UiMask] inventory[{mode}] 异常: {ex.GetType().Name}: {ex.Message}");
                }
            }
            _inventoryDialog.SetLegacyMode(InventoryMode.Normal);
        }

        GD.Print($"[UiMask] 本机常显控件（主面板/小地图/腰带/聊天条）灰块累计={DXButton.FallbackDrawReports.Count - totalGray}");
        GD.Print($"[UiMask] {(DXButton.FallbackDrawReports.Count == 0 ? "PASS" : "FAIL")} "
            + $"灰块总数={DXButton.FallbackDrawReports.Count}");
        GetTree().Quit(DXButton.FallbackDrawReports.Count == 0 ? 0 : 1);
    }

    private async Task UiMaskFramesAsync(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (!IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private string CaptureUiMaskShot(string outDir, string label)
    {
        try
        {
            var image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null) return "";
            string file = System.IO.Path.Combine(outDir, label + ".png");
            image.SavePng(file);
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[UiMask] 截图异常 {label}: {ex.Message}");
            return "";
        }
    }
}
