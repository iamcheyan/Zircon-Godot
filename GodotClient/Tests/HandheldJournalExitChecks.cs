using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ZirconClient.Controls;
using ZirconClient.Scripts;

public partial class HandheldJournalExitChecks : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var game = new GameScene();
        var journal = new HandheldJournal(game);
        try
        {
            var rows = new List<JournalEntry>();
            game.JournalEntries(5, 0, rows);
            Check(rows.First().Id == "exit" && rows[0].Name == "退出游戏", "the system landing page starts with Exit Game");
            game.JournalEntries(5, 5, rows);
            Check(rows.Any(r => r.Id == "exit") && rows.Any(r => r.Id == "logout"), "Leave still distinguishes exiting from character selection");
            foreach (int category in new[] { 0, 5 })
            {
                var action = game.JournalActions(5, category, "exit", false).Single();
                Check(action.Execute == null && action.Children != null, "exit requires confirmation");
                var choices = action.Children();
                Check(choices[0].Label == "取消" && choices[1].Execute != null, "confirmation defaults to Cancel");
            }
            journal.Open();
            journal.Command(Key.Q); // L1 wraps from Backpack to System.
            journal.Command(Key.Enter);
            Check(journal.HasActionOverlay && journal.Back(), "L1 then A opens the exit prompt and B can cancel");
            Check(!journal.HasActionOverlay, "cancelling leaves the journal open without exiting");
            Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex);
            Quit(1);
        }
        finally { journal.Free(); game.Free(); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        GD.Print("PASS: " + description);
    }
}
