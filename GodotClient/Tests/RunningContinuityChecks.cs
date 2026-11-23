using System;
using System.IO;
using System.Reflection;
using Godot;
using Library;
using ZirconClient.Scripts;
using Point = System.Drawing.Point;

// ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/RunningContinuityChecks.cs --offline-movement-test
public partial class RunningContinuityChecks : SceneTree
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public override void _Initialize() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        try
        {
            Require(AutoLoginArgs.Handheld && AutoLoginArgs.OfflineMovementTest,
                "Run with ZIRCON_HANDHELD=1 and -- --offline-movement-test.");
            CheckHeldStickContinuity();
            GD.Print("PASS: held stick walks 1, runs 2 then 2 without an idle tick, and stops on release");
            Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            Quit(1);
        }
    }

    private void CheckHeldStickContinuity()
    {
        // Keep GameScene detached so login, network and frame updates never run.
        // MapView needs a live viewport for SendMouseMove -> UpdatePlayerPosition.
        var game = new GameScene();
        var map = new MapView();
        var player = new PlayerRenderer { CellX = 1, CellY = 1 };
        string fixtureDirectory = Path.Combine(Path.GetTempPath(), $"zircon-running-{Guid.NewGuid():N}");
        try
        {
            Root.AddChild(map);
            map.AddChild(player);
            LoadWalkableMap(map, fixtureDirectory);

            Set(game, "_mapView", map);
            Set(game, "_player", player);
            Set(game, "_playerLocation", new Point(1, 1));
            Get<Stats>(game, "_playerStats")[Stat.BagWeight] = 100;
            Get<Stats>(game, "_playerStats")[Stat.WearWeight] = 100;
            Set(game, "_runCooldownUntilMs", 0d);
            Set(game, "_canRun", true);

            SetStick(1f);
            Tick(game, Godot.Time.GetTicksMsec());
            CheckSegment(game, player, 1, 1, MirAnimation.Walking);

            Tick(game, Get<double>(game, "_moveVisualLockUntilMs") - 1);
            CheckSegment(game, player, 1, 1, MirAnimation.Walking);

            // No interpolation or idle update between ticks: advance both the
            // injected movement clock and SendMouseMove's real-clock deadline.
            Tick(game, ExpireActiveSegment(game));
            CheckSegment(game, player, 2, 2, MirAnimation.Running);
            Tick(game, ExpireActiveSegment(game));
            CheckSegment(game, player, 4, 2, MirAnimation.Running);

            SetStick(0f);
            double expired = ExpireActiveSegment(game);
            Tick(game, expired);
            Tick(game, expired + 1);
            Require(Get<Point>(game, "_playerLocation") == new Point(6, 1)
                && player.CellX == 6 && player.CellY == 1
                && Get<double>(game, "_moveVisualLockUntilMs") == expired,
                "Releasing the stick must not start another segment.");
            Require(!Get<bool>(game, "_handheldStickMoving") && !Get<bool>(game, "_handheldMoveStarted"),
                "Releasing the stick must clear the current movement chain.");
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, recursive: true);
            SetStick(0f);
        }
    }

    private static void LoadWalkableMap(MapView map, string directory)
    {
        const short width = 8, height = 4;
        Directory.CreateDirectory(directory);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "walkable.map"))))
        {
            writer.Write(new byte[22]);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)0); // Padding before half-resolution background layer.
            writer.Write(new byte[width / 2 * (height / 2) * 3]);
            var cell = new byte[14];
            cell[0] = 3; // Both walkability bits set.
            for (int i = 0; i < width * height; i++) writer.Write(cell);
        }
        Set(map, "_mapPath", directory);
        map.LoadMap("walkable");
        Require(map.Map?.IsValid == true, "Could not load the walkable map fixture.");
    }

    private static void SetStick(float value)
    {
        using var motion = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = value };
        Input.ParseInputEvent(motion);
        Input.FlushBufferedEvents();
        Require(Input.GetJoyAxis(0, JoyAxis.LeftX) == value && Input.GetJoyAxis(0, JoyAxis.LeftY) == 0f,
            $"Godot did not observe the synthetic left stick ({value}, 0).");
    }

    private static double ExpireActiveSegment(GameScene game)
    {
        Require(Get<int>(game, "_moveFrameCount") > 1,
            "The previous segment must still be active at the deadline.");
        double deadline = Godot.Time.GetTicksMsec();
        Set(game, "_moveStartMs", deadline - Get<double>(game, "_moveDurationMs"));
        Set(game, "_moveVisualLockUntilMs", deadline);
        return deadline;
    }

    private static void CheckSegment(GameScene game, PlayerRenderer player, int fromX, int distance, MirAnimation animation)
    {
        var from = new Point(fromX, 1);
        var to = new Point(fromX + distance, 1);
        Require(Get<Point>(game, "_moveFrom") == from && Get<Point>(game, "_playerLocation") == to
            && player.CellX == to.X && player.CellY == to.Y && player.MoveDistance == distance,
            $"Expected a {distance}-cell segment from {from} to {to} on this tick; "
            + $"actual location {Get<Point>(game, "_playerLocation")}.");
        Require(Get<int>(game, "_moveFrameCount") > 1 && player.Animation == animation,
            $"Expected active {animation} at {to}, got {player.Animation}.");
        Require(Get<double>(game, "_moveServerLockUntilMs") == 0,
            "Offline movement must not wait for a server acknowledgement.");
    }

    private static void Tick(GameScene game, double now)
        => typeof(GameScene).GetMethod("ProcessHandheldMovement", PrivateInstance)!.Invoke(game, new object[] { now });

    private static T Get<T>(object target, string name)
        => (T)FindField(target, name).GetValue(target)!;

    private static void Set(object target, string name, object value)
        => FindField(target, name).SetValue(target, value);

    private static FieldInfo FindField(object target, string name)
        => target.GetType().GetField(name, PrivateInstance)
            ?? throw new MissingFieldException(target.GetType().FullName, name);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
