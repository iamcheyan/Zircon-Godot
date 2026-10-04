using System.Reflection;
using System.Security.Cryptography;
using Library;
using Library.SystemModels;
using MirDB;

namespace ClassicMagicFixer;

/// <summary>
/// 按照系统双轨实体保护体系，精确清理 487~492 临时怪物，
/// 并从原始备份库按原生 Index 恢复核心系统机制实体（144, 145, 146, 68, 118, 119）及其属性。
/// </summary>
internal static class RestoreNativeEntities
{
    private static readonly int[] NativeIndices = [68, 118, 119, 144, 145, 146];

    private static readonly string[] Mirrors =
    [
        "/home/tetsuya/development/zircon/System.db",
        "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db",
        "/home/tetsuya/mir2ei/Data/System.db",
        "/home/tetsuya/mir2ei/Database/System.db"
    ];

    public static int Run(string targetRoot, string backupDbPath)
    {
        static string NormalizeRoot(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

        targetRoot = NormalizeRoot(targetRoot);

        if (!File.Exists(backupDbPath))
        {
            Console.Error.WriteLine($"[FAIL] 备份库不存在: {backupDbPath}");
            return 1;
        }

        string tempBackupDir = Path.Combine(Path.GetTempPath(), "mir3_backup_restore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBackupDir);
        string tempBackupDb = Path.Combine(tempBackupDir, "System.db");
        File.Copy(backupDbPath, tempBackupDb, true);

        try
        {
            var source = new Session(SessionMode.System, tempBackupDir + Path.DirectorySeparatorChar, tempBackupDir + "/Backup/");
            source.Initialize(typeof(MonsterInfo).Assembly);

            var target = new Session(SessionMode.System, targetRoot, targetRoot + "Backup/");
            target.Initialize(typeof(MonsterInfo).Assembly);

            var sourceMonsters = source.GetCollection<MonsterInfo>();
            var sourceStats = source.GetCollection<MonsterInfoStat>();

            var targetMonsters = target.GetCollection<MonsterInfo>();
            var targetStats = target.GetCollection<MonsterInfoStat>();

            // 1. 删除前序智能体追加的 487..492 临时怪及其属性
            var tempMonsters = targetMonsters.Binding
                .Where(x => x.Index >= 487 && x.Index <= 492)
                .ToList();

            int deletedStats = 0;
            foreach (var m in tempMonsters)
            {
                var statsToDelete = targetStats.Binding.Where(s => s.Monster == m).ToList();
                foreach (var s in statsToDelete)
                {
                    deletedStats++;
                    s.Delete();
                }
                Console.WriteLine($"[清理] 移除临时怪物 Index={m.Index} Name={m.MonsterName} Flag={m.Flag}");
                m.Delete();
            }

            var indexProperty = typeof(DBObject).GetProperty("Index", BindingFlags.Public | BindingFlags.Instance);
            if (indexProperty == null || !indexProperty.CanWrite)
            {
                // Property has internal setter
                indexProperty = typeof(DBObject).GetProperty("Index", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            int restored = 0;
            foreach (int nativeIndex in NativeIndices)
            {
                // 检查 target 是否已存在原生 Index 的怪物
                var existing = targetMonsters.Binding.FirstOrDefault(x => x.Index == nativeIndex);
                if (existing != null)
                {
                    Console.WriteLine($"[保留] 原生怪物 Index={nativeIndex} 已存在: {existing.MonsterName} (Flag={existing.Flag})");
                    continue;
                }

                var src = sourceMonsters.Binding.FirstOrDefault(x => x.Index == nativeIndex);
                if (src == null)
                {
                    Console.Error.WriteLine($"[FAIL] 备份库中未找到原生 Index={nativeIndex} 的怪物！");
                    return 1;
                }

                var dst = targetMonsters.CreateNewObject();
                // 恢复原生 Index
                indexProperty.SetValue(dst, nativeIndex);

                dst.MonsterName = src.MonsterName;
                dst.Image = src.Image;
                dst.AI = src.AI;
                dst.Level = src.Level;
                dst.ViewRange = src.ViewRange;
                dst.CoolEye = src.CoolEye;
                dst.Experience = src.Experience;
                dst.Undead = src.Undead;
                dst.CanPush = src.CanPush;
                dst.CanTame = src.CanTame;
                dst.AttackDelay = src.AttackDelay;
                dst.MoveDelay = src.MoveDelay;
                dst.IsBoss = src.IsBoss;
                dst.Flag = src.Flag;
                dst.FaceImage = src.FaceImage;

                int statCount = 0;
                foreach (var srcStat in sourceStats.Binding.Where(x => x.Monster == src))
                {
                    var dstStat = targetStats.CreateNewObject();
                    dstStat.Monster = dst;
                    dstStat.Stat = srcStat.Stat;
                    dstStat.Amount = srcStat.Amount;
                    statCount++;
                }

                restored++;
                Console.WriteLine($"[恢复原生] Index={nativeIndex,-3} Name='{dst.MonsterName,-20}' Flag={dst.Flag,-18} AI={dst.AI,-3} Img={dst.Image,-16} Lv={dst.Level} ({statCount} stats)");
            }

            // 保持 targetMonsters.Binding 按照 Index 升序排列
            var itemsField = typeof(System.Collections.ObjectModel.Collection<MonsterInfo>).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
            var innerList = itemsField?.GetValue(targetMonsters.Binding) as List<MonsterInfo>;
            if (innerList != null)
            {
                innerList.Sort((a, b) => a.Index.CompareTo(b.Index));
            }

            // 保持 targetMonsters.Index (最大已分配 Index) 处于合理值（最大对象的 Index）
            int maxIndex = targetMonsters.Binding.Max(x => x.Index);
            if (targetMonsters.Index < maxIndex)
            {
                targetMonsters.Index = maxIndex;
            }

            target.Save(true);
            Console.WriteLine($"原生实体恢复成功！新增 {restored} 个原生实体，清理 {tempMonsters.Count} 个临时怪与 {deletedStats} 个临时属性，当前总怪物数: {targetMonsters.Count}");

            Sync(targetRoot);
            return 0;
        }
        finally
        {
            if (Directory.Exists(tempBackupDir))
            {
                try { Directory.Delete(tempBackupDir, true); } catch { }
            }
        }
    }

    private static void Sync(string root)
    {
        string source = Path.Combine(root, "System.db");
        string expected = Md5(source);
        Console.WriteLine($"System.db 源文件 MD5: {expected}");

        foreach (string target in Mirrors)
        {
            if (Path.GetFullPath(source) != Path.GetFullPath(target))
            {
                File.Copy(source, target, true);
                Console.WriteLine($"  -> 已同步覆盖 {target}");
            }
        }

        foreach (string target in Mirrors)
        {
            string actual = Md5(target);
            Console.WriteLine($"  [{(actual == expected ? "OK" : "FAIL")}] {target} {actual}");
        }
    }

    private static string Md5(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
