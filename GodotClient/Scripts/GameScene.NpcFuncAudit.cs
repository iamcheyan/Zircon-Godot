using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// NPC 功能性验证通道（<c>--npc-func-audit</c>）。
///
/// 与 <see cref="GameScene.StartNpcAudit"/>（全量巡检，只证明「点得开」）不同，这里证明
/// 「点了真的生效」：每一项目标功能都用真实 UI 路径触发，并给出可核对的断言值，
/// 只看断言不看弹窗。
///
/// 用例（每个用例一台独立 NPC，坐标取自同一份 tools/npc_audit_manifest.json）：
///   buy      #13 啊康（比奇 0）：@givegold → 点 NPC → 点「购买物品」入口 → 商品面板第 1 行双击下单
///                             断言：金币正好减少该商品价格，且背包里该物品 +1
///   teleport #39 六面神石（比奇 0）：点 NPC → 点第 1 个目的地选项
///                             断言：地图文件名变成该选项 Teleport 动作的目标图，且金币正好少 TakeGold 的值
///   storage  #147 赵老头（边境城市 01）：点 NPC → 点「寄存物品」→ 背包第一件物品搬进仓库空格
///                             断言：S.NPCStorage 到达 + 仓库格出现该物品、背包源格变空
///   sell     #19 怡美（比奇 0，可选）：商店页里右键选中一件可售物品 → 点背包「出售」按钮
///                             断言：金币增加、背包该物品减少
///
/// 输出（沿用巡检 JSONL 字段，另加 <c>func</c>）：
///   {"index":13,...,"pages":[...],"errors":[...],
///    "func":{"cases":[{"name":"buy","ok":true,"error":"","evidence":["gold 100000->99950","inv 木剑 x0->x1"]}]}}
/// 每个断言点截图写到 &lt;out&gt;/shots/&lt;index&gt;_&lt;seq&gt;_p&lt;page&gt;.png。
///
/// 真实 UI 路径说明（不绕过界面直接发包）：
///   - 对话内嵌选项：NPCTextControl._GuiInput（与鼠标点选项同一个方法）
///   - 商品下单：NPCGoodsPanel 行按钮的 DXControl 双击（走 NPCGoodsPanel.BuySelected）
///   - 仓库搬运：背包格 _GuiInput(左键按下) 拿起 → 仓库格 _GuiInput(左键按下) 放下（DXItemCell.MoveItem 真实路径）
///   - 卖出：背包格 _GuiInput(右键按下) 选中 → InventoryDialog.SellButton 点击（MouseClick → SellSelected）
///   为取到「当前渲染的商品行」给 NPCGoodsPanel 加了 4 个只读属性（VisibleRows / FirstVisibleRowIndex /
///   SelectedIndex / GoodCount），没有其它私有成员被绕过。
/// </summary>
public partial class GameScene
{
    private const double NpcFuncWaitMs = 5000.0;
    private const long NpcFuncGiveGold = 100000;

    private bool _npcFuncAuditStarted;
    private bool _npcFuncEventsHooked;

    // 到达包的计数与时间线（证据用；计数只在断言里取差值）
    private int _npcFuncCurrencyEvents, _npcFuncItemEvents, _npcFuncItemsEvents, _npcFuncMapEvents;
    private int _npcFuncStorageEvents, _npcFuncUserLocEvents, _npcFuncItemsGainedEvents, _npcFuncItemMoveEvents;
    private readonly List<string> _npcFuncEventLog = new();

    /// <summary>用例 → 目标 NPC index（清单里的 index）。</summary>
    private static readonly (string Case, int Index)[] NpcFuncCases =
    {
        ("buy", 13),
        ("teleport", 39),
        ("storage", 147),
        ("sell", 19),
    };

    private sealed class NpcFuncCaseResult
    {
        public string Name = "";
        public bool Ok;
        public string Error = "";
        public readonly List<string> Evidence = new();
    }

    // ---- 入口 ---------------------------------------------------------------

    internal void StartNpcFuncAudit() => _ = RunNpcFuncAuditAsync();

    private async Task RunNpcFuncAuditAsync()
    {
        string manifestPath = ResolveNpcAuditPath(AutoLoginArgs.NpcAuditManifest);
        string outDir = ResolveNpcAuditPath(AutoLoginArgs.NpcFuncAuditOut);
        GD.Print($"[NpcFuncAudit] start manifest={manifestPath} out={outDir}");

        List<NpcAuditEntry> entries;
        try
        {
            entries = LoadNpcAuditManifest(manifestPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcFuncAudit] manifest 解析失败: {ex.GetType().Name}: {ex.Message}");
            GetTree().Quit();
            return;
        }

        var only = AutoLoginArgs.NpcFuncAuditOnly();
        var cases = NpcFuncCases.Where(c => only.Count == 0 || only.Contains(c.Case)).ToArray();
        if (cases.Length == 0)
        {
            GD.PrintErr($"[NpcFuncAudit] --npc-func-audit-only={string.Join(",", only)} 没有匹配的用例");
            GetTree().Quit();
            return;
        }

        Directory.CreateDirectory(outDir);
        string shotsDir = Path.Combine(outDir, "shots");
        Directory.CreateDirectory(shotsDir);
        string resultsPath = Path.Combine(outDir, "results.jsonl");

        HookNpcFuncEvents();

        int ok = 0, failed = 0;
        using (var writer = new StreamWriter(resultsPath, append: false) { AutoFlush = true })
        {
            foreach (var (caseName, index) in cases)
            {
                var entry = entries.FirstOrDefault(e => e.Index == index);
                var rec = new NpcAuditRecord
                {
                    Index = index,
                    Name = entry?.Name ?? "",
                    Map = entry?.MapFile ?? "",
                    X = entry?.X ?? 0,
                    Y = entry?.Y ?? 0,
                    EntryPage = entry?.EntryPage ?? 0,
                };
                var res = new NpcFuncCaseResult { Name = caseName };

                if (entry == null)
                {
                    rec.Errors.Add("manifest_missing");
                    res.Error = "manifest_missing";
                    GD.PrintErr($"[NpcFuncAudit] {caseName}: 清单里没有 index={index}");
                }
                else
                {
                    try
                    {
                        await RunNpcFuncCaseAsync(caseName, rec, entry, res, shotsDir);
                    }
                    catch (Exception ex)
                    {
                        res.Ok = false;
                        res.Error = "exception";
                        res.Evidence.Add($"{ex.GetType().Name}: {ex.Message}");
                        GD.PrintErr($"[NpcFuncAudit] {caseName} 异常: {ex}");
                    }
                }

                rec.Cases.Add(res);
                if (res.Ok) ok++; else failed++;
                writer.WriteLine(BuildResultLine(rec));
                GD.Print($"[NpcFuncAudit] {caseName} index={index} name={rec.Name} ok={res.Ok} error={res.Error} "
                    + $"evidence=[{string.Join(" | ", res.Evidence)}]");
            }
        }

        GD.Print($"[NpcFuncAudit] SUMMARY total={cases.Length} ok={ok} failed={failed}");
        GD.Print($"[NpcFuncAudit] results={resultsPath} shots={shotsDir}");

        if (AutoLoginArgs.NpcAuditQuit)
            GetTree().Quit();
    }

    private async Task RunNpcFuncCaseAsync(string caseName, NpcAuditRecord rec, NpcAuditEntry entry,
        NpcFuncCaseResult res, string shotsDir)
    {
        await EnsureNpcDialogClosedAsync();
        switch (caseName)
        {
            case "buy": await FuncCaseBuyAsync(rec, entry, res, shotsDir); break;
            case "teleport": await FuncCaseTeleportAsync(rec, entry, res, shotsDir); break;
            case "storage": await FuncCaseStorageAsync(rec, entry, res, shotsDir); break;
            case "sell": await FuncCaseSellAsync(rec, entry, res, shotsDir); break;
            default: res.Error = "unknown_case"; break;
        }
        await EnsureNpcDialogClosedAsync();
        res.Ok = res.Error.Length == 0;
    }

    // ---- 网络事件抓取（证据：证明包真的到了） ------------------------------

    private void HookNpcFuncEvents()
    {
        var conn = _net?.Connection;
        if (conn == null || _npcFuncEventsHooked) return;
        _npcFuncEventsHooked = true;

        conn.CurrencyChangedEvent += (idx, amount) =>
        {
            _npcFuncCurrencyEvents++;
            FuncEvent($"[CurrencyChanged] currency={idx} amount={amount}");
        };
        conn.ItemChangedEvent += p =>
        {
            _npcFuncItemEvents++;
            FuncEvent($"[ItemChanged] grid={p?.Link.GridType}#{p?.Link.Slot} count={p?.Link.Count} success={p?.Success}");
        };
        conn.ItemsChangedEvent += p =>
        {
            _npcFuncItemsEvents++;
            FuncEvent($"[ItemsChanged] links={p?.Links?.Count ?? 0} success={p?.Success}");
        };
        conn.MapChangedEvent += (mapIndex, instance) =>
        {
            _npcFuncMapEvents++;
            FuncEvent($"[MapChanged] map={mapIndex} instance={instance}");
        };
        conn.NPCStorageEvent += p =>
        {
            _npcFuncStorageEvents++;
            FuncEvent($"[NPCStorage] object={p?.ObjectID}");
        };
        conn.UserLocationEvent += (dir, loc) =>
        {
            _npcFuncUserLocEvents++;
            FuncEvent($"[UserLocation] ({loc.X},{loc.Y})");
        };
        // 服务端回话（GM 命令回执、出错提示、StorageSafeZone 之类）是断言失败时最重要的线索。
        conn.ChatEvent += p =>
        {
            string text = p?.Text ?? "";
            if (text.Length > 160) text = text.Substring(0, 160) + "…";
            FuncEvent($"[Chat] type={p?.Type} text={text}");
        };
        // 购买到货走 S.ItemsGained，仓库搬运/买入落格走 S.ItemMove —— 两条都要能作为到达证据。
        conn.ItemsGainedEvent += p =>
        {
            _npcFuncItemsGainedEvents++;
            string items = p?.Items == null
                ? "-"
                : string.Join("+", p.Items.Select(i => $"{i?.Info?.ItemName}x{i?.Count}"));
            FuncEvent($"[ItemsGained] {items}");
        };
        conn.ItemMoveEvent += p =>
        {
            _npcFuncItemMoveEvents++;
            FuncEvent($"[ItemMove] {p?.FromGrid}#{p?.FromSlot} -> {p?.ToGrid}#{p?.ToSlot} success={p?.Success}");
        };
    }

    private void FuncEvent(string line)
    {
        _npcFuncEventLog.Add(line);
        if (_npcFuncEventLog.Count > 400) _npcFuncEventLog.RemoveAt(0);
    }

    /// <summary>把当前用例期间（start 之后）的到达事件摘要成证据行。</summary>
    private string FuncEventsSince(int start, params string[] prefixes)
    {
        var lines = _npcFuncEventLog.Skip(Math.Max(0, start))
            .Where(l => prefixes.Length == 0 || prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)))
            .ToList();
        if (lines.Count == 0) return "(无相关到达包)";
        if (lines.Count > 6) lines = lines.Skip(lines.Count - 6).ToList();
        return string.Join(" ; ", lines);
    }

    // ---- 读值工具 -----------------------------------------------------------

    private static NPCPage FuncPage(int index)
        => Globals.NPCPageList?.Binding?.FirstOrDefault(p => p.Index == index);

    private static bool FuncPageHasAction(NPCPage page, NPCActionType type)
        => page?.Actions != null && page.Actions.Any(a => a.ActionType == type);

    private static string FuncActionSummary(NPCPage page)
        => page?.Actions == null
            ? "-"
            : string.Join(",", page.Actions.Select(a => a.ActionType.ToString()));

    private NPCButton FuncFindButton(int pageIndex, Func<NPCPage, bool> destinationMatch)
        => FuncPage(pageIndex)?.Buttons?.FirstOrDefault(b => b.DestinationPage != null && destinationMatch(b.DestinationPage));

    /// <summary>背包网格里是否还有该物品（legacy EI 网格会按 footprint 重排，不能用固定下标判断）。</summary>
    private bool FuncGridHasItem(ItemInfo info)
        => info != null && InventoryCells != null && InventoryCells.Any(c => c?.Item?.Info == info);

    private long FuncGold()
        => Currencies?.FirstOrDefault(x => x?.Info?.Type == CurrencyType.Gold)?.Amount ?? 0;

    private string FuncMapFile()
        => Globals.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == _playerMapIndex)?.FileName ?? "?";

    /// <summary>
    /// 背包里某物品的件数。用**权威数组** Inventory 统计，不用 UI 网格 Cells：
    /// legacy EI 的背包网格是按 footprint first-fit 重建的视图，格与物品不是一一对应，
    /// 直接遍历 Cells 会重复计数（实测同一批物品被数成 3 倍）。
    /// </summary>
    private long FuncInventoryCount(ItemInfo info)
        => info == null ? 0 : (Inventory?.Where(i => i?.Info == info).Sum(i => i.Count) ?? 0);

    private static void FuncClickControl(Control control)
    {
        if (control == null) return;
        var position = control.Size / 2f;
        control._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
        control._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    private async Task<string> FuncShotAsync(NpcAuditRecord rec, NpcAuditEntry entry, string shotsDir, int pageIndex)
    {
        await NextFrameAsync();
        RefreshSceneForCapture();
        await NextFrameAsync();
        return CaptureNpcAuditShot(shotsDir, entry.Index, rec.ShotSeq++, pageIndex);
    }

    /// <summary>记录一页（含截图），沿用巡检的 pages 字段格式。</summary>
    private async Task FuncRecordPageAsync(NpcAuditRecord rec, NpcAuditEntry entry, string shotsDir, int pageIndex)
    {
        string shot = await FuncShotAsync(rec, entry, shotsDir, pageIndex);
        rec.Pages.Add(new NpcAuditPageRecord
        {
            Page = pageIndex,
            Links = CurrentNpcLinkIds(),
            Shot = shot,
            Type = _npcDialog?.CurrentPageDialogType.ToString() ?? "",
        });
    }

    // ---- 通用步骤 -----------------------------------------------------------

    /// <summary>传送 + 找 NPC + 真实点击唤起对话 + 等入口页。</summary>
    private async Task<(ObjectRenderer Npc, int Page)> FuncEnterNpcAsync(NpcAuditRecord rec, NpcAuditEntry entry,
        NpcFuncCaseResult res)
    {
        await EnsureNpcDialogClosedAsync();
        if (!await TeleportToNpcAsync(entry))
        {
            rec.Errors.Add("timeout");
            res.Error = "timeout";
            return (null, -1);
        }

        var npc = FindTargetNpc(entry);
        if (npc == null)
        {
            rec.Errors.Add("npc_not_found");
            res.Error = "npc_not_found";
            return (null, -1);
        }

        await WaitNpcCallReadyAsync();
        int shows = _npcDialog?.PageShowCount ?? 0;
        bool clicked = await ClickNpcAsync(npc);
        rec.Clicked = clicked;
        if (!clicked)
        {
            rec.Errors.Add("click_missed");
            res.Error = "click_missed";
            return (null, -1);
        }

        int page = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, shows);
        if (page < 0)
        {
            rec.Errors.Add("no_response");
            res.Error = "no_response";
            return (null, -1);
        }

        rec.EntryPage = page;
        res.Evidence.Add($"NPC object={npc.ObjectID} 入口页={page} (DialogType={_npcDialog?.CurrentPageDialogType})");
        return (npc, page);
    }

    /// <summary>在当前页上找到满足条件的选项按钮并**用真实控件路径**点它，返回新页号（失败 -1）。</summary>
    private async Task<int> FuncClickOptionAsync(ObjectRenderer npc, NpcAuditRecord rec, NpcAuditEntry entry,
        NpcFuncCaseResult res, string shotsDir, int pageIndex, Func<NPCPage, bool> destinationMatch, string errorCode)
    {
        var button = FuncFindButton(pageIndex, destinationMatch);
        if (button == null)
        {
            res.Error = errorCode;
            res.Evidence.Add($"页 {pageIndex} 上没有匹配的选项（actions={FuncActionSummary(FuncPage(pageIndex))}）");
            return -1;
        }

        var dest = button.DestinationPage;
        res.Evidence.Add($"点击选项 ButtonID={button.ButtonID} -> 页 {dest.Index} (DialogType={dest.DialogType}, actions=[{FuncActionSummary(dest)}])");

        int shows = _npcDialog?.PageShowCount ?? 0;
        if (!ClickNpcLink(button.ButtonID))
        {
            res.Error = "link_missed";
            return -1;
        }

        int next = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, shows);
        if (next < 0)
        {
            res.Error = errorCode;
            res.Evidence.Add("点击后 5s 内没有新对话页");
            return -1;
        }
        await FuncRecordPageAsync(rec, entry, shotsDir, next);
        return next;
    }

    /// <summary>商品面板第 1 行双击下单（走 NPCGoodsPanel 行按钮的真实双击处理）。</summary>
    private async Task<bool> FuncDoubleClickGoodsRowAsync(NPCGoodsPanel panel, int rowIndex, NpcFuncCaseResult res)
    {
        if (panel == null || rowIndex < 0 || rowIndex >= panel.VisibleRows.Count) return false;
        var row = panel.VisibleRows[rowIndex];
        row._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true });
        await NextFrameAsync();
        // 可堆叠商品会先弹数量确认框：点它自己的 OK 按钮（同一控件路径），不绕过界面。
        var amountDialog = UILayer?.GetChildren().OfType<ItemAmountDialog>().FirstOrDefault(d => d.Visible);
        if (amountDialog != null)
        {
            var okButton = amountDialog.Controls.OfType<DXButton>()
                .FirstOrDefault(b => b.Type == DXButton.ButtonType.Default);
            res.Evidence.Add($"数量确认框 Amount={amountDialog.Amount} OkEnabled={amountDialog.OkEnabled}，点击其 OK 按钮");
            FuncClickControl(okButton);
            await NextFrameAsync();
        }
        return true;
    }

    // ---- 用例：购买 ---------------------------------------------------------

    private async Task FuncCaseBuyAsync(NpcAuditRecord rec, NpcAuditEntry entry, NpcFuncCaseResult res, string shotsDir)
    {
        int eventStart = _npcFuncEventLog.Count;
        var (npc, entryPage) = await FuncEnterNpcAsync(rec, entry, res);
        if (npc == null) return;
        await FuncRecordPageAsync(rec, entry, shotsDir, entryPage);

        if (!await FuncGiveGoldAsync(res))
        {
            res.Error = "give_gold_failed";
            return;
        }
        long goldAfterGive = FuncGold();

        int shopPage = await FuncClickOptionAsync(npc, rec, entry, res, shotsDir, entryPage,
            p => p.DialogType == NPCDialogType.BuySell, "no_shop_button");
        if (shopPage < 0) return;

        var shop = FuncPage(shopPage);
        var good = shop?.Goods?.FirstOrDefault();
        if (good?.Item == null)
        {
            res.Error = "no_goods";
            return;
        }

        var currency = shop.Currency ?? Globals.CurrencyInfoList?.Binding?.FirstOrDefault(x => x.Type == CurrencyType.Gold);
        long cost = good.CostFor(currency, 1);
        var info = good.Item;
        long invBefore = FuncInventoryCount(info);
        res.Evidence.Add($"商品[0] {info.Local()}(ItemInfo {info.Index}) 价格={cost} 类型={info.ItemType} 堆叠={info.StackSize}");

        var panel = _npcDialog?.Controls?.OfType<NPCGoodsPanel>().FirstOrDefault();
        if (panel == null || panel.VisibleRows.Count == 0)
        {
            res.Error = "goods_panel_missing";
            return;
        }
        res.Evidence.Add($"商品面板行数={panel.VisibleRows.Count} 首行下标={panel.FirstVisibleRowIndex} 商品总数={panel.GoodCount}");

        int currencyEvents = _npcFuncCurrencyEvents;
        int itemsGainedEvents = _npcFuncItemsGainedEvents;
        await FuncDoubleClickGoodsRowAsync(panel, 0, res);

        bool settled = await WaitUntilAsync(
            () => FuncGold() == goldAfterGive - cost && FuncInventoryCount(info) == invBefore + 1,
            NpcFuncWaitMs);
        await FuncShotAsync(rec, entry, shotsDir, shopPage);

        long goldAfter = FuncGold();
        long invAfter = FuncInventoryCount(info);
        res.Evidence.Add($"断言: gold {goldAfterGive}->{goldAfter} 期望降 {cost}；inv {info.Local()} {invBefore}->{invAfter} 期望升 1");
        res.Evidence.Add($"到达包: ItemsGained+{_npcFuncItemsGainedEvents - itemsGainedEvents} "
            + $"CurrencyChanged+{_npcFuncCurrencyEvents - currencyEvents}；"
            + FuncEventsSince(eventStart, "[ItemsGained]", "[CurrencyChanged]", "[ItemMove]"));

        if (goldAfter != goldAfterGive - cost)
        {
            res.Error = "gold_mismatch";
            return;
        }
        if (invAfter != invBefore + 1)
        {
            res.Error = settled ? "inv_mismatch" : "buy_not_confirmed";
            return;
        }
    }

    private async Task<bool> FuncGiveGoldAsync(NpcFuncCaseResult res)
    {
        long before = FuncGold();
        int events = _npcFuncCurrencyEvents;
        int chatStart = _npcFuncEventLog.Count;
        string character = AutoLoginArgs.Character;
        if (string.IsNullOrWhiteSpace(character)) character = "TestHero";
        SendChat($"@givegold {character} {NpcFuncGiveGold}");
        bool ok = await WaitUntilAsync(() => FuncGold() >= before + NpcFuncGiveGold, 10000.0);
        res.Evidence.Add($"@givegold {character} {NpcFuncGiveGold}: gold {before}->{FuncGold()} "
            + $"CurrencyChanged+{_npcFuncCurrencyEvents - events} 服务端回话={FuncEventsSince(chatStart, "[Chat]")}");
        return ok;
    }

    // ---- 用例：收费传送 -----------------------------------------------------

    private async Task FuncCaseTeleportAsync(NpcAuditRecord rec, NpcAuditEntry entry, NpcFuncCaseResult res, string shotsDir)
    {
        int eventStart = _npcFuncEventLog.Count;
        var (npc, entryPage) = await FuncEnterNpcAsync(rec, entry, res);
        if (npc == null) return;
        await FuncRecordPageAsync(rec, entry, shotsDir, entryPage);

        var button = FuncFindButton(entryPage, p => FuncPageHasAction(p, NPCActionType.Teleport));
        if (button == null)
        {
            res.Error = "no_teleport_button";
            res.Evidence.Add($"页 {entryPage} 上没有 Teleport 选项（actions={FuncActionSummary(FuncPage(entryPage))}）");
            return;
        }

        var dest = button.DestinationPage;
        var teleport = dest.Actions.First(a => a.ActionType == NPCActionType.Teleport);
        string destMapFile = teleport.MapParameter1?.FileName ?? "";
        long fee = dest.Actions.Where(a => a.ActionType == NPCActionType.TakeGold).Sum(a => a.IntParameter1);
        res.Evidence.Add($"选项 ButtonID={button.ButtonID} -> 页 {dest.Index} actions=[{FuncActionSummary(dest)}] "
            + $"目标图={destMapFile} 收费={fee}");

        long goldBefore = FuncGold();
        string mapBefore = FuncMapFile();
        int idxBefore = _playerMapIndex;
        int mapEvents = _npcFuncMapEvents;
        int locEvents = _npcFuncUserLocEvents;

        if (!ClickNpcLink(button.ButtonID))
        {
            res.Error = "link_missed";
            return;
        }
        // 这类页没有正文：服务端执行 Teleport 后直接关闭对话，所以判定的是「地图真的变了」。
        bool arrived = await WaitUntilAsync(
            () => _playerMapIndex != idxBefore || !string.Equals(FuncMapFile(), mapBefore, StringComparison.OrdinalIgnoreCase),
            15000.0);
        await WaitUntilAsync(() => _mapView?.Map != null && _npcDialog?.Visible != true, 3000.0);
        await FuncShotAsync(rec, entry, shotsDir, idxBefore);

        long goldAfter = FuncGold();
        string mapAfter = FuncMapFile();
        int idxAfter = _playerMapIndex;
        res.Evidence.Add($"断言: map {idxBefore}({mapBefore}) -> {idxAfter}({mapAfter}) 期望 {destMapFile}");
        res.Evidence.Add($"断言: gold {goldBefore}->{goldAfter} 期望降 {fee}");
        res.Evidence.Add($"到达包: MapChanged+{_npcFuncMapEvents - mapEvents} UserLocation+{_npcFuncUserLocEvents - locEvents} "
            + $"dialogClosed={_npcDialog?.Visible != true}；{FuncEventsSince(eventStart, "[MapChanged]", "[UserLocation]", "[CurrencyChanged]")}");

        if (!arrived || (destMapFile.Length > 0
                && !string.Equals(mapAfter, destMapFile, StringComparison.OrdinalIgnoreCase)))
        {
            res.Error = "teleport_not_arrived";
            return;
        }
        if (fee > 0 && goldBefore - goldAfter != fee)
        {
            res.Error = "fee_mismatch";
            return;
        }
    }

    /// <summary>
    /// 真实拖放搬运：左键点源格「拿起」→ 左键点目标格「放下」。
    /// 两次 <c>_GuiInput</c> 走的就是 DXItemCell._GuiInput → MoveItem() 这条鼠标路径，
    /// 最终由 DXItemCell.MoveItem(DXItemCell) → GameScene.SendItemMove 发包。
    /// </summary>
    private async Task<bool> FuncDragToStorageAsync(DXItemCell source, DXItemCell target, NpcFuncCaseResult res)
    {
        if (source?.Item == null || target == null) return false;
        int storageSlot = target.Slot;
        DXItemCell.SelectedCell = null;
        source._GuiInput(new InputEventMouseButton { Position = source.Size / 2f, ButtonIndex = MouseButton.Left, Pressed = true });
        await NextFrameAsync();
        bool picked = DXItemCell.SelectedCell == source;
        res.Evidence.Add($"拿起背包格 slot={source.Slot} {source.Item.Info?.Local()} x{source.Item.Count} "
            + $"-> SelectedCell={(picked ? source.Slot.ToString() : "null")}");
        target._GuiInput(new InputEventMouseButton { Position = target.Size / 2f, ButtonIndex = MouseButton.Left, Pressed = true });
        bool moved = await WaitUntilAsync(() => Storage[storageSlot] != null, NpcFuncWaitMs);
        if (!moved) DXItemCell.SelectedCell = null;
        return moved;
    }

    /// <summary>
    /// 安全区是**格子级**数据（SafeZoneInfo 区域）：绕着目标 NPC、以及同一张图上其它
    /// NPC 所在的镇内点位逐个试落点，直到客户端收到 SafeZoneChanged(InSafeZone=true)。
    /// 仓库窗口是独立窗口，换坐标不影响它保持打开。
    /// </summary>
    private async Task<bool> FuncTryReachSafeZoneAsync(NpcAuditEntry entry)
    {
        var candidates = new List<(int X, int Y)>();
        (int dx, int dy)[] offsets =
        {
            (0, -3), (-3, 0), (3, 0), (0, 3), (-6, -6), (6, 6), (0, -6), (0, 6), (-10, 0), (10, 0),
        };
        foreach (var (dx, dy) in offsets) candidates.Add((entry.X + dx, entry.Y + dy));
        try
        {
            // 同一张图上的其它 NPC 坐标基本都是镇内点位（仓库/商店/传送都开在安全区里）。
            var all = LoadNpcAuditManifest(ResolveNpcAuditPath(AutoLoginArgs.NpcAuditManifest));
            foreach (var other in all.Where(e => string.Equals(e.MapFile, entry.MapFile, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((other.X, other.Y));
                candidates.Add((other.X, other.Y - 2));
            }
        }
        catch (Exception ex)
        {
            _npcFuncEventLog.Add($"[SafeZone] 候选点补充失败: {ex.Message}");
        }

        foreach (var (x, y) in candidates.Distinct().Take(24))
        {
            if (x <= 0 || y <= 0) continue;
            SendChat($"@move {entry.MapFile} {x} {y}");
            if (await WaitUntilAsync(() => InSafeZone, 1500.0))
            {
                FuncEvent($"[SafeZone] 落点({x},{y}) 命中安全区");
                return true;
            }
            FuncEvent($"[SafeZone] 落点({x},{y}) 不安全");
        }
        return InSafeZone;
    }

    // ---- 用例：仓库存取 -----------------------------------------------------

    private async Task FuncCaseStorageAsync(NpcAuditRecord rec, NpcAuditEntry entry, NpcFuncCaseResult res, string shotsDir)
    {
        int eventStart = _npcFuncEventLog.Count;
        var (npc, entryPage) = await FuncEnterNpcAsync(rec, entry, res);
        if (npc == null) return;
        await FuncRecordPageAsync(rec, entry, shotsDir, entryPage);

        var button = FuncFindButton(entryPage, p => FuncPageHasAction(p, NPCActionType.Storage));
        if (button == null)
        {
            res.Error = "no_storage_button";
            res.Evidence.Add($"页 {entryPage} 上没有 Storage 选项（actions={FuncActionSummary(FuncPage(entryPage))}）");
            return;
        }

        var dest = button.DestinationPage;
        res.Evidence.Add($"选项 ButtonID={button.ButtonID} -> 页 {dest.Index} actions=[{FuncActionSummary(dest)}]");
        res.Evidence.Add($"安全区 InSafeZone={InSafeZone}（服务端 ItemMove 的 Storage 分支要求安全区）");

        int storageEvents = _npcFuncStorageEvents;
        int shows = _npcDialog?.PageShowCount ?? 0;
        if (!ClickNpcLink(button.ButtonID))
        {
            res.Error = "link_missed";
            return;
        }

        bool opened = await WaitUntilAsync(
            () => _npcFuncStorageEvents > storageEvents && _storageDialog?.Visible == true, NpcFuncWaitMs);
        await WaitForPageAsync(npc.ObjectID, 1000.0, shows);
        await FuncShotAsync(rec, entry, shotsDir, FuncPage(entryPage) != null ? entryPage : 0);
        res.Evidence.Add($"断言: S.NPCStorage 到达 x{_npcFuncStorageEvents - storageEvents}，仓库窗口 Visible={_storageDialog?.Visible}；"
            + FuncEventsSince(eventStart, "[NPCStorage]", "[NPCClose]"));
        if (!opened)
        {
            res.Error = storageEvents == _npcFuncStorageEvents ? "storage_packet_missing" : "storage_window_missing";
            return;
        }
        // 仓库存取在服务端要求安全区（PlayerObject.ItemMove 的 Storage 分支），而安全区是
        // **格子级**数据（SafeZoneInfo 区域），NPC 门口那格不一定在里面。先等一会儿
        // （S.SafeZoneChanged 可能晚到），但不在这里判死：真试一次搬运 + 看服务端回话更准。
        if (!InSafeZone) await WaitUntilAsync(() => InSafeZone, 6000.0);
        var source = InventoryCells?.FirstOrDefault(c => c?.Item != null && !c.Locked && c.Item.Slot < Globals.InventorySize);
        if (source == null)
        {
            res.Error = "no_inventory_item";
            return;
        }
        var target = StorageCells?.FirstOrDefault(c => c != null && c.Item == null);
        if (target == null)
        {
            res.Error = "no_free_storage_slot";
            return;
        }

        int invSlot = source.Slot;
        int storageSlot = target.Slot;
        var info = source.Item.Info;
        long count = source.Item.Count;
        long invBefore = FuncInventoryCount(info);
        int itemEvents = _npcFuncItemEvents;
        int itemMoveEvents = _npcFuncItemMoveEvents;
        int chatStart = _npcFuncEventLog.Count;

        res.Evidence.Add($"搬运前 InSafeZone={InSafeZone} 位置=({_playerLocation.X},{_playerLocation.Y}) "
            + $"源=背包[{invSlot}] {info?.Local()} x{count} -> 目标=仓库[{storageSlot}]");

        // 真实拖放路径：点源格「拿起」→ 点目标格「放下」（DXItemCell.MoveItem()），同一格子不重复踩
        bool moved = await FuncDragToStorageAsync(source, target, res);
        if (!moved)
        {
            res.Evidence.Add($"首次搬运未生效，服务端回话={FuncEventsSince(chatStart, "[Chat]", "[SafeZone]")}");
            if (await FuncTryReachSafeZoneAsync(entry))
            {
                var retryTarget = StorageCells?.FirstOrDefault(c => c != null && c.Item == null);
                if (retryTarget != null)
                {
                    storageSlot = retryTarget.Slot;
                    res.Evidence.Add($"换到安全区落点 ({_playerLocation.X},{_playerLocation.Y}) 后重试 -> 仓库[{storageSlot}]");
                    moved = await FuncDragToStorageAsync(source, retryTarget, res);
                }
            }
        }
        await NextFrameAsync();
        await FuncShotAsync(rec, entry, shotsDir, 0);

        long invAfter = FuncInventoryCount(info);
        var stored = Storage[storageSlot];
        res.Evidence.Add($"断言: Storage[{storageSlot}]={(stored == null ? "null" : $"{stored.Info?.Local()} x{stored.Count}")}，"
            + $"背包 {info?.Local()} {invBefore}->{invAfter}，背包网格里还有该物品={FuncGridHasItem(info)}");
        res.Evidence.Add($"到达包: ItemMove+{_npcFuncItemMoveEvents - itemMoveEvents} ItemChanged+{_npcFuncItemEvents - itemEvents}；"
            + FuncEventsSince(eventStart, "[ItemMove]", "[ItemChanged]", "[NPCStorage]"));

        if (stored == null || stored.Info != info || stored.Count != count)
        {
            res.Error = "storage_move_failed";
            return;
        }
        if (invAfter != invBefore - count)
        {
            res.Error = "inventory_not_decremented";
            return;
        }
    }

    // ---- 用例：卖出（可选） -------------------------------------------------

    /// <summary>卖出用例的兜底候选（清单 index）：数据层没给出可卖商店时按这个顺序试。</summary>
    private static readonly int[] NpcFuncSellFallbacks = { 19, 13, 14 };

    private async Task FuncCaseSellAsync(NpcAuditRecord rec, NpcAuditEntry entry, NpcFuncCaseResult res, string shotsDir)
    {
        var entries = LoadNpcAuditManifest(ResolveNpcAuditPath(AutoLoginArgs.NpcAuditManifest));
        var tried = new List<string>();

        // 卖出的前提是「对话页 Types 非空」（服务端 PlayerObject.NPCSell 第一行就要求它，
        // 客户端也只在 Types.Count>0 时把背包切到出售模式）。清单里没有这项信息，
        // 所以从数据层反查：哪些 NPC 的入口页能通到可卖的商店页，再按名字在清单里定位。
        var sellableNames = FuncSellableNpcNames();
        var candidates = new List<NpcAuditEntry>();
        candidates.AddRange(entries.Where(e => sellableNames.Contains(e.Name))
            .OrderBy(e => e.MapFile == "0" ? 0 : 1).Take(3));
        res.Evidence.Add($"数据层可卖商店的 NPC 名字 {sellableNames.Count} 个；清单命中候选 "
            + $"[{string.Join(", ", candidates.Select(c => $"#{c.Index} {c.Name}({c.MapFile})"))}]");
        foreach (int fallback in NpcFuncSellFallbacks)
        {
            var e = entries.FirstOrDefault(x => x.Index == fallback);
            if (e != null && !candidates.Contains(e)) candidates.Add(e);
        }

        foreach (var target in candidates)
        {
            // 记录字段跟随**实际使用**的商店 NPC（清单里的 #19 只是用例的默认目标）。
            rec.Index = target.Index;
            rec.Name = target.Name;
            rec.Map = target.MapFile;
            rec.X = target.X;
            rec.Y = target.Y;
            var attempt = new NpcFuncCaseResult { Name = "sell" };
            await FuncTrySellAtAsync(rec, target, attempt, shotsDir);
            if (attempt.Error.Length == 0)
            {
                res.Evidence.Add($"卖出在 #{target.Index} {target.Name} 上成立"
                    + (tried.Count == 0 ? "" : $"（先前尝试：{string.Join("; ", tried)}）"));
                res.Evidence.AddRange(attempt.Evidence);
                return;
            }

            tried.Add($"#{target.Index} {target.Name}: {attempt.Error}");
            // 只有「这家店不收东西」才换下一家；其它失败说明链路本身有问题，直接报出来。
            if (attempt.Error is not ("no_sellable_item" or "types_empty" or "no_shop_button"))
            {
                res.Error = attempt.Error;
                res.Evidence.Add($"#{target.Index} {target.Name} 尝试失败：{attempt.Error}");
                res.Evidence.AddRange(attempt.Evidence);
                return;
            }
            await EnsureNpcDialogClosedAsync();
        }

        res.Error = "no_sellable_item";
        res.Evidence.Add($"所有候选商店都没有可售类型：{string.Join("; ", tried)}");
        // 全库统计：能卖的页到底有没有（服务端 NPCSell 要求 NPCPage.Types.Count > 0）。
        var allPages = Globals.NPCPageList?.Binding;
        int buySellPages = allPages?.Count(p => p.DialogType == NPCDialogType.BuySell) ?? 0;
        int sellablePages = allPages?.Count(p => p.DialogType == NPCDialogType.BuySell && p.Types is { Count: > 0 }) ?? 0;
        res.Evidence.Add($"全库 BuySell 页={buySellPages}，其中 Types 非空（可卖）的页={sellablePages}");
    }

    /// <summary>
    /// 数据层反查：哪些 NPC 名字（NPCInfo.NPCName）的对话树里能通到一个可卖的商店页
    /// （BuySell 且 Types 非空 —— 服务端 PlayerObject.NPCSell 的硬前提）。
    /// 入口页本身是商店页、或入口页某个按钮指向商店页都算。
    /// </summary>
    private static HashSet<string> FuncSellableNpcNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var infos = Globals.NPCInfoList?.Binding;
        if (infos == null) return names;
        foreach (var info in infos)
        {
            var entryPage = info?.EntryPage;
            if (entryPage == null || string.IsNullOrWhiteSpace(info.NPCName)) continue;
            bool sellable = IsSellableShopPage(entryPage)
                || entryPage.Buttons?.Any(b => IsSellableShopPage(b.DestinationPage)) == true;
            if (sellable) names.Add(info.NPCName);
        }
        return names;
    }

    private static bool IsSellableShopPage(NPCPage page)
        => page != null && page.DialogType == NPCDialogType.BuySell && page.Types is { Count: > 0 };

    /// <summary>
    /// 与 InventoryDialog.TrySelectForSale 同样的可售判定（背包格、非锁定、非婚戒、非无价值、
    /// Info.CanSell、类型在本店可售列表里）。<paramref name="requireUnlocked"/> = false 时允许带
    /// Locked 标记（调用方会先解锁）。
    /// </summary>
    private static bool FuncSellableCell(DXItemCell cell, HashSet<ItemType> types, bool requireUnlocked)
    {
        var item = cell?.Item;
        if (item?.Info == null || cell.Locked || cell.GridType != GridType.Inventory) return false;
        if (item.Info.CanSell != true) return false;
        if (item.Flags.HasFlag(UserItemFlags.Marriage) || item.Flags.HasFlag(UserItemFlags.Worthless)) return false;
        if (!types.Contains(item.Info.ItemType)) return false;
        if (requireUnlocked && item.Flags.HasFlag(UserItemFlags.Locked)) return false;
        return true;
    }

    private async Task FuncTrySellAtAsync(NpcAuditRecord rec, NpcAuditEntry entry, NpcFuncCaseResult res, string shotsDir)
    {
        int eventStart = _npcFuncEventLog.Count;
        var (npc, entryPage) = await FuncEnterNpcAsync(rec, entry, res);
        if (npc == null) return;
        await FuncRecordPageAsync(rec, entry, shotsDir, entryPage);

        // 优先点「可卖的商店页」；没有才退回任意 BuySell 页（好在证据里说明那页 Types 为空）。
        int shopPage = await FuncClickOptionAsync(npc, rec, entry, res, shotsDir, entryPage,
            IsSellableShopPage, "no_shop_button");
        if (shopPage < 0)
        {
            res.Error = "";
            shopPage = await FuncClickOptionAsync(npc, rec, entry, res, shotsDir, entryPage,
                p => p.DialogType == NPCDialogType.BuySell, "no_shop_button");
        }
        if (shopPage < 0) return;

        var shop = FuncPage(shopPage);
        var types = shop?.Types?.Select(t => t.ItemType).ToHashSet() ?? new HashSet<ItemType>();
        var inventoryDialog = _inventoryDialog;
        res.Evidence.Add($"#{entry.Index} {entry.Name} 商店页={shopPage} 可售类型=[{string.Join(",", types)}] "
            + $"背包出售模式={inventoryDialog?.IsSellMode} 出售按钮Visible={inventoryDialog?.SellButton?.Visible}");
        if (types.Count == 0)
        {
            // 服务端 PlayerObject.NPCSell 要求 NPCPage.Types.Count > 0，客户端也不会进出售模式。
            res.Error = "types_empty";
            return;
        }

        var panel = _npcDialog?.Controls?.OfType<NPCGoodsPanel>().FirstOrDefault();
        // 先挑一件「已经能卖」的；没有就挑同类型但带 Locked 的（NPC 买来的物品服务端
        // NPCBuy 会打上 UserItemFlags.Locked 防倒卖），走真实解锁路径后再卖。
        DXItemCell cell = InventoryCells?.FirstOrDefault(c => FuncSellableCell(c, types, true))
            ?? InventoryCells?.FirstOrDefault(c => FuncSellableCell(c, types, false));
        if (cell == null)
        {
            // 背包里没有这家店收的东西：先用同一个商店买 1 件（走真实下单路径），再卖回去。
            if (panel == null || panel.VisibleRows.Count == 0 || shop == null)
            {
                res.Error = "no_sellable_item";
                res.Evidence.Add("背包没有可售物品，且商品面板不可用");
                return;
            }
            var good = shop.Goods?.FirstOrDefault(g => g?.Item != null && types.Contains(g.Item.ItemType));
            if (good?.Item == null)
            {
                res.Error = "no_sellable_item";
                return;
            }
            long invBeforeBuy = FuncInventoryCount(good.Item);
            res.Evidence.Add($"背包无可售物品 → 先买入 {good.Item.Local()} 用于卖出验证");
            if (!await FuncGiveGoldAsync(res)) { res.Error = "give_gold_failed"; return; }
            int rowIndex = Math.Max(0, shop.Goods.IndexOf(good) - panel.FirstVisibleRowIndex);
            if (!await FuncDoubleClickGoodsRowAsync(panel, rowIndex, res)) { res.Error = "no_sellable_item"; return; }
            if (!await WaitUntilAsync(() => FuncInventoryCount(good.Item) > invBeforeBuy, NpcFuncWaitMs))
            {
                res.Error = "buy_for_sell_failed";
                return;
            }
            cell = InventoryCells?.FirstOrDefault(c => c?.Item?.Info == good.Item)
                ?? InventoryCells?.FirstOrDefault(c => FuncSellableCell(c, types, false));
        }
        if (cell == null)
        {
            res.Error = "no_sellable_item";
            res.Evidence.Add("背包里没有这家店收的、可出售的物品（也不买不到可卖的）");
            return;
        }

        // NPC 买来的物品带 Locked 标记：按键盘/点击锁定的同一条路径解锁（DXItemCell.ToggleLock
        // → GameScene.SendItemLock → C.ItemLock），否则客户端 TrySelectForSale 会直接拒绝。
        if (cell.Item.Flags.HasFlag(UserItemFlags.Locked))
        {
            res.Evidence.Add($"背包格 slot={cell.Slot} {cell.Item.Info.Local()} 带 Locked 标记 → ToggleLock() 解锁");
            cell.ToggleLock();
            bool unlocked = await WaitUntilAsync(
                () => cell.Item != null && !cell.Item.Flags.HasFlag(UserItemFlags.Locked), NpcFuncWaitMs);
            res.Evidence.Add($"解锁结果 Locked={cell.Item?.Flags.HasFlag(UserItemFlags.Locked) ?? true}（S.ItemLock 已到达）");
            if (!unlocked)
            {
                res.Error = "unlock_failed";
                return;
            }
        }

        await FuncShotAsync(rec, entry, shotsDir, shopPage);
        var info = cell.Item.Info;
        long invBefore = FuncInventoryCount(info);
        long goldBefore = FuncGold();
        int itemEvents = _npcFuncItemsEvents;
        int currencyEvents = _npcFuncCurrencyEvents;

        // 真实右键选中（DXItemCell._GuiInput 右键 → TrySelectItemForNpcSale）
        cell._GuiInput(new InputEventMouseButton { Position = cell.Size / 2f, ButtonIndex = MouseButton.Right, Pressed = true });
        await NextFrameAsync();
        bool selected = inventoryDialog?.SelectedItems?.Contains(cell) == true;
        res.Evidence.Add($"右键选中背包格 slot={cell.Slot} {info.Local()} x{cell.Item.Count} "
            + $"SelectedItems={inventoryDialog?.SelectedItems?.Count ?? 0}");
        if (!selected)
        {
            res.Error = "sell_select_failed";
            return;
        }

        // 真实点击背包窗的「出售」按钮（MouseClick → InventoryDialog.SellSelected）
        FuncClickControl(inventoryDialog.SellButton);

        bool settled = await WaitUntilAsync(
            () => FuncGold() > goldBefore && FuncInventoryCount(info) < invBefore, NpcFuncWaitMs);
        await NextFrameAsync();
        await FuncShotAsync(rec, entry, shotsDir, shopPage);

        long goldAfter = FuncGold();
        long invAfter = FuncInventoryCount(info);
        res.Evidence.Add($"断言: gold {goldBefore}->{goldAfter}（+{goldAfter - goldBefore}）；inv {info.Local()} {invBefore}->{invAfter}");
        res.Evidence.Add($"到达包: ItemsChanged+{_npcFuncItemsEvents - itemEvents} CurrencyChanged+{_npcFuncCurrencyEvents - currencyEvents}；"
            + FuncEventsSince(eventStart, "[ItemsChanged]", "[CurrencyChanged]"));

        if (goldAfter <= goldBefore)
        {
            res.Error = settled ? "sell_gold_mismatch" : "sell_not_confirmed";
            return;
        }
        if (invAfter >= invBefore)
        {
            res.Error = "sell_item_not_removed";
            return;
        }
    }
}
