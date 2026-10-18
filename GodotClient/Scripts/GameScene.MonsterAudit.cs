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
            List<string> beforeList = ListMonstersNearby(14);
            SendChat($"@monster {name} 1");
            bool spawned = await WaitUntilAsync(() => ListMonstersNearby(14).Count > beforeList.Count, 6000);
            await WaitSecondsAsync(0.5);
            List<string> afterList = ListMonstersNearby(14);
            string newNames = string.Join("/", afterList.Where(x => !beforeList.Contains(x)));

            string shot = CaptureMonsterAuditShot(outDir, i, name);
            if (spawned) okCount++;
            rows.Add(JsonSerializer.Serialize(new
            {
                monster = name,
                spawned,
                beforeCount = beforeList.Count,
                afterCount = afterList.Count,
                newDisplay = newNames,
                shot,
            }));
            GD.Print($"[MonsterAudit] {name}: spawned={spawned} {beforeList.Count}->{afterList.Count} new=[{newNames}] shot={shot}");
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[MonsterAudit] 写 results.jsonl 异常: {ex.Message}"); }

        SendChat("@toggleGM");
        GD.Print($"[MonsterAudit] SUMMARY total={rows.Count} spawned={okCount} missing={rows.Count - okCount}");
        if (AutoLoginArgs.MonsterAuditQuit) GetTree().Quit(rows.Count == okCount ? 0 : 1);
    }

    /// <summary>
    /// 统计玩家附近（半径内）的怪物对象，并返回它们的显示名。
    /// 不用名字匹配：客户端显示名是本地化中文，而 @monster 用的是库内英文名，
    /// 两者对不上会导致"全都没刷出"的假象（2026-10-18 实测踩到）。
    /// 改为对比"发送前后半径内怪物数量"，与名字无关。
    /// </summary>
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
