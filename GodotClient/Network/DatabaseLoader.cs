using System;
using System.IO;
using System.Reflection;
using Godot;
using Library;
using Library.SystemModels;
using MirDB;

namespace ZirconClient.Network;

// 客户端启动时加载 System.db 的系统表到 Globals.*List。
// 原版客户端在 Client/Envir/CEnvir.cs LoadDatabase() 里做同样的事。
// 服务端发的包(ItemsGained/MarketPlaceConsign/...)反序列化时会调
// ClientUserItem.Complete() 访问 Globals.ItemInfoList —— 不加载就会 NRE 断线。
public static class DatabaseLoader
{
    public static Session Session { get; private set; }

    public static bool Load()
    {
        try
        {
            string projectDir = ProjectSettings.GlobalizePath("res://");
            string root = Path.GetFullPath(Path.Combine(projectDir, "..", "Debug", "Client", "Data"))
                + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(root, "System.db")) && Directory.Exists("/home/tetsuya/mir3ei/Data"))
            {
                root = "/home/tetsuya/mir3ei/Data" + Path.DirectorySeparatorChar;
            }

            GD.Print($"[DB] 加载 System.db 从: {root}");

            Session = new Session(SessionMode.Users, root) { BackUp = false };
            Session.Initialize(Assembly.GetAssembly(typeof(ItemInfo)));

            if (!Session.SystemDatabaseExists)
            {
                GD.PrintErr($"[DB] System.db 不存在于 {root}");
                return false;
            }

            Globals.ItemInfoList = Session.GetCollection<ItemInfo>();
            Globals.MagicInfoList = Session.GetCollection<MagicInfo>();
            Globals.MapInfoList = Session.GetCollection<MapInfo>();
            Globals.CurrencyInfoList = Session.GetCollection<CurrencyInfo>();
            Globals.InstanceInfoList = Session.GetCollection<InstanceInfo>();
            Globals.NPCPageList = Session.GetCollection<NPCPage>();
            Globals.MonsterInfoList = Session.GetCollection<MonsterInfo>();
            Globals.FishingInfoList = Session.GetCollection<FishingInfo>();
            Globals.StoreInfoList = Session.GetCollection<StoreInfo>();
            Globals.NPCInfoList = Session.GetCollection<NPCInfo>();
            Globals.MovementInfoList = Session.GetCollection<MovementInfo>();
            Globals.QuestInfoList = Session.GetCollection<QuestInfo>();
            Globals.QuestTaskList = Session.GetCollection<QuestTask>();
            Globals.CompanionInfoList = Session.GetCollection<CompanionInfo>();
            Globals.CompanionLevelInfoList = Session.GetCollection<CompanionLevelInfo>();
            Globals.DisciplineInfoList = Session.GetCollection<DisciplineInfo>();
            Globals.FameInfoList = Session.GetCollection<FameInfo>();
            Globals.BundleInfoList = Session.GetCollection<BundleInfo>();
            Globals.LootBoxInfoList = Session.GetCollection<LootBoxInfo>();
            Globals.HelpInfoList = Session.GetCollection<HelpInfo>();
            Globals.MilestoneInfoList = Session.GetCollection<MilestoneInfo>();
            Globals.MilestoneTaskInfoList = Session.GetCollection<MilestoneInfoTask>();

            GD.Print($"[DB] 加载完成: 物品 {Globals.ItemInfoList.Count}, 地图 {Globals.MapInfoList.Count}, 怪物 {Globals.MonsterInfoList.Count}, 魔法 {Globals.MagicInfoList.Count}, 货币 {Globals.CurrencyInfoList.Count}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DB] 加载失败: {ex}");
            return false;
        }
    }
}
