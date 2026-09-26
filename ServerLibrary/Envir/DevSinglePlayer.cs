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
    /// 在 PlayerObject 构造完成（SetupMagic 之后）调用。幂等：
    /// 已注入过的角色（Level >= DevLevel 且标记技能）直接跳过。
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
        bool already = player.Level >= DevLevel;
        if (already)
        {
            SEnvir.Log($"[SingleDev] {TargetEmail} 已注入满级数据，跳过");
            EnsureVisibleExperience(player);
            return;
        }

        SEnvir.Log($"[SingleDev] 注入满级数据到 {TargetEmail} ...");

        // ---- 1. 等级：拉满（走 Level 属性 + RefreshStats，避免重复升级广播）----
        player.Level = DevLevel;
        player.Experience = 0;
        player.RefreshStats();
        player.SetHP(player.Stats[Stat.Health]);
        player.SetMP(player.Stats[Stat.Mana]);
        player.SetFP(player.CurrentFP);

        // ---- 2. 全技能：把职业可学的魔法全部学会并拉满等级 ----
        foreach (MagicInfo magic in SEnvir.MagicInfoList.Binding)
        {
            if (magic.School == MagicSchool.None) continue;

            UserMagic userMagic = player.Character.Magics.FirstOrDefault(x => x.Info == magic);
            if (userMagic == null)
            {
                userMagic = new UserMagic { Info = magic, Character = player.Character };
                player.Character.Magics.Add(userMagic);
                player.SetupMagic(userMagic);
            }

            if (userMagic.Level < Globals.MagicMaxLevel)
                userMagic.Level = Globals.MagicMaxLevel;
            userMagic.Experience = 0;
        }

        // ---- 3. 装备：把数据库里的可穿戴物品全部塞进背包（GainItem 自动发
        // S.ItemsGained 通知客户端，无需手动发包）----
        int given = 0;
        foreach (ItemInfo info in SEnvir.ItemInfoList.Binding)
        {
            if (info.ItemType == ItemType.Nothing) continue;
            // 排除不可穿戴/非装备类：系统、货币、捆包、箱子、任务类、部件
            if (info.ItemType is ItemType.System or ItemType.Currency or ItemType.Bundle
                or ItemType.LootBox or ItemType.ItemPart or ItemType.Emblem) continue;

            UserItem item = SEnvir.CreateFreshItem(new ItemCheck(info, 1, UserItemFlags.Bound, TimeSpan.Zero));
            if (item == null) continue;

            player.GainItem(item);
            given++;
        }

        // ---- 4. 货币：金币给足，方便测试商店 ----
        var gold = player.Character.Account.Currencies?.FirstOrDefault(x => x.Info.Type == CurrencyType.Gold);
        if (gold != null) gold.Amount = 100_000_000;

        player.RefreshWeight();
        SEnvir.Log($"[SingleDev] 注入完成: 等级 {player.Level}, 装备/物品 {given} 件, 魔法 {player.Character.Magics.Count} 个");
        EnsureVisibleExperience(player);
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
