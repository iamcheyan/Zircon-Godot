using System;
using System.Reflection;
using System.IO;
using ZirconClient.HandheldActions;
using Godot;
using Library;
using ZirconClient.Scripts;

// ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/HandheldActionIntegrationChecks.cs
public partial class HandheldActionIntegrationChecks : SceneTree
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Invoke(GameScene game, string name, params object[] args) =>
        typeof(GameScene).GetMethod(name, Instance).Invoke(game, args);
    private static void Set(GameScene game, string name, object value) =>
        typeof(GameScene).GetField(name, Instance).SetValue(game, value);

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    private static void CheckPersistence(GameScene game)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        if (AutoLoginArgs.Handheld)
        {
            Set(game, "_startGameShown", true);
            int firstIndex = Random.Shared.Next(100_000_000, int.MaxValue - 1);
            game.StartInfo = new StartInformation { Index = firstIndex, Class = MirClass.Wizard };
            Invoke(game, "EnsureHandheldActionConfiguration");
            string first = (string)typeof(GameScene).GetField("_handheldActionPath", Instance).GetValue(game);
            Check(!File.Exists(first), "configuration fixture does not replace an existing character file");
            try
            {
                var draft = game.CloneHandheldActionConfig();
                draft.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.FirstPage);
                Check(game.SaveHandheldActionBindings(draft) == null, "first character saves a binding through GameScene");
                game.StartInfo = new StartInformation { Index = firstIndex + 1, Class = MirClass.Wizard };
                Check(game.CloneHandheldActionConfig().Pages[0].Bindings[HandheldButton.X].Kind == ActionKind.Empty,
                    "another character does not inherit the saved binding");
                var reopened = new GameScene { StartInfo = new StartInformation { Index = firstIndex, Class = MirClass.Wizard } };
                try
                {
                    Set(reopened, "_startGameShown", true);
                    Check(reopened.CloneHandheldActionConfig().Pages[0].Bindings[HandheldButton.X].MenuAction == MenuAction.FirstPage,
                        "a fresh GameScene reloads the first character's saved binding");
                }
                finally { reopened.Free(); }
            }
            finally
            {
                File.Delete(first);
                game.StartInfo = null;
                Set(game, "_startGameShown", false);
            }
        }
        var makeDefaults = typeof(GameScene).GetMethod("CreateHandheldActionDefaults", Instance);
        Check(makeDefaults != null, "GameScene supplies per-character action defaults");
        var config = (HandheldActionConfiguration)makeDefaults.Invoke(game, null);
        Check(config.FixedSlots["L1+R1"].MenuAction == MenuAction.OpenMap, "default shoulder chord opens map");
        var useType = typeof(GameScene).GetMethod("IsHandheldUsableItemType", flags);
        bool Allows(ItemType type) => (bool)useType.Invoke(null, new object[] { type });
        Check(Allows(ItemType.Consumable) && Allows(ItemType.Scroll) && !Allows(ItemType.Weapon) && !Allows(ItemType.System),
            "action bindings only allow explicitly supported usable types");
        string path = Path.Combine(Path.GetTempPath(), $"zircon-action-integration-{Guid.NewGuid():N}.json");
        try
        {
            Set(game, "_handheldActionPath", path);
            Invoke(game, "ActivateHandheldActionConfiguration", config);
            var draft = (HandheldActionConfiguration)Invoke(game, "CloneHandheldActionConfig");
            draft.Pages[0].Bindings[HandheldButton.X] = ActionBinding.Menu(MenuAction.FirstPage);
            var saveDraft = typeof(GameScene).GetMethod("SaveHandheldActionBindings", Instance, null,
                new[] { typeof(HandheldActionConfiguration) }, null);
            Check(saveDraft.Invoke(game, new object[] { draft }) == null, "valid draft is persisted");
            draft.Pages[0].Bindings[HandheldButton.X].MenuAction = MenuAction.OpenSystem;
            draft.LongPressThresholdMs = 1;
            Check(saveDraft.Invoke(game, new object[] { draft }) is string, "invalid draft reports a save error");
            var current = (HandheldActionConfiguration)Invoke(game, "CloneHandheldActionConfig");
            Check(current.Pages[0].Bindings[HandheldButton.X].MenuAction == MenuAction.FirstPage,
                "invalid draft leaves the last effective binding intact");
            File.WriteAllText(path, "{broken");
            current.FixedSlots["L1+R1"] = ActionBinding.Menu(MenuAction.OpenSkills);
            Check(saveDraft.Invoke(game, new object[] { current }) is string && File.ReadAllText(path) == "{broken",
                "save refuses to overwrite a damaged existing file");
        }
        finally { File.Delete(path); }
    }

    private static void CheckPhysicalKeys(GameScene game)
    {
        bool Consumed(Key code, bool pressed, bool echo = false)
        {
            using var key = new InputEventKey { Keycode = code, Pressed = pressed, Echo = echo };
            return (bool)Invoke(game, "HandleHandheldInputEvent", key);
        }
        Check(!Consumed(Key.F2, true), "the physical F2 key is not the handheld menu action");
        foreach (var key in new[] { Key.Space, Key.F16, Key.Up, Key.Down, Key.Left, Key.Right,
                     Key.Q, Key.E, Key.Key1, Key.Key2, Key.Enter, Key.Escape })
            Check(Consumed(key, true) && Consumed(key, true, true) && Consumed(key, false),
                $"{key} press, repeat and release cannot leak to desktop shortcuts");
    }

    public override void _Initialize()
    {
        var game = new GameScene();
        try
        {
            OS.SetEnvironment("ZIRCON_HANDHELD_POINTER", "1");
            Set(game, "_handheldHudMode", false);
            Invoke(game, "UpdateHandheldPointerMode");
            Check(!game.HandheldPointerMode && OS.GetEnvironment("ZIRCON_HANDHELD_POINTER") == "0",
                "handheld disables the native pointer even in the legacy HUD");
            var position = new System.Drawing.Point(10, 10);
            Set(game, "_playerLocation", position);
            Set(game, "_playerDirection", MirDirection.Up);
            Check((System.Drawing.Point)Invoke(game, "HandheldMagicCell", new System.Drawing.Point(99, 99))
                == Functions.Move(position, MirDirection.Up, 3), "either HUD casts toward world aim regardless of pointer position");
            CheckPersistence(game);
            CheckPhysicalKeys(game);
            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
        finally { game.Free(); }
    }
}
