using System.Security.Cryptography;
using System.Text.Json;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// NPC 正统中文名全量对齐工具（将 230 个活动 NPC 名字从英文/代号更新为权威正统中文名）
/// </summary>
internal static class NPCNameAligner
{
    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/development/Zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    private class NPCJsonItem
    {
        public int npc_index { get; set; }
        public string name_zh { get; set; } = "";
        public string name_en { get; set; } = "";
        public string map_code { get; set; } = "";
        public string map_name_zh { get; set; } = "";
        public int x { get; set; }
        public int y { get; set; }
        public string category { get; set; } = "";
    }

    public static int Run(string targetRoot, bool dryRun = false)
    {
        string jsonPath = "/home/tetsuya/development/zircon/tools/aligned_npcs_230.json";
        if (!File.Exists(jsonPath))
        {
            Console.Error.WriteLine($"[错误] 找不到对齐数据文件: {jsonPath}");
            return 1;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        var items = JsonSerializer.Deserialize<List<NPCJsonItem>>(jsonContent);
        if (items == null || items.Count == 0)
        {
            Console.Error.WriteLine("[错误] 对齐数据解析失败或为空");
            return 1;
        }

        Console.WriteLine($"[加载] 从 {jsonPath} 加载了 {items.Count} 条 NPC 对齐配置。");

        var session = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
        session.Initialize(typeof(NPCInfo).Assembly, typeof(MapInfo).Assembly, typeof(MapRegion).Assembly);

        var npcColl = session.GetCollection<NPCInfo>();
        var npcMap = npcColl.Binding.ToDictionary(n => n.Index);

        int updatedCount = 0;
        int skippedCount = 0;
        int notFoundCount = 0;

        foreach (var item in items)
        {
            if (!npcMap.TryGetValue(item.npc_index, out var npc))
            {
                Console.WriteLine($"[未找到] NPC #{item.npc_index} 不在数据库中");
                notFoundCount++;
                continue;
            }

            if (npc.NPCName != item.name_zh)
            {
                Console.WriteLine($"[更新] NPC #{item.npc_index,-3} [{item.map_name_zh} ({item.x},{item.y})] '{npc.NPCName}' -> '{item.name_zh}'");
                if (!dryRun)
                {
                    npc.NPCName = item.name_zh;
                }
                updatedCount++;
            }
            else
            {
                skippedCount++;
            }
        }

        Console.WriteLine($"\n[统计] 待更新: {updatedCount}, 已一致: {skippedCount}, 数据库未找到: {notFoundCount}");

        if (dryRun)
        {
            Console.WriteLine("[演练模式] 未向数据库写入任何改动。");
            return 0;
        }

        if (updatedCount > 0)
        {
            session.Save(true);
            Console.WriteLine($"[保存] 成功保存 {updatedCount} 条 NPC 名字更新到数据库。");
            return Sync(targetRoot) ? 0 : 1;
        }

        Console.WriteLine("[完成] 所有 NPC 名字已是最新，无需修改。");
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
