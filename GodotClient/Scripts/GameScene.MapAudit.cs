using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Library;
using C = Library.Network.ClientPackets;

namespace ZirconClient.Scripts;

/// <summary>
/// 逐图实机走查（--map-audit）：用游戏内 GM 命令 <c>@move &lt;mapFile&gt;</c> 把角色带到每张地图，
/// 确认**客户端真的把这张图解析并通过 MapView 加载出来**，同时留一张实机截图。
///
/// 为什么需要它：
///   · 审计发现 39 张发行地图是 **13 字节/格** 的单元格布局，而客户端/服务端原先按 14 字节读
///     （客户端在流末尾抛 EndOfStreamException → 地形不加载；服务端索引越界）。
///     修完必须逐图实机验证"真能加载"，而不是只看代码；
///   · 审计要求"全部有连接点的地图 + 可疑连接点"实机截图留档，本通道同时产出这些图。
///
/// 输出：&lt;out&gt;/&lt;mapFile&gt;.png + results.jsonl（地图、MapInfo 索引、是否加载、宽高、截图路径、备注）。
/// 需要 Admin 账号（@move 是 GM 命令）。
/// </summary>
public partial class GameScene
{
    private bool _mapAuditStarted;
    private bool _portalAuditStarted;
    internal void StartMapAudit() => _ = RunMapAuditAsync();
    internal void StartPortalAudit() => _ = RunPortalAuditAsync();

    private async Task RunMapAuditAsync()
    {
        string outDir = AutoLoginArgs.MapAuditOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[MapAudit] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.6);

        // 暖机：刚进游戏时服务端可能还没把会话切到 Game 阶段，此时发 @move 会被静默忽略
        string currentStem = Globals.MapInfoList?.Binding
            .FirstOrDefault(m => m.Index == _playerMapIndex)?.FileName;
        if (!string.IsNullOrEmpty(currentStem))
        {
            SendChat($"@move {currentStem} {_playerLocation.X} {_playerLocation.Y}");
            await WaitSecondsAsync(1.0);
        }

        var stems = AutoLoginArgs.MapAuditOnly();
        var rows = new List<string>();
        int okCount = 0;
        GD.Print($"[MapAudit] start 共 {stems.Count} 张图 out={outDir}");

        for (int i = 0; i < stems.Count; i++)
        {
            string stem = stems[i];
            var info = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.FileName == stem);
            bool loaded = false;
            int width = 0, height = 0;
            string note = "";

            if (info == null)
            {
                note = "mapinfo_missing";
            }
            else
            {
                SendChat($"@move {stem}");
                loaded = await WaitUntilAsync(() => _playerMapIndex == info.Index && _mapView?.Map != null, 25000);
                if (loaded)
                {
                    width = _mapView.Map.Width;
                    height = _mapView.Map.Height;
                    await WaitSecondsAsync(0.6);
                    RefreshSceneForCapture();
                }
                else note = "map_not_loaded";
            }

            string shot = CaptureMapAuditShot(outDir, stem);
            if (loaded) okCount++;
            rows.Add(JsonSerializer.Serialize(new
            {
                map = stem,
                mapIndex = info?.Index ?? -1,
                loaded,
                width,
                height,
                shot,
                note,
            }));
            GD.Print($"[MapAudit] {stem}: loaded={loaded} {width}x{height} {note} shot={shot}");
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[MapAudit] 写 results.jsonl 失败: {ex.Message}"); }

        GD.Print($"[MapAudit] SUMMARY total={rows.Count} loaded={okCount} failed={rows.Count - okCount}");
        if (AutoLoginArgs.MapAuditQuit) GetTree().Quit(rows.Count == okCount ? 0 : 1);
    }

    /// <summary>
    /// 传送门实机验证（--portal-audit）：规格 <c>"srcMap:x:y:dstMap,..."</c>。
    /// 用 @move 把角色**放到源格上**，看服务端是否把角色送到目标地图——用于验收
    /// "端点不可站"修复（原格不可站 → 踩门没反应；改到可站格后应正常传送）。
    /// </summary>
    private async Task RunPortalAuditAsync()
    {
        string outDir = AutoLoginArgs.PortalAuditOut;
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) { GD.PrintErr($"[PortalAudit] 无法创建输出目录 {outDir}: {ex.Message}"); }

        for (int i = 0; i < 180 && (_mapView?.Map == null || !_startGameShown); i++)
            await NextFrameAsync();
        await WaitSecondsAsync(0.6);

        var rows = new List<string>();
        int okCount = 0;
        var specs = AutoLoginArgs.PortalAuditOnly();
        GD.Print($"[PortalAudit] start 共 {specs.Count} 个传送门 out={outDir}");

        foreach (string spec in specs)
        {
            string[] parts = spec.Split(':');
            if (parts.Length is not (4 or 5)
                || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
            {
                rows.Add(JsonSerializer.Serialize(new { spec, ok = false, note = "bad_spec" }));
                GD.PrintErr($"[PortalAudit] 规格非法: {spec}（应为 srcMap:x:y:dstMap）");
                continue;
            }

            string srcStem = parts[0], dstStem = parts[parts.Length - 1];
            var dstInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.FileName == dstStem);
            int mapBefore = _playerMapIndex;
            int mapAfterTeleport = -1;
            bool ok = false;
            string note = "";
            int dir = 0;
            bool walkMode = parts.Length == 5 && int.TryParse(parts[3], out dir);

            if (dstInfo == null)
            {
                note = "dst_mapinfo_missing";
            }
            else
            {
                SendChat($"@move {srcStem} {x} {y}");
                await WaitSecondsAsync(1.2);

                // 强化判定：必须真的站在源图上，才谈"踩门是否生效"，否则会把"传送失败"误判成"门没开"
                var srcInfo = Globals.MapInfoList?.Binding.FirstOrDefault(m => m.FileName == srcStem);
                mapAfterTeleport = _playerMapIndex;
                if (srcInfo != null && _playerMapIndex != srcInfo.Index)
                    note = $"teleport_failed(on map {_playerMapIndex}, want {srcInfo.Index})";

                // 走路模式（5 段规格 srcMap:x:y:dir:dstMap）：@move 是直接置位、不会触发"踩格"事件，
                // 必须真的走一步踩上门格，才能验证传送门本身是否生效。
                if (walkMode)
                {
                    if (_playerLocation.X != x || _playerLocation.Y != y)
                    {
                        note = $"from_cell_not_reached({_playerLocation.X},{_playerLocation.Y})";
                    }
                    else
                    {
                        _net.Connection.Enqueue(new C.Move { Direction = (MirDirection)dir, Distance = 1 });
                        await WaitSecondsAsync(1.0);
                    }
                }

                if (note.Length == 0)
                    ok = await WaitUntilAsync(() => _playerMapIndex == dstInfo.Index, 12000);
                if (!ok && note.Length == 0)
                    note = _playerMapIndex == mapBefore ? "still_on_source_map" : "moved_elsewhere";
                await WaitSecondsAsync(0.4);
            }

            string shot = CaptureMapAuditShot(outDir, $"{srcStem}_{x}_{y}_to_{dstStem}");
            if (ok) okCount++;
            rows.Add(JsonSerializer.Serialize(new
            {
                portal = spec,
                fromMapIndex = mapBefore,
                mapAfterTeleport,
                toMapIndex = _playerMapIndex,
                expectedMap = dstStem,
                ok,
                shot,
                note,
            }));
            GD.Print($"[PortalAudit] {spec}: ok={ok} map {mapBefore}->{_playerMapIndex} {note} shot={shot}");
        }

        try { File.WriteAllLines(Path.Combine(outDir, "results.jsonl"), rows); }
        catch (Exception ex) { GD.PrintErr($"[PortalAudit] 写 results.jsonl 失败: {ex.Message}"); }

        GD.Print($"[PortalAudit] SUMMARY total={rows.Count} passed={okCount} failed={rows.Count - okCount}");
        if (AutoLoginArgs.PortalAuditQuit) GetTree().Quit(rows.Count == okCount ? 0 : 1);
    }

    private string CaptureMapAuditShot(string outDir, string stem)
    {
        try
        {
            Image image = GetViewport()?.GetTexture()?.GetImage();
            if (image == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in stem)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
            }
            string file = Path.Combine(outDir, (sb.Length > 0 ? sb.ToString() : "map") + ".png");
            image.SavePng(file);
            return file;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapAudit] 截图异常 {stem}: {ex.Message}");
            return "";
        }
    }
}
