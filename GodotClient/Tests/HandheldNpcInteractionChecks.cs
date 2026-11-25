using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Library;
using Library.Network;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Network;
using ZirconClient.Scripts;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

public partial class HandheldNpcInteractionChecks : SceneTree
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var game = new GameScene(); var player = new PlayerRenderer(); var net = new NetworkManager();
        var npc = new ObjectRenderer { ObjectID = 77, Type = ObjectRenderer.Kind.NPC, CellX = 105, CellY = 100, Visible = true };
        NPCDialog dialog = null;
        CanvasLayer layer = null;
        try
        {
            GameScene.Game = game;
            var connection = (ServerConnection)RuntimeHelpers.GetUninitializedObject(typeof(ServerConnection));
            connection.Connected = true; connection.SendList = new ConcurrentQueue<Packet>();
            Set(net, "<Connection>k__BackingField", connection); Set(game, "_net", net);
            Set(game, "_startGameShown", true); Set(game, "_player", player);
            Set(game, "_playerLocation", new System.Drawing.Point(100, 100)); game._handheldFocusObject = npc;
            Call(game,"HandheldConfirm");
            Check(connection.SendList.OfType<C.NPCCall>().Count(p => p.ObjectID == 77) == 1
                && !connection.SendList.OfType<C.AutoPathWaypoint>().Any(), "NPC at distance five gets NPCCall like PC, not a walk to its blocked cell");
            // Use the real response dispatch, not a standalone widget test.
            layer = new CanvasLayer(); Root.AddChild(layer); Set(game,"_uiLayer",layer);
            dialog = new NPCDialog(); dialog.ApplyLegacyEiLayout(); layer.AddChild(dialog); dialog.Hide();
            Set(game,"_npcDialog",dialog);
            var page = new NPCPage(); Set(page,"_Say","你好，旅人。\n[移动至比奇城所需金钱 : 500 钱:7]\n[离开:0]");
            var response = new S.NPCResponse { ObjectID = 77, Page = page };
            Call(game,"OnNPCResponse",response);
            Call(game,"SyncHandheldNpcDialogue");
            var view = (HandheldNpcDialogue)typeof(GameScene).GetField("_handheldNpcDialogue",Fields).GetValue(game);
            Check(view?.Visible == true && view.OptionCount == 2 && dialog.HandheldOptions[0].label.Contains("500"), "real response opens full-screen dialogue and preserves colons inside labels");
            connection.SendList.Clear();
            Call(game,"RouteHandheldNpcKey",new InputEventKey { Keycode = Key.Enter, Pressed = true });
            Call(game,"RouteHandheldNpcKey",new InputEventKey { Keycode = Key.Enter, Pressed = true });
            Check(connection.SendList.OfType<C.NPCButton>().Count(p => p.ButtonID == 7)==1, "A selects the inline server button once while waiting");
            // Refreshing, including a service-ledger retry, must not re-arm a
            // request just because time passed or the widget was recreated.
            Call(game,"SyncHandheldNpcDialogue");
            Check(!(bool)Call(game,"TrySubmitHandheldNpcReply", dialog, dialog.PageShowCount, 7), "dialogue and ledger share one in-flight reply guard");
            dialog.ShowPage(new S.NPCResponse { ObjectID = 77, Page = null });
            Call(game,"SyncHandheldNpcDialogue");
            Check(view.OptionCount == 0 && !dialog.HandheldPageValid, "unresolved response clears old replies instead of relabeling them as a new page");
            Call(game,"RouteHandheldNpcKey",new InputEventKey { Keycode = Key.Enter, Pressed = true });
            Check(connection.SendList.OfType<C.NPCButton>().Count()==1, "unresolved page never sends an old button");
            Set(page, "_Say", "等级为7时可以修炼{FCOLOR/10}基本剑术{FCOLOR/12}的第一阶段。\n[前一步:1]");
            dialog.ShowPage(response);
            Check(!dialog.HandheldBody.Contains("FCOLOR") && dialog.HandheldBody.Contains("[color=#00ff00]基本剑术[/color]", StringComparison.OrdinalIgnoreCase), "real catalog color tags become visible color, not script text");
            Set(page, "_Say", "你好，旅人。\n[离开:0]");
            // A modal callback also belongs to one exact NPC page, even when
            // the next page happens to reuse the same controls and button IDs.
            var objects = (System.Collections.Generic.Dictionary<uint,ObjectRenderer>)typeof(GameScene).GetField("_objects",Fields).GetValue(game);
            objects[npc.ObjectID] = npc;
            dialog.ShowPage(response);
            bool confirmed = false;
            var prompt = new ConfirmDialog("测试费用", "确认", () => confirmed = true);
            WindowManager.Open(prompt, layer);
            dialog.ShowPage(response);
            prompt.HandleHandheldKey(Key.Left);
            prompt.HandleHandheldKey(Key.Enter);
            Check(!confirmed && !prompt.Visible, "page change invalidates an outstanding confirmation before callback");
            prompt.Free();
            // A sell-only page is still a merchant, even with zero buyable goods.
            Set(page, "_DialogType", NPCDialogType.BuySell);
            dialog.JournalGoods.Show();
            Check(dialog.JournalGoods.GoodCount == 0 && (bool)Call(game,"JournalMerchantAvailable"), "sell-only NPC remains a merchant with zero purchasable goods");
            Set(page, "_DialogType", NPCDialogType.None);
            Call(game,"SyncHandheldNpcDialogue");
            Call(game,"RouteHandheldNpcKey",new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Check(!view.Visible && !dialog.Visible && connection.SendList.OfType<C.NPCClose>().Count()==1, "B closes dialogue and sends NPC close instead of a world action");
            Quit(0);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
        finally { GameScene.Game = null; WindowManager.OpenWindows.Clear(); layer?.Free(); npc.Free(); player.Free(); net.Free(); game.Free(); }
    }
    private static void Set(object o,string name,object value) => o.GetType().GetField(name,Fields).SetValue(o,value);
    private static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Fields).Invoke(o,args);
    private static void Check(bool condition,string name) { if(!condition) throw new Exception(name); GD.Print("PASS: "+name); }
}
