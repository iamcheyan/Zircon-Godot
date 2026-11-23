using Library;
using Library.Network.ServerPackets;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using System;
using System.Linq;
using S = Library.Network.ServerPackets;

namespace Server.Envir;

/// <summary>
/// 单机开发模式（--singleplayer-dev）：客户端单机模式拉起服务端时，给测试账号
/// TestHero 注入满级/全技能/全装备，方便本地 UI 测试，无需手动练级。
///
/// 由 ServerCore Program.Main 解析命令行参数后启用（Config.SinglePlayerDev），
/// PlayerObject 构造函数末尾调用 ApplySinglePlayerDev() 完成注入。
/// 只影响启用该标志的启动，正常联机启动不注入任何数据。
/// </summary>
public static class DevSinglePlayer
{
    /// <summary>单机模式注入的目标账号邮箱（与客户端 local 测试账号一致）。</summary>
    public const string TargetEmail = "test@test.com";

    /// <summary>注入后角色等级（原版 Config.MaxLevel 默认 10，单机模式放开到 255）。</summary>
    public const int DevLevel = 255;

    /// <summary>
    /// 在 PlayerObject 构造完成（SetupMagic 之后）调用。逐项补齐等级、
    /// 技能与物品；等级已满不代表初始化完成，失败后再次登录可安全重试。
    /// </summary>
    public static void Apply(PlayerObject player)
    {
        if (!Config.SinglePlayerDev) return;
        if (player?.Character?.Account == null) return;
        if (!string.Equals(player.Character.Account.EMailAddress, TargetEmail,
                StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            ApplyCore(player);
        }
        catch (Exception ex)
        {
            SEnvir.Log($"[SingleDev] 注入异常: {ex}");
        }
    }

    private static void ApplyCore(PlayerObject player)
    {
        SEnvir.Log($"[SingleDev] 检查测试数据: {player.Name}, 等级 {player.Level}, " +
            $"技能 {player.Character.Magics.Count}, 物品 {player.Character.Items.Count}");

        // 等级与剩余数据分别补齐；旧版本可能已保存 255 级但在创建技能时中断。
        bool levelChanged = player.Level < DevLevel;
        if (levelChanged)
        {
            player.Level = DevLevel;
            player.Experience = 0;
        }

        // 该开发测试账号保留原有“跨职业全技能”口径，便于 UI 测试；
        // 关闭的修炼系统除外。不改已有快捷键、不重复创建持久化/运行时技能。
        int learned = EnsureMagics(player);
        // The constructor has loaded the inventory but starts with empty Stats.
        // RefreshStats also computes the carried weight, including on retries.
        player.RefreshStats();
        if (levelChanged || learned > 0)
        {
            player.SetHP(player.Stats[Stat.Health]);
            player.SetMP(player.Stats[Stat.Mana]);
            player.SetFP(player.CurrentFP);
        }

        // 每个目标定义只补一件：先看背包、已穿戴和账号仓库，避免重新登录
        // 或初始化中途重试时重复刷物品。保留原有测试物品范围与绑定标记。
        var owned = player.Character.Items.Concat(player.Character.Account.Items)
            .Where(item => item?.Info != null && item.Count > 0)
            .Select(item => item.Info.Index).ToHashSet();
        int given = 0, deferred = 0;
        foreach (ItemInfo info in SEnvir.ItemInfoList.Binding)
        {
            if (info.ItemType == ItemType.Nothing) continue;
            // 排除不可穿戴/非装备类：系统、货币、捆包、箱子、任务类、部件
            if (info.ItemType is ItemType.System or ItemType.Currency or ItemType.Bundle
                or ItemType.LootBox or ItemType.ItemPart or ItemType.Emblem) continue;

            if (owned.Contains(info.Index)) continue;
            var check = new ItemCheck(info, 1, UserItemFlags.Bound, TimeSpan.Zero);
            if (!player.CanGainItems(true, check))
            {
                deferred++;
                continue;
            }
            UserItem item = SEnvir.CreateFreshItem(check);
            if (item == null) continue;

            player.GainItem(item);
            // GainItem can reject/consume an item. Only report a granted item
            // when the character really owns it; do not log a false completion.
            if (player.Character.Items.Any(x => x?.Info == info && x.Count > 0))
            {
                owned.Add(info.Index);
                given++;
            }
        }

        // ---- 4. 货币：金币给足，方便测试商店 ----
        var gold = player.Character.Account.Currencies?.FirstOrDefault(x => x.Info.Type == CurrencyType.Gold);
        if (gold != null && gold.Amount < 100_000_000) gold.Amount = 100_000_000;

        player.RefreshWeight();
        SEnvir.Log($"[SingleDev] 补齐完成: {player.Name}, 等级 {player.Level}, " +
            $"新增物品 {given} 件, 负重暂缓 {deferred} 件, 新增技能 {learned} 个, " +
            $"物品合计 {player.Character.Items.Count}, 技能合计 {player.Character.Magics.Count}");
        EnsureVisibleExperience(player);
    }

    /// <summary>
    /// Uses the same MirDB factory/association path as @giveSkills. A naked new
    /// UserMagic has no Collection.Session and throws in the first setter.
    /// Kept separate so persistence and retry behavior can be checked in isolation.
    /// </summary>
    internal static int EnsureMagics(PlayerObject player)
    {
        int learned = 0;
        foreach (MagicInfo magic in SEnvir.MagicInfoList.Binding)
        {
            if (magic.School == MagicSchool.None ||
                (!Config.EnableDiscipline && magic.School == MagicSchool.Discipline)) continue;

            UserMagic userMagic = player.Character.Magics.FirstOrDefault(x => x?.Info == magic);
            if (userMagic == null)
            {
                userMagic = SEnvir.UserMagicList.CreateNewObject();
                userMagic.Character = player.Character; // association adds to Character.Magics
                userMagic.Info = magic;
                learned++;
            }
            if (userMagic.Level < Globals.MagicMaxLevel)
                userMagic.Level = Globals.MagicMaxLevel;
            // Do not reset existing experience or player-defined shortcut keys.
            if (!player.MagicObjects.ContainsKey(magic.Magic))
                player.SetupMagic(userMagic);
        }
        return learned;
    }

    /// <summary>
    /// 开发模式让经验条有可见比例。满级角色本级经验往往接近 0，
    /// 旧版主 HUD 的经验条（GameInter F63）按比例填充，肉眼就是一条空槽，
    /// UI 验收时无法判断控件是否真的接好。这里在比例过低时补到本级上限的一半，
    /// 并广播一次 S.LevelChanged 让客户端立刻重画。只影响 --singleplayer-dev。
    /// </summary>
    private static void EnsureVisibleExperience(PlayerObject player)
    {
        if (player.MaxExperience <= 0) return;
        if (player.Experience > player.MaxExperience / 100) return; // 已有可见比例则不动

        player.Experience = player.MaxExperience / 2;
        player.Enqueue(new S.LevelChanged
        {
            Level = player.Level,
            Experience = player.Experience,
            MaxExperience = player.MaxExperience,
        });
        SEnvir.Log($"[SingleDev] 经验条可见化: {player.Experience}/{player.MaxExperience}");
    }
}
