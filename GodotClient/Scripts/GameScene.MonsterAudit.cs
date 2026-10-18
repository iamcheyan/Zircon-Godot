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

    private async Task RunMonsterAuditAsync()
    {
        string outDir = AutoLoginArgs.MonsterAuditOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.6);

        // GM 模式：怪物不主动攻击，免得刷 boss 时被打死
        SendChat("@toggleGM");
        await WaitSecondsAsync(1.0);

        var names = AutoLoginArgs.MonsterAuditOnly();
        var rows = new List<string>();
        int okCount = 0;
        GD.Print($"[MonsterAudit] start 共 {names.Count} 只 out={outDir}");

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            SendChat($"@monster {name} 1");
            bool spawned = await WaitUntilAsync(() => FindMonsterNearby(name, 12) != null, 6000);
            ObjectRenderer found = spawned ? FindMonsterNearby(name, 12) : null;
            if (spawned) await WaitSecondsAsync(0.5);

            string shot = CaptureMonsterAuditShot(outDir, i, name);
            if (spawned) okCount++;
            rows.Add(JsonSerializer.Serialize(new
            {
                monster = name,
                spawned,
                objectId = found?.ObjectID ?? 0,
                cellX = found?.CellX ?? -1,
                cellY = found?.CellY ?? -1,
                display = found?.DisplayName ?? "",
                shot,
            }));
            GD.Print($"[MonsterAudit] {name}: spawned={spawned} cell=({found?.CellX},{found?.CellY}) shot={shot}");
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 写 results.jsonl 异常: {ex.Message}"); }

        SendChat("@toggleGM");
        GD.Print($"[MonsterAudit] SUMMARY total={rows.Count} spawned={okCount} missing={rows.Count - okCount}");
        if (AutoLoginArgs.MonsterAuditQuit) GetTree().Quit(rows.Count == okCount ? 0 : 1);
    }

    /// <summary>在玩家附近（半径内）找名字匹配的怪物对象。</summary>
    private ObjectRenderer FindMonsterNearby(string name, int radius)
    {
        if (_objects == null) return null;
        foreach (ObjectRenderer ob in _objects.Values)
        {
            if (ob?.Type != ObjectRenderer.Kind.Monster) continue;
            if (!string.Equals(ob.DisplayName, name, StringComparison.OrdinalIgnoreCase)) continue;
            int dx = Math.Abs(ob.CellX - _playerLocation.X);
            int dy = Math.Abs(ob.CellY - _playerLocation.Y);
            if (dx <= radius && dy <= radius) return ob;
        }
        return null;
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
