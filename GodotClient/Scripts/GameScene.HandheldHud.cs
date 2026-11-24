using System;
using System.Collections.Generic;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

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

    internal (string Name, float? Health, bool Locked) HandheldHudTarget()
    {
        var target = _handheldFocusObject ?? _combatController?.TargetObject;
        if (target == null || !IsInstanceValid(target) || target.Dead) return ("", null, false);
        float? hp = null;
        if (target.Type == ObjectRenderer.Kind.Monster && target.MaxHealth > 0)
            hp = HandheldHud.ResourceFraction(target.Health, target.MaxHealth);
        else if (target.Type == ObjectRenderer.Kind.Player && _otherPlayers.TryGetValue(target.ObjectID, out var player) && player.MaxHealth > 0)
            hp = HandheldHud.ResourceFraction(player.Health, player.MaxHealth);
        return (target.DisplayName ?? target.Name, hp, _handheldTargetLocked);
    }
}
