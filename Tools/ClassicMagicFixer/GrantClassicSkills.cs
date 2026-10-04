using System.Security.Cryptography;
using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;

namespace ClassicMagicFixer;

/// <summary>
/// 一次性维护工具：把 Users.db 中的 UserMagic 孤儿引用清理并按
/// ClassicSkills 白名单重发 TestHero 的全套经典技能。
/// 用法: GrantClassicSkills <DatabaseRoot>
/// </summary>
internal static class TeleportTool
{
    /// <summary>把角色移动到指定地图坐标（仅用于实机验证前的定位）。</summary>
    public static int Run(string root, string characterName, string mapFileName, int x, int y)
    {
        var system = new Session(SessionMode.System, root, root + "Backup/");
        system.Initialize(typeof(MagicInfo).Assembly, typeof(MapInfo).Assembly);
        var map = system.GetCollection<MapInfo>().Binding
            .FirstOrDefault(m => string.Equals(m.FileName, mapFileName, StringComparison.OrdinalIgnoreCase));
        if (map == null) { Console.WriteLine($"map '{mapFileName}' not found"); return 1; }
        int mapIndex = map.Index;

        var users = new Session(SessionMode.Users, root, root + "Backup/");
        users.Initialize(typeof(MagicInfo).Assembly, typeof(AccountInfo).Assembly);
        var hero = users.GetCollection<CharacterInfo>().Binding.FirstOrDefault(c => c.CharacterName == characterName);
        if (hero == null) { Console.WriteLine($"character '{characterName}' not found"); return 1; }

        // CurrentMap 是 Users 库里的 MapInfo 引用，必须用 Users 库自己的集合对象
        var usersMap = users.GetCollection<MapInfo>().Binding
            .FirstOrDefault(m => string.Equals(m.FileName, mapFileName, StringComparison.OrdinalIgnoreCase));
        if (usersMap == null) { Console.WriteLine($"map '{mapFileName}' missing in Users.db"); return 1; }

        hero.CurrentMap = usersMap;
        hero.CurrentLocation = new System.Drawing.Point(x, y);
        users.Save(true);
        Console.WriteLine($"{characterName} -> map '{map.FileName}' (idx {mapIndex}) @ ({x},{y})");
        return 0;
    }
}

internal static class AccountGrantor
{
    /// <summary>把指定账号的 Admin 置为 true（GM），用于实机验证 @ 命令。</summary>
    public static int Run(string root, string accountName)
    {
        var session = new Session(SessionMode.Users, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(AccountInfo).Assembly);

        var accounts = session.GetCollection<AccountInfo>().Binding;
        foreach (var a in accounts)
        {
            bool isTarget = a.EMailAddress.Equals(accountName, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  {a.EMailAddress,-30} Admin={a.Admin}");
            if (isTarget && !a.Admin)
            {
                a.Admin = true;
                Console.WriteLine($"    -> 已授予 GM");
            }
        }

        session.Save(true);
        return 0;
    }
}

internal static class GrantClassicSkills
{
    public static int Run(string root)
    {
        var session = new Session(SessionMode.Users, root, root + "Backup/");
        session.Initialize(typeof(MagicInfo).Assembly, typeof(AccountInfo).Assembly);

        var characters = session.GetCollection<CharacterInfo>();
        var hero = characters.Binding.FirstOrDefault(c => c.CharacterName == "TestHero");
        if (hero == null)
        {
            Console.WriteLine("TestHero not found");
            return 1;
        }

        Console.WriteLine($"TestHero: level={hero.Level} class={hero.Class} magics={hero.Magics.Count}");

        // 1. 清理悬空引用（Info 被删的 UserMagic）
        int orphan = 0;
        foreach (var um in hero.Magics.ToList())
        {
            if (um.Info != null) continue;
            orphan++;
            um.Delete();
        }
        Console.WriteLine($"orphan UserMagic removed: {orphan}");

        // 2. 需要系统库来解析 ClassicSkills —— 用反射从 ClassicMagicPurity 取表
        var skills = ClassicMagicPurity.ClassicSkillsTable();

        var systemSession = new Session(SessionMode.System, root, root + "Backup/");
        systemSession.Initialize(typeof(MagicInfo).Assembly);
        var magicInfos = systemSession.GetCollection<MagicInfo>().Binding;

        int granted = 0, kept = 0;
        foreach (var skill in skills)
        {
            var info = magicInfos.FirstOrDefault(x => x.Magic == skill.Magic);
            if (info == null) { Console.WriteLine($"  [skip] {skill.Magic} (no MagicInfo)"); continue; }
            if (info.NeedLevel1 > hero.Level) { Console.WriteLine($"  [skip] {info.Name} (NeedLevel1 {info.NeedLevel1} > {hero.Level})"); continue; }

            var existing = hero.Magics.FirstOrDefault(x => x.Info == info);
            if (existing != null) { kept++; continue; }

            var um = session.GetCollection<UserMagic>().CreateNewObject();
            um.Character = hero;
            um.Info = info;
            um.Level = (byte)(hero.Level >= info.NeedLevel3 ? 3 : hero.Level >= info.NeedLevel2 ? 2 : 1);
            granted++;
        }

        session.Save(true);
        Console.WriteLine($"granted={granted} kept={kept} total={hero.Magics.Count}");
        return 0;
    }
}
