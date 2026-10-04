using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;

namespace NpcAligner
{
    // NPC 体系对齐迁移工具（Step 3）
    //
    //   NpcAligner verify  <db_root> <matrix.json>             只校验计划，不改库
    //   NpcAligner apply   <db_root> <matrix.json>             干跑：打印将要迁移/停用的数量
    //   NpcAligner apply   <db_root> <matrix.json> commit      正式写库
    //
    // 铁律（对齐规划 §三）：
    //   * Migrate  —— 交集类：把 NPC 的 MapRegion 迁到原版权威地图/坐标
    //   * Disable  —— 新版多余 NPC：Region 置 null（软停用），服务端 SEnvir 跳过生成
    //                 🔴 绝不物理 DELETE（会打断 NPCPage/NPCAction/NPCCheck 外键树）
    //   * Retain / Pending —— 不动
    class Program
    {
        sealed class Row
        {
            public int npc_index;
            public string npc_name;
            public string cur_map;
            public int? cur_x;
            public int? cur_y;
            public string target_map;
            public int? target_x;
            public int? target_y;
            public string target_name_zh;
            public string mud3_id;
            public string action;
            public string reason;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 3)
            {
                Console.WriteLine("用法: NpcAligner <verify|apply> <db_root> <matrix.json> [commit]");
                return 1;
            }

            string mode = args[0];
            string root = args[1];
            if (!root.EndsWith("/") && !root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;
            string matrixFile = args[2];
            bool commit = args.Length > 3 && args[3].Equals("commit", StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(matrixFile))
            {
                Console.WriteLine($"找不到对齐矩阵: {matrixFile}");
                return 1;
            }

            var rows = LoadMatrix(matrixFile);
            Console.WriteLine($"对齐矩阵: {rows.Count} 条  来源 {matrixFile}");
            int nMigrate = rows.Count(r => r.action == "Migrate");
            int nDisable = rows.Count(r => r.action == "Disable");
            int nRetain = rows.Count(r => r.action == "Retain");
            int nPending = rows.Count(r => r.action == "Pending");
            Console.WriteLine($"  Migrate {nMigrate} / Disable {nDisable} / Retain {nRetain} / Pending {nPending}\n");

            // 铁律自检：矩阵里出现未定义动作直接拒绝
            var bad = rows.Where(r => r.action != "Migrate" && r.action != "Disable"
                                  && r.action != "Retain" && r.action != "Pending").ToList();
            if (bad.Count > 0)
            {
                Console.WriteLine($"拒绝：矩阵含未知动作 {bad.Count} 条");
                return 1;
            }
            // 停用项必须没有 Mud3 权威身份（防止误停经典 NPC）
            var badDisable = rows.Where(r => r.action == "Disable" && !string.IsNullOrEmpty(r.mud3_id)).ToList();
            if (badDisable.Count > 0)
            {
                Console.WriteLine($"拒绝：{badDisable.Count} 个停用项带 Mud3 身份，疑似经典 NPC 误判");
                foreach (var r in badDisable) Console.WriteLine($"  [{r.npc_index}] {r.npc_name} {r.mud3_id}");
                return 1;
            }

            // 写库前强制停服检查（AGENTS.md §四.1：服务端在跑绝不写库）
            if (commit && Port7000Open())
            {
                Console.WriteLine("拒绝：端口 7000 仍在监听，服务端未停。请先停止 ServerCore 再写库。");
                return 1;
            }

            var session = new Session(SessionMode.Both, root);
            // 铁律（Mir3-Research/AGENTS.md §七.1）：必须传 LibraryCore + ServerLibrary
            // 两个程序集。只传一个会让 Server.DBModels.* 全部 GetType 失败（静默丢表），
            // save 时可能写出残缺库。
            session.Initialize(Assembly.GetAssembly(typeof(ItemInfo)),
                               Assembly.GetAssembly(typeof(Server.DBModels.AccountInfo)));
            Console.WriteLine($"库路径: {session.SystemPath}  存在={session.SystemDatabaseExists}");

            var maps = session.GetCollection<MapInfo>().Binding.ToList();
            var npcs = session.GetCollection<NPCInfo>().Binding.ToList();
            var regions = session.GetCollection<MapRegion>();
            Console.WriteLine($"现役: 地图 {maps.Count} / NPC {npcs.Count} / MapRegion {regions.Binding.Count}\n");

            var npcByIndex = npcs.ToDictionary(n => n.Index);
            var mapByFile = new Dictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in maps)
                if (!string.IsNullOrEmpty(m.FileName)) mapByFile[m.FileName] = m;

            var errors = new List<string>();
            var migrate = new List<(Row row, MapInfo map, int x, int y)>();
            var disable = new List<(Row row, NPCInfo npc)>();

            foreach (var r in rows)
            {
                if (r.action != "Migrate" && r.action != "Disable") continue;
                if (!npcByIndex.TryGetValue(r.npc_index, out var npc))
                {
                    errors.Add($"[{r.npc_index}] {r.npc_name}: 数据库无此 NPC");
                    continue;
                }

                if (r.action == "Disable")
                {
                    disable.Add((r, npc));
                    continue;
                }

                // Migrate：校验目标地图存在 + 坐标非负
                if (!mapByFile.TryGetValue(r.target_map ?? "", out var map))
                {
                    errors.Add($"[{r.npc_index}] {r.npc_name}: 目标地图 {r.target_map} 不存在");
                    continue;
                }
                int x = r.target_x ?? -1, y = r.target_y ?? -1;
                if (x < 0 || y < 0)
                {
                    errors.Add($"[{r.npc_index}] {r.npc_name}: 目标坐标非法 ({x},{y})");
                    continue;
                }
                migrate.Add((r, map, x, y));
            }

            if (errors.Count > 0)
            {
                Console.WriteLine("=== 校验失败 ===");
                foreach (var e in errors) Console.WriteLine("  " + e);
                return 1;
            }

            Console.WriteLine("=== 计划 ===");
            Console.WriteLine($"  迁移 Migrate : {migrate.Count}");
            foreach (var (r, map, x, y) in migrate)
            {
                string oldDesc = Describe(npcByIndex[r.npc_index].Region);
                Console.WriteLine($"    [{r.npc_index,3}] {Trim(r.npc_name, 16)} {oldDesc} -> {map.FileName}({x},{y})  {r.mud3_id} {r.target_name_zh}");
            }
            Console.WriteLine($"  软停用 Disable: {disable.Count}");
            foreach (var (r, npc) in disable)
            {
                Console.WriteLine($"    [{r.npc_index,3}] {Trim(r.npc_name, 20)} {Describe(npc.Region)}  -> Region=null");
            }

            if (mode == "verify")
            {
                Console.WriteLine("\nverify 模式：仅校验，未改库。");
                return 0;
            }

            if (!commit)
            {
                Console.WriteLine("\n干跑完成（未写库）。加 commit 参数执行正式写入。");
                return 0;
            }

            // ---------- 正式写入 ----------
            Console.WriteLine("\n=== 正式写入 ===");
            int moved = 0, reused = 0, created = 0, disabled = 0, skippedSame = 0;

            foreach (var (r, map, x, y) in migrate)
            {
                var npc = npcByIndex[r.npc_index];
                var old = npc.Region;

                // 已在目标点位：不动，避免制造空 region
                if (old != null && old.Map == map && old.PointRegion != null && old.PointRegion.Length == 1
                    && old.PointRegion[0].X == x && old.PointRegion[0].Y == y)
                {
                    skippedSame++;
                    continue;
                }

                // 仅当旧 region 只被本 NPC 引用时才原位改，否则新建 region
                bool exclusive = old != null && CountOtherRefs(session, old, npc) == 0;
                MapRegion target;
                if (exclusive)
                {
                    target = old;
                    reused++;
                }
                else
                {
                    target = regions.CreateNewObject();
                    target.RegionType = old?.RegionType ?? RegionType.None;
                    target.Size = old?.Size ?? 0;
                    created++;
                }

                // 保留 " / <店型>" 后缀约定
                string suffix = old?.Description ?? "";
                int slash = suffix.IndexOf(" / ");
                if (slash >= 0) suffix = suffix.Substring(slash + 3);
                target.Description = suffix.Length > 0
                    ? $"{map.FileName} / {suffix}"
                    : (old?.Description ?? npc.NPCName);
                target.Map = map;
                target.PointRegion = new Point[] { new Point(x, y) };
                npc.Region = target;
                moved++;
                Console.WriteLine($"  迁移 [{r.npc_index,3}] {Trim(r.npc_name, 16)} -> {map.FileName}({x},{y}) [{(exclusive ? "原位改" : "新建region")}]");
            }

            foreach (var (r, npc) in disable)
            {
                if (npc.Region == null) continue;
                npc.Region = null;   // 软停用：脚本树保留，服务端跳过生成
                disabled++;
                Console.WriteLine($"  停用 [{r.npc_index,3}] {Trim(r.npc_name, 20)} Region=null");
            }

            Console.WriteLine($"\n结果: 迁移 {moved} (原位改 {reused} / 新建 {created}) | 已在位 {skippedSame} | 软停用 {disabled}");

            session.Save(true);
            Console.WriteLine("已写库（全量保存）");
            return 0;
        }

        static List<Row> LoadMatrix(string file)
        {
            var list = new List<Row>();
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var r = new Row
                {
                    npc_index = GetInt(e, "npc_index"),
                    npc_name = GetStr(e, "npc_name"),
                    cur_map = GetStr(e, "cur_map"),
                    cur_x = GetIntOrNull(e, "cur_x"),
                    cur_y = GetIntOrNull(e, "cur_y"),
                    target_map = GetStr(e, "target_map"),
                    target_x = GetIntOrNull(e, "target_x"),
                    target_y = GetIntOrNull(e, "target_y"),
                    target_name_zh = GetStr(e, "target_name_zh"),
                    mud3_id = GetStr(e, "mud3_id"),
                    action = GetStr(e, "action"),
                    reason = GetStr(e, "reason"),
                };
                list.Add(r);
            }
            return list;
        }

        static string GetStr(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int GetInt(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) && v.TryGetInt32(out int r) ? r : -1;
        static int? GetIntOrNull(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int r) ? r : (int?)null;

        static string Describe(MapRegion region)
        {
            if (region == null) return "(Region=null)";
            var p = region.PointRegion?.FirstOrDefault();
            return $"{region.Map?.FileName ?? "?"}({p?.X.ToString() ?? "?"},{p?.Y.ToString() ?? "?"})";
        }

        static string Trim(string s, int n)
        {
            s ??= "";
            return s.Length <= n ? s : s.Substring(0, n);
        }

        // 统计 region 被多少"其它"对象引用（除本 NPC 自身）
        static int CountOtherRefs(Session session, MapRegion region, NPCInfo self)
        {
            int count = 0;
            var refTypes = new Type[]
            {
                typeof(CastleInfo), typeof(FishingInfo), typeof(InstanceInfo),
                typeof(PlayerEventTrigger), typeof(MonsterEventTrigger), typeof(MonsterEventAction),
                typeof(MilestoneInfo), typeof(MineInfo), typeof(MovementInfo), typeof(NPCInfo),
                typeof(QuestInfo), typeof(RespawnInfo), typeof(SafeZoneInfo),
            };
            foreach (var t in refTypes)
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.PropertyType != typeof(MapRegion)) continue;
                    IEnumerable<DBObject> obs;
                    if (t == typeof(CastleInfo)) obs = session.GetCollection<CastleInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(PlayerEventTrigger)) obs = session.GetCollection<PlayerEventTrigger>().Binding.Cast<DBObject>();
                    else if (t == typeof(MonsterEventTrigger)) obs = session.GetCollection<MonsterEventTrigger>().Binding.Cast<DBObject>();
                    else if (t == typeof(MonsterEventAction)) obs = session.GetCollection<MonsterEventAction>().Binding.Cast<DBObject>();
                    else if (t == typeof(FishingInfo)) obs = session.GetCollection<FishingInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(InstanceInfo)) obs = session.GetCollection<InstanceInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(MilestoneInfo)) obs = session.GetCollection<MilestoneInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(MineInfo)) obs = session.GetCollection<MineInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(MovementInfo)) obs = session.GetCollection<MovementInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(NPCInfo)) obs = session.GetCollection<NPCInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(QuestInfo)) obs = session.GetCollection<QuestInfo>().Binding.Cast<DBObject>();
                    else if (t == typeof(RespawnInfo)) obs = session.GetCollection<RespawnInfo>().Binding.Cast<DBObject>();
                    else obs = session.GetCollection<SafeZoneInfo>().Binding.Cast<DBObject>();

                    foreach (var ob in obs)
                    {
                        if (p.GetValue(ob) != region) continue;
                        if (ob is NPCInfo n && n == self) continue;
                        count++;
                    }
                }
            }
            return count;
        }

        static bool Port7000Open()
        {
            try
            {
                using var client = new TcpClient();
                var task = client.ConnectAsync("127.0.0.1", 7000);
                return task.Wait(300) && client.Connected;
            }
            catch { return false; }
        }
    }
}
