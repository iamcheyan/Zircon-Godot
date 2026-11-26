using System;
using System.Linq;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Controls;
using ZirconClient.Scripts;

/// <summary>Handheld HUD experience bar: thin yellow meter under mana, showing the current share.</summary>
public partial class HandheldExpBarChecks : SceneTree
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); GD.Print("PASS: " + text); }

    public override async void _Initialize()
    {
        try
        {
            var bounds = typeof(HandheldHud).GetMethod("ExperienceBounds", Flags)!;
            var fraction = typeof(HandheldHud).GetMethod("ExperienceFraction", Flags)!;
            foreach (var size in new[] { new Vector2(640, 360), new Vector2(480, 320), new Vector2(1280, 720) })
            {
                var layout = (Rect2[])typeof(HandheldHud).GetMethod("LayoutForViewport", Flags)!.Invoke(null, new object[] { size })!;
                var meter = (Rect2)bounds.Invoke(null, new object[] { size })!;
                Check(new Rect2(Vector2.Zero, size).Encloses(meter), $"experience bar fits {size}");
                Check(meter.Position.Y > layout[1].Position.Y && meter.Position.X >= layout[1].Position.X,
                    "experience bar sits under the mana bar");
                Check(!meter.Intersects(layout[2]) && !meter.Intersects(layout[3]) && !meter.Intersects(layout[4]),
                    "experience bar does not overlap the map, belt or actions");
            }
            Check((float)fraction.Invoke(null, new object[] { 250m, 1000m })! == .25f, "25% experience fills a quarter");
            Check((float)fraction.Invoke(null, new object[] { 5m, 0m })! == 1f, "no experience table is shown as full/MAX");
            Check((float)fraction.Invoke(null, new object[] { 2000m, 1000m })! == 1f, "overflow is clamped");
            Check((float)fraction.Invoke(null, new object[] { -5m, 1000m })! == 0f, "negative is clamped");

            var viewport = new SubViewport
            {
                Size = new Vector2I(640, 360),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            Root.AddChild(viewport);
            var game = new GameScene();
            void Set(string field, object value) => typeof(GameScene).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, value);
            Set("_currentHP", 9000);
            Set("_currentMP", 320);
            Set("_playerExperience", 246800m);
            Set("_playerMaxExperience", 1000000m);
            game.PlayerStats[Stat.Health] = 13000;
            game.PlayerStats[Stat.Mana] = 1490;
            var hud = new HandheldHud(game);
            viewport.AddChild(hud);
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = viewport.GetTexture().GetImage();
            Check(image.GetWidth() == 640 && image.GetHeight() == 360, "captured the handheld HUD at 640x360");
            var exp = (Rect2)bounds.Invoke(null, new object[] { new Vector2(640, 360) })!;
            var pixel = image.GetPixel((int)(exp.Position.X + 20), (int)(exp.Position.Y + 2));
            Check(pixel.R > .5f && pixel.G > .4f && pixel.B < .45f, $"experience bar renders yellow at its start (got {pixel})");
            var empty = image.GetPixel((int)(exp.End.X - 3), (int)(exp.Position.Y + 2));
            Check(empty.B < .3f, $"unfilled experience stays dark (got {empty})");
            string path = OS.GetEnvironment("ZIRCON_EXP_BAR_CAPTURE");
            if (!string.IsNullOrWhiteSpace(path)) Check(image.SavePng(path) == Error.Ok, "saved experience bar screenshot");
            hud.QueueFree(); game.Free(); viewport.QueueFree();
            Quit(0);
        }
        catch (Exception e) { GD.PrintErr(e); Quit(1); }
    }
}
