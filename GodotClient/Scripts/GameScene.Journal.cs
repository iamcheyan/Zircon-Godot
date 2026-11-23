using System;
using System.Collections.Generic;
using Godot;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

internal sealed record JournalEntry(string Id, string Name, string Detail, string Badge = "", Texture2D Icon = null, int Width = 1, int Height = 1);
internal sealed record JournalAction(string Label, Func<string> Execute = null, Func<IReadOnlyList<JournalAction>> Children = null);

public partial class GameScene
{
    private HandheldJournal _journal;
    private Key _journalDirection, _journalStickDirection;
    private int _journalScrollDirection;
    private double _journalKeyAt, _journalStickAt, _journalScrollAt;
    private bool _journalStickNeutral;
    private bool JournalOpen => _journal?.IsOpen == true;

    internal void JournalEntries(int tab, int cat, List<JournalEntry> rows)
    {
        rows.Clear();
        if (tab is 0 or 1) JournalGearEntries(tab, cat, rows);
        else if (tab is 2 or 3) JournalLoreEntries(tab, cat, rows);
        else if (tab is 4 or 5) JournalUtilityEntries(tab, cat, rows);
    }

    internal IReadOnlyList<JournalAction> JournalActions(int tab, int cat, string id, bool secondary) => tab switch
    {
        0 or 1 => JournalGearActions(tab, cat, id, secondary),
        2 or 3 => JournalLoreActions(tab, cat, id, secondary),
        4 or 5 => JournalUtilityActions(tab, cat, id, secondary),
        _ => Array.Empty<JournalAction>(),
    };

    private void ResetJournalNavigation()
    {
        _journalDirection = _journalStickDirection = Key.None;
        _journalScrollDirection = 0;
        _journalStickNeutral = true;
        _handheldSelectDownMs = -1;
        _selectorRepeatKey = Key.None;
    }

    private void OpenJournal()
    {
        if (!_startGameShown || HandheldHasModalWindow() || HandheldTextEditing) return;
        if (_journal == null)
        {
            var layer = new CanvasLayer { Name = "HandheldJournalLayer", Layer = 80 };
            AddChild(layer);
            _journal = new HandheldJournal(this);
            layer.AddChild(_journal);
        }
        _handheldQuickSelect?.Close();
        StopHandheldMovement();
        ClearHandheldFocus();
        _pendingMagicPacket = null;
        DXItemCell.SelectedCell = null;
        _selectedCurrency = null;
        _pendingNpcClickObjectId = 0;
        GetViewport()?.GuiGetFocusOwner()?.ReleaseFocus();
        ResetJournalNavigation();
        _journal.Open();
        UpdateHandheldPointerMode();
    }

    private void CloseJournal()
    {
        _journal.Close();
        ResetJournalNavigation();
        StopHandheldMovement();
        UpdateHandheldPointerMode();
    }

    // A server-opened window, confirmation or map owns input until it closes.
    // Hide, don't destroy: the journal retains the page, focus and scroll position.
    private void SyncJournalVisibility()
    {
        if (!JournalOpen) return;
        bool show = !HandheldHasModalWindow() && !JournalServiceActive && !HandheldTextEditing;
        if (_journal.Visible == show) return;
        _journal.Visible = show;
        ResetJournalNavigation();
        UpdateHandheldPointerMode();
    }

    private bool RouteJournalKey(InputEventKey key, double now)
    {
        if (!JournalOpen) return false;
        SyncJournalVisibility();
        if (!_journal.Visible)
        {
            if (_bigMap?.Visible == true && key.Keycode is Key.F15 or Key.Escape)
            {
                if (key.Pressed && !key.Echo) { _bigMap.Visible = false; SyncJournalVisibility(); }
                return true;
            }
            // Never let Menu, Start or Select override a child dialog. Esc and
            // ordinary keys retain the legacy window's own close/edit semantics.
            return key.Keycode is Key.F11 or Key.Menu or Key.F10 or Key.F15;
        }
        if (!key.Pressed)
        {
            if (key.Keycode == _journalDirection) _journalDirection = Key.None;
            return true;
        }
        if (key.Echo) return true;
        switch (key.Keycode)
        {
            case Key.F11: case Key.Menu: CloseJournal(); break;
            case Key.Escape: if (!_journal.Back()) CloseJournal(); break;
            case Key.F10:
                if (_journal.HasActionOverlay) _journal.Back(); else ToggleHandheldHudMode();
                break;
            case Key.F15:
                if (!_journal.HasActionOverlay) OpenBigMap();
                break;
            case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                _journalDirection = key.Keycode;
                _journalKeyAt = now + 380;
                _journal.Command(key.Keycode);
                break;
            default: _journal.Command(key.Keycode == Key.KpEnter ? Key.Enter : key.Keycode); break;
        }
        SyncJournalVisibility();
        return true;
    }

    private void ProcessJournal(double now)
    {
        SyncJournalVisibility();
        if (!_journal.Visible) return;
        if (_journalDirection != Key.None && now >= _journalKeyAt)
        {
            _journal.Command(_journalDirection);
            _journalKeyAt = now + 145;
        }
        var axis = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
        float ry = Input.GetJoyAxis(0, JoyAxis.RightY);
        if (_journalStickNeutral)
        {
            if (axis.LengthSquared() < 0.1f && Mathf.Abs(ry) < .32f) _journalStickNeutral = false;
            return;
        }
        Key dir = axis.LengthSquared() < .25f ? Key.None : Mathf.Abs(axis.X) > Mathf.Abs(axis.Y)
            ? (axis.X > 0 ? Key.Right : Key.Left) : (axis.Y > 0 ? Key.Down : Key.Up);
        if (dir != Key.None && (dir != _journalStickDirection || now >= _journalStickAt))
        {
            _journal.Command(dir);
            _journalStickAt = now + (dir == _journalStickDirection ? 145 : 380);
        }
        _journalStickDirection = dir;
        int scroll = Mathf.Abs(ry) < .5f ? 0 : Math.Sign(ry);
        if (scroll != 0 && (scroll != _journalScrollDirection || now >= _journalScrollAt))
        {
            _journal.ScrollDetail(scroll);
            _journalScrollAt = now + 120;
        }
        _journalScrollDirection = scroll;
    }
}
