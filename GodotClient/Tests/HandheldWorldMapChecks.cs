using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Library;
using Library.Network;
using ZirconClient.Controls;
using ZirconClient.Formats;
using ZirconClient.Network;
using ZirconClient.Scripts;

public partial class HandheldWorldMapChecks : SceneTree
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        GD.Print("PASS: " + description);
    }

    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        GameScene game = null;
        NetworkManager network = null;
        PlayerRenderer player = null;
        MiniMapDialog minimap = null;
        try
        {
            var type = typeof(GameScene).Assembly.GetType("ZirconClient.Controls.HandheldWorldMap");
            Check(type != null, "Select has a dedicated full-screen handheld map");
            var layout = type.GetMethod("FitMap", BindingFlags.Static | BindingFlags.NonPublic);
            Rect2 Fit(float width, float height) => (Rect2)layout.Invoke(null, new object[] { new Vector2(640, 296), new Vector2(width, height) });
            var fullMap = new Rect2(0, 0, 640, 296);
            Check(Fit(1000, 300) == fullMap && Fit(500, 500) == fullMap && Fit(300, 900) == fullMap,
                "wide, square and tall textures occupy the entire map area from the top-left corner");
            Check(Fit(0, 0) == default, "missing map has no terrain bounds");

            game = new GameScene();
            game.SetProcess(false);
            game.SetProcessInput(false);
            network = new NetworkManager();
            var connection = (ServerConnection)RuntimeHelpers.GetUninitializedObject(typeof(ServerConnection));
            connection.Connected = true;
            connection.SendList = new ConcurrentQueue<Packet>();
            Set(network, "<Connection>k__BackingField", connection);
            Set(game, "_net", network);
            Set(game, "_startGameShown", true);
            player = new PlayerRenderer();
            Set(game, "_player", player);
            minimap = new MiniMapDialog();
            Set(game, "_miniMap", minimap);
            Set(game, "_handheldAutoRun", true);
            string capture = OS.GetEnvironment("ZIRCON_HANDHELD_MAP_CAPTURE");
            SubViewport viewport = null;
            if (!string.IsNullOrEmpty(capture))
            {
                viewport = new SubViewport { Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                Root.AddChild(viewport);
                var map = Globals.MapInfoList.Binding.First(x => x.MiniMap > 0 && x.FileName == "0");
                int width = 1000, height = 1000;
                if (MirMap.TryGetDimensions(System.IO.Path.Combine(OS.GetEnvironment("MIR3_EI_ROOT"), "Map", map.FileName + ".map"), out int w, out int h))
                    (width, height) = (w, h);
                minimap.SetMap(map, width, height, 1);
                minimap.UpdatePlayer(width * 4 / 10, height * 5 / 10);
                Set(game, "_playerMapIndex", map.Index);
                Set(game, "_playerLocation", new System.Drawing.Point(width * 4 / 10, height * 5 / 10));
                Set(game, "_playerDirection", MirDirection.Right);
                Set(game, "_currentHP", 1280);
                Set(game, "_currentMP", 272);
                Set(game, "_playerLevel", 255);
                game.StartInfo = new StartInformation { Name = "TestHero", Class = MirClass.Warrior };
                game.PlayerStats[Stat.Health] = 1560;
                game.PlayerStats[Stat.Mana] = 360;
                Set(game, "_playerPoison", PoisonType.Green | PoisonType.Slow);
                var buffs = (Dictionary<int, ClientBuffInfo>)typeof(GameScene).GetField("_buffs", Private).GetValue(game);
                buffs[1] = new ClientBuffInfo { Type = BuffType.Veteran };
                buffs[2] = new ClientBuffInfo { Type = BuffType.ItemBuffPermanent };
            }

            bool KeyEvent(Key key, bool pressed, double now) => (bool)Act("RouteHandheldInputKey", new InputEventKey { Keycode = key, Pressed = pressed }, false, now);
            bool Open() => (bool)typeof(GameScene).GetProperty("HandheldWorldMapOpen", Private).GetValue(game);
            KeyEvent(Key.F15, true, 100);
            Check(!Open(), "Select waits for release to distinguish a hold");
            KeyEvent(Key.F15, false, 800);
            Check(!Open(), "a long press never opens the map");
            KeyEvent(Key.F15, true, 900);
            KeyEvent(Key.F15, false, 960);
            Check(Open() && !(bool)typeof(GameScene).GetField("_handheldAutoRun", Private).GetValue(game), "Select tap opens the map and suspends auto-run");
            Check(!(bool)typeof(GameScene).GetProperty("HandheldActionOwnsWorld", Private).GetValue(game), "map owns input while visible");
            Check(KeyEvent(Key.Enter, true, 1000) && KeyEvent(Key.Tab, true, 1000) && connection.SendList.IsEmpty, "map consumes actions and legacy hotkeys");
            if (viewport != null)
            {
                var layer = game.GetChildren().OfType<CanvasLayer>().Single();
                game.RemoveChild(layer);
                viewport.AddChild(layer);
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(this, SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var frame = viewport.GetTexture().GetImage();
                var mapControl = layer.GetChild<Control>(0);
                Check(mapControl.Size == new Vector2(640, 360), "overlay covers the 640×360 inset viewport");
                Check(frame.GetPixel(10, 32) != frame.GetPixel(10, 350), "terrain and status strip are both drawn");
                Check(frame.GetPixel(260, 148).R > .9f && frame.GetPixel(260, 148).G > .9f
                    && frame.GetPixel(260, 148).B > .9f, "direction triangle is drawn at the player's projected position");
                Check(frame.SavePng(capture) == Error.Ok, "captured 640×360 game UI rendered with real terrain");
                viewport.RemoveChild(layer);
                game.AddChild(layer);
                viewport.QueueFree();
            }
            KeyEvent(Key.F15, true, 1100);
            KeyEvent(Key.F15, false, 1150);
            Check(!Open(), "Select closes the map");
            KeyEvent(Key.F15, true, 1200);
            KeyEvent(Key.F15, false, 1250);
            Check(KeyEvent(Key.Escape, true, 1300) && !Open(), "B closes the map without passing into the world");
            var journal = new HandheldJournal(game);
            game.AddChild(journal);
            Set(game, "_journal", journal);
            journal.Open();
            KeyEvent(Key.F15, true, 1400);
            KeyEvent(Key.F15, false, 1450);
            Check(Open() && journal.IsOpen && !journal.Visible, "Select opens the map over the journal without closing it");
            KeyEvent(Key.Escape, true, 1500);
            Check(!Open() && journal.Visible, "closing the map restores the journal");
            Quit(0);

            object Act(string name, params object[] args) => game.GetType().GetMethod(name, Private | BindingFlags.Public).Invoke(game, args);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
        finally
        {
            game?.Free();
            minimap?.Free();
            player?.Free();
            network?.Free();
        }
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
}
