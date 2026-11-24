using System;
using System.IO;
using System.Reflection;
using Godot;
using ZirconClient.Controls;
using ZirconClient.HandheldActions;
using ZirconClient.Scripts;

// No server, real save, or operating system input.
public partial class HandheldBindingsChecks : SceneTree
{
    static void Check(bool b,string s){if(!b)throw new InvalidOperationException(s);GD.Print("PASS: "+s);}
    private static string _visualDirectory;
    private static readonly (string Name, Key[] Keys)[] VisualStates =
    {
        ("bindings-page-1", new[] { Key.E }),
        ("bindings-page-2", new[] { Key.E }),
        ("bindings-fixed", new[] { Key.Enter }),
        ("bindings-picker", new[] { Key.E, Key.E }),
        ("bindings-functions-category", new[] { Key.E }),
        ("bindings-clear-category", new[] { Key.Enter }),
        ("bindings-save-error", new[] { Key.Escape, Key.F13 }),
        ("bindings-timing", Array.Empty<Key>())
    };
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Set(GameScene game,string name,object value) => typeof(GameScene).GetField(name,Private).SetValue(game,value);
    static object Invoke(GameScene game,string name,params object[] args) => typeof(GameScene).GetMethod(name,Private).Invoke(game,args);
    static void CheckEditor(Type type)
    {
        string dir=Path.Combine(Path.GetTempPath(),"zircon-bindings-ui-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(dir,"actions.json");
        var game=new GameScene();
        object ui=null;
        try
        {
            Directory.CreateDirectory(dir);
            Set(game,"_handheldActionPath",path);
            var config=HandheldActionConfiguration.CreateDefault();
            config.Pages[0].Bindings[HandheldButton.X]=ActionBinding.Menu(MenuAction.OpenMap);
            Invoke(game,"ActivateHandheldActionConfiguration",config);
            ui=Activator.CreateInstance(type,new object[]{game});
            var open=type.GetMethod("Open"); var key=type.GetMethod("HandleKey");
            open.Invoke(ui,null);
            key.Invoke(ui,new object[]{Key.Enter});
            key.Invoke(ui,new object[]{Key.Escape});
            var unchanged=(HandheldActionConfiguration)Invoke(game,"CloneHandheldActionConfig");
            Check(unchanged.Pages[0].Bindings[HandheldButton.X].MenuAction==MenuAction.OpenMap,
                "B cancels the candidate picker without changing its current binding");
            key.Invoke(ui,new object[]{Key.Enter}); key.Invoke(ui,new object[]{Key.Q}); key.Invoke(ui,new object[]{Key.Enter});
            var saved=HandheldActionConfigurationStore.Load(path);
            Check(saved.Pages[0].Bindings[HandheldButton.X].Kind==ActionKind.Empty,
                "L1/R1 category navigation and A persist the confirmed clear candidate");
            File.WriteAllText(path,"{broken");
            key.Invoke(ui,new object[]{Key.Space});
            Check(File.ReadAllText(path)=="{broken","failed binding save preserves the damaged original file");
            var retained=(HandheldActionConfiguration)Invoke(game,"CloneHandheldActionConfig");
            Check(retained.Pages[0].Bindings[HandheldButton.X].Kind==ActionKind.Empty,
                "failed binding save leaves the last effective action configuration intact");
        }
        finally
        {
            if(ui is GodotObject obj && GodotObject.IsInstanceValid(obj)) obj.Free();
            game.Free();
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }
    private static HandheldActionBindings _captureUi;
    private static GameScene _captureGame;
    private static void AssertVisualState(int index)
    {
        int tab = (int)typeof(HandheldActionBindings).GetField("_tab", Private).GetValue(_captureUi);
        bool picker = (bool)typeof(HandheldActionBindings).GetField("_picker", Private).GetValue(_captureUi);
        bool timing = (bool)typeof(HandheldActionBindings).GetField("_timingSettings", Private).GetValue(_captureUi);
        int category = (int)typeof(HandheldActionBindings).GetField("_category", Private).GetValue(_captureUi);
        string message = (string)typeof(HandheldActionBindings).GetField("_message", Private).GetValue(_captureUi);
        bool valid = index switch
        {
            0 => tab == 0 && !picker && !timing,
            1 => tab == 1 && !picker && !timing,
            2 => tab == 2 && !picker && !timing,
            3 => tab == 2 && picker && !timing && category == 0,
            4 => tab == 2 && picker && !timing && category == 2,
            5 => tab == 2 && picker && !timing && category == 3,
            6 => tab == 2 && picker && !timing && message.StartsWith("保存失败", StringComparison.Ordinal),
            7 => tab == 2 && !picker && timing,
            _ => false
        };
        Check(valid, "editor state is correct before capture: " + VisualStates[index].Name);
    }

    private static async System.Threading.Tasks.Task CaptureVisualStates(SceneTree tree, Type type)
    {
        _visualDirectory = OS.GetEnvironment("ZIRCON_BINDING_VISUAL_DIR");
        if (string.IsNullOrWhiteSpace(_visualDirectory)) return;
        Directory.CreateDirectory(_visualDirectory);
        DisplayServer.WindowSetSize(new Vector2I(640, 360));
        _captureGame = new GameScene();
        string path = Path.Combine(Path.GetTempPath(), "zircon-binding-visual-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{broken");
        Set(_captureGame, "_handheldActionPath", path);
        var config = HandheldActionConfiguration.CreateDefault();
        config.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.OpenMap);
        config.Pages[0].Bindings[HandheldButton.Y] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.Pages[1].Bindings[HandheldButton.Up] = ActionBinding.Menu(MenuAction.OpenSystem);
        config.FixedSlots["L2tap"] = ActionBinding.Menu(MenuAction.OpenInventory);
        config.FixedSlots["L2hold"] = ActionBinding.Menu(MenuAction.OpenMap);
        config.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenQuests);
        Invoke(_captureGame, "ActivateHandheldActionConfiguration", config);
        _captureUi = (HandheldActionBindings)Activator.CreateInstance(type, new object[] { _captureGame });
        tree.GetRoot().AddChild(_captureUi);
        _captureUi.Open();
        for (int i = 0; i < VisualStates.Length; i++)
        {
            AssertVisualState(i);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = tree.GetRoot().GetTexture().GetImage();
            string screenshot = Path.Combine(_visualDirectory, VisualStates[i].Name + ".png");
            Error error = image.SavePng(screenshot);
            Check(error == Error.Ok && new FileInfo(screenshot).Length > 0, "rendered control captured: " + VisualStates[i].Name);
            foreach (var key in VisualStates[i].Keys) _captureUi.HandleKey(key);
        }
        _captureUi.Close();
        _captureUi.Free(); _captureGame.Free();
        File.Delete(path);
    }

    public override async void _Initialize()
    {
        try
        {
            var type=typeof(HandheldHud).Assembly.GetType("ZirconClient.Controls.HandheldActionBindings");
            Check(type!=null,"binding screen exists separately from the journal");
            var bounds=type.GetMethod("CardBounds",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            Check(bounds!=null,"binding screen exposes six selectable positions");
            var boxes=(Rect2[])bounds.Invoke(null,null);
            Check(boxes.Length==6,"two-by-three cards show six positions per tab");
            var viewport=new Rect2(0,0,640,360);
            for(int i=0;i<boxes.Length;i++)
            {
                Check(viewport.Encloses(boxes[i]),"binding card is inside handheld viewport");
                for(int j=i+1;j<boxes.Length;j++)Check(!boxes[i].Intersects(boxes[j]),"binding cards do not overlap");
            }
            Check(typeof(HandheldHud).GetMethod("ActionSlotBounds",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!=null,"HUD maps all six direct inputs to visible positions");
            CheckEditor(type);
            await CaptureVisualStates(this, type);
            Quit(0);
        }
        catch(Exception e){GD.PrintErr(e);Quit(1);}
    }
}
