using System.Drawing;
using System.Security.Cryptography;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 六面神石坐标对齐与审计工具
/// </summary>
internal static class HexaStoneAligner
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private static readonly Dictionary<string, (int X, int Y)> TargetCoords = new()
    {
        ["1"] = (416, 177),   // 道馆
        ["0"] = (498, 461),   // 比奇
        ["2"] = (306, 242),   // 毒蛇山谷
        ["4"] = (432, 81),    // 绿洲
        ["8"] = (288, 237),   // 潘夜岛
    };

    public static int Run(string targetRoot, bool dryRun)
    {
        targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot)) + Path.DirectorySeparatorChar;

        var session = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        session.Initialize(typeof(NPCInfo).Assembly, typeof(MapInfo).Assembly, typeof(MapRegion).Assembly);

        var npcs = session.GetCollection<NPCInfo>();
        var stones = npcs.Binding.Where(n => n.Image == 56 || n.NPCName.Contains("Stone", StringComparison.OrdinalIgnoreCase)).ToList();

        Console.WriteLine($"找到 {stones.Count} 个六面神石/石阵相关 NPC:");
        int modified = 0;

        foreach (var stone in stones)
        {
            var map = stone.Region?.Map;
            var pts = stone.Region?.PointRegion;
            string currentPos = pts != null && pts.Length > 0 ? $"({pts[0].X}, {pts[0].Y})" : "None";
            string mapFile = map?.FileName ?? "None";
            string mapDesc = map?.Description ?? "Unknown";

            Console.WriteLine($"  NPC #{stone.Index}: Name='{stone.NPCName}', Image={stone.Image}, Map='{mapDesc}' ({mapFile}), Pos={currentPos}");

            if (map != null && TargetCoords.TryGetValue(map.FileName, out var target))
            {
                if (pts == null || pts.Length == 0 || pts[0].X != target.X || pts[0].Y != target.Y)
                {
                    Console.WriteLine($"    -> [调整坐标] 从 {currentPos} -> ({target.X}, {target.Y})");
                    if (!dryRun)
                    {
                        stone.Region.PointRegion = [new Point(target.X, target.Y)];
                    }
                    modified++;
                }
                else
                {
                    Console.WriteLine($"    -> [已对齐] 坐标已在 ({target.X}, {target.Y})");
                }
            }
        }

        if (dryRun)
        {
            Console.WriteLine($"[dry-run] 共发现需要修改 {modified} 处坐标。未写入。");
            return 0;
        }

        if (modified > 0)
        {
            session.Save(true);
            Console.WriteLine($"[保存] 成功保存 {modified} 处六面神石坐标修改。");
            return Sync(targetRoot) ? 0 : 1;
        }

        Console.WriteLine("无需修改。");
        return 0;
    }

    private static bool Sync(string root)
    {
        string src = Path.Combine(root, "System.db");
        byte[] srcHash = SHA256.HashData(File.ReadAllBytes(src));
        string hex = Convert.ToHexString(srcHash);
        Console.WriteLine($"[源 SHA256] {src}: {hex}");

        foreach (var dest in Mirrors)
        {
            string absDest = Path.GetFullPath(dest);
            if (string.Equals(absDest, Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) continue;

            string dir = Path.GetDirectoryName(absDest)!;
            Directory.CreateDirectory(dir);
            File.Copy(src, absDest, overwrite: true);

            byte[] destHash = SHA256.HashData(File.ReadAllBytes(absDest));
            if (!srcHash.SequenceEqual(destHash))
            {
                Console.Error.WriteLine($"[FATAL] 同步后校验失败: {absDest}");
                return false;
            }
            Console.WriteLine($"[已同步] {absDest}");
        }
        return true;
    }
}
