using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using ZirconClient.Controls;
using ZirconClient.Scripts;

// godot-mono --headless --path GodotClient --script res://Tests/HandheldPromptChecks.cs -- --handheld
public partial class HandheldPromptChecks : SceneTree
{
    private readonly List<DXWindow> _prompts = new();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public override async void _Initialize()
    {
        SubViewport viewport = null;
        int exitCode = 0;
        try
        {
            await ToSignal(this, SignalName.ProcessFrame);
            Check(AutoLoginArgs.Handheld, "Run with -- --handheld");
            viewport = new SubViewport { Size = new Vector2I(640, 360) };
            Root.AddChild(viewport);
            var layer = new CanvasLayer { Transform = Transform2D.Identity.Scaled(Vector2.One * .8f) };
            viewport.AddChild(layer);
            CheckAmounts(layer);
            CheckConfirmations(layer);
            CheckGameInputWithoutService(layer);
            GD.Print("PASS: handheld prompt bounds, input, stale context and one-shot cancellation");
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            exitCode = 1;
        }
        foreach (var prompt in _prompts)
        {
            if (prompt.Visible) WindowManager.Close(prompt);
            prompt.QueueFree();
        }
        viewport?.QueueFree();
        await ToSignal(this, SignalName.ProcessFrame);
        Quit(exitCode);
    }

    private void CheckAmounts(CanvasLayer layer)
    {
        long sent = -1;
        int calls = 0;
        var amount = new ItemAmountDialog("Buy potions", 25, 1, value => { sent = value; calls++; });
        Open(amount, layer);
        Check(amount.AmountBox == null && layer.GetViewport().GuiGetFocusOwner() == null, "No native text focus on handheld");
        Check(amount.HandleHandheldKey(Key.Left) && amount.Amount == 0 && !amount.OkEnabled, "Left decrements to zero and disables OK");
        Check(amount.HandleHandheldKey(Key.Enter) && amount.Visible && calls == 0, "Zero cannot be confirmed");
        Check(amount.HandleHandheldKey(Key.Right) && amount.Amount == 1, "Right increments by one");
        Check(amount.HandleHandheldKey(Key.Up) && amount.Amount == 6, "Up adds the original step");
        Check(amount.HandleHandheldKey(Key.Down) && amount.Amount == 1, "Down subtracts the original step");
        amount.ApplyText("bad amount");
        Check(amount.Amount == 0 && !amount.OkEnabled, "Invalid text falls back to zero");
        amount.ApplyText("999");
        Check(amount.Amount == 25 && amount.OkEnabled, "Amount clamps to the configured maximum");
        amount.HandleHandheldKey(Key.Enter);
        amount.HandleHandheldKey(Key.Enter);
        Buttons(amount)[0].EmitSignal(Button.SignalName.Pressed);
        Check(sent == 25 && calls == 1 && Closed(amount), "Only one quantity callback, even with repeated activation");

        var large = new ItemAmountDialog("Currency", long.MaxValue, long.MaxValue - 1, _ => calls++);
        Open(large, layer);
        Check(large.HandleHandheldKey(Key.Up) && large.Amount == long.MaxValue, "Up saturates without overflowing");
        large.HandleHandheldKey(Key.Escape);
        Check(calls == 1 && Closed(large), "B cancels quantity");

        bool current = true;
        var stale = new ItemAmountDialog("Buy potions", 25, 1, _ => calls++) { ContextValid = () => current };
        Open(stale, layer);
        current = false;
        stale.HandleHandheldKey(Key.Enter);
        Check(calls == 1 && Closed(stale), "Stale amount closes without invoking its callback");

        var cancelled = new ItemAmountDialog("Buy potions", 25, 1, _ => calls++);
        Open(cancelled, layer);
        WindowManager.Close(cancelled);
        Buttons(cancelled)[0].EmitSignal(Button.SignalName.Pressed);
        Check(calls == 1 && Closed(cancelled), "WindowManager cancellation blocks queued quantity activation");
    }

    private void CheckConfirmations(CanvasLayer layer)
    {
        int calls = 0;
        var safeDefault = new ConfirmDialog("Sell the selected items?", "Sell", () => calls++);
        Open(safeDefault, layer);
        Check(safeDefault.HandleHandheldKey(Key.Enter) && calls == 0 && Closed(safeDefault), "A defaults to Cancel");

        var confirmation = new ConfirmDialog("Sell the selected items?", "Sell", () => calls++);
        Open(confirmation, layer);
        Check(confirmation.HandleHandheldKey(Key.Left), "Left selects Confirm");
        confirmation.HandleHandheldKey(Key.KpEnter);
        confirmation.HandleHandheldKey(Key.Enter);
        Buttons(confirmation)[0].EmitSignal(Button.SignalName.Pressed);
        Check(calls == 1 && Closed(confirmation), "Confirmation runs exactly once");

        var right = new ConfirmDialog("Sell?", "Sell", () => calls++);
        Open(right, layer);
        right.HandleHandheldKey(Key.Left);
        right.HandleHandheldKey(Key.Right);
        right.HandleHandheldKey(Key.Enter);
        Check(calls == 1 && Closed(right), "Right selects Cancel again");

        var back = new ConfirmDialog("Sell?", "Sell", () => calls++);
        Open(back, layer);
        back.HandleHandheldKey(Key.Left);
        back.HandleHandheldKey(Key.Escape);
        Check(calls == 1 && Closed(back), "B cancels even when Confirm is selected");

        var stale = new ConfirmDialog("Sell?", "Sell", () => calls++) { ContextValid = () => false };
        Open(stale, layer);
        Buttons(stale)[0].EmitSignal(Button.SignalName.Pressed);
        Check(calls == 1 && Closed(stale), "Stale pointer confirmation also closes without callback");

        var cancelled = new ConfirmDialog("Sell?", "Sell", () => calls++);
        Open(cancelled, layer);
        WindowManager.Close(cancelled);
        Buttons(cancelled)[0].EmitSignal(Button.SignalName.Pressed);
        Check(calls == 1 && Closed(cancelled), "WindowManager cancellation blocks queued Confirm activation");
    }

    private void CheckGameInputWithoutService(CanvasLayer layer)
    {
        var game = new GameScene();
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(GameScene).GetField("_uiLayer", flags).SetValue(game, layer);
            var handleInput = typeof(GameScene).GetMethod("HandleHandheldInputEvent", flags);
            bool KeyEvent(Key code, bool pressed = true, bool echo = false)
            {
                using var input = new InputEventKey { Keycode = code, Pressed = pressed, Echo = echo };
                return (bool)handleInput.Invoke(game, new object[] { input });
            }

            long value = 0;
            var amount = new ItemAmountDialog("Split stack", 25, 1, count => value = count);
            Open(amount, layer);
            Check(KeyEvent(Key.Right) && amount.Amount == 2, "GameScene routes Right to a non-NPC quantity prompt");
            Check(KeyEvent(Key.Right, echo: true) && amount.Amount == 2, "quantity prompt consumes key repeats without another adjustment");
            Check(KeyEvent(Key.Enter) && Closed(amount) && value == 2, "GameScene A confirms the quantity without a mouse");
            Check(KeyEvent(Key.Enter, pressed: false), "confirm release cannot become a world action");

            int calls = 0;
            var confirm = new ConfirmDialog("Drop item?", "Drop", () => calls++);
            Open(confirm, layer);
            Check(KeyEvent(Key.Left) && KeyEvent(Key.Escape) && Closed(confirm) && calls == 0,
                "GameScene B cancels the selected confirmation without executing it");
        }
        finally { game.Free(); }
    }

    private static Button[] Buttons(DXWindow window) => window.GetChild(0).GetChildren().OfType<Button>().ToArray();

    private static bool Closed(DXWindow window) => !window.Visible && !WindowManager.OpenWindows.Contains(window);

    private void Open(DXWindow window, CanvasLayer layer)
    {
        _prompts.Add(window);
        WindowManager.Open(window, layer);
        Transform2D transform = window.GetGlobalTransformWithCanvas();
        var bounds = new Rect2(transform * Vector2.Zero, transform * window.Size - transform * Vector2.Zero);
        Check(new Rect2(0, 0, 640, 360).Encloses(bounds) && bounds.GetCenter().IsEqualApprox(new Vector2(320, 180)),
            $"Prompt stays centered inside 640×360 on a scaled HUD: {bounds}");
        Check(!window.DrawChrome && window.DefaultCloseButton == null && Buttons(window).All(b => b.FocusMode == Control.FocusModeEnum.None),
            "Uses custom chrome and focus-free buttons");
    }
}
