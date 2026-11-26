using System.Drawing;
using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using Server.Models.Monsters;

// Exercise the actual ServerLibrary, with no game loop, listener, or live database.
var root = Directory.CreateTempSubdirectory("zircon-spawn-checks-");
try
{
    SEnvir.Now = new DateTime(2026, 11, 26, 12, 0, 0);
    SEnvir.Random = new Random(1);
    Config.MapPath = root.FullName;
    var session = new Session(SessionMode.Both, root.FullName + Path.DirectorySeparatorChar);
    session.Initialize(typeof(MapInfo).Assembly, typeof(Map).Assembly);
    SEnvir.SafeZoneInfoList = session.GetCollection<SafeZoneInfo>();
    SEnvir.MovementInfoList = session.GetCollection<MovementInfo>();
    SEnvir.NPCInfoList = session.GetCollection<NPCInfo>();
    SEnvir.RespawnInfoList = session.GetCollection<RespawnInfo>();
    SEnvir.QuestInfoList = session.GetCollection<QuestInfo>();
    SEnvir.CurrencyInfoList = session.GetCollection<CurrencyInfo>();
    SEnvir.BuffInfoList = session.GetCollection<BuffInfo>();

    // Minimal 2x2 map in the classic format, with only (0,0) walkable.
    const int cellOffset = 28 + 3;
    var bytes = new byte[cellOffset + 4 * 14];
    bytes[22] = bytes[24] = 2;
    bytes[cellOffset] = 3;
    var map = LoadMap("fixture");
    var monster = session.GetCollection<MonsterInfo>().CreateNewObject();
    var valid = new Point(0, 0);
    var blocked = new Point(1, 1);

    var invalidRegion = Region(map, blocked, new Point(-1, -1));
    CheckFailedRegion(map, invalidRegion, monster);
    var invalidSpawn = SpawnGroup(invalidRegion, monster, 3);
    invalidSpawn.DoSpawn(eventSpawn: true);
    Check(invalidSpawn.AliveCount == 0 && map.Objects.Count == 0, "failed respawn attempts do not inflate AliveCount");

    var validRegion = Region(map, valid);
    var untracked = MonsterObject.GetMonster(monster);
    Check(untracked.Spawn(validRegion, null, 0) && untracked.CurrentCell == map.GetCell(valid),
        "Spawn(MapRegion) returns true and registers the monster on a walkable cell");
    untracked.Despawn();

    var spawn = SpawnGroup(validRegion, monster, 2);
    spawn.DoSpawn(eventSpawn: true);
    var mobs = map.Objects.OfType<MonsterObject>().ToArray();
    Check(spawn.AliveCount == 2 && mobs.Length == 2, "successful respawns are counted");
    Check(mobs.All(mob => mob.CurrentCell == map.GetCell(valid) && mob.Node != null && !mob.Dead),
        "two live monsters can spawn on the same cell");
    mobs[0].Despawn();
    Check(spawn.AliveCount == 1, $"live despawn releases exactly one slot (got {spawn.AliveCount}, expected 1)");
    Check(mobs[0].SpawnInfo == null && mobs[0].Node == null && mobs[0].CurrentCell == null &&
        !map.Objects.Contains(mobs[0]) && !map.GetCell(valid).Objects.Contains(mobs[0]) && !SEnvir.Objects.Contains(mobs[0]),
        "live despawn clears ownership and all registrations");
    spawn.DoSpawn(eventSpawn: true);
    Check(spawn.AliveCount == 2 && map.Objects.Count == 2, "next respawn fills the vacated slot");
    foreach (var mob in map.Objects.ToArray()) mob.Despawn();
    Check(spawn.AliveCount == 0, "despawning all live monsters empties the group");

    // Give the real event handler a credited character on a separate map, so
    // visibility, socket sends and experience rewards do not enter this check.
    var character = session.GetCollection<CharacterInfo>().CreateNewObject();
    character.Account = session.GetCollection<AccountInfo>().CreateNewObject();
    var player = new PlayerObject(character, null) { CurrentMap = LoadMap("credit-fixture") };
    player.Node = SEnvir.Objects.AddLast(player);
    var clearEvent = session.GetCollection<MonsterEventInfo>().CreateNewObject();
    clearEvent.TrackingType = EventTrackingType.Global;
    clearEvent.MaxValue = 10;
    var trigger = session.GetCollection<MonsterEventTrigger>().CreateNewObject();
    trigger.Event = clearEvent;
    trigger.Monster = monster;
    trigger.Type = MonsterEventTriggerType.MonsterClear;
    trigger.MapParameter1 = map.Info;
    trigger.Value = 1;
    SEnvir.Spawns.Add(spawn);

    spawn.DoSpawn(eventSpawn: true);
    mobs = map.Objects.OfType<MonsterObject>().ToArray();
    foreach (var mob in mobs) mob.EXPOwner = player;
    mobs[0].Die();
    Check(spawn.AliveCount == 1 && SEnvir.EventLogs.Count == 0, "first death decrements but does not fire MONSTERCLEAR");
    mobs[0].Despawn();
    Check(spawn.AliveCount == 1, "despawning an already-counted corpse does not decrement again");
    mobs[1].Die();
    var log = SEnvir.EventLogs.Single(x => x.MonsterEvent == clearEvent);
    Check(spawn.AliveCount == 0 && log.CurrentValue == 1, "last death fires MONSTERCLEAR once");
    mobs[1].Despawn();
    Check(spawn.AliveCount == 0 && log.CurrentValue == 1, "corpse removal does not fire MONSTERCLEAR again");

    spawn.Info.Count = 1;
    spawn.DoSpawn(eventSpawn: true);
    var last = map.Objects.OfType<MonsterObject>().Single();
    last.EXPOwner = player;
    last.Despawn();
    Check(spawn.AliveCount == 0 && log.CurrentValue == 1, "live removal of the last monster does not fire MONSTERCLEAR");

    CheckGateExpiry<JinamStoneGate>(69);
    CheckGateExpiry<NetherworldGate>(30);

    var tinyMap = LoadMap("quota-fixture");
    var tinyRegion = Region(tinyMap, blocked);
    var smallGroup = SpawnGroup(tinyRegion, monster, 1);
    var largeGroup = SpawnGroup(tinyRegion, monster, 3);
    smallGroup.DoSpawn(eventSpawn: false);
    largeGroup.DoSpawn(eventSpawn: false);
    Check(smallGroup.AliveCount == 0 && largeGroup.AliveCount == 1 &&
        tinyMap.Objects.OfType<MonsterObject>().Single().CurrentLocation == valid,
        "one eligible cell is assigned proportionally regardless of rule order or broken legacy point");

    void CheckGateExpiry<T>(int ai) where T : MonsterObject
    {
        var definition = session.GetCollection<MonsterInfo>().CreateNewObject();
        definition.AI = ai;
        var gateSpawn = SpawnGroup(validRegion, definition, 1);
        gateSpawn.DoSpawn(eventSpawn: true);
        var gate = map.Objects.OfType<T>().Single();
        Check(gateSpawn.AliveCount == 1, $"{typeof(T).Name} starts with a counted slot");
        SEnvir.Now = SEnvir.Now.AddMinutes(20);
        gate.Process();
        Check(gateSpawn.AliveCount == 0 && gate.SpawnInfo == null && gate.Node == null,
            $"{typeof(T).Name} expiry releases exactly one slot");
    }

    Map LoadMap(string name)
    {
        File.WriteAllBytes(Path.Combine(root.FullName, name + ".map"), bytes);
        var info = session.GetCollection<MapInfo>().CreateNewObject();
        info.FileName = info.Description = name;
        var loaded = SEnvir.GetMap(info);
        Check(loaded.Width > 0 && loaded.Height > 0, $"{name} loaded");
        return loaded;
    }

    MapRegion Region(Map owner, params Point[] points)
    {
        var region = session.GetCollection<MapRegion>().CreateNewObject();
        region.Map = owner.Info;
        region.PointList = [.. points];
        return region;
    }

    SpawnInfo SpawnGroup(MapRegion region, MonsterInfo definition, int count)
    {
        var info = SEnvir.RespawnInfoList.CreateNewObject();
        info.Region = region;
        info.Monster = definition;
        info.Count = count;
        return new SpawnInfo(info, null, 0);
    }
}
finally
{
    root.Delete(recursive: true);
}

static void CheckFailedRegion(Map map, MapRegion region, MonsterInfo monster)
{
    Check(region.PointList.All(p => map.GetCell(p) == null), "all fixture points are unspawnable");
    var mob = MonsterObject.GetMonster(monster);
    bool spawned = mob.Spawn(region, null, 0);
    Check(!spawned, "Spawn(MapRegion) returns false after all attempts fail");
    Check(mob.Node == null && mob.CurrentCell == null && map.Objects.Count == 0 && SEnvir.Objects.Count == 0,
        "failed spawn leaves no cell, map, or global registration");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}
