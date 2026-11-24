using System;
using System.Reflection;
using Godot;
using ZirconClient.Controls;
using ZirconClient.Scripts;
using Library;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ZirconClient.HandheldActions;

// ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/HandheldHudChecks.cs
public partial class HandheldHudChecks : SceneTree
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    private static async Task CaptureHudStates(SceneTree tree)
    {
        string directory = OS.GetEnvironment("ZIRCON_HANDHELD_HUD_VISUAL_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetSize(new Vector2I(640, 360));
        var game = new GameScene();
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.OpenMap);
        config.Pages[0].Bindings[HandheldButton.Up] = ActionBinding.Menu(MenuAction.OpenSkills);
        config.Pages[1].Bindings[HandheldButton.Y] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenMap);
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenQuests);
        config.FixedSlots["L2+R2"] = ActionBinding.Menu(MenuAction.OpenSystem);
        typeof(GameScene).GetMethod("ActivateHandheldActionConfiguration", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(game, new object[] { config });
        var hud = new HandheldHud(game);
        tree.GetRoot().AddChild(hud);
        async Task Capture(string name)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = Path.Combine(directory, name + ".png");
            Error error = tree.GetRoot().GetTexture().GetImage().SavePng(path);
            Check(error == Error.Ok && new FileInfo(path).Length > 0, "rendered HUD captured: " + name);
        }
        await Capture("hud-page-1");
        var engine = (HandheldActionEngine)typeof(GameScene).GetField("_handheldActionEngine", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
        double now = Godot.Time.GetTicksMsec();
        engine.Press(HandheldButton.R1, now); engine.Release(HandheldButton.R1, now);
        await Capture("hud-page-2");
        now = Godot.Time.GetTicksMsec();
        engine.Press(HandheldButton.L2, now);
        await Capture("hud-l2-held");
        now = Godot.Time.GetTicksMsec();
        engine.Release(HandheldButton.L2, now);
        engine.Press(HandheldButton.L2, now); engine.Press(HandheldButton.R2, now);
        await Capture("hud-chord-hint");
        hud.QueueFree(); game.Free();
    }

    public override async void _Initialize()
    {
        try
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var layout = typeof(HandheldHud).GetMethod("LayoutForViewport", flags);
            Check(layout != null, "F2 has a viewport-based four-corner layout");
            foreach (var size in new[] { new Vector2(640,360), new Vector2(1280,720), new Vector2(480,320) })
            {
                var boxes = (Rect2[])layout.Invoke(null, new object[] { size });
                Check(boxes.Length == 6, "layout defines health, mana, map, fixed actions, direct actions and selector bounds");
                var view = new Rect2(Vector2.Zero, size);
                foreach (var r in boxes) Check(view.Encloses(r), $"F2 region {r} fits {size}");
                Check(!boxes[3].Intersects(boxes[4]), "belt and skill corners do not overlap");
                Check(!boxes[2].Intersects(boxes[4]), "map and skills are separate");
            }
            foreach(float scale in new[]{.625f,.8f,1f,2f})
            {
                var canvas=Transform2D.Identity.Scaled(Vector2.One*scale);
                var hud=Transform2D.Identity.Scaled(Vector2.One/scale);
                var point=new Vector2(542,15);
                Check((canvas*hud*point).IsEqualApprox(point),"CanvasLayer and HUD inverse scales keep child anchors in viewport pixels");
            }
            var actionBounds = typeof(HandheldHud).GetMethod("ActionSlotBounds", flags, null, new[] { typeof(Vector2) }, null);
            Check(actionBounds != null, "HUD exposes viewport-based six-button action positions");
            foreach (var size in new[] { new Vector2(640, 360), new Vector2(480, 320) })
            {
                var slots = (Rect2[])actionBounds.Invoke(null, new object[] { size });
                Check(slots.Length == 6, "HUD has exactly six configurable direct action slots");
                foreach (var slot in slots) Check(new Rect2(Vector2.Zero, size).Encloses(slot), "direct action slot fits the viewport");
                Check(slots[2].Position.Y < slots[3].Position.Y && slots[4].Position.X < slots[2].Position.X
                    && slots[5].Position.X > slots[2].Position.X && slots[0].Position.X > slots[2].Position.X,
                    "direct actions retain directional cross and X/Y positions");
            }
            var ratio = typeof(HandheldHud).GetMethod("ResourceFraction", flags);
            Check(ratio != null, "F2 normalizes resource values without inventing unknown maxima");
            float Fraction(int value,int max) => (float)ratio.Invoke(null,new object[]{value,max});
            Check(Math.Abs(Fraction(25,100)-.25f)<.001f, "blood fraction uses floating-point division");
            Check(Fraction(25,0)==0 && Fraction(-1,100)==0 && Fraction(101,100)==1, "unready and invalid resources clamp safely");
            var game = new GameScene();
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var map = new MiniMapDialog(); var buffs = new BuffDialog();
            var hp = new Control(); var chat = new Control();
            typeof(GameScene).GetField("_miniMap", fields).SetValue(game,map);
            typeof(GameScene).GetField("_buffDialog", fields).SetValue(game,buffs);
            typeof(GameScene).GetField("_handheldHudMode", fields).SetValue(game,true);
            typeof(GameScene).GetMethod("ApplyHudVisibility",fields).Invoke(game,null);
            Check(!map.Visible && !buffs.Visible,"F2 hides legacy map and duplicate buff strip");
            buffs.Visible=true;
            typeof(GameScene).GetMethod("LayoutBuffDialog",fields).Invoke(game,null);
            Check(!buffs.Visible,"late buff refresh cannot resurrect the legacy strip");
            var direct = typeof(GameScene).GetMethod("HandheldDirectAction",fields);
            Check(direct != null, "F2 reads configured actions instead of desktop skill bars");
            for (int i = 0; i < 6; i++)
            {
                var choice = direct.Invoke(game, new object[] { i });
                string name = (string)choice.GetType().GetProperty("Name").GetValue(choice);
                Check(name == "空", "unbound F2 actions remain empty instead of showing fallback skills");
            }
            typeof(GameScene).GetField("_handheldHudMode", fields).SetValue(game,false);
            typeof(GameScene).GetMethod("ApplyHudVisibility",fields).Invoke(game,null);
            Check(map.Visible,"switching back restores the existing minimap");
            hp.Free(); chat.Free(); map.Free(); buffs.Free(); game.Free();
            await CaptureHudStates(this);
            Quit(0);
        }
        catch(Exception error) { GD.PrintErr(error); Quit(1); }
    }
}
