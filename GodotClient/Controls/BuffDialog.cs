using ZirconClient.Scripts;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Library;
using Library.SystemModels;

namespace ZirconClient.Controls;

/// <summary>
/// Buff 图标栏 (移植自 Client/Scenes/Views/BuffDialog.cs)。
/// 数据来自 GameScene buff 字典; 永久 ItemBuff 合并 + 倒计时上色 (CBIcons.Zl 图标)。
/// </summary>
public partial class BuffDialog : DXWindow
{
    private static readonly Color IndianRed = new(0.80f, 0.36f, 0.36f);
    private static readonly Color CadetBlue = new(0.37f, 0.62f, 0.63f);

    private readonly List<ClientBuffInfo> _currentBuffs = new();

    /// <summary>图标数量变化导致 Size 改变时，由 GameScene 重新锚到小地图左侧。</summary>
    public event Action LayoutNeeded;

    public BuffDialog()
    {
        HasFooter = false;
        HasTitle = false;
        HasTopBorder = false;
        ShowCloseButton = false;
        Movable = false;
        Size = new Vector2I(30, 30);
        Opacity = 0.6f;
        Visible = false;
    }

    /// <summary>GameScene 调用: buff 字典变化后刷新图标</summary>
    public void BuffsChanged(Dictionary<int, ClientBuffInfo> buffs)
    {
        var previousSize = Size;
        foreach (Control child in GetChildren())
        {
            if (child is DXImageControl)
            {
                if (child is DXControl control) RemoveControl(control);
                child.QueueFree();
            }
        }
        _currentBuffs.Clear();

        var list = buffs?.Values
            .Where(x => x != null && x.Type != BuffType.Ranking && x.Type != BuffType.Developer)
            .ToList() ?? new List<ClientBuffInfo>();

        // 永久 ItemBuff 合并到合成项 (原版 FirstOrDefault 兜底)
        var permanentStats = new List<Stats>();
        foreach (var buff in list.Where(x => x.Type == BuffType.ItemBuff && x.RemainingTime == TimeSpan.MaxValue).ToList())
        {
            var itemInfo = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == buff.ItemIndex);
            if (itemInfo?.Stats != null)
                permanentStats.Add(itemInfo.Stats);
            list.Remove(buff);
        }

        if (permanentStats.Count > 0)
        {
            var combined = new Stats();
            foreach (var s in permanentStats)
                combined.Add(s);
            list.Insert(0, new ClientBuffInfo
            {
                Index = 0,
                Type = BuffType.ItemBuffPermanent,
                Stats = combined,
                RemainingTime = TimeSpan.MaxValue,
            });
        }

        list.Sort((a, b) => b.RemainingTime.CompareTo(a.RemainingTime));

        int cols = Math.Min(6, Math.Max(1, list.Count));
        int rows = Math.Max(1, 1 + (list.Count - 1) / 6);
        Size = new Vector2I(3 + cols * 27, 3 + rows * 27);

        for (int i = 0; i < list.Count; i++)
        {
            var buff = list[i];
            var icon = new DXImageControl
            {
                LibraryFile = LibraryFile.CBIcon,
                Index = GetBuffIcon(buff),
                Location = new Vector2I(3 + (i % 6) * 27, 3 + (i / 6) * 27),
            };
            icon.BeforeDraw += (o, e) => ColorBuffIcon(icon, buff);
            icon.TooltipText = GetBuffHint(buff);
            AddControl(icon);
            _currentBuffs.Add(buff);
        }

        Visible = list.Count > 0;
        // Size 可能仍是 30x30（单个 buff），也必须重锚，否则会停在构造默认 (0,0)。
        if (Visible || Size != previousSize)
            LayoutNeeded?.Invoke();
    }

    private static void ColorBuffIcon(DXImageControl icon, ClientBuffInfo buff)
    {
        if (buff.Pause)
        {
            icon.SelfModulate = IndianRed;
            return;
        }

        if (buff.RemainingTime == TimeSpan.MaxValue || buff.RemainingTime.TotalSeconds >= 10)
        {
            icon.SelfModulate = Colors.White;
            return;
        }

        float t = (float)(buff.RemainingTime.TotalSeconds / 10.0);
        icon.SelfModulate = Colors.White.Lerp(CadetBlue, 1f - t);
    }

    private static string GetBuffHint(ClientBuffInfo buff)
    {
        if (buff == null) return string.Empty;
        string name = buff.Type switch
        {
            BuffType.ItemBuff => Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == buff.ItemIndex)?.Local() ?? Lang.BuffItemLabel,
            BuffType.ItemBuffPermanent => Lang.BuffItemLabel2,
            BuffType.HuntGold => Lang.BuffUi121Label,
            BuffType.Observable => Lang.BuffAllowLabel,
            BuffType.Castle => Lang.BuffUi123Label,
            BuffType.Guild => Lang.CommonControlConfigWindowColoursTabGuildChatLabel,
            BuffType.Companion => Lang.CompanionDialogTitle,
            BuffType.MapEffect => Lang.BuffEffectsLabel,
            BuffType.InstanceEffect => Lang.BuffEffectsLabel2,
            BuffType.Fame => Lang.BuffUi126Label,
            _ => buff.Type.ToString(),
        };
        if (buff.Pause) name += Lang.BuffPauseLabel;
        if (buff.RemainingTime != TimeSpan.MaxValue)
            name += string.Format(Lang.BuffUi128Label, Math.Max(0, buff.RemainingTime.TotalSeconds));
        return name;
    }

    /// <summary>BuffType -> CBIcons 图标帧索引 (照原版 switch)</summary>
    public static int GetBuffIcon(ClientBuffInfo buff)
    {
        switch (buff.Type)
        {
            case BuffType.Castle: return 242;
            case BuffType.Observable: return 172;
            case BuffType.Veteran: return 171;
            case BuffType.Brown: return 229;
            case BuffType.PKPoint: return 266;
            case BuffType.ItemBuff:
                {
                    var info = Globals.ItemInfoList?.Binding.FirstOrDefault(x => x.Index == buff.ItemIndex);
                    return info?.BuffIcon ?? 73;
                }
            case BuffType.PvPCurse: return 241;
            case BuffType.ItemBuffPermanent: return 81;
            case BuffType.HuntGold: return 264;
            case BuffType.Companion: return 137;
            case BuffType.MapEffect: return 76;
            case BuffType.InstanceEffect: return 76;
            case BuffType.Guild: return 140;
            case BuffType.Fame: return 80;
            case BuffType.RedGem: return 210;
            case BuffType.BlueGem: return 211;
            case BuffType.CursedGem: return 212;
            case BuffType.Heal: return 78;
            case BuffType.Invisibility: return 74;
            case BuffType.MagicResistance: return 92;
            case BuffType.Resilience: return 91;
            case BuffType.PoisonousCloud: return 98;
            case BuffType.FullBloom: return 162;
            case BuffType.WhiteLotus: return 163;
            case BuffType.RedLotus: return 164;
            case BuffType.MagicShield: return 100;
            case BuffType.FrostBite: return 221;
            case BuffType.ElementalSuperiority: return 93;
            case BuffType.BloodLust: return 90;
            case BuffType.Cloak: return 160;
            case BuffType.GhostWalk: return 160;
            case BuffType.TheNewBeginning: return 166;
            case BuffType.Redemption: return 258;
            case BuffType.Renounce: return 94;
            case BuffType.Defiance: return 97;
            case BuffType.Might: return 96;
            case BuffType.ReflectDamage: return 98;
            case BuffType.Endurance: return 95;
            case BuffType.JudgementOfHeaven: return 99;
            case BuffType.StrengthOfFaith: return 141;
            case BuffType.CelestialLight: return 142;
            case BuffType.SoulResonance: return 149;
            case BuffType.Transparency: return 160;
            case BuffType.LifeSteal: return 98;
            case BuffType.DefensiveBlow: return 157;
            case BuffType.DarkConversion: return 166;
            case BuffType.DragonRepulse: return 165;
            case BuffType.Evasion: return 167;
            case BuffType.RagingWind: return 168;
            case BuffType.MagicWeakness: return 182;
            case BuffType.Concentration: return 200;
            case BuffType.Spiritualism: return 202;
            case BuffType.LastStand: return 204;
            case BuffType.Invincibility: return 203;
            case BuffType.ElementalHurricane: return 98;
            case BuffType.SuperiorMagicShield: return 161;
            default: return 73;
        }
    }
}
