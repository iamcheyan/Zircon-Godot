using System;
using Godot;
using Library;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private HandheldNpcDialogue _handheldNpcDialogue;
    private int _handheldNpcRevision = -1;
    private NPCDialog _handheldNpcReplyOwner;
    private int _handheldNpcReplyRevision = -1;
    private bool _handheldNpcReplyPending;
    private double _handheldNpcStickAt;
    private Key _handheldNpcStickDirection;
    private bool _handheldNpcStickNeutral = true;
    private bool HandheldNpcDialogueOpen => _handheldNpcDialogue?.Visible == true;

    private void SyncHandheldNpcDialogue()
    {
        bool show = AutoLoginArgs.Handheld && JournalWindowVisible(_npcDialog)
            && JournalInputPane() == _npcDialog && !JournalNativeServiceVisible
            && _net?.Connection?.Connected == true && _player?.Dead == false && !_observer;
        if (!show)
        {
            _handheldNpcDialogue?.Hide();
            return;
        }
        if (_handheldNpcDialogue == null)
        {
            var layer = new CanvasLayer { Name = "HandheldNpcDialogueLayer", Layer = 98 };
            AddChild(layer);
            _handheldNpcDialogue = new HandheldNpcDialogue();
            layer.AddChild(_handheldNpcDialogue);
            _handheldNpcDialogue.OptionSelected += SelectHandheldNpcOption;
        }
        bool newPage = _handheldNpcRevision != _npcDialog.PageShowCount;
        if (newPage)
        {
            _handheldNpcRevision = _npcDialog.PageShowCount;
            _handheldNpcDialogue.SetPage(_npcDialog.HandheldNpcTitle, _npcDialog.HandheldBody,
                _npcDialog.HandheldPortrait, _npcDialog.HandheldOptions);
        }
        if (!_handheldNpcDialogue.Visible)
        {
            _handheldNpcStickNeutral = true;
            _handheldNpcStickDirection = Key.None;
            SetJournalServiceFocus(null, null);
            _handheldNpcDialogue.Open();
        }
        _handheldNpcDialogue.SetPending(HandheldNpcReplyPending(_npcDialog, _npcDialog.PageShowCount));
    }

    private void SelectHandheldNpcOption(int id)
    {
        if (!HandheldNpcDialogueOpen || JournalInputPane() != _npcDialog
            || _handheldNpcRevision != _npcDialog.PageShowCount
            || !_npcDialog.HandheldOptions.Exists(option => option.id == id)
            || _net?.Connection?.Connected != true || _player?.Dead != false || _observer)
        {
            _handheldNpcDialogue?.SetPending(false);
            return;
        }
        if (id == 0) { CloseNPCDialog(); _handheldNpcDialogue.Hide(); return; }
        if (!TrySubmitHandheldNpcReply(_npcDialog, _handheldNpcRevision, id))
            _handheldNpcDialogue.SetPending(HandheldNpcReplyPending(_npcDialog, _npcDialog.PageShowCount));
    }

    private bool HandheldNpcReplyPending(NPCDialog owner, int revision) =>
        _handheldNpcReplyPending && owner == _handheldNpcReplyOwner && revision == _handheldNpcReplyRevision;

    private bool TrySubmitHandheldNpcReply(NPCDialog owner, int revision, int id)
    {
        if (owner != _npcDialog || !JournalWindowVisible(owner) || owner.PageShowCount != revision
            || !owner.HandheldPageValid || !owner.HandheldOptions.Exists(option => option.id == id)
            || JournalInputPane() != owner || _net?.Connection?.Connected != true || _observer || _player?.Dead != false
            || HandheldNpcReplyPending(owner, revision)) return false;
        if (id == 0) { CloseNPCDialog(); return true; }
        _handheldNpcReplyOwner = owner;
        _handheldNpcReplyRevision = revision;
        _handheldNpcReplyPending = true;
        SendNPCButton(id);
        return true;
    }

    private bool RouteHandheldNpcKey(InputEventKey key)
    {
        if (!HandheldNpcDialogueOpen) return false;
        if (!key.Pressed || key.Echo) return true;
        if (key.Keycode == Key.Escape)
        {
            CloseNPCDialog();
            _handheldNpcDialogue.Hide();
        }
        else if (key.Keycode == Key.Q) _handheldNpcDialogue.PageOptions(-1);
        else if (key.Keycode == Key.E) _handheldNpcDialogue.PageOptions(1);
        else _handheldNpcDialogue.HandleInput(key.Keycode);
        return true;
    }

    private void ProcessHandheldNpcStick(double now)
    {
        var axis = new Vector2(Input.GetJoyAxis(0, JoyAxis.LeftX), Input.GetJoyAxis(0, JoyAxis.LeftY));
        float right = Input.GetJoyAxis(0, JoyAxis.RightY);
        if (_handheldNpcStickNeutral)
        {
            if (axis.LengthSquared() < .1f && Math.Abs(right) < .3f) _handheldNpcStickNeutral = false;
            return;
        }
        Key direction = axis.Y < -.5f ? Key.Up : axis.Y > .5f ? Key.Down : Key.None;
        if (direction != Key.None && (direction != _handheldNpcStickDirection || now >= _handheldNpcStickAt))
        {
            _handheldNpcDialogue.HandleInput(direction);
            _handheldNpcStickAt = now + (direction == _handheldNpcStickDirection ? 160 : 360);
        }
        _handheldNpcStickDirection = direction;
        if (Math.Abs(right) > .5f && now >= _handheldNpcStickAt)
        {
            _handheldNpcDialogue.ScrollBody(Math.Sign(right));
            _handheldNpcStickAt = now + 120;
        }
    }
}
