using System.Drawing;
using Library;
using Library.SystemModels;
using MirDB;

namespace MapConnectionFixer;

/// <summary>
/// 阶段 B：地下城/洞窟层级连接全量打通。
///
/// 权威源：Mapinfo.txt（GB18030），2438 行
///   <源地图> <源X>,<源Y&gt; -&gt; &lt;目标地图&gt; &lt;目标X&gt;,&lt;目标Y&gt;
///
/// 语义要点（Mud3 原版实测）：
///   · 所有连接行都是**单向**的，没有一行带精确反向行；
///   · 双向通道由两条相邻行承担，例如
///       D001 30,328  -&gt; D002 34,323     （D001 门口格 → D002 落地格）
///       D002 34,322  -&gt; D001 30,329     （D002 门口格 → D001 落地格）
///     两者落点各偏移 1 格，故以容差 1 判定是否已有回程行。
///   · 另有 723 条是同图内传送（SourceMap == DestMap）。
///
/// 铁律 3：所有落点经 MapCellMap.NearestWalkable 校正，绝不写入不可行走格。
/// </summary>
internal static class DungeonFixer
{
    // 官方落点与本库 .map 存在尺寸/地形差异时需要较大外扩半径：
    // 实测 D903(61,92) 最近可行走点在 13 格外、D9032(6,17) 在 26 格外。
    private const int MaxSearchRadius = 40;

    public static int Run(string root, string mapDir, bool dryRun)
    {
        var all = Mud3Config.LoadConnections();
        Console.WriteLine($"Mapinfo.txt 解析: {all.Count} 条连接");

        var session = new Session(SessionMode.System, root, root + "Backup/");
        session.Initialize(typeof(ItemInfo).Assembly);

        var mapCol = session.GetCollection<MapInfo>();
        var regionCol = session.GetCollection<MapRegion>();
        var moveCol = session.GetCollection<MovementInfo>();

        var registered = mapCol.Binding.ToDictionary(m => m.FileName, m => m, StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0;
        int skippedNoMap = 0, skippedNoCell = 0, skippedBadPoint = 0;

        // 现有连接索引：用于幂等（避免重复插入同一条通道）
        var existingMoves = moveCol.Binding.ToList();

        foreach (var c in all)
        {
            if (!registered.TryGetValue(c.SourceMap, out var srcMap) ||
                !registered.TryGetValue(c.DestMap, out var dstMap))
            {
                skippedNoMap++;
                continue;
            }

            if (!MapCellMap.TryLoad(mapDir, c.SourceMap, out var srcCells) ||
                !MapCellMap.TryLoad(mapDir, c.DestMap, out var dstCells))
            {
                skippedNoCell++;
                continue;
            }

            // 铁律 3：两侧落点都必须落在可行走格上
            if (!srcCells.NearestWalkable(c.SourceX, c.SourceY, MaxSearchRadius, out var srcPt))
            {
                skippedBadPoint++;
                Console.WriteLine($"  [警告] {c.SourceMap} ({c.SourceX},{c.SourceY}) 半径 {MaxSearchRadius} 内无可行走点，跳过");
                continue;
            }
            if (!dstCells.NearestWalkable(c.DestX, c.DestY, MaxSearchRadius, out var dstPt))
            {
                skippedBadPoint++;
                Console.WriteLine($"  [警告] {c.DestMap} ({c.DestX},{c.DestY}) 半径 {MaxSearchRadius} 内无可行走点，跳过");
                continue;
            }

            bool adjusted = srcPt.X != c.SourceX || srcPt.Y != c.SourceY
                         || dstPt.X != c.DestX || dstPt.Y != c.DestY;

            // 幂等键 = 完整通道签名（源图+源点+目标图+落点）。
            // 只用「源图+源点」会误吞同一点位上的其他通道（如城门关口）。
            var existing = existingMoves.FirstOrDefault(m =>
                m.SourceRegion?.Map == srcMap && m.DestinationRegion?.Map == dstMap &&
                m.SourceRegion.PointRegion.Length > 0 && m.DestinationRegion.PointRegion.Length > 0 &&
                m.SourceRegion.PointRegion[0] == srcPt && m.DestinationRegion.PointRegion[0] == dstPt);

            // 该通道是否已有回程行（决定图标：Cave 进 / Exit 出）
            bool hasReverse = Mud3Config.HasReverseLine(all, c);
            MapIcon icon = c.SourceMap == c.DestMap
                ? MapIcon.Up
                : (hasReverse ? MapIcon.Exit : MapIcon.Cave);

            // 源端/落端区域都必须复用：每次新建会让 MapRegion 表无限膨胀
            // （幂等性验证：第二次运行 MD5 仍变化即为该缺陷）。
            // 落点区域只被这一条 Movement 引用，复用是安全的；
            // 不同通道之间的区域绝不共享（否则回程会落在错误格子）。
            var srcRegion = existing?.SourceRegion ?? regionCol.CreateNewObject();
            srcRegion.Map = srcMap;
            srcRegion.Description = $"{c.SourceMap} -> {c.DestMap} @({c.SourceX},{c.SourceY})";
            srcRegion.PointRegion = new[] { srcPt };
            srcRegion.Size = 1;

            var dstRegion = existing?.DestinationRegion ?? regionCol.CreateNewObject();
            dstRegion.Map = dstMap;
            dstRegion.Description = $"{c.SourceMap} -> {c.DestMap} @({c.SourceX},{c.SourceY}) 落点";
            dstRegion.PointRegion = new[] { dstPt };
            dstRegion.Size = 1;

            var move = existing ?? moveCol.CreateNewObject();
            if (existing == null) existingMoves.Add(move);
            move.SourceRegion = srcRegion;
            move.DestinationRegion = dstRegion;
            move.Icon = icon;

            if (existing == null) created++; else updated++;

            if (adjusted)
                Console.WriteLine($"  [校正] {c.SourceMap}({c.SourceX},{c.SourceY})->{c.DestMap}({c.DestX},{c.DestY})"
                                + $" 落点改为 ({srcPt.X},{srcPt.Y})->({dstPt.X},{dstPt.Y})");
        }

        // 清理孤立 MapRegion：重复运行或历史工具遗留的无主区块。
        // 必须在建新连接**之后**清理，否则会把刚建好、尚未被 Movement 引用的区域误删。
        int orphans = 0;
        foreach (var r in regionCol.Binding.ToList())
        {
            bool used = r.SourceMovements.Count > 0 || r.DestinationMovements.Count > 0
                        || r.SafeZones.Count > 0 || r.BindSafeZones.Count > 0
                        || r.NPCs.Count > 0 || r.Respawns.Count > 0
                        || r.QuestTasks.Count > 0;
            if (used) continue;
            r.Delete();
            orphans++;
        }
        Console.WriteLine($"清理孤立 MapRegion: {orphans} 个（剩余 {regionCol.Count}）");

        Console.WriteLine($"\n连接重建: 新增 {created} / 更新 {updated} / "
            + $"跳过(地图未登记) {skippedNoMap} / 跳过(缺 .map) {skippedNoCell} / 跳过(无落点) {skippedBadPoint}");
        Console.WriteLine($"MovementInfo 总数: {moveCol.Count}");

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
