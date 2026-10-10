using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

/// <summary>
/// 全量 NPC 实机巡检（<c>--npc-audit</c>）。
///
/// 目的：一条通道就能回答「230 个 NPC 里哪些真的能用」。做法是逐条读
/// <c>tools/npc_audit_manifest.json</c>，对每个 NPC：
///   1. 用游戏内聊天发 <c>@move &lt;mapFile&gt; &lt;x&gt; &lt;y-2&gt;</c> 把角色传送到它面前（Admin GM 命令，
///      必要时退回 y-3 / y+2 / x±2，坐标到位与地图切换都用轮询而不是固定 sleep）；
///   2. 在 <c>_objects</c> 里找 <c>Kind.NPC</c> 且与目标坐标/名字最接近的对象；
///   3. 用与真实鼠标完全相同的代码路径唤起对话：<c>PickObjectAtCellForAudit</c> →
///      <c>MouseObject</c> → <c>_UnhandledInput</c> 左键按下/抬起（与 StartInteractionAudit 同款）；
///   4. 等 <c>S.NPCResponse</c> 让 <c>_npcDialog</c> 显示出来（5s 超时 = no_response）；
///   5. 截图 <c>&lt;out&gt;/shots/&lt;index&gt;_&lt;seq&gt;_p&lt;pageIndex&gt;.png</c>；
///   6. DFS 遍历本页可点链接（深度 &lt;= 4、按页 Index 去重），点击走 NPCTextControl._GuiInput
///      这条真实控件路径；探索兄弟分支时重开对话并重放路径；
///   7. 关闭对话（CloseNPCDialog，等价于点 id=0 的退出链接）后进入下一个 NPC。
///
/// 结果每个 NPC 一行写进 <c>&lt;out&gt;/results.jsonl</c>（写一行立即 flush），失败原因是固定短码：
/// npc_not_found / click_missed / no_response / timeout / link_failed。
/// 注意：大量 NPC 在数据层就没有入口页，这种 no_response 是**预期观察结果**，不是巡检自身的 bug。
/// </summary>
public partial class GameScene
{
    private const int NpcAuditMaxDepth = 4;
    private const double NpcAuditTeleportBudgetMs = 25000.0;
    private const double NpcAuditResponseTimeoutMs = 5000.0;

    private bool _npcAuditStarted;

    // ---- 数据模型 -----------------------------------------------------------

    private sealed class NpcAuditEntry
    {
        public int Index;
        public string Name = "";
        public string MapFile = "";
        public string MapDesc = "";
        public int X;
        public int Y;
        public string Category = "";
        public string Mud3File = "";
        public int EntryPage;
        public readonly List<string> Services = new();
    }

    private sealed class NpcAuditPageRecord
    {
        public int Page;
        public List<int> Links = new();
        public string Shot = "";
        public string Type = "";
    }

    private sealed class NpcAuditRecord
    {
        public int Index;
        public string Name = "";
        public string Map = "";
        public int X;
        public int Y;
        public bool Clicked;
        public int EntryPage;
        public readonly List<NpcAuditPageRecord> Pages = new();
        public readonly List<string> Errors = new();
    }

    // ---- 入口 ---------------------------------------------------------------

    /// <summary>由 _Process 在进入游戏且地图就绪后调用一次；启动异步巡检流程。</summary>
    internal void StartNpcAudit() => _ = RunNpcAuditAsync();

    private async Task RunNpcAuditAsync()
    {
        string manifestPath = ResolveNpcAuditPath(AutoLoginArgs.NpcAuditManifest);
        string outDir = ResolveNpcAuditPath(AutoLoginArgs.NpcAuditOut);
        GD.Print($"[NpcAudit] start manifest={manifestPath} out={outDir} cwd={Directory.GetCurrentDirectory()}");

        List<NpcAuditEntry> entries;
        try
        {
            entries = LoadNpcAuditManifest(manifestPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcAudit] manifest 解析失败: {ex.GetType().Name}: {ex.Message}");
            GetTree().Quit();
            return;
        }

        var only = AutoLoginArgs.NpcAuditOnly();
        if (only.Count > 0)
            entries = entries.Where(x => only.Contains(x.Index)).ToList();
        int limit = AutoLoginArgs.NpcAuditLimit;
        if (limit > 0 && entries.Count > limit)
            entries = entries.Take(limit).ToList();

        if (entries.Count == 0)
        {
            GD.PrintErr($"[NpcAudit] 没有可巡检的 NPC（manifest={manifestPath}, only={only.Count}）");
            GetTree().Quit();
            return;
        }

        Directory.CreateDirectory(outDir);
        string shotsDir = Path.Combine(outDir, "shots");
        Directory.CreateDirectory(shotsDir);
        string resultsPath = Path.Combine(outDir, "results.jsonl");

        int ok = 0, noResponse = 0, notFound = 0, clickMissed = 0, timeout = 0;
        // 每次运行重写 results.jsonl，避免上一轮的旧记录污染统计。
        using (var writer = new StreamWriter(resultsPath, append: false) { AutoFlush = true })
        {
            foreach (var entry in entries)
            {
                NpcAuditRecord rec;
                try
                {
                    rec = await AuditOneNpcAsync(entry, shotsDir);
                }
                catch (Exception ex)
                {
                    rec = new NpcAuditRecord
                    {
                        Index = entry.Index, Name = entry.Name, Map = entry.MapFile,
                        X = entry.X, Y = entry.Y, EntryPage = entry.EntryPage,
                    };
                    rec.Errors.Add("timeout");
                    GD.PrintErr($"[NpcAudit] [{entry.Index}] 巡检异常: {ex.GetType().Name}: {ex.Message}");
                }

                // 写一行立即 flush（AutoFlush），脚本可以边跑边看结果。
                writer.WriteLine(BuildResultLine(rec));

                if (rec.Errors.Contains("npc_not_found")) notFound++;
                else if (rec.Errors.Contains("click_missed")) clickMissed++;
                else if (rec.Errors.Contains("no_response")) noResponse++;
                else if (rec.Errors.Contains("timeout")) timeout++;
                else ok++;

                GD.Print($"[NpcAudit] [{rec.Index}] {rec.Name} pages={rec.Pages.Count} clicked={rec.Clicked} errors=[{string.Join(",", rec.Errors)}]");
            }
        }

        GD.Print($"[NpcAudit] SUMMARY total={entries.Count} ok={ok} no_response={noResponse} not_found={notFound} click_missed={clickMissed} timeout={timeout}");
        GD.Print($"[NpcAudit] results={resultsPath} shots={shotsDir}");

        if (AutoLoginArgs.NpcAuditQuit)
            GetTree().Quit();
    }

    // ---- 单个 NPC -----------------------------------------------------------

    private async Task<NpcAuditRecord> AuditOneNpcAsync(NpcAuditEntry entry, string shotsDir)
    {
        var rec = new NpcAuditRecord
        {
            Index = entry.Index, Name = entry.Name, Map = entry.MapFile,
            X = entry.X, Y = entry.Y, EntryPage = entry.EntryPage,
        };

        // 每个 NPC 从「对话已关闭」的干净状态开始，避免上一轮的窗口/页号被误判成本轮响应。
        await EnsureNpcDialogClosedAsync();

        if (!await TeleportToNpcAsync(entry))
        {
            rec.Errors.Add("timeout");
            GD.PrintErr($"[NpcAudit] [{entry.Index}] 传送未到位 (timeout)");
            return rec;
        }

        ObjectRenderer npc = FindTargetNpc(entry);
        if (npc == null)
        {
            rec.Errors.Add("npc_not_found");
            GD.PrintErr($"[NpcAudit] [{entry.Index}] 未找到 NPC 对象 (player=({_playerLocation.X},{_playerLocation.Y}))");
            return rec;
        }

        await WaitNpcCallReadyAsync();
        int showsBeforeClick = _npcDialog?.PageShowCount ?? 0;
        bool clicked = await ClickNpcAsync(npc);
        rec.Clicked = clicked;
        if (!clicked)
        {
            rec.Errors.Add("click_missed");
            GD.PrintErr($"[NpcAudit] [{entry.Index}] 点击未触发 NPC 调用 (object={npc.ObjectID})");
            return rec;
        }

        int entryPage = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, showsBeforeClick);
        if (entryPage < 0)
        {
            rec.Errors.Add("no_response");
            GD.PrintErr($"[NpcAudit] [{entry.Index}] 5s 内无 NPCResponse (object={npc.ObjectID})");
            await EnsureNpcDialogClosedAsync();
            return rec;
        }
        // entryPage 记「实测入口页」——它才是服务端真正发过来的页，也是 pages[0].page；
        // 清单里的 entryPage 只作为期望值，不一致时打到日志（说明数据层重建过页表）。
        if (rec.EntryPage != entryPage)
        {
            if (rec.EntryPage > 0)
                GD.Print($"[NpcAudit] [{entry.Index}] 入口页差异: 清单={rec.EntryPage} 实测={entryPage}");
            rec.EntryPage = entryPage;
        }

        var visited = new HashSet<int> { entryPage };
        await AuditPageAsync(rec, entry, npc, new List<int>(), entryPage, 0, visited, shotsDir);

        await EnsureNpcDialogClosedAsync();
        return rec;
    }

    // ---- 传送 ---------------------------------------------------------------

    /// <summary>
    /// 逐个尝试落点（默认 y-2，然后 y-3 / y+2 / x±2），每个落点等「地图切换 + 玩家坐标到位」，
    /// 总预算 25s。到位后再宽限几秒等 NPC 对象进 _objects。
    /// </summary>
    private async Task<bool> TeleportToNpcAsync(NpcAuditEntry entry)
    {
        (int dx, int dy)[] offsets = { (0, -2), (0, -3), (0, 2), (-2, 0), (2, 0) };
        double deadline = Godot.Time.GetTicksMsec() + NpcAuditTeleportBudgetMs;

        foreach (var (dx, dy) in offsets)
        {
            double remaining = deadline - Godot.Time.GetTicksMsec();
            if (remaining <= 500.0) break;

            int tx = entry.X + dx, ty = entry.Y + dy;
            if (tx <= 0 || ty <= 0) continue;

            GD.Print($"[NpcAudit] [{entry.Index}] @move {entry.MapFile} {tx} {ty}");
            SendChat($"@move {entry.MapFile} {tx} {ty}");

            if (!await WaitUntilAsync(() => IsPlayerNearNpc(entry), Math.Min(8000.0, remaining)))
                continue;

            // 坐标已到位：再宽限等地图对象同步（NPC 本身找不到时由调用方记 npc_not_found）。
            double grace = Math.Min(4000.0, deadline - Godot.Time.GetTicksMsec());
            if (grace > 0) await WaitUntilAsync(() => FindTargetNpc(entry) != null, grace);
            return true;
        }

        return IsPlayerNearNpc(entry);
    }

    private bool IsPlayerNearNpc(NpcAuditEntry entry)
    {
        if (_mapView?.Map == null) return false;
        var info = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.Index == _playerMapIndex);
        if (info == null || !string.Equals(info.FileName, entry.MapFile, StringComparison.OrdinalIgnoreCase))
            return false;
        return Math.Abs(_playerLocation.X - entry.X) <= 3 && Math.Abs(_playerLocation.Y - entry.Y) <= 3;
    }

    /// <summary>
    /// 找目标 NPC：坐标最接近的对象，同距离时优先名字与清单一致。
    /// 名字同时比对 NPCInfo.NPCName（库里的原生名）和对象 DisplayName（本地化后的名字），
    /// 清单里的 230 个名字两者之一应当命中。
    /// </summary>
    private ObjectRenderer FindTargetNpc(NpcAuditEntry entry)
    {
        ObjectRenderer best = null;
        int bestScore = int.MaxValue;
        bool hasName = !string.IsNullOrWhiteSpace(entry.Name);
        foreach (var ob in _objects.Values)
        {
            if (ob?.Type != ObjectRenderer.Kind.NPC) continue;
            int distance = Math.Max(Math.Abs(ob.CellX - entry.X), Math.Abs(ob.CellY - entry.Y));
            if (distance > 6) continue;
            string rawName = GetNpcInfo(ob.ObjectID)?.NPCName ?? "";
            string localName = ob.DisplayName ?? "";
            bool nameMatches = hasName
                && (string.Equals(rawName, entry.Name, StringComparison.Ordinal)
                    || string.Equals(localName, entry.Name, StringComparison.Ordinal));
            int score = distance * 10 + (nameMatches ? 0 : 5);
            if (score >= bestScore) continue;
            bestScore = score;
            best = ob;
        }
        return best;
    }

    // ---- 真实点击路径 -------------------------------------------------------

    /// <summary>与 <see cref="StartInteractionAudit"/> 完全同款的 NPC 唤起路径。</summary>
    private async Task<bool> ClickNpcAsync(ObjectRenderer npc)
    {
        if (npc == null) return false;
        var cell = new System.Drawing.Point(npc.CellX, npc.CellY);
        var hit = _combatController?.PickObjectAtCellForAudit(cell);
        _combatController.MouseObject = hit;
        _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        bool pending = _pendingNpcClickObjectId == npc.ObjectID;
        _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await NextFrameAsync();
        return pending && hit != null && hit.Type == ObjectRenderer.Kind.NPC && _npcObjectId == npc.ObjectID;
    }

    /// <summary>
    /// 真实控件路径点击某个内嵌选项：命中区取该 Id 的第一个字形矩形（保证 HasPoint 成立），
    /// 调 NPCTextControl._GuiInput —— 与鼠标点在选项上走的是同一个方法。
    /// </summary>
    private bool ClickNpcLink(int id)
    {
        var text = _npcDialog?.LegacyText;
        if (text == null) return false;
        bool found = false;
        Rect2 hit = default;
        foreach (var (rect, buttonId) in text.ButtonAreas)
        {
            if (buttonId != id) continue;
            hit = rect;
            found = true;
            break;
        }
        if (!found) return false;

        var center = hit.Position + hit.Size * 0.5f;
        text._GuiInput(new InputEventMouseButton
        {
            Position = center,
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        return true;
    }

    private List<int> CurrentNpcLinkIds()
    {
        var ids = new List<int>();
        var text = _npcDialog?.LegacyText;
        if (text == null) return ids;
        foreach (var (_, id) in text.ButtonAreas)
            if (!ids.Contains(id)) ids.Add(id);
        return ids;
    }

    // ---- 对话状态等待 -------------------------------------------------------

    private async Task NextFrameAsync()
    {
        if (!IsInsideTree()) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<bool> WaitUntilAsync(Func<bool> predicate, double ms)
    {
        double deadline = Godot.Time.GetTicksMsec() + ms;
        while (true)
        {
            try
            {
                if (predicate()) return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NpcAudit] predicate 异常: {ex.Message}");
                return false;
            }
            if (Godot.Time.GetTicksMsec() >= deadline) return false;
            await NextFrameAsync();
        }
    }

    /// <summary>等一个真正的新页到达：ShowPage 次数必须超过 <paramref name="showCountAbove"/>。</summary>
    private async Task<int> WaitForPageAsync(uint npcObjectId, double ms, int showCountAbove)
    {
        double deadline = Godot.Time.GetTicksMsec() + ms;
        while (Godot.Time.GetTicksMsec() < deadline)
        {
            await NextFrameAsync();
            if (_npcDialog == null || !_npcDialog.Visible) continue;
            if (_npcObjectId != npcObjectId) continue;
            if (_npcDialog.PageShowCount <= showCountAbove) continue;
            return _npcDialog.CurrentPageIndex;
        }
        return -1;
    }

    private async Task EnsureNpcDialogClosedAsync()
    {
        CloseNPCDialog();
        await WaitUntilAsync(() => _npcDialog == null || !_npcDialog.Visible, 2000.0);
        await NextFrameAsync();
    }

    /// <summary>TrySendNpcCall 有 1s 节流，点击前必须等冷却过去，否则会被静默丢弃。</summary>
    private async Task WaitNpcCallReadyAsync()
    {
        if (Godot.Time.GetTicksMsec() >= _nextNpcCallMs + 100.0) return;
        await WaitUntilAsync(() => Godot.Time.GetTicksMsec() >= _nextNpcCallMs + 100.0, 3000.0);
    }

    // ---- 链接遍历 -----------------------------------------------------------

    private async Task AuditPageAsync(NpcAuditRecord rec, NpcAuditEntry entry, ObjectRenderer npc,
        List<int> path, int pageIndex, int depth, HashSet<int> visited, string shotsDir)
    {
        // 本页至少渲染两帧再截图（ShowPage 只是设了文字，纹理要等下一帧画出来）。
        await NextFrameAsync();
        RefreshSceneForCapture();
        await NextFrameAsync();

        var links = CurrentNpcLinkIds();
        string shot = CaptureNpcAuditShot(shotsDir, entry.Index, rec.Pages.Count, pageIndex);
        string type = _npcDialog?.CurrentPageDialogType.ToString() ?? "";
        rec.Pages.Add(new NpcAuditPageRecord { Page = pageIndex, Links = links, Shot = shot, Type = type });
        GD.Print($"[NpcAudit] [{entry.Index}] page={pageIndex} depth={depth} type={type} links=[{string.Join(",", links)}] shot={shot}");

        if (depth >= NpcAuditMaxDepth) return;

        foreach (int id in links)
        {
            if (id == 0) continue;   // id=0 是「退出」链接，属于关闭对话，不作为分支探索

            // 每次探索兄弟分支前把自己放回本页：不在本页就重开对话并重放路径。
            if (_npcDialog?.Visible != true || _npcDialog.CurrentPageIndex != pageIndex)
            {
                int back = await OpenNpcToPageAsync(npc, path);
                if (back != pageIndex)
                {
                    rec.Errors.Add("link_failed");
                    GD.PrintErr($"[NpcAudit] [{entry.Index}] 无法回到 page={pageIndex}（重放得到 {back}），跳过 link={id}");
                    continue;
                }
            }

            int showsBefore = _npcDialog.PageShowCount;
            if (!ClickNpcLink(id))
            {
                rec.Errors.Add("link_failed");
                GD.PrintErr($"[NpcAudit] [{entry.Index}] page={pageIndex} link={id} 命中区缺失");
                continue;
            }

            int next = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, showsBefore);
            if (next < 0)
            {
                rec.Errors.Add("link_failed");
                GD.PrintErr($"[NpcAudit] [{entry.Index}] page={pageIndex} link={id} link_failed（5s 无新页）");
                continue;
            }
            if (!visited.Add(next))
                continue;   // 按页 Index 去重

            var childPath = new List<int>(path) { id };
            await AuditPageAsync(rec, entry, npc, childPath, next, depth + 1, visited, shotsDir);
        }
    }

    /// <summary>重开对话并从入口页重放 <paramref name="path"/> 里的链接，返回最终页号（失败 -1）。</summary>
    private async Task<int> OpenNpcToPageAsync(ObjectRenderer npc, IReadOnlyList<int> path)
    {
        await EnsureNpcDialogClosedAsync();
        await WaitNpcCallReadyAsync();

        int showsBefore = _npcDialog?.PageShowCount ?? 0;
        if (!await ClickNpcAsync(npc)) return -1;

        int page = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, showsBefore);
        if (page < 0) return -1;

        foreach (int id in path)
        {
            int before = _npcDialog.PageShowCount;
            if (!ClickNpcLink(id)) return -1;
            page = await WaitForPageAsync(npc.ObjectID, NpcAuditResponseTimeoutMs, before);
            if (page < 0) return -1;
        }
        return page;
    }

    // ---- 截图 ---------------------------------------------------------------

    private string CaptureNpcAuditShot(string shotsDir, int npcIndex, int seq, int pageIndex)
    {
        try
        {
            var image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null)
            {
                GD.PrintErr($"[NpcAudit] viewport 无可用图像，跳过截图 index={npcIndex} seq={seq}");
                return "";
            }
            string file = Path.Combine(shotsDir, $"{npcIndex}_{seq}_p{pageIndex}.png");
            var err = image.SavePng(file);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[NpcAudit] SavePng 失败 {file}: {err}");
                return "";
            }
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcAudit] 截图异常: {ex.GetType().Name}: {ex.Message}");
            return "";
        }
    }

    /// <summary>与 HexaAudit 同款：截图前强制刷新玩家/物体屏幕坐标，避免刚切图时画面是空的。</summary>
    private void RefreshSceneForCapture()
    {
        if (_mapView?.Map == null) return;
        UpdatePlayerPosition();
        UpdateObjectPositions();
        _mapView.QueueRedraw();
        foreach (var ob in _objects.Values) ob?.QueueRedraw();
    }

    // ---- 清单与结果序列化 ---------------------------------------------------

    /// <summary>
    /// 把命令行里的相对路径解析成真实路径。Godot 的进程工作目录不一定是仓库根
    /// （实测是 GodotClient/ 的同级），所以按「原样 → res:// 项目目录 → 项目目录上一级」
    /// 依次尝试，保证 `tools/npc_audit_manifest.json` 这种仓库相对路径能命中。
    /// </summary>
    private static string ResolveNpcAuditPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (Path.IsPathRooted(path)) return path;
        if (File.Exists(path) || Directory.Exists(path)) return Path.GetFullPath(path);

        try
        {
            string projectDir = ProjectSettings.GlobalizePath("res://");
            string[] candidates =
            {
                Path.Combine(projectDir, path),
                Path.Combine(projectDir, "..", path),
                Path.Combine(Directory.GetCurrentDirectory(), path),
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return Path.GetFullPath(candidate);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcAudit] 路径解析失败 {path}: {ex.Message}");
        }
        return path;
    }

    private static List<NpcAuditEntry> LoadNpcAuditManifest(string path)
    {
        var list = new List<NpcAuditEntry>();
        if (!File.Exists(path))
        {
            GD.PrintErr($"[NpcAudit] 清单不存在: {path}");
            return list;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("npcs", out var npcs) || npcs.ValueKind != JsonValueKind.Array)
        {
            GD.PrintErr($"[NpcAudit] 清单缺少 npcs 数组: {path}");
            return list;
        }

        foreach (var node in npcs.EnumerateArray())
        {
            var entry = new NpcAuditEntry
            {
                Index = ReadInt(node, "index"),
                Name = ReadString(node, "name"),
                MapFile = ReadString(node, "mapFile"),
                MapDesc = ReadString(node, "mapDesc"),
                X = ReadInt(node, "x"),
                Y = ReadInt(node, "y"),
                Category = ReadString(node, "category"),
                Mud3File = ReadString(node, "mud3File"),
                EntryPage = ReadInt(node, "entryPage"),
            };
            if (node.TryGetProperty("services", out var services) && services.ValueKind == JsonValueKind.Array)
                foreach (var s in services.EnumerateArray())
                    entry.Services.Add(s.GetString() ?? "");
            if (entry.Index > 0) list.Add(entry);
        }
        return list;
    }

    private static int ReadInt(JsonElement node, string name)
        => node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string ReadString(JsonElement node, string name)
        => node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string BuildResultLine(NpcAuditRecord rec)
    {
        var sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append($"\"index\":{rec.Index},\"name\":{JsonString(rec.Name)},\"map\":{JsonString(rec.Map)},");
        sb.Append($"\"x\":{rec.X},\"y\":{rec.Y},\"clicked\":{(rec.Clicked ? "true" : "false")},\"entryPage\":{rec.EntryPage},\"pages\":[");
        for (int i = 0; i < rec.Pages.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var page = rec.Pages[i];
            sb.Append($"{{\"page\":{page.Page},\"links\":[{string.Join(",", page.Links)}],");
            sb.Append($"\"shot\":{JsonString(page.Shot)},\"type\":{JsonString(page.Type)}}}");
        }
        sb.Append("],\"errors\":[");
        for (int i = 0; i < rec.Errors.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonString(rec.Errors[i]));
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>手写转义（不用 JsonSerializer 是为了不让中文变成 \uXXXX）。</summary>
    private static string JsonString(string value)
    {
        var sb = new StringBuilder((value?.Length ?? 0) + 2);
        sb.Append('"');
        foreach (char c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
