using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Library;
using Library.Network;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

var probe = new C.SelectLanguage { Language = GmInspectProtocol.Probe };
byte[] bytes = probe.GetPacketBytes();

if (args.Length == 1)
{
    var baseline = new AssemblyLoadContext("legacy").LoadFromAssemblyPath(Path.GetFullPath(args[0]));
    var packet = baseline.GetType(typeof(Packet).FullName, throwOnError: true);
    var table = (System.Collections.IList)packet.GetField("Packets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    var updatedTable = (System.Collections.IList)typeof(Packet).GetField("Packets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    for (int id = 0; id < table.Count; id++)
        Check(((Type)table[id]).FullName == ((Type)updatedTable[id]).FullName, $"legacy opcode {id} unchanged");
    Check(updatedTable.Count == table.Count + 2 && (Type)updatedTable[table.Count] == typeof(C.GmInspectRequest) &&
        (Type)updatedTable[table.Count + 1] == typeof(S.GmInspectResult), "exactly two packet IDs appended in wire order");

    // A new client's opt-in must decode with the old library, with the original frame fully consumed.
    var parameters = new object[] { bytes, null };
    var decoded = packet.GetMethod("ReceivePacket", new[] { typeof(byte[]), typeof(byte[]).MakeByRefType() }).Invoke(null, parameters);
    Check(decoded.GetType().FullName == typeof(C.SelectLanguage).FullName && ((byte[])parameters[1]).Length == 0 &&
          (string)decoded.GetType().GetProperty("Language").GetValue(decoded) == GmInspectProtocol.Probe, "legacy probe decoding");
    Console.WriteLine($"PASS: {table.Count} legacy opcodes unchanged; legacy library decodes the probe");
}

var request = RoundTrip(new C.GmInspectRequest { MapIndex = 123 });
Check(request.MapIndex == 123, "request map ID round-trip");

// Empty data root + in-memory definitions. Never open or save live databases.
string root = Path.GetFullPath(Path.Combine(".artifacts", "gm-inspect-fixture-" + Guid.NewGuid().ToString("N")));
try
{
    var session = new Session(SessionMode.None, root + Path.DirectorySeparatorChar);
    session.Initialize(typeof(MapInfo).Assembly, typeof(AccountInfo).Assembly);
    SEnvir.Session = session;
    SEnvir.MapInfoList = session.GetCollection<MapInfo>();
    SEnvir.BuffInfoList = session.GetCollection<BuffInfo>();
    var version = Create<SystemDatabaseInfo>();
    version.Name = Session.SystemDatabaseInfoName;
    version.Version = "2026.11.26.1";

    MapInfo source = SEnvir.MapInfoList.CreateNewObject();
    source.Description = "Source";
    source.FileName = "source";
    MapInfo target = SEnvir.MapInfoList.CreateNewObject();
    target.Description = "Destination";
    target.FileName = "dest";
    target.MinimumLevel = 12;
    target.MaximumLevel = 34;
    target.RequiredClass = RequiredClass.Taoist;
    MapInfo cold = SEnvir.MapInfoList.CreateNewObject();
    cold.FileName = "unloaded";

    Point origin = new(1, 1), blocked = new(0, 1), outside = new(2, 1), npcLive = new(0, 0);
    MapRegion sourceRegion = Region(source, origin);
    MapRegion targetRegion = Region(target, origin, blocked, outside);
    var movement = Create<MovementInfo>();
    movement.SourceRegion = sourceRegion;
    movement.DestinationRegion = targetRegion;
    movement.NeedHole = true;
    movement.NeedItem = Create<ItemInfo>();
    movement.NeedItem.ItemName = "Gate Key";
    movement.NeedSpawn = Create<RespawnInfo>();
    movement.NeedInstance = Create<InstanceInfo>();
    movement.NeedInstance.Name = "Instance Gate";
    movement.Effect = MovementEffect.SpecialRepair;
    var coldMovement = Create<MovementInfo>();
    coldMovement.SourceRegion = sourceRegion;
    coldMovement.DestinationRegion = Region(cold, blocked);

    var npc = Create<NPCInfo>();
    npc.NPCName = "Clerk";
    npc.Region = sourceRegion;
    npc.EntryPage = Create<NPCPage>();
    npc.EntryPage.Description = "Welcome";
    npc.EntryPage.Say = "Hello";
    var shop = Create<NPCPage>();
    shop.Say = "Buy and sell";
    shop.DialogType = NPCDialogType.BuySell;
    Link(npc.EntryPage, shop);
    Link(shop, npc.EntryPage); // Cycle.
    var store = Create<NPCAction>();
    store.Page = npc.EntryPage;
    store.ActionType = NPCActionType.Storage;
    var unconfiguredTeleport = Create<NPCAction>();
    unconfiguredTeleport.Page = npc.EntryPage;
    unconfiguredTeleport.ActionType = NPCActionType.Teleport;
    var unknownNpc = Create<NPCInfo>();
    unknownNpc.NPCName = "Teleport Shop"; // Names are not service evidence.
    unknownNpc.Region = sourceRegion;

    var maps = (Dictionary<MapInfo, Map>)typeof(SEnvir).GetField("Maps", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    Map runtimeSource = LoadedMap(source, origin, npcLive);
    maps.Add(source, runtimeSource);
    maps.Add(target, LoadedMap(target, origin));
    runtimeSource.NPCs.Add(new NPCObject { NPCInfo = npc, CurrentLocation = npcLive });

    SEnvir.Now = new DateTime(2026, 11, 26);
    Config.TestServer = true;
    var connection = NewConnection();
    var observer = NewConnection();
    connection.Observers.Add(observer);
    var account = Create<AccountInfo>();
    var character = Create<CharacterInfo>();
    character.Account = account;
    connection.Account = account;
    account.Connection = connection;
    connection.Player = (PlayerObject)RuntimeHelpers.GetUninitializedObject(typeof(PlayerObject));
    connection.Player.Character = character;
    connection.Stage = GameStage.Game;
    var query = new C.GmInspectRequest { MapIndex = source.Index };

    connection.Process(query);
    Check(connection.SendList.IsEmpty, "no result sent without opt-in");
    ExpectDenied(Probe(), "AdminRequired");
    ExpectDenied(Query(query), "AdminRequired");
    account.Admin = true;
    connection.Stage = GameStage.Observer;
    ExpectDenied(Probe(), "NotInGame");
    connection.Stage = GameStage.Game;
    Config.TestServer = false;
    ExpectDenied(Probe(), "TestServerRequired");
    Config.TestServer = true;
    Check(Probe().Allowed, "permanent admin authorized");
    account.Admin = false;
    account.TempAdmin = true;
    S.GmInspectResult capability = Probe();
    Check(capability.Allowed && capability.MapIndex == GmInspectProtocol.ProbeMapIndex && capability.Ports.Count == 0 && capability.Npcs.Count == 0 &&
        capability.SystemDatabaseVersion == version.Version, "temp admin receives data-free capability and loaded DB version");
    connection.Process(probe);
    Check(connection.SendList.IsEmpty, "probe response rate bounded");
    connection.Process(new C.GmInspectRequest { MapIndex = GmInspectProtocol.ProbeMapIndex });
    Check(connection.SendList.IsEmpty, "query cannot generate a capability response");

    S.GmInspectResult result = Query(query);
    Check(result.Allowed && result.MapIndex == source.Index && result.MapName == source.Description && result.MapFileName == source.FileName &&
        result.Reason == "" && !result.Truncated, "authorized selected-map wire response");
    GmInspectPort port = result.Ports.Single(x => x.MovementIndex == movement.Index);
    Check(port.SourcePoints.SequenceEqual(new[] { origin }) && port.DestinationMapIndex == target.Index && port.DestinationMapFileName == target.FileName &&
        port.Destinations.Select(x => x.Location).SequenceEqual(targetRegion.PointRegion), "portal wire positions in both directions of the row");
    Check(port.MinimumLevel == 12 && port.MaximumLevel == 34 && port.RequiredClass == RequiredClass.Taoist && port.NeedHole &&
        port.NeedItem == "Gate Key" && port.NeedSpawnIndex == movement.NeedSpawn.Index && port.NeedInstance == "Instance Gate" &&
        port.Effect == MovementEffect.SpecialRepair, "portal wire gates and effect");
    Check(port.Reason == "BlockedTerrain" && port.Destinations.Select(x => x.Reason).SequenceEqual(new[] { "", "BlockedTerrain", "OutOfBounds" }), "terrain classified per coordinate");
    Check(result.Ports.Single(x => x.MovementIndex == coldMovement.Index).Destinations.Single().Reason == "TerrainUnknown" &&
        !maps.ContainsKey(cold) && targetRegion.PointList == null, "read-only inspection neither loads maps nor changes regions");
    GmInspectNpc row = result.Npcs.Single(x => x.NpcIndex == npc.Index);
    Check(row.HasLocation && row.IsSpawned && row.Location == npcLive && row.EntryPageIndex == npc.EntryPage.Index && row.EntryPage == "Welcome" &&
        row.Services.SequenceEqual(new[] { "BuySell", "Storage" }), "live NPC coordinate and evidenced service tags");
    GmInspectNpc fallback = result.Npcs.Single(x => x.NpcIndex == unknownNpc.Index);
    Check(fallback.HasLocation && !fallback.IsSpawned && fallback.Location == origin && fallback.Services.Count == 0 && fallback.EntryPageIndex == -1,
        "configured NPC fallback does not guess services");

    connection.Process(query);
    Check(connection.SendList.IsEmpty, "query rate bounded after success");
    account.TempAdmin = false;
    ExpectDenied(Query(query), "AdminRequired");
    connection.Process(query);
    Check(connection.SendList.IsEmpty, "query rate bounded after denial");
    account.Admin = true;
    var missing = Query(new C.GmInspectRequest { MapIndex = int.MaxValue });
    Check(missing.Allowed && missing.Reason == "MapNotFound" && missing.Ports.Count == 0 && missing.Npcs.Count == 0, "unknown map is not an authorization failure");
    Check(observer.SendList.IsEmpty, "observers never receive inspection data");

    targetRegion.PointRegion = Enumerable.Repeat(origin, 65).ToArray();
    var capped = Query(query);
    Check(capped.Truncated && capped.Ports.Single(x => x.MovementIndex == movement.Index).Destinations.Count == 64, "oversized coordinate region is capped and reported");
    Check(!Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories).Any(), "no saves written by fixture or inspection");
    Console.WriteLine("PASS: inspection authorization, rate limiting, observer privacy, portal and NPC wire round-trips, truncation and read-only behavior");

    T Create<T>() where T : DBObject, new() => session.GetCollection<T>().CreateNewObject();

    MapRegion Region(MapInfo map, params Point[] points)
    {
        var region = Create<MapRegion>();
        region.Map = map;
        region.PointRegion = points;
        return region;
    }

    void Link(NPCPage from, NPCPage to)
    {
        var button = Create<NPCButton>();
        button.Page = from;
        button.DestinationPage = to;
    }

    S.GmInspectResult Probe()
    {
        SEnvir.Now = SEnvir.Now.AddMilliseconds(500);
        connection.Process(RoundTrip(probe));
        return Result();
    }

    S.GmInspectResult Query(C.GmInspectRequest packet)
    {
        SEnvir.Now = SEnvir.Now.AddMilliseconds(500);
        connection.Process(RoundTrip(packet));
        return Result();
    }

    S.GmInspectResult Result()
    {
        Check(connection.SendList.TryDequeue(out Packet reply) && reply is S.GmInspectResult && !reply.ObserverPacket &&
            connection.SendList.IsEmpty, "one private reply");
        return RoundTrip((S.GmInspectResult)reply);
    }
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void ExpectDenied(S.GmInspectResult result, string reason) =>
    Check(!result.Allowed && result.Reason == reason && result.Ports.Count == 0 && result.Npcs.Count == 0 && result.SystemDatabaseVersion == "", reason);

static T RoundTrip<T>(T original) where T : Packet
{
    var decoded = Packet.ReceivePacket(original.GetPacketBytes(), out byte[] extra);
    Check(decoded is T && extra.Length == 0, $"round trip {typeof(T).Name}");
    return (T)decoded;
}

static SConnection NewConnection()
{
    // Use the real handler and enqueue without a live socket or character login.
    var connection = (SConnection)RuntimeHelpers.GetUninitializedObject(typeof(SConnection));
    connection.Connected = true;
    connection.SendList = new ConcurrentQueue<Packet>();
    connection.Observers = new List<SConnection>();
    return connection;
}

static Map LoadedMap(MapInfo info, params Point[] walkable)
{
    var map = new Map(info);
    var cells = new Cell[2, 2];
    foreach (Point point in walkable) cells[point.X, point.Y] = new Cell(point);
    typeof(Map).GetProperty(nameof(Map.Cells)).SetValue(map, cells);
    typeof(Map).GetProperty(nameof(Map.Width)).SetValue(map, 2);
    typeof(Map).GetProperty(nameof(Map.Height)).SetValue(map, 2);
    return map;
}

static void Check(bool pass, string detail)
{
    if (!pass) throw new InvalidOperationException(detail);
}
