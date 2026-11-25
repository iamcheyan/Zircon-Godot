using System;
using System.Linq;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.Scripts;
using S = Library.Network.ServerPackets;

// ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/HandheldServiceFocusChecks.cs
public partial class HandheldServiceFocusChecks : SceneTree
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var game = new GameScene();
        var layer = new CanvasLayer();
        var npc = new NPCDialog();
        var inventory = new InventoryDialog();
        try
        {
            GameScene.Game = game;
            Root.AddChild(layer);
            Set(game, "_uiLayer", layer);
            Set(game, "_npcDialog", npc);
            Set(game, "_inventoryDialog", inventory);

            var pages = Globals.NPCPageList.Binding;
            var normal = pages.First(p => p.DialogType == NPCDialogType.None && !string.IsNullOrEmpty(p.Say));
            var sale = pages.First(p => p.DialogType == NPCDialogType.BuySell && p.Types.Count > 0 && p.Goods.Count == 0);
            npc.ShowPage(new S.NPCResponse { Page = normal });
            npc.ShowPage(new S.NPCResponse { Page = sale });
            Check(inventory.IsSellMode && inventory.IsVisibleInTree()
                && WindowManager.OpenWindows.Last() == inventory, "fixture displays the original sale bag above the NPC window");

            if (AutoLoginArgs.Handheld)
            {
                Check(ReferenceEquals(Invoke(game, "JournalInputPane"), npc), "sell-only NPC reply focus stays in the handheld service book");
                var prompt = new ConfirmDialog("确认出售？", "确认", null);
                WindowManager.Open(prompt, layer);
                Check(ReferenceEquals(Invoke(game, "JournalInputPane"), prompt), "an actual confirmation still takes priority over the handheld service book");
            }
            else
            {
                Check(npc.HandheldOptions.Count == 0, "desktop still uses its native NPC links and sale bag");
            }
            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
        finally
        {
            GameScene.Game = null;
            WindowManager.OpenWindows.Clear();
            layer.Free();
            game.Free();
        }
    }

    private static object Invoke(GameScene game, string name, params object[] args)
        => typeof(GameScene).GetMethod(name, Instance).Invoke(game, args);

    private static void Set(GameScene game, string name, object value)
        => typeof(GameScene).GetField(name, Instance).SetValue(game, value);

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }
}
