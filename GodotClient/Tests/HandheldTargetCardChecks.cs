using System;
using System.Linq;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.Scripts;
using S = Library.Network.ServerPackets;

public partial class HandheldTargetCardChecks : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        GameScene game = null;
        SubViewport viewport = null;
        ObjectRenderer monster = null, item = null;
        try
        {
            var method = typeof(HandheldHud).GetMethod("TargetCardBounds", BindingFlags.Static | BindingFlags.NonPublic);
            Check(method != null, "target has its own lower-right card bounds");
            foreach (var size in new[] { new Vector2(640,360), new Vector2(480,320), new Vector2(1280,720) })
            {
                var bounds = (Rect2)method.Invoke(null, new object[] { size });
                Check(new Rect2(Vector2.Zero,size).Encloses(bounds), $"target card fits {size}");
                Check(bounds.End.X == size.X-16 && bounds.Position.Y > size.Y/3, "target is right-anchored in the lower half");
                var layout = HandheldHud.LayoutForViewport(size);
                Check(!bounds.Intersects(layout[2]) && !bounds.Intersects(layout[3]) && !bounds.Intersects(layout[4]),
                    "target card does not cover map or action controls");
                if (size.X >= 640) Check(bounds.End.Y == size.Y-16, "target sits on the bottom margin when space allows");
            }
            game = new GameScene();
            var definition = Globals.MonsterInfoList.Binding.First(m => m.MonsterName == "Decaying Ghoul");
            monster = ObjectRenderer.CreateMonster(new S.ObjectMonster { MonsterIndex = definition.Index });
            Check(monster != null, "monster uses a real game definition and sprite library");
            monster.Health = 650; monster.MaxHealth = 1000;
            game._handheldFocusObject = monster;
            var target = game.HandheldHudTarget();
            Check(target.Health == .65f && target.Detail.Contains("650") && target.Detail.Contains("1,000"), "target health matches live target values");
            Check(target.Icon != null, "monster portrait or actual idle sprite is available");
            Check(!target.Locked, "unlocked focus is not labeled as locked");

            viewport = new SubViewport { Size = new Vector2I(640,360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            Root.AddChild(viewport);
            var hud = new HandheldHud(game);
            viewport.AddChild(hud);
            string dir = OS.GetEnvironment("ZIRCON_TARGET_CAPTURE_DIR");
            async System.Threading.Tasks.Task Capture(string name)
            {
                await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    using var image = viewport.GetTexture().GetImage();
                    Check(image.SavePng(System.IO.Path.Combine(dir,name+".png")) == Error.Ok, "captured "+name);
                }
            }
            await Capture("monster-target");
            monster.MaxHealth = 0;
            Check(game.HandheldHudTarget().Health == null && game.HandheldHudTarget().Detail.Contains("未知"), "unknown health is not fabricated");
            monster.Dead = true;
            Check(game.HandheldHudTarget() == HandheldTargetInfo.Empty, "dead target removes the entire card");
            monster.Dead = false;

            var itemDefinition = Globals.ItemInfoList.Binding.First(info => info.ItemType == ItemType.Weapon &&
                MirSkin.GetTexture(LibraryFile.Inventory, info.Image) != null);
            var instance = new ClientUserItem(itemDefinition,3);
            item = ObjectRenderer.CreateItem(new S.ObjectItem { Item = instance });
            Check(item != null && ReferenceEquals(item.Item,instance), "ground item retains its real inventory item definition and quantity");
            game._handheldFocusObject = item;
            target = game.HandheldHudTarget();
            Check(target.Health == null && target.Detail.Contains("3"), "item shows quantity rather than a monster HP bar");
            Check(ReferenceEquals(target.Icon, MirSkin.GetTexture(LibraryFile.Inventory, DXItemCell.GetItemDrawIndex(instance))),
                "item card uses the bag artwork, not Ground artwork");
            await Capture("item-target");
            item.DisplayName = new string('装',100);
            await Capture("long-name-target");
            game._handheldFocusObject = null;
            Check(game.HandheldHudTarget() == HandheldTargetInfo.Empty, "clearing focus removes stale target information");
            Quit(0);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
        finally
        {
            viewport?.Free(); monster?.Free(); item?.Free(); game?.Free();
        }
    }
    private static void Check(bool ok,string text)
    {
        if (!ok) throw new InvalidOperationException(text);
        GD.Print("PASS: "+text);
    }
}
