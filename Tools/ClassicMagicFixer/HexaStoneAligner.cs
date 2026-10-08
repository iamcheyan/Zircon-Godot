using System.Drawing;
using System.Security.Cryptography;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 六面神石全图补齐与坐标对齐工具（严格依据原版 MUD3 Merchant.txt 权威真值）
/// </summary>
internal static class HexaStoneAligner
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/development/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private record StoneSpec(string MapFile, int X, int Y, int Image, string EntryPageName, string Note);

    // 权威清单：33 处 MUD3 官方六面神石 + 3 处官方地图底座/EI3.0 实机神石
    private static readonly StoneSpec[] ExpectedStones =
    [
        // 比奇城 (Map 0) - MUD3 4处
        new("0", 498, 463, 56, "BT Teleporter", "13Move_Bichon1 (比奇南)"),
        new("0", 507, 313, 56, "BT Teleporter", "13Move_Bichon2 (比奇东)"),
        new("0", 370, 336, 56, "BT Teleporter", "13Move_Bichon3 (比奇北)"),
        new("0", 379, 444, 56, "BT Teleporter", "13Move_Bichon4 (比奇西)"),

        // 边境城市 (Map 01) - MUD3 3处
        new("01", 456, 216, 56, "BC Teleporter", "13Move_Kugkyung1 (边境北)"),
        new("01", 411, 287, 56, "BC Teleporter", "13Move_Kugkyung2 (边境西)"),
        new("01", 463, 356, 56, "BC Teleporter", "13Move_Kugkyung3 (边境东)"),

        // 银杏山谷 (Map 02) - MUD3 1处
        new("02", 249, 144, 56, "BT Teleporter", "13Move_Eunhang (银杏)"),

        // 道馆 (Map 1) - MUD3 1处 (416, 179)
        new("1", 416, 179, 56, "LP Teleporter", "13Move_DoGwan (道馆日弘门)"),

        // 毒蛇山谷 (Map 2) - MUD3 2处 (306, 244), (314, 193)
        new("2", 306, 244, 56, "BV Teleporter", "13Move_SnakeVally1 (毒蛇南)"),
        new("2", 314, 193, 56, "BV Teleporter", "13Move_SnakeVally2 (毒蛇北)"),

        // 沙巴克城 (Map 3: 350x350) - 3处有效神石 (MUD3 2处 + 皇宫内墙实机底座 1处; (294,539)与(49,566)为攻城战大地图遗留坐标已剔除)
        new("3", 222, 159, 56, "SK Teleporter", "13Move_Sabuk1 (沙巴克正门/外城)"),
        new("3", 71, 140, 56, "SK Teleporter", "13Move_Sabuk4 (沙巴克西)"),
        new("3", 51, 222, 56, "SK1 Teleporter", "Sabuk Inner Wall (沙巴克皇宫内墙底座)"),

        // 绿洲 (Map 4) - MUD3 1处 (435, 83)
        new("4", 435, 83, 56, "NV Teleporter", "13Move_Oasis (绿洲)"),

        // 沙漠土城 (Map 5) - MUD3 2处 + 实机底座 2处
        new("5", 204, 289, 56, "MW Teleporter", "13Move_Samak1 (土城南)"),
        new("5", 112, 177, 56, "MW1 Teleporter", "13Move_Samak2 (土城内/西)"),
        new("5", 63, 195, 56, "MW Teleporter", "13Move_Samak3 (土城西门地基 - 实机补齐)"),
        new("5", 227, 128, 56, "MW Teleporter", "13Move_Samak4 (土城东门地基 - 实机补齐)"),

        // 沙漠 (Map 6) - MUD3 1处
        new("6", 273, 731, 56, "MW Teleporter", "13Move_Ant (沙漠蚂蚁洞)"),

        // 盟重县 (Map 74) - MUD3 2处
        new("74", 349, 329, 56, "MW Teleporter", "13Move_Mongchon1 (盟重东)"),
        new("74", 271, 267, 56, "MW Teleporter", "13Move_Mongchon2 (盟重西)"),

        // 石阁庙 (Map 75) - MUD3 1处
        new("75", 184, 90, 56, "MW Teleporter", "13Move_SukGak (石阁庙)"),

        // 潘夜岛 (Map 8) - MUD3 5处 (Image 57)
        new("8", 288, 241, 57, "FV Teleporter", "13Move_HalfNight1 (潘夜村庄)"),
        new("8", 113, 463, 57, "FV Teleporter", "13Move_HalfNight2 (潘夜西岸)"),
        new("8", 668, 388, 57, "FV Teleporter", "13Move_HalfNight3 (潘夜东岸)"),
        new("8", 448, 579, 57, "FV Teleporter", "13Move_HalfNight4 (潘夜南岸)"),
        new("8", 424, 239, 57, "FV Teleporter", "13Move_HalfNight5 (潘夜村北)"),

        // 流放岛 / 潘夜石窟 (Map 81) - MUD3 1处 (Image 57)
        new("81", 129, 265, 57, "LL Teleporter", "13Move_RedZone (流放岛)"),

        // 潘夜神殿1层大厅 (Map D1110) - MUD3 2处 (Image 57)
        new("D1110", 15, 18, 57, "Teleport Banya Hall", "13Move_HalfTemple1 (神殿1层)"),
        new("D1110", 28, 31, 57, "Teleport Banya Hall", "13Move_HalfTemple2 (神殿1层)"),

        // 潘夜神殿3层西部 (Map D11031) - MUD3 1处 (Image 57)
        new("D11031", 199, 257, 57, "Teleport Banya Hall", "13Move_HalfTemple (神殿3层西部)"),

        // 潘夜神殿5层 (Map D1105) - MUD3 1处 (Image 57)
        new("D1105", 219, 99, 57, "Teleport Banya Hall", "13Move_HalfTemple (神殿5层)"),

        // 诺玛沙漠 (Map 41) - MUD3 1处 (Image 56)
        new("41", 184, 136, 56, "II Teleporter", "13Move_Numa (诺玛沙漠)"),

        // 潘夜神殿 (Map D1115) - EI3.0 1处 (Image 57)
        new("D1115", 358, 353, 57, "Teleport Banya Hall", "13Move_HalfTemple (神殿D1115)")
    ];

    public static int Run(string targetRoot, bool dryRun)
    {
        targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot)) + Path.DirectorySeparatorChar;

        var session = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        session.Initialize(typeof(NPCInfo).Assembly, typeof(MapInfo).Assembly, typeof(MapRegion).Assembly);

        var maps = session.GetCollection<MapInfo>().Binding.ToDictionary(m => m.FileName, m => m, StringComparer.OrdinalIgnoreCase);
        var pages = session.GetCollection<NPCPage>().Binding.ToDictionary(p => p.Description, p => p, StringComparer.OrdinalIgnoreCase);
        var npcCollection = session.GetCollection<NPCInfo>();
        var regionCollection = session.GetCollection<MapRegion>();

        Console.WriteLine($"[六面神石对齐与补齐] 目标数据库: {targetRoot}System.db");
        Console.WriteLine($"  可用地图数: {maps.Count}, NPCPage数: {pages.Count}, 当前NPC总数: {npcCollection.Binding.Count}");

        // 1. 清理孤立废弃 NPC（Map 为 null，属于私服外挂残余）
        var orphanNpcs = npcCollection.Binding
            .Where(n => n.Region == null || n.Region.Map == null)
            .Where(n => n.NPCName.Contains("传送") || n.NPCName.Contains("NPC") || n.Image == 56 || n.Image == 57 || n.Image == 156)
            .ToList();

        Console.WriteLine($"[1/4 孤立废弃NPC清理] 发现 {orphanNpcs.Count} 个无地图/无效NPC:");
        foreach (var orphan in orphanNpcs)
        {
            Console.WriteLine($"  删除孤立 NPC #{orphan.Index}: '{orphan.NPCName}', Image={orphan.Image}");
            if (!dryRun)
            {
                orphan.Delete();
            }
        }

        // 2. 收集现有候选神石 NPC
        var existingStones = npcCollection.Binding
            .Where(n => n.Region?.Map != null)
            .Where(n => n.Image == 56 || n.Image == 57 || n.Image == 156 ||
                        n.NPCName.Contains("Stone", StringComparison.OrdinalIgnoreCase) ||
                        n.NPCName.StartsWith("13Move_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"[2/4 现有神石分析] 找到 {existingStones.Count} 个有效六面神石/石阵 NPC");

        var matchedNpcs = new HashSet<NPCInfo>();
        int updatedCount = 0;
        int createdCount = 0;

        // 3. 对齐与补齐 36 个官方/实机神石
        Console.WriteLine($"[3/4 神石遍历与补齐] 开始核对预期 36 处神石 (MUD3 33处 + 实机底座3处)...");

        foreach (var spec in ExpectedStones)
        {
            if (!maps.TryGetValue(spec.MapFile, out var mapInfo))
            {
                Console.Error.WriteLine($"[ERROR] 地图文件 '{spec.MapFile}' 在 System.db 中未注册！跳过 {spec.Note}");
                continue;
            }

            pages.TryGetValue(spec.EntryPageName, out var entryPage);

            // 在同地图候选 NPC 中查找最匹配的已有 NPC
            NPCInfo? bestMatch = null;
            int bestDist = int.MaxValue;

            foreach (var stone in existingStones)
            {
                if (matchedNpcs.Contains(stone)) continue;
                if (!string.Equals(stone.Region.Map.FileName, spec.MapFile, StringComparison.OrdinalIgnoreCase)) continue;

                var pts = stone.Region.PointRegion;
                int dist = int.MaxValue;
                if (pts != null && pts.Length > 0)
                {
                    dist = Math.Abs(pts[0].X - spec.X) + Math.Abs(pts[0].Y - spec.Y);
                }

                // 精确坐标匹配（dist == 0）
                if (dist == 0)
                {
                    bestMatch = stone;
                    bestDist = 0;
                    break;
                }

                // 名字前缀匹配（如 13Move_Bichon1）
                if (spec.Note.Contains(stone.NPCName, StringComparison.OrdinalIgnoreCase))
                {
                    if (dist < bestDist)
                    {
                        bestMatch = stone;
                        bestDist = dist;
                    }
                }
                // 邻近匹配（40 格以内，如之前偏移过的道馆、比奇、沙城等）
                else if (dist <= 40 && dist < bestDist)
                {
                    bestMatch = stone;
                    bestDist = dist;
                }
            }

            if (bestMatch != null)
            {
                matchedNpcs.Add(bestMatch);
                var pts = bestMatch.Region.PointRegion;
                var curPos = pts != null && pts.Length > 0 ? $"({pts[0].X}, {pts[0].Y})" : "None";

                bool needsCoordFix = pts == null || pts.Length == 0 || pts[0].X != spec.X || pts[0].Y != spec.Y;
                bool needsNameFix = bestMatch.NPCName != "Hexa Holy Stone";
                bool needsImageFix = bestMatch.Image != spec.Image;
                bool needsPageFix = bestMatch.EntryPage == null && entryPage != null;

                if (needsCoordFix || needsNameFix || needsImageFix || needsPageFix)
                {
                    Console.WriteLine($"  [更新] NPC #{bestMatch.Index} ({spec.Note}):");
                    if (needsCoordFix) Console.WriteLine($"    坐标: {curPos} -> ({spec.X}, {spec.Y})");
                    if (needsNameFix) Console.WriteLine($"    名称: '{bestMatch.NPCName}' -> 'Hexa Holy Stone'");
                    if (needsImageFix) Console.WriteLine($"    造型: {bestMatch.Image} -> {spec.Image}");
                    if (needsPageFix) Console.WriteLine($"    脚本: '{bestMatch.EntryPage?.Description}' -> '{entryPage?.Description}'");

                    if (!dryRun)
                    {
                        bestMatch.Region.PointRegion = [new Point(spec.X, spec.Y)];
                        bestMatch.NPCName = "Hexa Holy Stone";
                        bestMatch.Image = spec.Image;
                        if (entryPage != null && (bestMatch.EntryPage == null || bestMatch.EntryPage.Description == ""))
                        {
                            bestMatch.EntryPage = entryPage;
                        }
                    }
                    updatedCount++;
                }
                else
                {
                    Console.WriteLine($"  [一致] NPC #{bestMatch.Index} ({spec.Note}): ({spec.X}, {spec.Y}), Image={spec.Image}, Page='{bestMatch.EntryPage?.Description}'");
                }
            }
            else
            {
                // 新建神石 NPC
                Console.WriteLine($"  [新增] 地图 '{mapInfo.Description}' ({spec.MapFile}) ({spec.X}, {spec.Y}) -> {spec.Note}, Page='{entryPage?.Description}'");
                if (!dryRun)
                {
                    var newRegion = regionCollection.CreateNewObject();
                    newRegion.Map = mapInfo;
                    newRegion.Description = $"Hexa Holy Stone ({spec.X}, {spec.Y})";
                    newRegion.PointRegion = [new Point(spec.X, spec.Y)];

                    var newNpc = npcCollection.CreateNewObject();
                    newNpc.Region = newRegion;
                    newNpc.NPCName = "Hexa Holy Stone";
                    newNpc.Image = spec.Image;
                    newNpc.FaceImage = 0;
                    newNpc.EntryPage = entryPage;
                }
                createdCount++;
            }
        }

        // 4. 处理多余重复 NPC（例如 D11031 里的重复神石）
        var remainingStones = existingStones.Where(s => !matchedNpcs.Contains(s)).ToList();
        int duplicateDeleted = 0;
        foreach (var extra in remainingStones)
        {
            var pts = extra.Region.PointRegion;
            var posStr = pts != null && pts.Length > 0 ? $"({pts[0].X}, {pts[0].Y})" : "None";

            // 如果是 Sabuk 的内墙 SK1 Teleporter (51, 221) 或 Banya Island 12 的神石，保留
            if (string.Equals(extra.Region.Map.FileName, "3", StringComparison.OrdinalIgnoreCase) && pts != null && pts.Length > 0 && pts[0].X == 51 && pts[0].Y == 221)
            {
                Console.WriteLine($"  [保留专用NPC] NPC #{extra.Index} Sabuk内墙神石: Pos={posStr}, Page='{extra.EntryPage?.Description}'");
                continue;
            }
            if (string.Equals(extra.Region.Map.FileName, "12", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  [保留专用NPC] NPC #{extra.Index} 潘夜岛12神石: Pos={posStr}, Page='{extra.EntryPage?.Description}'");
                continue;
            }

            // 其他冗余神石（如 D11031 重复的 #89 或 #254）进行清理
            Console.WriteLine($"  [删除冗余NPC] NPC #{extra.Index}: Name='{extra.NPCName}', Map='{extra.Region.Map.Description}' ({extra.Region.Map.FileName}), Pos={posStr}");
            if (!dryRun)
            {
                extra.Delete();
            }
            duplicateDeleted++;
        }

        Console.WriteLine($"[统计] 更新: {updatedCount}, 新建: {createdCount}, 清理孤立: {orphanNpcs.Count}, 清理冗余: {duplicateDeleted}");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] 演练完成，未写入数据库。");
            return 0;
        }

        int totalChanges = updatedCount + createdCount + orphanNpcs.Count + duplicateDeleted;
        if (totalChanges > 0)
        {
            session.Save(true);
            Console.WriteLine($"[保存] 成功保存 {totalChanges} 项修改到数据库。");
            return Sync(targetRoot) ? 0 : 1;
        }

        Console.WriteLine("所有六面神石已与 MUD3 官方数据完全一致，无需修改。");
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
