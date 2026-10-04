using System.Drawing;
using System.Text.Json;
using Library.SystemModels;
using MirDB;

namespace MapConnectionFixer;

/// <summary>
/// 从最终 System.db 导出全部地图与连接关系为结构化 JSON，
/// 供 mir3-website 重建「地图资料」页（分类浏览 + 连接点标注）。
/// </summary>
internal static class MapExporter
{
    /// <summary>向上查找含 Debug/ServerCore 的仓库根，使工具与 CWD 解耦。</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Debug", "ServerCore", "Map")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("找不到 Debug/ServerCore/Map 目录");
        return dir.FullName;
    }

    private static string MapDir()
    {
        // 以仓库根为基准定位 .map 目录；工具可能被从任意 CWD 调用，
        // 因此不能依赖相对 CWD 的 Path.GetFullPath。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Debug", "ServerCore", "Map")))
            dir = dir.Parent;
        if (dir == null)
            throw new DirectoryNotFoundException("找不到 Debug/ServerCore/Map 目录");
        return Path.Combine(dir.FullName, "Debug", "ServerCore", "Map");
    }

    // Linux 上 File.Exists 区分大小写：System.db 里登记的是 d807，
    // 磁盘上是 d807.map，而 Mud3 官方写的是 D807。不做大小写不敏感匹配会
    // 把这些图误判成「缺地图文件」。
    private static bool HasMapFile(string fn)
    {
        string dir = MapDir();
        if (File.Exists(Path.Combine(dir, fn + ".map"))) return true;
        return Directory.EnumerateFiles(dir, "*.map")
            .Any(p => string.Equals(Path.GetFileNameWithoutExtension(p), fn,
                                    StringComparison.OrdinalIgnoreCase));
    }

    public static void Run(Session session, string outPath)
    {
        var mapCol = session.GetCollection<MapInfo>().Binding;
        var moveCol = session.GetCollection<MovementInfo>().Binding;
        var zoneCol = session.GetCollection<SafeZoneInfo>().Binding;

        // ---- 分类规则：按 FileName 前缀/数字段归类 ----
        string Classify(string fn)
        {
            // 城镇主城
            if (fn is "0" or "1" or "2" or "4" or "5" or "8" or "9" or "01" or "02" or "41" or "74" or "81")
                return "城镇主城";
            // 道馆/行会/特殊城镇
            if (fn.StartsWith("1_") || fn.StartsWith("0_")) return "城镇附属";
            if (fn.StartsWith("D")) return "地下城/洞窟";
            if (fn.StartsWith("E")) return "特殊副本";
            if (fn.StartsWith("DM")) return "魔窟";
            if (fn.StartsWith("z")) return "监狱/特殊";
            if (fn.StartsWith("d")) return "地下城/洞窟";
            return "其他";
        }

        var mapNodes = new List<Dictionary<string, object>>();
        foreach (var m in mapCol.OrderBy(x => x.FileName, StringComparer.Ordinal))
        {
            string fn = m.FileName;
            var node = new Dictionary<string, object>
            {
                ["id"] = fn,
                ["index"] = m.Index,
                ["name"] = m.Description ?? fn,
                ["category"] = Classify(fn),
                ["allowRT"] = m.AllowRT,
                ["hasMapFile"] = HasMapFile(fn),
            };
            mapNodes.Add(node);
        }

        // ---- 连接关系：source -> dest，含坐标 ----
        var links = new List<Dictionary<string, object>>();
        foreach (var mv in moveCol)
        {
            var s = mv.SourceRegion;
            var d = mv.DestinationRegion;
            if (s?.Map == null || d?.Map == null) continue;
            string sPt = s.PointRegion.Length > 0 ? $"{s.PointRegion[0].X},{s.PointRegion[0].Y}" : "";
            string dPt = d.PointRegion.Length > 0 ? $"{d.PointRegion[0].X},{d.PointRegion[0].Y}" : "";
            links.Add(new Dictionary<string, object>
            {
                ["from"] = s.Map.FileName,
                ["fromName"] = s.Map.Description ?? s.Map.FileName,
                ["fromPt"] = sPt,
                ["to"] = d.Map.FileName,
                ["toName"] = d.Map.Description ?? d.Map.FileName,
                ["toPt"] = dPt,
                ["icon"] = (int)mv.Icon,
            });
        }

        // ---- 安全区 ----
        var zones = new List<Dictionary<string, object>>();
        foreach (var z in zoneCol)
        {
            var map = z.Region?.Map ?? z.BindRegion?.Map;
            if (map == null) continue;
            string bindPt = z.BindRegion?.PointRegion?.Length > 0
                ? $"{z.BindRegion.PointRegion[0].X},{z.BindRegion.PointRegion[0].Y}" : "";
            zones.Add(new Dictionary<string, object>
            {
                ["map"] = map.FileName,
                ["mapName"] = map.Description ?? map.FileName,
                ["bindPt"] = bindPt,
                ["regionCount"] = z.Region?.PointRegion?.Length ?? 0,
            });
        }

        var root = new Dictionary<string, object>
        {
            ["exportedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["mapCount"] = mapCol.Count,
            ["linkCount"] = links.Count,
            ["zoneCount"] = zones.Count,
            ["maps"] = mapNodes,
            ["links"] = links,
            ["zones"] = zones,
        };

        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(outPath, json, System.Text.Encoding.UTF8);
        Console.WriteLine($"导出完成: {outPath} | 地图 {mapCol.Count} / 连接 {links.Count} / 安全区 {zones.Count}");
    }
}
