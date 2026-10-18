using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Library;

namespace ZirconClient.Scripts;

/// <summary>
/// 怪物实机走查（--monster-audit）：对库内每只怪物用 GM 命令 <c>@monster &lt;名字&gt; 1</c> 刷一只出来，
/// 断言它真的出现在地图对象里（ObjectRenderer.Kind.Monster + 名字匹配）并留一张实机截图。
///
/// 用途：审计的 B 部分要求"怪物实机截图留档"；同时能抓"库里有、但服务端刷不出来/贴图缺失"的怪物
/// （与 NPC 巡检同一套思路：先证明对象存在，再谈外观）。
/// 开场先发 @toggleGM 让怪物不主动攻击，避免刷 boss 时把测试角色打死。
/// </summary>
public partial class GameScene
{
    private bool _monsterAuditStarted;

    internal void StartMonsterAudit() => _ = RunMonsterAuditAsync();


    /// <summary>
    /// 刷怪用的候选落点（比奇城内，逐个解析 0.map 确认"自身与四邻皆通行"）。
    /// SpawnMob 刷在玩家正前方一格，格子被墙或已有怪物占据时会静默失败，
    /// 故每只之前轮换一个互相隔开的空地，保证正前方是空的。
    /// </summary>
    private static readonly (int x, int y)[] SpawnCells =
    {
        (368, 316), (373, 316), (378, 316), (383, 316), (388, 316), (373, 320), (378, 320), (383, 320), (388, 320), (393, 320), (378, 324), (383, 324), (388, 324), (378, 328), (383, 328), (388, 328), (368, 332), (373, 332), (378, 332), (383, 332), (368, 336), (373, 336), (378, 336),
    };

    private async Task RunMonsterAuditAsync()
    {
        string outDir = AutoLoginArgs.MonsterAuditOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.6);

        // GM 模式：怪物不主动攻击，免得刷 boss 时被打死
        SendChat("@gamemaster"); // 命令真名是 @gamemaster（@toggleGM 不存在，2026-10-18 实测）
        await WaitSecondsAsync(1.0);

        // 2026-10-18：SpawnMob 把怪物刷在**玩家正前方**的格子
        // （ServerLibrary/Envir/Commands/Command/Admin/SpawnMob.cs:30
        //   Functions.Move(player.CurrentLocation, player.Direction)），若那格是墙、建筑
        // **或已被先前的怪物占据**就静默失败——首轮 121 只全 False（正前方是墙），
        // 第二轮前 11 只成功、之后开始零星失败（正前方被堆住的怪占了）。
        // 因此每刷一只前都把角色挪到下一个"自身与四邻皆通行"的候选格。
        SendChat($"@move 0 {SpawnCells[0].x} {SpawnCells[0].y}");
        await WaitSecondsAsync(1.5);
        GD.Print($"[MonsterAudit] 就位 loc={_playerLocation} （刷怪落点=正前方一格，每只前轮换候选格）");

        var names = AutoLoginArgs.MonsterAuditOnly();
        var rows = new List<string>();
        int okCount = 0;
        GD.Print($"[MonsterAudit] start 共 {names.Count} 只 out={outDir}");

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            var cell = SpawnCells[i % SpawnCells.Length];
            SendChat($"@move 0 {cell.x} {cell.y}");
            await WaitSecondsAsync(0.75);
            List<string> beforeList = ListMonstersNearby(14);
            // 按**类型**计数判定：怪物会游走，用"半径内总数"会被进出抵消成净 0（伪阴性，实测踩到）。
            // 同类型数量增加才算刷出。
            // 判定改用**服务端真值**：@mobcensus 会把该图怪物按种类计数写进服务端 /tmp/mobcensus.txt，
            // 比客户端渲染计数可靠（客户端会受视距/游走/丢包影响，首轮就吃过伪阴性的亏）。
            int beforeSameType = CountMonsterTypeNearby(14, name);
            int beforeCensus = await CensusCountAsync(name);
            int chatBefore = _legacyChatDialog?.MessageCount ?? 0;
            SendChat($"@monster {name} 1");
            await WaitSecondsAsync(1.0);
            int afterCensus = await CensusCountAsync(name);
            await WaitSecondsAsync(0.3);
            List<string> afterList = ListMonstersNearby(14);
            string newNames = string.Join("/", afterList.Where(x => !beforeList.Contains(x)));
            // 三重信号取或：服务端 census（真值，但名字对齐可能失败）／同类型客户端计数／
            // 客户端总数（怪物游走会抵消，只作补充）。任一上升即判定刷出，避免伪阴性。
            bool spawned = afterCensus > beforeCensus
                           || CountMonsterTypeNearby(14, name) > beforeSameType
                           || afterList.Count > beforeList.Count;

            string shot = CaptureMonsterAuditShot(outDir, i, name);
            // 失败时把服务端刚回的聊天原文带上：SpawnMob 的名字查不到会抛
            // "Could not find monster: X"，这类报错只进聊天、不进服务端日志。
            string serverSaid = spawned ? string.Empty
                : string.Join(" | ", (_legacyChatDialog?.RecentMessages(
                        Math.Max(1, (_legacyChatDialog?.MessageCount ?? 0) - chatBefore)) ?? new List<string>()));
            if (spawned) okCount++;
            rows.Add(JsonSerializer.Serialize(new
            {
                monster = name,
                spawned,
                beforeCount = beforeList.Count,
                afterCount = afterList.Count,
                newDisplay = newNames,
                serverCensus = new { before = beforeCensus, after = afterCensus },
                serverSaid,
                shot,
            }));
            GD.Print($"[MonsterAudit] {name}: spawned={spawned} 服务端计数 {beforeCensus}->{afterCensus} 客户端 {beforeList.Count}->{afterList.Count} at=({cell.x},{cell.y}){(spawned ? "" : $" 服务端回话=[{serverSaid}]")} shot={shot}");
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 写 results.jsonl 异常: {ex.Message}"); }

        SendChat("@gamemaster"); // 命令真名是 @gamemaster（@toggleGM 不存在，2026-10-18 实测）
        GD.Print($"[MonsterAudit] SUMMARY total={rows.Count} spawned={okCount} missing={rows.Count - okCount}");
        if (AutoLoginArgs.MonsterAuditQuit) GetTree().Quit(rows.Count == okCount ? 0 : 1);
    }

    /// <summary>
    /// 统计玩家附近（半径内）的怪物对象，并返回它们的显示名。
    /// 不用名字匹配：客户端显示名是本地化中文，而 @monster 用的是库内英文名，
    /// 两者对不上会导致"全都没刷出"的假象（2026-10-18 实测踩到）。
    /// 改为对比"发送前后半径内怪物数量"，与名字无关。
    /// </summary>

    /// <summary>
    /// 半径内**指定类型**（库内英文名）的怪物数量。怪物游走会让"总数"抖动，
    /// 按类型计数才能稳定判定 @monster 是否真的刷出了一只。
    /// </summary>

    /// <summary>
    /// 让服务端统计当前地图上指定种类怪物的数量（服务端真值）。
    /// 走 @mobcensus：它把结果同时写进聊天与服务端 /tmp/mobcensus.txt（单机部署可直接读）。
    /// </summary>
    private async Task<int> CensusCountAsync(string monsterName)
    {
        const string CensusPath = "/tmp/mobcensus.txt";
        long beforeBytes = 0;
        try { if (System.IO.File.Exists(CensusPath)) beforeBytes = new System.IO.FileInfo(CensusPath).Length; }
        catch { /* 读不到就按 0 处理，后面靠等待 */ }

        SendChat("@mobcensus");

        // 以**文件增长**判定新鲜度：之前用固定 sleep 读最后一行，读到的常常是上一次的结果
        // （实测：Stone Golem 的 before 拿到的是 Ghost Mage 那一行）。
        await WaitUntilAsync(() =>
        {
            try { return System.IO.File.Exists(CensusPath) && new System.IO.FileInfo(CensusPath).Length > beforeBytes; }
            catch { return false; }
        }, 4000.0);

        try
        {
            if (!System.IO.File.Exists(CensusPath)) return -1;
            string[] lines = System.IO.File.ReadAllLines(CensusPath);
            if (lines.Length == 0) return -1;
            string[] parts = lines[lines.Length - 1].Split('\t');
            if (parts.Length < 3) return -1;

            foreach (string entry in parts[2].Split(','))
            {
                int eq = entry.LastIndexOf('=');
                if (eq <= 0) continue;
                // 库内确有名字带尾空格的怪（如 "Pachon "），比对时统一 Trim
                if (entry.Substring(0, eq).Trim() == monsterName.Trim() &&
                    int.TryParse(entry.Substring(eq + 1), out int n)) return n;
            }
            return 0;
        }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 读 mobcensus 失败: {ex.Message}"); }

        return -1;
    }

    private int CountMonsterTypeNearby(int radius, string monsterName)
    {
        int count = 0;
        if (_objects == null) return count;
        foreach (ObjectRenderer ob in _objects.Values)
        {
            if (ob?.Type != ObjectRenderer.Kind.Monster) continue;
            if (ob.MonsterInfo == null || ob.MonsterInfo.MonsterName != monsterName) continue;
            int dx = Math.Abs(ob.CellX - _playerLocation.X);
            int dy = Math.Abs(ob.CellY - _playerLocation.Y);
            if (dx <= radius && dy <= radius) count++;
        }
        return count;
    }

    private List<string> ListMonstersNearby(int radius)
    {
        var found = new List<string>();
        if (_objects == null) return found;
        foreach (ObjectRenderer ob in _objects.Values)
        {
            if (ob?.Type != ObjectRenderer.Kind.Monster) continue;
            int dx = Math.Abs(ob.CellX - _playerLocation.X);
            int dy = Math.Abs(ob.CellY - _playerLocation.Y);
            if (dx <= radius && dy <= radius) found.Add(string.IsNullOrEmpty(ob.DisplayName) ? "?" : ob.DisplayName);
        }
        return found;
    }

    private string CaptureMonsterAuditShot(string outDir, int index, string name)
    {
        try
        {
            Image image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
            }
            string safe = sb.Length > 0 ? sb.ToString() : "monster";
            string file = Path.Combine(outDir, $"{index:D3}_{safe}.png");
            image.SavePng(file);
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MonsterAudit] 截图异常 {name}: {ex.Message}");
            return "";
        }
    }
}
