using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private HandheldWorldMap _handheldWorldMap;
    private bool HandheldWorldMapOpen => _handheldWorldMap?.Visible == true;

    private void OpenHandheldWorldMap()
    {
        StopHandheldMovement();
        if (_handheldWorldMap == null)
        {
            var layer = new CanvasLayer { Name = "HandheldWorldMapLayer", Layer = 90 };
            AddChild(layer);
            _handheldWorldMap = new HandheldWorldMap(this);
            layer.AddChild(_handheldWorldMap);
        }
        _handheldWorldMap.Show();
        SyncJournalVisibility();
    }

    private void CloseHandheldWorldMap()
    {
        _handheldWorldMap.Hide();
        _handheldSelectDownMs = -1;
        SyncJournalVisibility();
    }

    private void RouteHandheldSelect(InputEventKey key, double now)
    {
        bool fromJournal = JournalOpen && _journal.Visible && !_journal.HasActionOverlay
            && !HandheldHasModalWindow() && !HandheldTextEditing;
        if (key.Pressed)
        {
            if (!key.Echo && _handheldSelectDownMs < 0 &&
                (HandheldWorldMapOpen || _bigMap?.Visible == true || HandheldActionOwnsWorld || fromJournal))
            {
                _handheldSelectDownMs = now;
                _handheldSelectLong = false;
            }
            return;
        }

        if (_handheldSelectDownMs < 0) return;
        bool tap = !_handheldSelectLong && now - _handheldSelectDownMs < HandheldLongPressMs;
        _handheldSelectDownMs = -1;
        if (!tap) return;

        if (HandheldWorldMapOpen) CloseHandheldWorldMap();
        else if (_bigMap?.Visible == true) _bigMap.Visible = false;
        else if (HandheldActionOwnsWorld || fromJournal) OpenHandheldWorldMap();
        UpdateHandheldPointerMode();
    }

    internal (string Map, int X, int Y, string Character, string State, bool Warning) HandheldMapStatus()
    {
        var map = GetMapInfo(_playerMapIndex);
        var character = StartInfo == null ? string.Empty : $"{StartInfo.Name}  Lv.{PlayerLevel} {StartInfo.Class.Local()}";
        string state;
        if (_player?.Dead == true) state = "已死亡";
        else if (_playerPoison != PoisonType.None) state = PoisonStatus();
        else if (IsMounted) state = "骑乘中";
        else state = "状态正常";
        return (map?.Local() ?? "当前地图", _playerLocation.X, _playerLocation.Y, character,
            state, _player?.Dead == true || _playerPoison != PoisonType.None);
    }

    private string PoisonStatus()
    {
        var names = new System.Collections.Generic.List<string>();
        if (_playerPoison.HasFlag(PoisonType.Green) || _playerPoison.HasFlag(PoisonType.Red)) names.Add("中毒");
        if (_playerPoison.HasFlag(PoisonType.Paralysis)) names.Add("麻痹");
        if (_playerPoison.HasFlag(PoisonType.Slow)) names.Add("减速");
        if (_playerPoison.HasFlag(PoisonType.Silenced)) names.Add("沉默");
        if (_playerPoison.HasFlag(PoisonType.Abyss)) names.Add("失明");
        if (_playerPoison.HasFlag(PoisonType.Burn) || _playerPoison.HasFlag(PoisonType.HellFire)) names.Add("灼烧");
        if (_playerPoison.HasFlag(PoisonType.Hemorrhage)) names.Add("流血");
        if ((_playerPoison & (PoisonType.Containment | PoisonType.Binding | PoisonType.Chain | PoisonType.WraithGrip)) != 0) names.Add("束缚");
        if (_playerPoison.HasFlag(PoisonType.Neutralize)) names.Add("虚弱");
        if (_playerPoison.HasFlag(PoisonType.Fear)) names.Add("恐惧");
        if (_playerPoison.HasFlag(PoisonType.Parasite)) names.Add("寄生");
        return names.Count == 0 ? "异常状态" : string.Join(" · ", names);
    }
}
