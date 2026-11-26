using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

internal sealed record HandheldTargetInfo(string Name, float? Health, bool Locked, Texture2D Icon, string Detail, string Stats)
{
    public static readonly HandheldTargetInfo Empty = new("", null, false, null, "", "");
}

// Read-only presentation adapter. No item/magic packets and no input routing.
public partial class GameScene
{
    internal MiniMapDialog HandheldMiniMapSource => _miniMap;
    internal MirDirection HandheldPlayerDirection => _playerDirection;
    internal void FillHandheldHudBuffs(List<int> icons)
    {
        icons.Clear();
        foreach (var buff in _buffs.Values)
        {
            if (buff == null || buff.Type is BuffType.Ranking or BuffType.Developer) continue;
            icons.Add(BuffDialog.GetBuffIcon(buff));
        }
    }

    internal HandheldTargetInfo HandheldHudTarget() => DescribeHandheldTarget(
        _handheldFocusObject ?? _combatController?.TargetObject, _handheldTargetLocked);

    internal HandheldTargetInfo DescribeHandheldTarget(ObjectRenderer target, bool locked)
    {
        if (target == null || !IsInstanceValid(target) || target.Dead) return HandheldTargetInfo.Empty;

        string name = target.DisplayName ?? target.Name;
        float? hp = null;
        Texture2D icon = null;
        string detail = "";
        string stats = "";

        switch (target.Type)
        {
            case ObjectRenderer.Kind.Monster:
                if (target.MaxHealth > 0)
                    hp = HandheldHud.ResourceFraction(target.Health, target.MaxHealth);
                string health = hp.HasValue ? $"HP {target.Health:N0}/{target.MaxHealth:N0}" : "HP 未知";
                int level = target.MonsterInfo?.Level ?? target.Level;
                detail = target.MonsterInfo != null || level > 0 ? $"Lv. {level} · {health}" : health;
                icon = GetHandheldMonsterIcon(target);
                stats = GetHandheldMonsterStats(target.MonsterInfo?.Stats);
                break;
            case ObjectRenderer.Kind.Item when target.Item?.Info != null:
                int frame = DXItemCell.GetItemDrawIndex(target.Item);
                icon = MirSkin.GetTexture(LibraryFile.Inventory, frame) ?? MirSkin.GetTexture(LibraryFile.StoreItem, frame);
                detail = $"数量 {target.Item.Count:N0}";
                stats = $"类型 {target.Item.Info.ItemType}";
                break;
            case ObjectRenderer.Kind.NPC:
                if (GetNpcInfo(target.ObjectID) is { } npc)
                    icon = MirSkin.GetTexture(LibraryFile.NPCImage, npc.FaceImage);
                break;
            case ObjectRenderer.Kind.Player:
                if (_otherPlayers.TryGetValue(target.ObjectID, out var player) && player.MaxHealth > 0)
                    hp = HandheldHud.ResourceFraction(player.Health, player.MaxHealth);
                break;
        }

        return new HandheldTargetInfo(name, hp, locked, icon, detail, stats);
    }

    private static Texture2D GetHandheldMonsterIcon(ObjectRenderer target)
    {
        if (target.MonsterInfo?.FaceImage is > 0 and var face && MirSkin.GetTexture(LibraryFile.MonImage, face) is { } portrait)
            return portrait;
        if (target.BodyLibrary == null || !target.FrameTable.TryGetValue(target.IdleAnimation, out var idle) || idle is not { FrameCount: > 0 })
            return null;

        int frame = target.BodyShape * target.BodyOffSet + idle.StartIndex + idle.OffSet * (int)target.Direction;
        return target.BodyLibrary.GetImageTexture(frame);
    }

    private static string GetHandheldMonsterStats(Stats stats)
    {
        if (stats?.Values == null) return "";
        string ac = BaseAttribute("AC", Stat.MinAC, Stat.MaxAC);
        string dc = BaseAttribute("DC", Stat.MinDC, Stat.MaxDC);
        if (ac.Length == 0 && dc.Length == 0) return "";
        return "基础 " + (ac.Length == 0 ? dc : dc.Length == 0 ? ac : $"{ac} · {dc}");

        string BaseAttribute(string label, Stat min, Stat max)
        {
            if (stats.Values.TryGetValue(max, out int upper)) return $"{label} {stats[min]}–{upper}";
            return stats.Values.TryGetValue(min, out int lower) ? $"{label} {lower}" : "";
        }
    }
}
