using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Scripts;

public partial class HandheldEquippedMarkersChecks : SceneTree
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); GD.Print("PASS: " + name); }
    private static void Set(object obj, string key, object value) => obj.GetType().GetField(key, Private).SetValue(obj,value);
    public override async void _Initialize()
    {
        await ToSignal(this,SignalName.ProcessFrame);
        var game = new GameScene();
        InventoryDialog bag = null;
        try
        {
            var info = new ItemInfo();
            Set(info,"_ItemName","同款头盔"); Set(info,"_ItemType",ItemType.Helmet);
            var spare = new ClientUserItem(info,1) { Index = 100, Slot = 0 };
            var worn = new ClientUserItem(info,1) { Index = 101, Slot = Globals.EquipmentOffSet + (int)EquipmentSlot.Helmet };
            game.Inventory = new ClientUserItem[] { spare, null };
            game.Equipment[(int)EquipmentSlot.Helmet] = worn;
            var rows = new List<JournalEntry>();
            game.JournalEntries(0,1,rows);
            var actual = rows.SingleOrDefault(r => r.Id == $"equipped:{(int)EquipmentSlot.Helmet}:101");
            Check(actual != null && actual.Badge.Contains("已穿"), "bag Equipment tab includes actual worn gear with its own marker");
            Check(!rows.Single(r => r.Id=="0:100").Badge.Contains("已穿"), "same-name spare helmet is never marked worn");
            Check(game.JournalGearPresentation(0,1,actual,true).Detail.Contains("头盔"), "equipped detail identifies the destination body slot");
            game.JournalEntries(0,0,rows);
            Check(rows.Any(r => r.Id==actual.Id), "All tab also shows equipped gear");
            game.JournalEntries(0,2,rows);
            Check(rows.All(r => !r.Id.StartsWith("equipped:")), "consumable category is not polluted with equipped rows");
            Check(game.JournalActions(0,1,actual.Id,false).Count > 0, "A on equipped gear can open matching replacements");
            game.Equipment[(int)EquipmentSlot.Helmet] = null;
            game.Inventory[1] = worn; worn.Slot = 1;
            game.JournalEntries(0,1,rows);
            Check(rows.All(r => !r.Id.StartsWith("equipped:")) && rows.Any(r => r.Id=="1:101"), "unloaded item becomes a normal bag item without stale worn marker");
            Check(game.JournalActions(0,1,actual.Id,false).Count==0, "stale equipped entry cannot act on a replacement item");
            game.Equipment[(int)EquipmentSlot.Helmet] = spare;
            game.JournalEntries(0,1,rows);
            Check(rows.Count(r => r.Id.EndsWith(":100"))==1, "temporary double array reference renders only one copy of the actual item");
            string capture = OS.GetEnvironment("ZIRCON_EQUIPPED_MARKER_CAPTURE");
            if (!string.IsNullOrEmpty(capture))
            {
                var real = Globals.ItemInfoList.Binding.First(i => i.ItemType == ItemType.Armour && i.ItemName == "Commoner Outfit (M)");
                game.Inventory = new ClientUserItem[] { new(real,1) { Index=200, Slot=0 } };
                bag = new InventoryDialog(); bag.ApplyLegacyEiLayout(); bag.Grid.ItemGrid = game.Inventory;
                bag.ConfigureLegacyInventoryGrid(); Set(game,"_inventoryDialog",bag);
                Array.Clear(game.Equipment);
                game.Equipment[(int)EquipmentSlot.Armour] = new ClientUserItem(real,1) { Index=201, Slot=Globals.EquipmentOffSet+(int)EquipmentSlot.Armour };
                var viewport = new SubViewport { Size=new Vector2I(640,360), RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
                Root.AddChild(viewport);
                var view = new HandheldJournal(game); viewport.AddChild(view); view.Open();
                await ToSignal(this,SignalName.ProcessFrame); await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var pixels = viewport.GetTexture().GetImage();
                Check(pixels.SavePng(capture)==Error.Ok, "captured worn item marker beside an identical spare");
                viewport.Free();
            }
            Quit(0);
        }
        catch(Exception e) { GD.PrintErr(e); Quit(1); }
        finally { bag?.Free(); game.Free(); }
    }
}
