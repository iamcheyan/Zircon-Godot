using Library;
using Library.Network.ClientPackets;
using Library.SystemModels;
using Server.DBModels;
using Server.Envir.Commands.Exceptions;
using Server.Models;
using System;
using System.Collections.Generic;
using S = Library.Network.ServerPackets;

namespace Server.Envir.Commands.Command.Admin
{
    /// <summary>
    /// 清理背包里的杂物（<c>@dropJunk</c>），给测试号腾格子/负重。
    ///
    /// **为什么需要它**：背包没有槽位上限（<c>CanGainItems</c> 的产品口径是
    /// 「不设槽位上限，负重是唯一上限」），所以批量 <c>@make</c> 时的
    /// <c>Can not hold anymore</c> 全部来自**负重**判定。杂物（药水、矿石、
    /// 卷轴、火把、绳索…）既重又不占装备位，是负重的主要占用方。
    ///
    /// **只删杂物，装备一律保留**：白名单是「能穿戴的东西」——
    /// Weapon / Armour / Helmet / Shoes / Necklace / Bracelet / Ring / Torch /
    /// DarkStone / LightStone / Amulet / Fire。消耗品、矿石、肉、技能书、
    /// 普通道具、货币全部丢弃。清完之后背包里只剩武器 / 衣服 / 首饰。
    ///
    /// 走 <see cref="S.ItemDelete"/> 逐格通知客户端，与玩家手动点丢弃走同一条
    /// 协议，客户端不需要额外刷新逻辑。
    /// </summary>
    class DropJunk : AbstractCommand<IAdminCommand>
    {
        public override string VALUE => "DROPJUNK";

        /// <summary>
        /// 保留的 ItemType —— 武器 / 衣服 / 首饰 / 坐骑装备 / 火把。
        /// 按 EquipmentSlot（LibraryCore/Enum.cs:89）能穿戴的类别取，另外
        /// 保留 HorseArmour（马铠，坐骑槽）与 Torch（火把，独立槽）。
        /// </summary>
        private static readonly HashSet<ItemType> KeepTypes = new()
        {
            ItemType.Weapon,
            ItemType.Armour,
            ItemType.Helmet,
            ItemType.Shoes,
            ItemType.Necklace,
            ItemType.Bracelet,
            ItemType.Ring,
            ItemType.Torch,
            ItemType.DarkStone,
            ItemType.Amulet,
            ItemType.HorseArmour,
            ItemType.Costume,
            ItemType.Shield,
            ItemType.Emblem,
            ItemType.Flower,
            ItemType.Hook,
            ItemType.Float,
            ItemType.Bait,
            ItemType.Finder,
            ItemType.Reel,
        };

        public override void Action(PlayerObject player)
        {
            List<UserItem> junk = new List<UserItem>();
            List<int> junkSlots = new List<int>();

            for (int i = 0; i < player.Inventory.Length; i++)
            {
                UserItem item = player.Inventory[i];
                if (item?.Info == null) continue;
                if (KeepTypes.Contains(item.Info.ItemType)) continue;
                if (item.Flags.HasFlag(UserItemFlags.Locked)) continue;
                if (item.Flags.HasFlag(UserItemFlags.Marriage)) continue;

                junk.Add(item);
                junkSlots.Add(i);
                player.Inventory[i] = null;
            }

            foreach (UserItem item in junk)
                player.RemoveItem(item);

            for (int i = 0; i < junkSlots.Count; i++)
                player.Enqueue(new S.ItemDelete
                {
                    Grid = GridType.Inventory,
                    Slot = junkSlots[i],
                    Success = true,
                });

            player.RefreshWeight();
            player.Enqueue(new S.WeightUpdate
            {
                BagWeight = player.BagWeight,
                WearWeight = player.WearWeight,
                HandWeight = player.HandWeight,
            });

            player.Connection.ReceiveChat(
                junk.Count == 0
                    ? "No junk in inventory."
                    : string.Format("Dropped {0} junk stack(s).", junk.Count),
                MessageType.System);
        }
    }
}
