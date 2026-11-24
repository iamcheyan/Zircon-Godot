using System;
using System.Reflection;
using Godot;
using ZirconClient.Controls;

// No server, save, or OS input. Verifies the configurable F2 action layout.
public partial class HandheldQuickLayoutChecks : SceneTree
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    public override void _Initialize()
    {
        try
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var boundsMethod = typeof(HandheldHud).GetMethod("ActionSlotBounds", flags, null, new[] { typeof(Vector2) }, null);
            Check(boundsMethod != null, "F2 exposes six bound actions instead of the retired selection wheel");
            foreach (var size in new[] { new Vector2(640, 360), new Vector2(480, 320) })
            {
                var viewport = new Rect2(Vector2.Zero, size);
                var slots = (Rect2[])boundsMethod.Invoke(null, new object[] { size });
                Check(slots.Length == 6, "both action pages use the same six physical positions");
                foreach (var slot in slots) Check(viewport.Encloses(slot), "action card fits the handheld viewport");
                Check(slots[2].Position.Y < slots[3].Position.Y
                    && slots[4].Position.X < slots[2].Position.X
                    && slots[5].Position.X > slots[2].Position.X
                    && slots[0].Position.X == slots[1].Position.X
                    && slots[0].Position.X > slots[2].Position.X,
                    "D-pad forms a cross beside vertically stacked X/Y cards");
            }
            Quit(0);
        }
        catch (Exception error) { GD.PrintErr(error); Quit(1); }
    }
}
