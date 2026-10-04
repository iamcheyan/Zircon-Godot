using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Security.Cryptography;
using System.Collections.Generic;
using Library;
using Library.MirDB;
using Library.SystemModels;
using MirDB;
using Server.DBModels;

namespace MapConnectionFixer;

class Program
{
    static void Main(string[] args)
    {
        string root = Path.GetFullPath("Debug/ServerCore/Database/") + Path.DirectorySeparatorChar;

        if (args.Length > 0 && args[0] == "--audit")
        {
            Console.WriteLine("=== 开始对全游戏地图连接 (Movement) 与安全区 (SafeZone) 进行全面体检 ===");
            AuditAll(root);
            return;
        }

        Console.WriteLine("=== 开始修复比奇 (0) 地图连接点与安全区坐标 ===");

        FixMapConnectionsAndSafeZone(root);
        SyncSystemDb(root);
        Verify(root);

        Console.WriteLine("\n=== 修复与同步完成！ ===");
    }

    static void FixMapConnectionsAndSafeZone(string root)
    {
        var session = new Session(SessionMode.System, root);
        session.Initialize(typeof(ItemInfo).Assembly);

        var mapCol = session.GetCollection<MapInfo>();
        var regionCol = session.GetCollection<MapRegion>();
        var movementCol = session.GetCollection<MovementInfo>();
        var safezoneCol = session.GetCollection<SafeZoneInfo>();

        var map0 = mapCol.Binding.FirstOrDefault(m => m.FileName == "0");
        var mapD001 = mapCol.Binding.FirstOrDefault(m => m.FileName == "D001");
        var mapD401 = mapCol.Binding.FirstOrDefault(m => m.FileName == "D401");
        var map1 = mapCol.Binding.FirstOrDefault(m => m.FileName == "1");
        var map2 = mapCol.Binding.FirstOrDefault(m => m.FileName == "2");

        if (map0 == null)
        {
            Console.WriteLine("错误: 找不到比奇地图 (0)！");
            return;
        }

        Console.WriteLine($"[1] 找到主地图: #{map0.Index} {map0.FileName} ({map0.Description})");

        string mapDir = Path.GetFullPath("Debug/ServerCore/Map/") + Path.DirectorySeparatorChar;
        var valid0 = LoadValidCells(Path.Combine(mapDir, "0.map"));
        var validD001 = LoadValidCells(Path.Combine(mapDir, "D001.map"));
        var validD401 = LoadValidCells(Path.Combine(mapDir, "D401.map"));
        var valid1 = LoadValidCells(Path.Combine(mapDir, "1.map"));
        var valid2 = LoadValidCells(Path.Combine(mapDir, "2.map"));

        // -------------------------------------------------------------
        // 1. 天然洞穴 (D001 ↔ 0 比奇县) 连接修复
        // -------------------------------------------------------------
        if (mapD001 != null)
        {
            Console.WriteLine("\n[2/5] 修复天然洞穴 (D001 ↔ 0) 连接...");

            // 1.1 比奇 -> D001 入口 (65, 174) -> (151, 362)
            var srcReg0_D001 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / Natural Cave 1 Entrance") 
                            ?? regionCol.CreateNewObject();
            srcReg0_D001.Map = map0;
            srcReg0_D001.Description = "0 / Natural Cave 1 Entrance";
            var d001Candidates = new[] { new Point(65, 174), new Point(65, 175), new Point(64, 174), new Point(64, 175) };
            srcReg0_D001.PointRegion = d001Candidates.Where(p => valid0.Contains(p)).ToArray();

            var dstRegD001_Landing = regionCol.Binding.FirstOrDefault(r => r.Map == mapD001 && r.Description == "D001 / Entrance Landing")
                                  ?? regionCol.CreateNewObject();
            dstRegD001_Landing.Map = mapD001;
            dstRegD001_Landing.Description = "D001 / Entrance Landing";
            dstRegD001_Landing.PointRegion = new Point[] { new Point(151, 362) };

            var move0_To_D001 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcReg0_D001) ?? movementCol.CreateNewObject();
            move0_To_D001.SourceRegion = srcReg0_D001;
            move0_To_D001.DestinationRegion = dstRegD001_Landing;
            move0_To_D001.Icon = MapIcon.Cave;

            // 1.2 D001 -> 比奇 出口 (149, 364) -> (64, 175)
            var srcRegD001_Door = regionCol.Binding.FirstOrDefault(r => r.Map == mapD001 && r.Description == "D001 / Entrance Door")
                               ?? regionCol.CreateNewObject();
            srcRegD001_Door.Map = mapD001;
            srcRegD001_Door.Description = "D001 / Entrance Door";
            var d001DoorCandidates = new[] { new Point(149, 364), new Point(150, 364), new Point(148, 366), new Point(150, 363) };
            srcRegD001_Door.PointRegion = d001DoorCandidates.Where(p => validD001.Contains(p)).ToArray();

            var dstReg0_FromD001 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / Natural Cave 1 Landing")
                                ?? regionCol.CreateNewObject();
            dstReg0_FromD001.Map = map0;
            dstReg0_FromD001.Description = "0 / Natural Cave 1 Landing";
            dstReg0_FromD001.PointRegion = new Point[] { new Point(64, 175) };

            var moveD001_To_0 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcRegD001_Door) ?? movementCol.CreateNewObject();
            moveD001_To_0.SourceRegion = srcRegD001_Door;
            moveD001_To_0.DestinationRegion = dstReg0_FromD001;
            moveD001_To_0.Icon = MapIcon.Exit;

            Console.WriteLine("  ✔ 已建立比奇 (65, 174) ↔ 天然洞穴1层 D001 (151, 362) 双向进出连接！");
        }

        // -------------------------------------------------------------
        // 2. 洞窟/绝望谷/跳蚤洞 (D401 ↔ 0 比奇县) 修复
        // -------------------------------------------------------------
        if (mapD401 != null)
        {
            Console.WriteLine("\n[3/5] 修复 D401 洞窟 (比奇 764, 206 ↔ D401 25, 181)...");

            var srcReg0_D401 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / D401 Cave Entrance")
                            ?? regionCol.CreateNewObject();
            srcReg0_D401.Map = map0;
            srcReg0_D401.Description = "0 / D401 Cave Entrance";
            srcReg0_D401.PointRegion = new Point[] { new Point(764, 206) };

            var dstRegD401_Landing = regionCol.Binding.FirstOrDefault(r => r.Map == mapD401 && r.Description == "D401 / Entrance Landing")
                                  ?? regionCol.CreateNewObject();
            dstRegD401_Landing.Map = mapD401;
            dstRegD401_Landing.Description = "D401 / Entrance Landing";
            dstRegD401_Landing.PointRegion = new Point[] { new Point(25, 181) };

            // 纠正原错连到 d6015 (生死关) 的 Movement #4102
            var move4102 = movementCol.Binding.FirstOrDefault(m => m.Index == 4102) ?? movementCol.CreateNewObject();
            move4102.SourceRegion = srcReg0_D401;
            move4102.DestinationRegion = dstRegD401_Landing;
            move4102.Icon = MapIcon.Cave;

            // 纠正原反向 Movement #4103
            var srcRegD401_Door = regionCol.Binding.FirstOrDefault(r => r.Map == mapD401 && r.Description == "D401 / Entrance Door")
                               ?? regionCol.CreateNewObject();
            srcRegD401_Door.Map = mapD401;
            srcRegD401_Door.Description = "D401 / Entrance Door";
            srcRegD401_Door.PointRegion = new Point[] { new Point(24, 182) };

            var dstReg0_FromD401 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / D401 Cave Landing")
                                ?? regionCol.CreateNewObject();
            dstReg0_FromD401.Map = map0;
            dstReg0_FromD401.Description = "0 / D401 Cave Landing";
            dstReg0_FromD401.PointRegion = new Point[] { new Point(763, 207) };

            var move4103 = movementCol.Binding.FirstOrDefault(m => m.Index == 4103) ?? movementCol.CreateNewObject();
            move4103.SourceRegion = srcRegD401_Door;
            move4103.DestinationRegion = dstReg0_FromD401;
            move4103.Icon = MapIcon.Exit;

            Console.WriteLine("  ✔ 已修正比奇 (764, 206) -> D401 (25, 181)，解绑错误的 d6015 生死关！");
        }

        // -------------------------------------------------------------
        // 3. 道馆 (1) 与蛇谷 (2) 关口通道校准
        // -------------------------------------------------------------
        Console.WriteLine("\n[4/5] 校准比奇通往道馆 (1) 与蛇谷 (2) 的关口通道...");

        // 清除错连到 82 (冰原雪城) 的连接
        var bad82Moves = movementCol.Binding.Where(m =>
            (m.SourceRegion?.Map?.FileName == "0" && m.DestinationRegion?.Map?.FileName == "82") ||
            (m.SourceRegion?.Map?.FileName == "82" && m.DestinationRegion?.Map?.FileName == "0")).ToList();
        foreach (var m in bad82Moves)
        {
            Console.WriteLine($"  - 移除错连到雪原的 Movement #{m.Index}");
            m.Delete();
        }

        // 3.0 清理比奇东北关口被误连到道馆的旧冲突连接 Movement #4032 - #4037
        var badOldMoves = movementCol.Binding.Where(m => m.Index >= 4032 && m.Index <= 4037).ToList();
        foreach (var m in badOldMoves)
        {
            Console.WriteLine($"  - 移除东北关口错连道馆的旧冲突连接 Movement #{m.Index}");
            m.Delete();
        }

        // 3.1 比奇西北 ↔ 道馆 (1): 0(207, 51) ↔ 1(514, 580)
        if (map1 != null)
        {
            var srcReg0_To1 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / To Taoist Temple Gate") 
                           ?? regionCol.CreateNewObject();
            srcReg0_To1.Map = map0;
            srcReg0_To1.Description = "0 / To Taoist Temple Gate";
            srcReg0_To1.PointRegion = new Point[] { new Point(206, 52), new Point(207, 51), new Point(208, 50), new Point(209, 49) };

            var dstReg1_From0 = regionCol.Binding.FirstOrDefault(r => r.Map == map1 && r.Description == "1 / From Bichon Landing")
                             ?? regionCol.CreateNewObject();
            dstReg1_From0.Map = map1;
            dstReg1_From0.Description = "1 / From Bichon Landing";
            dstReg1_From0.PointRegion = new Point[] { new Point(513, 581), new Point(514, 580), new Point(515, 579) };

            var move0_To_1 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcReg0_To1) ?? movementCol.CreateNewObject();
            move0_To_1.SourceRegion = srcReg0_To1;
            move0_To_1.DestinationRegion = dstReg1_From0;
            move0_To_1.Icon = MapIcon.Province;

            // 反向 1 -> 0
            var srcReg1_To0 = regionCol.Binding.FirstOrDefault(r => r.Map == map1 && r.Description == "1 / To Bichon Gate")
                           ?? regionCol.CreateNewObject();
            srcReg1_To0.Map = map1;
            srcReg1_To0.Description = "1 / To Bichon Gate";
            srcReg1_To0.PointRegion = new Point[] { new Point(514, 582), new Point(515, 581), new Point(516, 580) };

            var dstReg0_From1 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / From Taoist Temple Landing")
                             ?? regionCol.CreateNewObject();
            dstReg0_From1.Map = map0;
            dstReg0_From1.Description = "0 / From Taoist Temple Landing";
            dstReg0_From1.PointRegion = new Point[] { new Point(208, 52), new Point(209, 51), new Point(210, 50) };

            var move1_To_0 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcReg1_To0) ?? movementCol.CreateNewObject();
            move1_To_0.SourceRegion = srcReg1_To0;
            move1_To_0.DestinationRegion = dstReg0_From1;
            move1_To_0.Icon = MapIcon.Province;

            Console.WriteLine("  ✔ 已建立比奇西北关口 (207, 51) ↔ 道馆 (514, 580) 双向连接！");
        }

        // 3.2 比奇东北 ↔ 蛇谷 (2): 0(644, 16) ↔ 2(160, 370)
        if (map2 != null)
        {
            var srcReg0_To2 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / To Snake Valley Gate")
                           ?? regionCol.CreateNewObject();
            srcReg0_To2.Map = map0;
            srcReg0_To2.Description = "0 / To Snake Valley Gate";
            srcReg0_To2.PointRegion = new Point[] { new Point(643, 15), new Point(644, 16), new Point(645, 17), new Point(646, 18) };

            var dstReg2_From0 = regionCol.Binding.FirstOrDefault(r => r.Map == map2 && r.Description == "2 / From Bichon Landing")
                             ?? regionCol.CreateNewObject();
            dstReg2_From0.Map = map2;
            dstReg2_From0.Description = "2 / From Bichon Landing";
            dstReg2_From0.PointRegion = new Point[] { new Point(159, 369), new Point(160, 370), new Point(161, 371) };

            var move0_To_2 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcReg0_To2) ?? movementCol.CreateNewObject();
            move0_To_2.SourceRegion = srcReg0_To2;
            move0_To_2.DestinationRegion = dstReg2_From0;
            move0_To_2.Icon = MapIcon.Province;

            // 反向 2 -> 0
            var srcReg2_To0 = regionCol.Binding.FirstOrDefault(r => r.Map == map2 && r.Description == "2 / To Bichon Gate")
                           ?? regionCol.CreateNewObject();
            srcReg2_To0.Map = map2;
            srcReg2_To0.Description = "2 / To Bichon Gate";
            srcReg2_To0.PointRegion = new Point[] { new Point(158, 370), new Point(159, 371), new Point(160, 372) };

            var dstReg0_From2 = regionCol.Binding.FirstOrDefault(r => r.Map == map0 && r.Description == "0 / From Snake Valley Landing")
                             ?? regionCol.CreateNewObject();
            dstReg0_From2.Map = map0;
            dstReg0_From2.Description = "0 / From Snake Valley Landing";
            dstReg0_From2.PointRegion = new Point[] { new Point(643, 16), new Point(644, 18), new Point(645, 19) };

            var move2_To_0 = movementCol.Binding.FirstOrDefault(m => m.SourceRegion == srcReg2_To0) ?? movementCol.CreateNewObject();
            move2_To_0.SourceRegion = srcReg2_To0;
            move2_To_0.DestinationRegion = dstReg0_From2;
            move2_To_0.Icon = MapIcon.Province;

            Console.WriteLine("  ✔ 已建立比奇东北关口 (644, 16) ↔ 蛇谷 (160, 370) 双向连接！");
        }

        // -------------------------------------------------------------
        // 4. 安全区 (SafeZone) 坐标修复
        // -------------------------------------------------------------
        Console.WriteLine("\n[5/5] 修复比奇城安全区 (SafeZone) 坐标...");

        var sz29 = safezoneCol.Binding.FirstOrDefault(s => s.Index == 29) ??
                   safezoneCol.Binding.FirstOrDefault(s => s.Region?.Map == map0);

        if (sz29 != null)
        {
            // 原版比奇城中心大雕像老兵坐标: (458, 398)
            int centerX = 458;
            int centerY = 398;
            int radius = 18;

            var safePoints = new List<Point>();
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        var pt = new Point(centerX + dx, centerY + dy);
                        if (valid0.Contains(pt))
                        {
                            safePoints.Add(pt);
                        }
                    }
                }
            }

            var bindPoints = new List<Point>();
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    var pt = new Point(centerX + dx, centerY + dy);
                    if (valid0.Contains(pt))
                    {
                        bindPoints.Add(pt);
                    }
                }
            }

            var szRegion = sz29.Region ?? regionCol.CreateNewObject();
            szRegion.Map = map0;
            szRegion.Description = "Bichon Central SafeZone Area";
            szRegion.PointRegion = safePoints.ToArray();

            // 必须使用独立的 MapRegion 作为 BindRegion，防止引用同一个对象互相覆盖
            var bindRegion = (sz29.BindRegion != null && sz29.BindRegion != szRegion) 
                           ? sz29.BindRegion 
                           : regionCol.CreateNewObject();
            bindRegion.Map = map0;
            bindRegion.Description = "Bichon Central SafeZone Landing";
            bindRegion.PointRegion = bindPoints.ToArray();

            sz29.Region = szRegion;
            sz29.BindRegion = bindRegion;

            Console.WriteLine($"  ✔ 比奇城安全区已重置为中心 ({centerX}, {centerY})！");
            Console.WriteLine($"    - 安全保护区范围点数: {safePoints.Count} 格 (半径 {radius})");
            Console.WriteLine($"    - 出生/回城绑定区点数: {bindPoints.Count} 格");
        }

        session.Save(true);
        Console.WriteLine("\n所有修改已成功保存至 System.db！");
    }

    static void SyncSystemDb(string root)
    {
        Console.WriteLine("\n正在同步 System.db 到 4 处镜像...");
        string src = Path.Combine(root, "System.db");
        string[] targets =
        {
            "/home/tetsuya/mir2ei/Data/System.db",
            "/home/tetsuya/mir2ei/Database/System.db",
            Path.GetFullPath("System.db"),
            "/home/tetsuya/development/Debug/ServerCore/Database/System.db"
        };

        foreach (var t in targets)
        {
            File.Copy(src, t, true);
        }

        using var md5 = MD5.Create();
        using var stream = File.OpenRead(src);
        string hash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        Console.WriteLine($"全处 System.db 同步完成，统一 MD5: {hash}");
    }

    static void Verify(string root)
    {
        Console.WriteLine("\n读回校验修复结果...");
        var session = new Session(SessionMode.System, root);
        session.Initialize(typeof(ItemInfo).Assembly);

        var mapCol = session.GetCollection<MapInfo>();
        var movementCol = session.GetCollection<MovementInfo>();
        var safezoneCol = session.GetCollection<SafeZoneInfo>();

        var map0 = mapCol.Binding.FirstOrDefault(m => m.FileName == "0");
        Console.WriteLine($"比奇地图 [0] 当前连接总数: {movementCol.Binding.Count(m => m.SourceRegion?.Map == map0 || m.DestinationRegion?.Map == map0)}");

        foreach (var m in movementCol.Binding.Where(m => m.SourceRegion?.Map == map0 || m.DestinationRegion?.Map == map0))
        {
            var s = m.SourceRegion;
            var d = m.DestinationRegion;
            var s_pt = (s?.PointRegion != null && s.PointRegion.Length > 0) ? $"({s.PointRegion[0].X},{s.PointRegion[0].Y})" : "-";
            var d_pt = (d?.PointRegion != null && d.PointRegion.Length > 0) ? $"({d.PointRegion[0].X},{d.PointRegion[0].Y})" : "-";
            Console.WriteLine($"  ✔ Movement #{m.Index,-4} | {s?.Map?.FileName} {s_pt} -> {d?.Map?.FileName} {d_pt} | {s?.Description} -> {d?.Description}");
        }

        var sz = safezoneCol.Binding.FirstOrDefault(s => s.Region?.Map == map0);
        if (sz != null)
        {
            var r_pt = sz.Region?.PointRegion?.FirstOrDefault();
            var b_pt = sz.BindRegion?.PointRegion?.FirstOrDefault();
            Console.WriteLine($"  ✔ 比奇安全区: Region首点=({r_pt?.X},{r_pt?.Y}) (共{sz.Region?.PointRegion?.Length}点), Bind首点=({b_pt?.X},{b_pt?.Y})");
        }
    }

    static HashSet<Point> LoadValidCells(string mapPath)
    {
        var valid = new HashSet<Point>();
        if (!File.Exists(mapPath)) return valid;

        byte[] fileBytes = File.ReadAllBytes(mapPath);
        int width = fileBytes[23] << 8 | fileBytes[22];
        int height = fileBytes[25] << 8 | fileBytes[24];
        int offSet = 28 + width * height / 4 * 3;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                byte flag = fileBytes[offSet + (x * height + y) * 14];
                if ((flag & 0x02) == 2 && (flag & 0x01) == 1)
                {
                    valid.Add(new Point(x, y));
                }
            }
        }
        return valid;
    }

    static void AuditAll(string root)
    {
        var session = new Session(SessionMode.System, root);
        session.Initialize(typeof(ItemInfo).Assembly);

        var mapCol = session.GetCollection<MapInfo>();
        var movementCol = session.GetCollection<MovementInfo>();
        var safezoneCol = session.GetCollection<SafeZoneInfo>();

        Console.WriteLine($"\n[1. 地图总体统计]");
        Console.WriteLine($"  - MapInfo 登记总数: {mapCol.Binding.Count}");
        Console.WriteLine($"  - SafeZoneInfo 登记总数: {safezoneCol.Binding.Count}");
        Console.WriteLine($"  - MovementInfo 登记总数: {movementCol.Binding.Count}");

        // 1. SafeZone 坐标比对与分析
        Console.WriteLine($"\n[2. 安全区 (SafeZone) 现状审查]");
        var originalStartPoints = new Dictionary<string, (int x, int y)>
        {
            { "01", (439, 304) },
            { "02", (265, 207) },
            { "0",  (458, 398) },
            { "1",  (423, 102) },
            { "2",  (342, 222) },
            { "4",  (457, 77) },
            { "41", (167, 94) },
            { "5",  (216, 185) },
            { "74", (315, 287) },
            { "8",  (237, 273) },
            { "9",  (193, 572) },
            { "81", (130, 273) }
        };

        foreach (var sz in safezoneCol.Binding)
        {
            var map = sz.Region?.Map ?? sz.BindRegion?.Map;
            var regPt = sz.Region?.PointRegion?.FirstOrDefault();
            var bindPt = sz.BindRegion?.PointRegion?.FirstOrDefault();
            string fn = map?.FileName ?? "未知";
            string desc = map?.Description ?? "无描述";

            string status = "正常";
            if (originalStartPoints.TryGetValue(fn, out var orig))
            {
                // 计算与原版中心的距离
                if (bindPt != null)
                {
                    int dist = Math.Max(Math.Abs(bindPt.Value.X - orig.x), Math.Abs(bindPt.Value.Y - orig.y));
                    if (dist > 30) status = $"⚠️ 距离原版({orig.x},{orig.y})偏差较大 (Δ={dist})";
                    else status = $"✔ 与原版相符({orig.x},{orig.y})";
                }
            }
            Console.WriteLine($"  SafeZone #{sz.Index,-3} | 地图: {fn,-10} ({desc,-12}) | Region点数:{sz.Region?.PointRegion?.Length,4} | Bind首点:{bindPt} | {status}");
        }

        // 2. 坏连接与孤儿连接审查
        Console.WriteLine($"\n[3. 异常连接 (Movement) 扫描]");
        int invalidCount = 0;
        foreach (var m in movementCol.Binding)
        {
            bool badSource = m.SourceRegion == null || m.SourceRegion.Map == null || m.SourceRegion.PointRegion == null || m.SourceRegion.PointRegion.Length == 0;
            bool badDest = m.DestinationRegion == null || m.DestinationRegion.Map == null || m.DestinationRegion.PointRegion == null || m.DestinationRegion.PointRegion.Length == 0;

            if (badSource || badDest)
            {
                invalidCount++;
                Console.WriteLine($"  ⚠️ 坏连接 Movement #{m.Index}: Source={m.SourceRegion?.Map?.FileName} ({m.SourceRegion?.Description}), Dest={m.DestinationRegion?.Map?.FileName} ({m.DestinationRegion?.Description})");
            }
        }
        if (invalidCount == 0) Console.WriteLine("  ✔ 未发现任何 Source/Dest 为空的坏连接。");

        // 3. 洞窟内部层级连通性普查
        Console.WriteLine($"\n[4. 常见地下城/洞窟层级连接抽检]");
        string[][] dungeonChains = new string[][]
        {
            new string[] { "天然洞穴", "D001", "D002", "D003", "D004" },
            new string[] { "骷髅洞", "D011", "D012", "D013", "D014", "D015" },
            new string[] { "比奇矿区", "D202", "D203" },
            new string[] { "跳蚤洞/绝望谷", "D401", "D402", "D403", "D404", "D405", "D406" },
            new string[] { "沃玛神殿", "D1001", "D1002", "D1003", "D1004" },
            new string[] { "祖玛神殿", "D1101", "D1102", "D1103", "D1104", "D1105", "D1106", "D1107" },
            new string[] { "石墓(猪洞)", "D1201", "D1202", "D1203", "D1204", "D1205", "D1206", "D1207" },
            new string[] { "潘夜石窟", "D1401", "D1402", "D1403", "D1404", "D1405" }
        };

        foreach (var chain in dungeonChains)
        {
            string name = chain[0];
            Console.WriteLine($"  ▶ {name}:");
            for (int i = 1; i < chain.Length; i++)
            {
                string curr = chain[i];
                var mCurr = mapCol.Binding.FirstOrDefault(m => m.FileName == curr);
                if (mCurr == null)
                {
                    Console.WriteLine($"    - 地图 {curr} 在 System.db 中未注册！");
                    continue;
                }

                if (i < chain.Length - 1)
                {
                    string next = chain[i + 1];
                    var mNext = mapCol.Binding.FirstOrDefault(m => m.FileName == next);
                    if (mNext != null)
                    {
                        var forward = movementCol.Binding.Any(m => m.SourceRegion?.Map == mCurr && m.DestinationRegion?.Map == mNext);
                        var backward = movementCol.Binding.Any(m => m.SourceRegion?.Map == mNext && m.DestinationRegion?.Map == mCurr);
                        string status = (forward && backward) ? "✔ 双向连通" : (forward ? "⚠️ 仅有单向去路" : "❌ 未连通");
                        Console.WriteLine($"    - {curr} ↔ {next}: {status}");
                    }
                }
            }
        }
    }
}
