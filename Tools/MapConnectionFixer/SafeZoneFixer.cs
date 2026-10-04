using System.Drawing;
using Library;
using Library.SystemModels;
using MirDB;

namespace MapConnectionFixer;

/// <summary>
/// 阶段 A：主城安全区全量对齐。
///
/// 权威源：StartPoint.txt（GB18030），每行 <地图名> <中心X> <中心Y>。
/// 同一张图可出现多行 = 多个官方复活/回城落点，必须全部收入 BindRegion。
///
/// 铁律 2：Region（保护范围）与 BindRegion（出生落点）必须是两个**独立**的
/// MapRegion 实例，绝不能共用同一个对象——共用会导致后续任一侧被改写时
/// 另一侧被清空。
///
/// 铁律 3：所有写入 PointRegion 的点都经 MapCellMap.IsWalkable 过滤，
/// 否则服务端 EnsureSafeZoneBindPoints 会输出 [Safe Zone] Bad Location。
/// </summary>
internal static class SafeZoneFixer
{
    /// <summary>官方保护半径（文档 §2.2 表）。未列出的图取默认值。</summary>
    private static readonly Dictionary<string, int> Radii = new(StringComparer.Ordinal)
    {
        ["0"] = 18,    // 比奇县城
        ["4"] = 18,    // 盟重省（土城）
        ["5"] = 16,    // 沙漠绿洲
        ["8"] = 16,    // 潘夜岛
        ["01"] = 15,   // 边境城市
        ["02"] = 15,   // 银杏山谷
        ["1"] = 15,    // 道馆
        ["2"] = 15,    // 蛇谷
        ["41"] = 15,   // 诺玛沙漠
        ["74"] = 15,   // 诺玛村落
        ["9"] = 15,    // 死谷/灌木林
        ["81"] = 15,   // 流放岛
    };

    private const int DefaultRadius = 15;
    private const int BindHalfSize = 2;   // BindRegion 取中心 [-2..2] 共 5×5

    public static int Run(string root, string mapDir, bool dryRun)
    {
        var startPoints = Mud3Config.LoadStartPoints();
        Console.WriteLine($"StartPoint.txt 解析: {startPoints.Count} 个官方落点, "
                          + $"{startPoints.Select(s => s.MapName).Distinct().Count()} 张地图");

        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(ItemInfo).Assembly);

        var mapCol = session.GetCollection<MapInfo>();
        var regionCol = session.GetCollection<MapRegion>();
        var zoneCol = session.GetCollection<SafeZoneInfo>();

        var byFileName = mapCol.Binding
            .GroupBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var byMap = startPoints.GroupBy(s => s.MapName).ToList();
        int fixedCount = 0, skipped = 0;

        foreach (var group in byMap)
        {
            string fileName = group.Key;
            var centers = group.ToList();

            if (!byFileName.TryGetValue(fileName, out var mapInfo))
            {
                Console.WriteLine($"  [跳过] 地图 {fileName} 未在 System.db 登记"
                                  + $"（官方中心 {centers[0].X},{centers[0].Y}）——无法做阻挡过滤，写入必报 Bad Location");
                skipped++;
                continue;
            }

            if (!MapCellMap.TryLoad(mapDir, fileName, out var cellMap))
            {
                Console.WriteLine($"  [跳过] 地图 {fileName} 缺 .map 文件，无法校验阻挡");
                skipped++;
                continue;
            }

            // 官方第一行视为主中心（StartPoint.txt 同图多行时首行为中心，其余为附属复活点）
            var center = centers[0];
            int radius = Radii.TryGetValue(fileName, out var r) ? r : DefaultRadius;

            var regionPoints = cellMap.WalkableInRadius(center.X, center.Y, radius);
            if (regionPoints.Count == 0)
            {
                Console.WriteLine($"  [跳过] 地图 {fileName} 中心 ({center.X},{center.Y}) 半径 {radius} 内无可行走点");
                skipped++;
                continue;
            }

            // BindRegion 落点：主中心 5×5 + 该图其余官方落点
            var bindPoints = new List<Point>();
            foreach (var sp in centers)
            {
                bool placed = false;
                for (int dx = -BindHalfSize; dx <= BindHalfSize && !placed; dx++)
                for (int dy = -BindHalfSize; dy <= BindHalfSize && !placed; dy++)
                {
                    var p = new Point(sp.X + dx, sp.Y + dy);
                    if (!cellMap.IsWalkable(p)) continue;
                    if (bindPoints.Contains(p)) continue;
                    bindPoints.Add(p);
                    placed = true;
                }

                if (!placed && cellMap.NearestWalkable(sp.X, sp.Y, 8, out var near))
                {
                    if (!bindPoints.Contains(near)) bindPoints.Add(near);
                }
            }

            if (bindPoints.Count == 0)
            {
                Console.WriteLine($"  [跳过] 地图 {fileName} 无可用出生落点");
                skipped++;
                continue;
            }

            // 铁律 2：Region（保护范围）与 BindRegion（出生落点）必须是两个**独立**的
            // MapRegion 实例，绝不能共用同一个对象。
            //
            // 关键：MapRegion 的关联全是 [Association(..., Aggregate=true)]，
            // Delete() 一个 MapRegion 会**级联删除**所有引用它的 SafeZoneInfo。
            // 所以这里绝不删除旧 Region，只能就地改写其 PointRegion；
            // 若旧 Region 与 BindRegion 恰好是同一实例（历史脏数据），先拆开再建新的。
            var zone = zoneCol.Binding.FirstOrDefault(z =>
                z.Region?.Map == mapInfo || z.BindRegion?.Map == mapInfo);

            bool created = zone == null;
            if (created)
            {
                zone = zoneCol.CreateNewObject();
                zone.StartClass = RequiredClass.WarWizTao;
            }

            // --- 保护范围 Region：优先就地复用旧实例，避免级联删除 SafeZoneInfo ---
            var region = zone.Region?.Map == mapInfo ? zone.Region : regionCol.CreateNewObject();
            region.Map = mapInfo;
            region.Description = $"{fileName} / Safe Zone";
            region.PointRegion = regionPoints.ToArray();
            region.Size = regionPoints.Count;

            // --- 出生落点 BindRegion：独立实例，绝不与 region 共用 ---
            var bindRegion = zone.BindRegion != null && !ReferenceEquals(zone.BindRegion, region)
                ? zone.BindRegion
                : regionCol.CreateNewObject();
            bindRegion.Map = mapInfo;
            bindRegion.Description = $"{fileName} / Safe Zone Bind";
            bindRegion.PointRegion = bindPoints.ToArray();
            bindRegion.Size = bindPoints.Count;

            zone.Region = region;
            zone.BindRegion = bindRegion;

            fixedCount++;
            Console.WriteLine($"  [修正] 地图 {fileName} ({mapInfo.Description}) "
                + $"中心=({center.X},{center.Y}) R={radius} "
                + $"Region={regionPoints.Count}点 Bind={bindPoints.Count}点"
                + (created ? " [新建]" : " [更新]"));
        }

        Console.WriteLine($"\n安全区修正: {fixedCount} 张, 跳过 {skipped} 张");

        if (!dryRun)
        {
            session.Save(true);
            Console.WriteLine("已写入并保存 System.db");
        }
        else
        {
            Console.WriteLine("[dry-run] 未写库");
        }
        return 0;
    }
}
