using System;
using System.Collections.Generic;
using Godot;
using ZirconClient.Controls;

namespace ZirconClient.Scripts;

public partial class GameScene
{
    private sealed record NpcPrompt(DXWindow Window, Func<bool> Current);
    private readonly List<NpcPrompt> _handheldNpcPrompts = new();

    internal void BindHandheldNpcPrompt(DXWindow window)
    {
        if (!AutoLoginArgs.Handheld || window is not (ConfirmDialog or ItemAmountDialog)
            || !JournalWindowVisible(_npcDialog) || _npcObjectId == 0
            || JournalServiceRoot != _npcDialog || JournalInputPane() != _npcDialog) return;
        var owner = _npcDialog;
        uint npc = _npcObjectId;
        int page = owner.PageShowCount;
        bool Current() => IsInstanceValid(owner) && JournalWindowVisible(owner)
            && owner.HandheldPageValid && owner.PageShowCount == page && _npcObjectId == npc
            && _objects.ContainsKey(npc) && _net?.Connection?.Connected == true && !_observer && _player?.Dead == false;
        if (window is ConfirmDialog confirmation)
        {
            var prior = confirmation.ContextValid;
            confirmation.ContextValid = () => Current() && prior?.Invoke() != false;
        }
        else if (window is ItemAmountDialog amount)
        {
            var prior = amount.ContextValid;
            amount.ContextValid = () => Current() && prior?.Invoke() != false;
        }
        _handheldNpcPrompts.Add(new(window, Current));
    }

    private void InvalidateHandheldNpcPrompts()
    {
        for (int i = _handheldNpcPrompts.Count - 1; i >= 0; i--)
        {
            var entry = _handheldNpcPrompts[i];
            if (!IsInstanceValid(entry.Window) || entry.Window.IsQueuedForDeletion() || !entry.Window.Visible)
            {
                _handheldNpcPrompts.RemoveAt(i);
                continue;
            }
            if (entry.Current()) continue;
            WindowManager.Close(entry.Window);
            _handheldNpcPrompts.RemoveAt(i);
        }
    }
}
