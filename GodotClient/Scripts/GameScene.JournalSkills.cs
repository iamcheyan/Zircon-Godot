using System;
using System.Collections.Generic;
using System.Linq;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private IEnumerable<(MagicInfo Info, ClientUserMagic Magic)> JournalSkills(int cat)
    {
        foreach (var info in Globals.MagicInfoList?.Binding ?? Enumerable.Empty<MagicInfo>())
        {
            if (info == null || info.School is MagicSchool.None or MagicSchool.Discipline)
                continue;

            UserMagics.TryGetValue(info, out var magic);
            if (magic == null && (StartInfo?.Class is not { } playerClass || !info.MatchesClass(playerClass)))
                continue;

            if (cat switch
            {
                0 => magic != null,
                1 => magic != null && info.School is not (MagicSchool.Toggle or MagicSchool.Passive),
                2 => magic != null && info.School == MagicSchool.Toggle,
                3 => magic != null && info.School == MagicSchool.Passive,
                4 => magic == null,
                _ => false,
            })
                yield return (info, magic);
        }
    }

    private void JournalSkillEntries(int cat, List<JournalEntry> rows)
    {
        foreach (var (info, magic) in JournalSkills(cat).OrderBy(entry => entry.Info.NeedLevel1)
                     .ThenBy(entry => entry.Info.Index))
        {
            string type = info.School switch
            {
                MagicSchool.Toggle => "开关",
                MagicSchool.Passive => "被动",
                _ => "主动",
            };
            string name = info.Local() ?? info.Name;
            string detail = $"{type} · 职业 {info.RequiredClass.Local()} · 需要等级 {info.NeedLevel1}";
            string badge = "未学";
            if (magic == null)
            {
                detail += "\n尚未学习，需满足条件并学习对应技能书。";
            }
            else
            {
                badge = $"{(info.School != MagicSchool.Passive && JournalFavorite(info.Index) ? "★ " : "")}Lv.{magic.Level}";
                var key = JournalSkillKey(magic, MagicBarSpellSet);
                string binding = key == SpellKey.None ? "未绑定" : MagicDialog.SpellKeyText(key);
                detail += $"\n等级 {magic.Level} · 修炼值 {magic.Experience}\n共用快捷栏组 {MagicBarSpellSet} / 4 · {binding}";
                double remaining = Math.Max(0, (magic.NextCast - Library.Time.Now).TotalSeconds);
                detail += $"\n剩余冷却 {remaining:0.0} 秒";
                if (magic.ItemRequired)
                    detail += JournalSkillHasEquipment(info, magic) ? "\n需要对应技能戒指（已装备）" : "\n需要装备对应技能戒指";
            }
            int cost = info.BaseCost + (magic?.Level ?? 0) * info.LevelCost / 3;
            detail += $"\n耗蓝 {cost} · 基础冷却 {info.Delay / 1000d:0.###} 秒";
            if (!string.IsNullOrWhiteSpace(info.Description))
                detail += $"\n{info.Description}";

            rows.Add(new JournalEntry(info.Index.ToString(), name, detail, badge,
                MirSkin.GetTexture(LibraryFile.MagicIcon, info.Icon)));
        }
    }

    private IReadOnlyList<JournalAction> JournalSkillActions(int cat, string id, bool secondary)
    {
        if (!int.TryParse(id, out int skillIndex))
            return Array.Empty<JournalAction>();

        var (info, magic) = JournalSkills(cat).FirstOrDefault(entry => entry.Info.Index == skillIndex);
        if (info == null || magic == null || info.School == MagicSchool.Passive)
            return Array.Empty<JournalAction>();

        if (secondary)
        {
            return new[] { new JournalAction(JournalFavorite(skillIndex) ? "取消收藏" : "收藏技能", () =>
                JournalSkills(cat).Any(entry => entry.Info.Index == skillIndex && entry.Magic != null &&
                                                entry.Info.School != MagicSchool.Passive)
                    ? JournalToggleFavorite(skillIndex) : "技能已不可用") };
        }

        int set = MagicBarSpellSet;
        if (_observer || !JournalSkillHasEquipment(info, magic) || set is < 1 or > 4)
            return Array.Empty<JournalAction>();

        return new[] { new JournalAction($"更改该角色共用快捷栏 · 第 {set} 组", Children: () =>
            JournalSkillBindingChoices(cat, skillIndex, set)) };
    }

    private IReadOnlyList<JournalAction> JournalSkillBindingChoices(int cat, int skillIndex, int set)
    {
        var choices = new JournalAction[25];
        choices[0] = new JournalAction("清除绑定", () => JournalBindSkill(cat, skillIndex, set, SpellKey.None));
        for (int slot = 1; slot < choices.Length; slot++)
        {
            var key = (SpellKey)slot;
            choices[slot] = new JournalAction(MagicDialog.SpellKeyText(key),
                () => JournalBindSkill(cat, skillIndex, set, key));
        }
        return choices;
    }

    private string JournalBindSkill(int cat, int skillIndex, int set, SpellKey key)
    {
        if (MagicBarSpellSet != set) return "快捷栏组已更改，请重新选择";
        var (info, magic) = JournalSkills(cat).FirstOrDefault(entry => entry.Info.Index == skillIndex);
        if (_observer || magic == null || info.School == MagicSchool.Passive || !JournalSkillHasEquipment(info, magic))
            return "技能已不可绑定";
        if (_net?.Connection?.Connected != true) return "尚未连接服务器";

        if (key == JournalSkillKey(magic, set)) key = SpellKey.None;
        if (key != SpellKey.None)
        {
            foreach (var other in UserMagics.Values)
            {
                if (other != null && other != magic && JournalSkillKey(other, set) == key)
                    JournalSetSkillKey(other, set, SpellKey.None);
            }
        }

        JournalSetSkillKey(magic, set, key);
        SendMagicKey(info.Magic, magic.Set1Key, magic.Set2Key, magic.Set3Key, magic.Set4Key);
        RefreshMagicBars();
        return key == SpellKey.None ? $"已清除第 {set} 组绑定" : $"已绑定第 {set} 组 {MagicDialog.SpellKeyText(key)}";
    }

    private bool JournalSkillHasEquipment(MagicInfo info, ClientUserMagic magic) =>
        !magic.ItemRequired || Equipment.Any(item => item?.Info?.ItemEffect == ItemEffect.MagicRing &&
                                                     item.Info.Shape == info.Index);

    private static SpellKey JournalSkillKey(ClientUserMagic magic, int set) => set switch
    {
        1 => magic.Set1Key,
        2 => magic.Set2Key,
        3 => magic.Set3Key,
        4 => magic.Set4Key,
        _ => SpellKey.None,
    };

    private static void JournalSetSkillKey(ClientUserMagic magic, int set, SpellKey key)
    {
        switch (set)
        {
            case 1: magic.Set1Key = key; break;
            case 2: magic.Set2Key = key; break;
            case 3: magic.Set3Key = key; break;
            case 4: magic.Set4Key = key; break;
        }
    }
}
