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

public partial class HandheldEquipmentSwitchChecks : SceneTree
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Private).SetValue(o, value);
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); GD.Print("PASS: " + text); }
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        var game = new GameScene(); var player = new PlayerRenderer(); var net = new NetworkManager();
        var bag = new InventoryDialog(); var character = new CharacterDialog();
        try
        {
            GameScene.Game = game;
            var connection = (ServerConnection)RuntimeHelpers.GetUninitializedObject(typeof(ServerConnection));
            connection.Connected = true; connection.SendList = new ConcurrentQueue<Packet>();
            Set(net, "<Connection>k__BackingField", connection); Set(game, "_net", net); Set(game, "_player", player);
            game.StartInfo = new StartInformation { Class = MirClass.Warrior, Gender = MirGender.Male };
            Set(game, "_playerLevel", 10);
            game.PlayerStats[Stat.WearWeight] = 100;
            game.PlayerStats[Stat.HandWeight] = 100;
            ClientUserItem Item(int index, ItemType type, int level = 0)
            {
                var info = new ItemInfo();
                Set(info, "_ItemName", "候选 " + index); Set(info, "_ItemType", type);
                Set(info, "_RequiredClass", RequiredClass.All); Set(info, "_RequiredGender", RequiredGender.None);
                Set(info, "_RequiredType", RequiredType.Level); Set(info, "_RequiredAmount", level);
                return new ClientUserItem(info, 1) { Index = index, Slot = index - 1 };
            }
            game.Inventory = new[] { Item(1, ItemType.Helmet), Item(2, ItemType.Helmet, 30), Item(3, ItemType.Shoes), Item(4, ItemType.Ring) };
            Array.Resize(ref game.Inventory, 8);
            bag.Grid.ItemGrid = game.Inventory; bag.Grid.GridSize = new Vector2I(4, 2); bag.Grid.CreateGrid();
            Set(game, "_inventoryDialog", bag); game.InventoryCells = bag.Grid.Cells;
            game.EquipmentCells = character.Grid;
            foreach (var cell in game.EquipmentCells) cell.ItemGrid = game.Equipment;
            var helmets = game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Helmet, false);
            Check(helmets.Count == 2 && helmets.All(a => a.Label.Contains("候选")), "empty helmet slot lists every bag helmet and nothing else");
            var blocked = helmets[1];
            var reason = blocked.GetType().GetProperty("DisabledReason")?.GetValue(blocked) as string;
            Check(reason?.Contains("等级") == true && reason.Contains("30"), "unwearable helmet remains listed with the required level");
            blocked.Execute?.Invoke();
            Check(connection.SendList.IsEmpty, "disabled candidate cannot send an equip packet");
            helmets[0].Execute();
            var move = connection.SendList.OfType<C.ItemMove>().Single();
            Check(move.FromSlot == 0 && move.ToSlot == (int)EquipmentSlot.Helmet, "empty slot can equip the selected matching candidate");
            game.InventoryCells[0].Locked = false; game.EquipmentCells[(int)EquipmentSlot.Helmet].Locked = false;
            connection.SendList.Clear();
            var shoes = game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Shoes, false);
            Check(shoes.Count == 1 && shoes[0].Label.Contains("候选 3"), "empty shoes slot lists bag shoes");
            var old = Item(8, ItemType.Helmet); old.Slot = Globals.EquipmentOffSet + (int)EquipmentSlot.Helmet;
            game.Equipment[(int)EquipmentSlot.Helmet] = old;
            var equipped = game.JournalActions(1, 0, $"{(int)EquipmentSlot.Helmet}:{old.Index}", false);
            Check(equipped.Count == 2, "occupied helmet slot also opens matching bag replacements");
            Check(game.JournalActions(1, 0, $"{(int)EquipmentSlot.Helmet}:{old.Index}", true).Any(a => a.Label.Contains("卸下")), "X retains the unload action on occupied gear");
            game.Equipment[(int)EquipmentSlot.Helmet] = null;
            equipped[0].Execute();
            Check(connection.SendList.IsEmpty, "changed target gear cancels a stale replacement");
            Check(game.JournalActions(1, 0, $"{(int)EquipmentSlot.Helmet}:{old.Index}", true).Count == 0,
                "stale occupied gear ID cannot open unload or replacement actions");
            Set(game.Inventory[1].Info, "_RequiredAmount", 0);
            Set(game.Inventory[1].Info, "_RequiredClass", RequiredClass.Wizard);
            Check(game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Helmet, false)[1].DisabledReason == "职业不符", "class restriction is explained");
            Set(game.Inventory[1].Info, "_RequiredClass", RequiredClass.All);
            Set(game.Inventory[1].Info, "_RequiredGender", RequiredGender.Female);
            Check(game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Helmet, false)[1].DisabledReason == "性别不符", "gender restriction is explained");
            Set(game.Inventory[1].Info, "_RequiredGender", RequiredGender.None);
            Set(game.Inventory[1].Info, "_Weight", 101);
            Check(game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Helmet, false)[1].DisabledReason.Contains("负重"), "equipment weight restriction is explained");
            Set(game.Inventory[1].Info, "_Weight", 0);
            Set(game.Inventory[1].Info, "_RequiredAmount", 30);
            game.Inventory[2] = null;
            shoes[0].Execute();
            Check(connection.SendList.IsEmpty, "removed bag candidate cancels stale equip");
            foreach (var ringSlot in new[] { EquipmentSlot.RingL, EquipmentSlot.RingR })
            {
                var ring = game.JournalActions(1, 0, "empty:" + (int)ringSlot, false).Single();
                ring.Execute();
                Check(connection.SendList.OfType<C.ItemMove>().Single().ToSlot == (int)ringSlot, "ring candidate uses selected " + ringSlot + " slot");
                game.InventoryCells[3].Locked = false; game.EquipmentCells[(int)ringSlot].Locked = false;
                connection.SendList.Clear();
            }
            Check(game.JournalActions(1, 0, "empty:" + (int)EquipmentSlot.Shield, false).Count == 1,
                "empty category produces an explicit no-candidates message");
            string capture = OS.GetEnvironment("ZIRCON_EQUIPMENT_SWITCH_CAPTURE");
            if (!string.IsNullOrEmpty(capture))
            {
                game.StartInfo.Name = "TestHero";
                Set(game.Inventory[0].Info, "_ItemName", "布帽");
                Set(game.Inventory[1].Info, "_ItemName", "高级战盔");
                var viewport = new SubViewport { Size = new Vector2I(640,360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                Root.AddChild(viewport);
                var journal = new HandheldJournal(game);
                viewport.AddChild(journal);
                journal.Open(); journal.SelectTab(1);
                journal.Command(Key.Down); journal.Command(Key.Enter);
                Check(journal.HasActionOverlay, "selecting an empty helmet opens a browsable replacement list");
                journal.Command(Key.Down); journal.Command(Key.Enter);
                Check(connection.SendList.IsEmpty && journal.HasActionOverlay, "A on grey candidate leaves the chooser open without sending");
                await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                Check(image.SavePng(capture) == Error.Ok, "captured empty helmet switch chooser at 640×360");
                journal.Back();
                Check(!journal.HasActionOverlay && journal.IsOpen, "B returns from equipment choices to the character page");
                viewport.Free();
            }
            Quit(0);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
        finally { GameScene.Game = null; bag.Free(); character.Free(); net.Free(); player.Free(); game.Free(); }
    }
}
