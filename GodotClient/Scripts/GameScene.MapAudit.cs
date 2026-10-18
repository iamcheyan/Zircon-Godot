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

    internal void StartMapAudit() => _ = RunMapAuditAsync();

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
                loaded = await WaitUntilAsync(() => _playerMapIndex == info.Index && _mapView?.Map != null, 12000);
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
