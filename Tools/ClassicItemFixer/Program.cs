using System.Security.Cryptography;
using Library;
using Library.MirDB;
using Library.SystemModels;
using MirDB;
using Server.DBModels;

namespace ClassicItemFixer;

class Program
{
    static void Main(string[] args)
    {
        string root = Path.GetFullPath("Debug/ServerCore/Database/") + Path.DirectorySeparatorChar;
        Console.WriteLine($"=== ClassicItemFixer 启动 ===");
        Console.WriteLine($"数据库根目录: {root}");

        // 1. 处理 System.db
        CleanSystemDb(root);

        // 2. 处理 Users.db (清理 TestHero 背包中的刺客物品及溢出物品)
        CleanUsersDb(root);

        // 3. 同步 System.db 到 4 处镜像
        SyncSystemDb(root);

        Console.WriteLine("=== 所有修复与同步完成 ===");
    }

    static void CleanSystemDb(string root)
    {
        Console.WriteLine("\n[1/3] 正在加载并清理 System.db...");
        var session = new Session(SessionMode.System, root);
        session.Initialize(typeof(ItemInfo).Assembly, typeof(AccountInfo).Assembly);

        var itemCol = session.GetCollection<ItemInfo>();

        Console.WriteLine($"当前 ItemInfo 总数: {itemCol.Count}");

        var assassinItems = itemCol.Binding.Where(x => x.RequiredClass == RequiredClass.Assassin).ToList();
        Console.WriteLine($"发现刺客专属物品: {assassinItems.Count} 件:");

        foreach (var item in assassinItems)
        {
            Console.WriteLine($"  - [#{item.Index}] {item.ItemName} ({item.ItemType})");
            item.Delete();
        }

        session.Save(true);
        Console.WriteLine($"清理后 ItemInfo 总数: {itemCol.Count}");
    }

    static void CleanUsersDb(string root)
    {
        Console.WriteLine("\n[2/3] 正在加载并清理 Users.db...");
        var session = new Session(SessionMode.Users, root);
        session.Initialize(typeof(ItemInfo).Assembly, typeof(AccountInfo).Assembly);

        var characters = session.GetCollection<CharacterInfo>();
        var testHero = characters.Binding.FirstOrDefault(c => c.CharacterName == "TestHero");

        if (testHero != null)
        {
            Console.WriteLine($"找到角色 TestHero: 等级 {testHero.Level}, 职业 {testHero.Class}, 物品总数: {testHero.Items.Count}");
            var itemsToRemove = new List<UserItem>();

            foreach (var item in testHero.Items.ToList())
            {
                Console.WriteLine($"  - Slot={item.Slot}, Index={item.Index}, Info={(item.Info != null ? item.Info.ItemName : "NULL")}");
                if (item.Info == null || item.Info.RequiredClass == RequiredClass.Assassin)
                {
                    itemsToRemove.Add(item);
                }
            }

            Console.WriteLine($"从 TestHero 身上/背包清除无效或刺客物品: {itemsToRemove.Count} 件");
            foreach (var ui in itemsToRemove)
            {
                Console.WriteLine($"  - 移除 Slot={ui.Slot}: {ui.Info?.ItemName ?? "Unknown"}");
                ui.Delete();
            }

            session.Save(true);
            Console.WriteLine("Users.db 已更新保存。");
        }
        else
        {
            Console.WriteLine("未找到角色 TestHero，跳过角色背包清理。");
        }
    }

    static void SyncSystemDb(string root)
    {
        Console.WriteLine("\n[3/3] 正在同步 System.db 到 4 处镜像...");
        string sourceDb = Path.Combine(root, "System.db");

        string[] targets = new[]
        {
            "/home/tetsuya/mir2ei/Data/System.db",
            "/home/tetsuya/mir2ei/Database/System.db",
            "/home/tetsuya/development/zircon/System.db"
        };

        foreach (var target in targets)
        {
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(sourceDb, target, true);
            Console.WriteLine($"  -> 已覆盖: {target}");
        }

        // 校验 MD5
        using var md5 = MD5.Create();
        string srcHash = GetMd5(sourceDb);
        Console.WriteLine($"源数据库 MD5: {srcHash}");

        foreach (var target in targets)
        {
            string h = GetMd5(target);
            if (h == srcHash)
                Console.WriteLine($"  [OK] {target} MD5 一致");
            else
                Console.WriteLine($"  [FAIL] {target} MD5 不匹配: {h}");
        }
    }

    static string GetMd5(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }
}
