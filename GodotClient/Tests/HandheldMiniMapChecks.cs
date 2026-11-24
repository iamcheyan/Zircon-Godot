using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Library;
using Library.SystemModels;
using ZirconClient.Controls;
using ZirconClient.Scripts;

// godot-mono --headless --path GodotClient --script res://Tests/HandheldMiniMapChecks.cs
// To also test shader output:
// xvfb-run -a godot-mono --audio-driver Dummy --rendering-method gl_compatibility --path GodotClient --script res://Tests/HandheldMiniMapChecks.cs
public partial class HandheldMiniMapChecks : SceneTree
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        GD.Print("PASS: " + message);
    }

    private static T Field<T>(Type type, string name, object instance = null) =>
        (T)type.GetField(name, BindingFlags.NonPublic | (instance == null ? BindingFlags.Static : BindingFlags.Instance))!
            .GetValue(instance);

    public override async void _Initialize()
    {
        SubViewport viewport = null;
        MiniMapDialog source = null;
        Dictionary<(LibraryFile, int), Texture2D> textures = null;
        ImageTexture texture = null, exitTexture = null;
        const int fixtureFrame = 123456789;
        try
        {
            // Resolve by name so the pre-implementation test compiles and fails at runtime.
            var type = typeof(MiniMapDialog).Assembly.GetType("ZirconClient.Controls.HandheldMiniMap");
            Check(type != null && typeof(Control).IsAssignableFrom(type), "handheld minimap is available as a Control");
            var minimap = (Control)Activator.CreateInstance(type)!;
            viewport = new SubViewport
            {
                Size = new Vector2I(82, 82), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            viewport.AddChild(minimap);
            await ToSignal(this, SignalName.ProcessFrame);
            Root.AddChild(viewport);

            Check(minimap.Size == new Vector2(82, 82), "default HUD size is 82x82");
            var mapRect = minimap.GetChildren().OfType<TextureRect>().Single();
            Check(minimap.MouseFilter == Control.MouseFilterEnum.Ignore && minimap.FocusMode == Control.FocusModeEnum.None
                && minimap.GetChildren().OfType<Control>().All(child => child.MouseFilter == Control.MouseFilterEnum.Ignore),
                "minimap and its children do not intercept input");
            Check(!mapRect.Visible && mapRect.Texture == null, "no source does not invent a map texture");
            Check(mapRect.Material is ShaderMaterial, "map TextureRect has a canvas shader");
            var material = mapRect.Material;
            int children = minimap.GetChildCount();

            bool canReadPixels = DisplayServer.GetName() != "headless";
            if (canReadPixels)
            {
                await NextUpdate();
                using var empty = await ReadFrame(viewport);
                Check(empty.GetPixel(2, 2).A < 0.05f && empty.GetPixel(41, 1).A > 0.1f
                    && empty.GetPixel(41, 41).A > 0.1f && empty.GetPixel(41, 10).A < 0.05f,
                    "empty display has only a ring and a center dash, without north or terrain");
            }

            // Seed the existing texture cache so assertions do not depend on installed game assets.
            textures = Field<Dictionary<(LibraryFile, int), Texture2D>>(typeof(MirSkin), "_textures");
            using var pixels = Image.CreateEmpty(100, 90, false, Image.Format.Rgba8);
            var terrain = new Color(0.25f, 0.1f, 0.6f);
            var stripe = new Color(0.1f, 0.4f, 0.6f);
            pixels.Fill(terrain);
            pixels.FillRect(new Rect2I(60, 0, 40, 90), stripe);
            texture = ImageTexture.CreateFromImage(pixels);
            textures.Add((LibraryFile.MiniMap, fixtureFrame), texture);
            using var iconPixels = Image.CreateEmpty(6, 6, false, Image.Format.Rgba8);
            iconPixels.Fill(Colors.White);
            exitTexture = ImageTexture.CreateFromImage(iconPixels);
            textures.Add((LibraryFile.MiniMapIcon, fixtureFrame), exitTexture);
            source = new MiniMapDialog { Visible = false };
            source.Image.FixedSize = true;
            source.Image.Size = new Vector2(100, 90);
            // Detached MirDB records cannot use setters that notify a collection.
            var map = new MapInfo();
            typeof(MapInfo).GetField("_MiniMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(map, fixtureFrame);
            typeof(MapInfo).GetProperty(nameof(MapInfo.Index))!.SetValue(map, fixtureFrame);
            source.SetMap(map, 100, 90, 1);
            source.UpdatePlayer(50, 45);
            source.UpdateObject(2, 50, 65, ObjectRenderer.Kind.Monster);
            var staticMarkers = Field<List<Node>>(typeof(MiniMapDialog), "_staticMarkers", source);
            var npc = MapMarkerFactory.CreateNpcMarker(new NPCInfo { MapIcon = MapIcon.None });
            npc.Position = new Vector2(28, 44);
            source.Image.AddControl(npc);
            staticMarkers.Add(npc);
            var exit = new DXImageControl { LibraryFile = LibraryFile.MiniMapIcon, FixedSize = true, Size = new Vector2(6, 6) };
            MiniMapDialog.UpdateMapIcon(exit, MapIcon.Exit);
            exit.Index = fixtureFrame;
            // Project the exit to (13, 13), so its sprite straddles the rim.
            exit.Position = new Vector2(19, 14);
            source.Image.AddControl(exit);
            staticMarkers.Add(exit);

            type.GetProperty("Source")!.SetValue(minimap, source);
            type.GetProperty("PlayerDirection")!.SetValue(minimap, MirDirection.Right);
            var desktopPosition = source.Image.Position;
            await NextUpdate();
            var shader = (ShaderMaterial)material;
            Check(shader.GetShaderParameter("map_center").AsVector2() == new Vector2(50, 45),
                "shader uses source player coordinates, not the desktop dialog offset");
            Check(mapRect.Visible && mapRect.Texture == texture && !source.Visible,
                "HUD shares the existing texture even when the desktop dialog is hidden");

            if (canReadPixels)
            {
                using var frame = await ReadFrame(viewport);
                Check(IsColour(frame.GetPixel(41, 52), terrain) && IsColour(frame.GetPixel(53, 41), stripe)
                    && frame.GetPixel(3, 3).A < 0.05f,
                    "terrain is player-centered at native scale and clipped to the circle");
                Check(Enumerable.Range(38, 7).Any(x => Enumerable.Range(4, 9).Any(y => frame.GetPixel(x, y).R > 0.6f)),
                    "north indicator is visible at the top of the map");
                Check(frame.GetPixel(45, 41).R > 0.75f && frame.GetPixel(41, 34).R < 0.6f,
                    "player triangle points right without rotating terrain");
                Check(IsColour(frame.GetPixel(41, 61), Colors.Red) && IsColour(frame.GetPixel(21, 41), Colors.Yellow)
                    && IsColour(frame.GetPixel(14, 15), Colors.Green),
                    "object, NPC and exit marks appear relative to the centered player");
                Check(frame.GetPixel(11, 11).A < 0.05f, "exit sprite crossing the rim cannot draw outside the circle");
            }
            Check(source.Image.Position == desktopPosition, "HUD does not move or modify the desktop source");

            source.RemoveObject(2);
            source.UpdatePlayer(3, 45);
            type.GetProperty("PlayerDirection")!.SetValue(minimap, MirDirection.Down);
            await NextUpdate();
            Check(mapRect.Texture == texture && mapRect.Material == material && minimap.GetChildCount() == children,
                "player movement and rotation reuse the texture, material and nodes");
            Check(shader.GetShaderParameter("map_center").AsVector2() == new Vector2(3, 45),
                "shader stays player-centered at the edge of the map");
            if (canReadPixels)
            {
                using var frame = await ReadFrame(viewport);
                Check(frame.GetPixel(20, 41).A < 0.05f && IsColour(frame.GetPixel(55, 41), terrain),
                    "map edge discards out-of-range UVs instead of smearing or wrapping terrain");
                Check(frame.GetPixel(41, 45).R > 0.75f, "triangle follows a change in player direction");
            }

            source.SetMap(new MapInfo(), 100, 90, 1);
            await NextUpdate();
            Check(!mapRect.Visible && mapRect.Texture == null, "switching to a map without a minimap clears the old texture");
            if (canReadPixels)
            {
                using var frame = await ReadFrame(viewport);
                Check(frame.GetPixel(41, 10).A < 0.05f && frame.GetPixel(14, 15).A < 0.05f
                    && frame.GetPixel(41, 41).A > 0.1f && frame.GetPixel(41, 45).A < 0.05f,
                    "missing map also clears north, markers and arrow and restores the dash");
            }
            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
        finally
        {
            viewport?.Free();
            source?.Free();
            if (texture != null)
            {
                textures.Remove((LibraryFile.MiniMap, fixtureFrame));
                texture.Dispose();
            }
            if (exitTexture != null)
            {
                textures.Remove((LibraryFile.MiniMapIcon, fixtureFrame));
                exitTexture.Dispose();
            }
        }
    }

    private async Task NextUpdate()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
    }

    private async Task<Image> ReadFrame(SubViewport viewport)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return viewport.GetTexture().GetImage();
    }

    private static bool IsColour(Color actual, Color expected) => actual.A > 0.95f
        && Mathf.Abs(actual.R - expected.R) < 0.03f && Mathf.Abs(actual.G - expected.G) < 0.03f
        && Mathf.Abs(actual.B - expected.B) < 0.03f;
}
