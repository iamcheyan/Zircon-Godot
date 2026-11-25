using System;
using System.Collections.Generic;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private IReadOnlyList<JournalAction> JournalEquipmentCandidates(int destination)
    {
        var choices = new List<JournalAction>();
        var previous = Equipment[destination];
        for (int slot = 0; slot < Inventory.Length; slot++)
        {
            var item = Inventory[slot];
            if (item?.Info == null || !Functions.CorrectSlot(item.Info.ItemType, (EquipmentSlot)destination)) continue;
            int sourceSlot = slot;
            string reason = JournalEquipmentUnavailable(item, sourceSlot, destination);
            choices.Add(new JournalAction(item.Info.Local(), () =>
            {
                if ((uint)sourceSlot >= Inventory.Length || (uint)destination >= Equipment.Length
                    || !JournalSameItem(Inventory[sourceSlot], item) || !JournalSameItem(Equipment[destination], previous))
                    return "物品或装备槽已变化，请重新选择。";
                string currentReason = JournalEquipmentUnavailable(item, sourceSlot, destination);
                if (currentReason != null) return currentReason;
                var source = JournalSourceCell(false, sourceSlot, item);
                var target = EquipmentCells[destination];
                if (!JournalSameItem(target.Item, previous)) return "装备槽已变化，请重新选择。";
                return target.ToEquipment(source) ? "已请求更换装备，等待服务器确认。" : "当前无法穿戴，请重新核对条件。";
            }, DisabledReason: reason));
        }
        if (choices.Count == 0) choices.Add(new JournalAction("背包里没有适合此部位的装备", Information: true));
        return choices;
    }

    // Read-only explanation; CanWearItem may emit chat and must not run while
    // rendering a list. Actual submission still goes through ToEquipment.
    private string JournalEquipmentUnavailable(ClientUserItem item, int sourceSlot, int destination)
    {
        if (StartInfo == null) return "角色尚未就绪";
        if (_net?.Connection?.Connected != true) return "尚未连接服务器";
        if (IsObserver) return "观察模式不能换装";
        if (_player?.Dead != false) return "死亡时不能换装";
        if (IsFishingActive) return "钓鱼中不能换装";
        if (IsTamingActive) return "驯服中不能换装";
        var info = item.Info;
        RequiredGender gender = StartInfo.Gender == MirGender.Male ? RequiredGender.Male : RequiredGender.Female;
        if (!info.RequiredGender.HasFlag(gender)) return "性别不符";
        RequiredClass cls = StartInfo.Class switch {
            MirClass.Warrior => RequiredClass.Warrior, MirClass.Wizard => RequiredClass.Wizard,
            MirClass.Taoist => RequiredClass.Taoist, MirClass.Assassin => RequiredClass.Assassin, _ => RequiredClass.None
        };
        if (!info.RequiredClass.HasFlag(cls)) return "职业不符";
        int amount = info.RequiredAmount;
        int rebirth = PlayerStats[Stat.Rebirth];
        if (info.RequiredType == RequiredType.Level && PlayerLevel < amount && rebirth == 0)
            return $"等级不足：需 {amount} 级，当前 {PlayerLevel} 级";
        if (info.RequiredType == RequiredType.MaxLevel && (PlayerLevel > amount || rebirth > 0))
            return $"超过等级上限：最高 {amount} 级";
        if (info.RequiredType == RequiredType.CompanionLevel && (Companion == null || Companion.Level < amount))
            return $"伙伴等级不足：需 {amount} 级";
        if (info.RequiredType == RequiredType.MaxCompanionLevel && (Companion == null || Companion.Level > amount))
            return $"伙伴等级限制：最高 {amount} 级";
        var requirement = info.RequiredType switch {
            RequiredType.AC => (Stat.MaxAC, "防御"), RequiredType.MR => (Stat.MaxMR, "魔防"),
            RequiredType.DC => (Stat.MaxDC, "攻击"), RequiredType.MC => (Stat.MaxMC, "魔法"), RequiredType.SC => (Stat.MaxSC, "道术"),
            RequiredType.Health => (Stat.Health, "生命上限"), RequiredType.Mana => (Stat.Mana, "法力上限"),
            RequiredType.Accuracy => (Stat.Accuracy, "准确"), RequiredType.Agility => (Stat.Agility, "敏捷"),
            RequiredType.RebirthLevel or RequiredType.MaxRebirthLevel => (Stat.Rebirth, "转生等级"), _ => (Stat.None, "")
        };
        if (requirement.Item1 != Stat.None)
        {
            int current = PlayerStats[requirement.Item1];
            if (info.RequiredType == RequiredType.MaxRebirthLevel ? current > amount : current < amount)
                return $"{requirement.Item2}不符：要求 {amount}，当前 {current}";
        }
        var old = Equipment[destination];
        if (item.Flags.HasFlag(UserItemFlags.Locked)) return "物品已锁定";
        if (item.Flags.HasFlag(UserItemFlags.Marriage)) return "婚姻绑定物品不能移动";
        if (old?.Flags.HasFlag(UserItemFlags.Marriage) == true) return "当前装备为婚姻绑定，不能替换";
        var target = destination < EquipmentCells.Length ? EquipmentCells[destination] : null;
        if (!JournalReadyCell(target) || target.Slot != destination) return "装备槽正在处理中";
        if (JournalSourceCell(false, sourceSlot, item) == null) return "物品正在处理中或已不可用";
        if ((EquipmentSlot)destination is EquipmentSlot.Hook or EquipmentSlot.Float or EquipmentSlot.Bait or EquipmentSlot.Finder or EquipmentSlot.Reel
            && Equipment[(int)EquipmentSlot.Weapon]?.Info?.ItemEffect != ItemEffect.FishingRod)
            return "需要先装备鱼竿";
        int delta = item.Weight - (old?.Weight ?? 0);
        bool hand = (EquipmentSlot)destination is EquipmentSlot.Weapon or EquipmentSlot.Torch or EquipmentSlot.Shield;
        int weight = (hand ? HandWeight : WearWeight) + delta;
        int capacity = PlayerStats[hand ? Stat.HandWeight : Stat.WearWeight];
        if (weight > capacity) return $"{(hand ? "腕力" : "装备负重")}不足：{weight}/{capacity}";
        return null;
    }
}
