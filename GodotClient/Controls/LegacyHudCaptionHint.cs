using Godot;

namespace ZirconClient.Controls;

/// <summary>Cursor anchored EI caption box. Clipping the text child reproduces the original reveal region.</summary>
public partial class LegacyHudCaptionHint : DXControl
{
    public DXLabel TextLabel { get; }

    public LegacyHudCaptionHint()
    {
        Clip = true;
        IsControl = false;
        PassThrough = true;
        TextLabel = new DXLabel
        {
            Name = "Text",
            FontSize = 12,
            TextColour = Colors.Black,
            Align = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            AutoSize = false,
            IsControl = false,
            PassThrough = true,
        };
        AddControl(TextLabel);
    }

    protected override void DrawControl()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 150f / 255f));
        // Keep the frame one pixel inside the clip region: zero-origin fills
        // were lost by the nested Control clip while the trailing edges survived.
        if (Size.X >= 4 && Size.Y >= 4)
        {
            DrawRect(new Rect2(1, 1, Size.X - 2, 1), Colors.Black);
            DrawRect(new Rect2(1, Size.Y - 2, Size.X - 2, 1), Colors.Black);
            DrawRect(new Rect2(1, 1, 1, Size.Y - 2), Colors.Black);
            DrawRect(new Rect2(Size.X - 2, 1, 1, Size.Y - 2), Colors.Black);
        }
    }
}
