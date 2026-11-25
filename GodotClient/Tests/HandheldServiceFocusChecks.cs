using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Controls;
using ZirconClient.Network;
using ZirconClient.Scripts;
using C = Library.Network.ClientPackets;
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
            var sale = pages.First(p => p.DialogType == NPCDialogType.BuySell && p.Types.Count > 0 && p.Goods.Count == 0
                && p.Buttons.Any(button => button.ButtonID > 0));
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
                WindowManager.Close(prompt);
                prompt.Free();
                CheckSaleReply(game, sale);
                string capture = OS.GetEnvironment("ZIRCON_SERVICE_FOCUS_CAPTURE");
                if (!string.IsNullOrEmpty(capture)) await CaptureServiceBook(game, capture);
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

    private static void CheckSaleReply(GameScene game, Library.SystemModels.NPCPage sale)
    {
        const uint objectId = 42;
        using var player = new PlayerRenderer();
        using var npcObject = new ObjectRenderer { ObjectID = objectId, Type = ObjectRenderer.Kind.NPC };
        using var network = new NetworkManager();
        var connection = (ServerConnection)RuntimeHelpers.GetUninitializedObject(typeof(ServerConnection));
        connection.Connected = true;
        connection.SendList = new ConcurrentQueue<Packet>();
        typeof(NetworkManager).GetField("<Connection>k__BackingField", Instance).SetValue(network, connection);
        Set(game, "_net", network);
        Set(game, "_player", player);
        var objects = (Dictionary<uint, ObjectRenderer>)typeof(GameScene).GetField("_objects", Instance).GetValue(game);
        objects.Add(objectId, npcObject);
        try
        {
            Invoke(game, "OnNPCResponse", new S.NPCResponse { ObjectID = objectId, Page = sale });
            var dialog = (NPCDialog)typeof(GameScene).GetField("_npcDialog", Instance).GetValue(game);
            int replyId = dialog.HandheldOptions[0].id;
            Check(replyId > 0, "the fixture starts with a reply that requires a server response");
            using var confirm = new InputEventKey { Keycode = Key.Enter, Pressed = true };
            Check((bool)Invoke(game, "HandleHandheldInputEvent", confirm) && connection.SendList.IsEmpty,
                "first A reviews the sell-only page reply without sending it");
            using var release = new InputEventKey { Keycode = Key.Enter, Pressed = false };
            for (int i = 0; i < 3; i++)
            {
                Invoke(game, "HandleHandheldInputEvent", release);
                Invoke(game, "HandleHandheldInputEvent", confirm);
            }
            Check(connection.SendList.OfType<C.NPCButton>().Select(button => button.ButtonID).SequenceEqual(new[] { replyId }),
                "the sell-only page sends its reviewed reply once across repeated confirmations");
        }
        finally
        {
            objects.Remove(objectId);
            Set(game, "_net", null);
            Set(game, "_player", null);
        }
    }

    private async System.Threading.Tasks.Task CaptureServiceBook(GameScene game, string path)
    {
        var book = (HandheldJournalService)typeof(GameScene).GetField("_serviceBook", Instance).GetValue(game);
        var overlay = book.GetParent();
        var viewport = new SubViewport { Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        Root.AddChild(viewport);
        try
        {
            overlay.Reparent(viewport, false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = viewport.GetTexture().GetImage();
            Check(image.GetSize() == new Vector2I(640, 360) && image.SavePng(path) == Error.Ok,
                "sell-only service book captured at 640×360");
        }
        finally { viewport.Free(); }
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
