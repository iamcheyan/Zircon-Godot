using System;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.Scripts;

// Run without a game server or save:
// ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/HandheldInputChecks.cs
public partial class HandheldInputChecks : SceneTree
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Field(object target, string field, object value)
        => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    public override void _Initialize()
    {
        try
        {
            var game = new GameScene();
            var layer = new CanvasLayer();
            Field(game, "_uiLayer", layer);
            var chat = new ChatTextBox { Visible = true };
            layer.AddChild(chat);
            Field(game, "_chatTextBox", chat);
            Check(!(bool)Call(game, "HandheldHasModalWindow"), "permanent chat HUD does not block gameplay");
            var dialog = new ProbeDialog { Visible = true };
            layer.AddChild(dialog);
            Check((bool)Call(game, "HandheldHasModalWindow"), "visible dialog blocks world input");
            dialog.Visible = false;
            Check(!(bool)Call(game, "HandheldHasModalWindow"), "closing dialog restores world input");
            Field(game, "_handheldHudMode", false);
            Field(game, "_handheldPointerMode", true);
            Check(!game.HandheldCombatBlocked, "default HUD pointer alone does not block combat");
            var selector = new HandheldQuickSelect(game);
            Field(game, "_handheldQuickSelect", selector);
            selector.OpenBelt();
            Call(game, "UpdateHandheldPointerMode");
            Check(!game.HandheldHudMode && !game.HandheldPointerMode,
                "opening selector preserves default HUD aim mode while suppressing pointer");
            var fallbackCell = new System.Drawing.Point(99, 99);
            Check((System.Drawing.Point)Call(game, "HandheldMagicCell", fallbackCell) == fallbackCell,
                "default HUD selector confirmation keeps pointer ground aim");
            selector.HandleKey(Key.Escape);
            Field(game, "_handheldHudMode", true);
            selector.OpenBelt();
            Call(game, "UpdateHandheldPointerMode");
            Check(game.HandheldHudMode && !game.HandheldPointerMode,
                "opening selector preserves compact HUD aim mode while suppressing pointer");
            Field(game, "_playerLocation", new System.Drawing.Point(10, 10));
            Field(game, "_playerDirection", MirDirection.Up);
            Check((System.Drawing.Point)Call(game, "HandheldMagicCell", fallbackCell)
                    == Functions.Move(new System.Drawing.Point(10, 10), MirDirection.Up, 3),
                "compact HUD selector confirmation uses world aim");
            selector.Move(7);
            Check(selector.SelectedIndex == 7 && selector.IsOpen, "belt page two accepts slot eight without a bounds error");
            selector.Move(3);
            Check(selector.SelectedIndex == 0, "belt navigation wraps all ten slots");
            selector.HandleKey(Key.Escape);
            Check(!selector.IsOpen, "B closes selector without a server or item operation");
            Check(!(bool)Call(game, "HandheldHasModalWindow"), "selector close leaves HUD non-modal");
            selector.Free();
            layer.Free();
            game.Free();
            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
    }

    private partial class ProbeDialog : DXWindow { }
}
