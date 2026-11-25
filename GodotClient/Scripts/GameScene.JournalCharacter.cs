using System;
using System.Collections.Generic;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    internal static PaperDoll CreateJournalPaperDoll() => new() { Name = "JournalPaperDoll" };

    // EquipmentSlot order, including the five fishing slots.
    private static readonly string[] JournalSlotNames =
    {
        "武器", "衣服", "头盔", "火把", "项链", "左手镯", "右手镯", "左戒指", "右戒指", "鞋子", "毒药",
        "护身符", "鲜花", "马甲", "徽章", "盾牌", "时装", "鱼钩", "浮标", "鱼饵", "探鱼器", "绕线轮"
    };

    private void JournalCharacterEntries(int cat, List<JournalEntry> rows)
    {
        var stats = PlayerStats;
        void Value(string id, string name, string value) => rows.Add(new(id, name, $"{name}：{value}", value));
        void Attribute(Stat stat, string name) => Value(stat.ToString(), name, stats[stat].ToString());
        void Range(Stat min, Stat max, string name) => Value(min.ToString(), name, $"{stats[min]}–{stats[max]}");

        switch (cat)
        {
            case 0:
                for (int slot = 0; slot < Equipment.Length; slot++)
                {
                    var item = Equipment[slot];
                    string name = slot < JournalSlotNames.Length ? JournalSlotNames[slot] : $"装备位 {slot + 1}";
                    if (item?.Info != null)
                        rows.Add(JournalItemEntry(JournalGearId(slot, item), $"{name} · {item.Info.Local()}", "已装备"));
                    else
                        rows.Add(new($"empty:{slot}", name, item == null ? "此装备位为空。按 A 从背包选择对应部位的装备。不可穿戴的候选会标明原因。" : "装备资料不可用。", item == null ? "空" : "未知"));
                }
                break;
            case 1 when stats != null:
                Value("level", "等级", PlayerLevel.ToString());
                Value("hp", "生命", $"{CurrentHealth}/{stats[Stat.Health]}");
                Value("mp", "魔法值", $"{CurrentMana}/{stats[Stat.Mana]}");
                Range(Stat.MinDC, Stat.MaxDC, "攻击");
                Range(Stat.MinMC, Stat.MaxMC, "魔法");
                Range(Stat.MinSC, Stat.MaxSC, "道术");
                Range(Stat.MinAC, Stat.MaxAC, "防御");
                Range(Stat.MinMR, Stat.MaxMR, "魔防");
                Attribute(Stat.Accuracy, "准确");
                Attribute(Stat.Agility, "敏捷");
                Attribute(Stat.AttackSpeed, "攻击速度");
                Attribute(Stat.CriticalChance, "暴击几率");
                Attribute(Stat.CriticalDamage, "暴击伤害");
                Attribute(Stat.Luck, "幸运");
                Value("wear", "装备负重", $"{WearWeight}/{stats[Stat.WearWeight]}");
                Value("hand", "腕力", $"{HandWeight}/{stats[Stat.HandWeight]}");
                break;
            case 2 when stats != null:
                Attribute(Stat.PhysicalResistance, "物理抗性");
                Attribute(Stat.FireResistance, "火抗性");
                Attribute(Stat.IceResistance, "冰抗性");
                Attribute(Stat.LightningResistance, "雷抗性");
                Attribute(Stat.WindResistance, "风抗性");
                Attribute(Stat.HolyResistance, "神圣抗性");
                Attribute(Stat.DarkResistance, "黑暗抗性");
                Attribute(Stat.PhantomResistance, "幻影抗性");
                break;
            case 3:
                foreach (var title in Milestones)
                {
                    if (string.IsNullOrWhiteSpace(title.Info?.Title)) continue;
                    rows.Add(new(title.Index.ToString(), title.Info.Title, title.Info.Description ?? title.Info.Task ?? title.Info.Title,
                        title.Active ? "已佩戴" : title.IsComplete ? "已获得" : "未达成"));
                }
                break;
            case 4:
                rows.Add(new("mount", "坐骑状态", IsMounted ? $"骑乘中 · {_playerHorse}" : "当前未骑乘。", IsMounted ? "已骑乘" : "未骑乘"));
                break;
        }
    }

    private IReadOnlyList<JournalAction> JournalCharacterActions(int cat, string id, bool secondary)
    {
        if (cat == 0)
        {
            int colon = id?.IndexOf(':') ?? -1;
            if (colon > 0 && int.TryParse(id.AsSpan(0, colon), out int slot) && (uint)slot < Equipment.Length &&
                Equipment[slot] is { Info: not null } item && int.TryParse(id.AsSpan(colon + 1), out int index) && item.Index == index)
                return secondary ? JournalItemActions(true, slot, item, false) : JournalEquipmentCandidates(slot);
            if (id != null && id.StartsWith("empty:", StringComparison.Ordinal)
                && int.TryParse(id.AsSpan(6), out int emptySlot) && (uint)emptySlot < Equipment.Length
                && Equipment[emptySlot] == null && !secondary)
                return JournalEquipmentCandidates(emptySlot);
        }
        if (IsObserver || secondary) return Array.Empty<JournalAction>();
        if (cat == 3 && int.TryParse(id, out int key) && _milestones.TryGetValue(key, out var title) && title.IsComplete)
        {
            bool wear = !title.Active;
            return new JournalAction[] { new(wear ? "佩戴称号" : "卸下称号", () =>
            {
                if (IsObserver || _net?.Connection?.Connected != true || !_milestones.TryGetValue(key, out var current) || !current.IsComplete || current.Active == wear)
                    return "称号状态已改变，未发送请求。";
                SendMilestoneActive(key, wear);
                return "已请求更换称号。";
            }) };
        }
        if (cat == 4 && id == "mount")
        {
            bool mounted = IsMounted;
            return new JournalAction[] { new(mounted ? "下马" : "上马", () =>
            {
                if (IsObserver || _player?.Dead != false || _net?.Connection?.Connected != true || IsMounted != mounted)
                    return "当前无法切换骑乘状态，未发送请求。";
                HandleKeyBind(KeyBindAction.MountToggle);
                return mounted ? "已请求下马。" : "已请求上马。";
            }) };
        }
        return Array.Empty<JournalAction>();
    }
}
