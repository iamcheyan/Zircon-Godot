using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using Library;
using Library.SystemModels;
using MirDB;
using Server.Envir;
using Server.Models;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: SpawnDataRepair <input System.db copy> <map directory> [new output directory]");
    return 2;
}

try
{
    Run(args);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void Run(string[] args)
{
    var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "repair.json")))!;
    var input = Path.GetFullPath(args[0]);
    Config.MapPath = Path.GetFullPath(args[1]);
    var output = args.Length == 3 ? Path.GetFullPath(args[2]) : null;
    if (output != null && Path.Exists(output))
        throw new IOException($"Output directory must be new: {output}");

    var scratch = Directory.CreateTempSubdirectory("zircon-spawn-repair-");
    try
    {
        var copy = Path.Combine(scratch.FullName, "System.db");
        File.Copy(input, copy);
        CheckHash(copy, plan.InputSha256);
        foreach (var (map, hash) in plan.MapSha256)
            CheckHash(Path.Combine(Config.MapPath, map + ".map"), hash);

        var session = Load(scratch.FullName);
        Console.WriteLine($"Input System.db version {session.SystemDatabaseVersion}");
        var respawns = session.GetCollection<RespawnInfo>();
        var rules = respawns.Binding.ToDictionary(rule => rule.Index);
        var maps = plan.MapSha256.Keys.ToDictionary(name => name, name => LoadMap(session, name));
        var geometry = maps.ToDictionary(entry => entry.Key, entry => EligibleCells(session, entry.Value));
        var rooms = session.GetCollection<MapRegion>();
        var touchedRules = new List<RespawnInfo>();
        var touchedRegions = new List<MapRegion>();

        foreach (var floor in plan.Ordinary)
        {
            var bladesman = Rule(rules, floor.Bladesman, floor.Map, "Bone Bladesman", false);
            var archer = Rule(rules, floor.Archer, floor.Map, "Bone Archer", false);
            Require(bladesman.Region == archer.Region && bladesman.Region.Description == "Whole Map",
                $"Unexpected ordinary regions on {floor.Map}.");
            var cells = geometry[floor.Map];
            Require(cells.Length == floor.Eligible, $"Unexpected capacity on {floor.Map}: {cells.Length}.");
            var pair = new[] { bladesman, archer };
            var sum = pair.Sum(rule => rule.Count);
            var budget = cells.Length - floor.Reserved;
            var shares = pair.Select(rule => (Rule: rule, Whole: rule.Count * budget / sum, Remainder: rule.Count * budget % sum))
                .OrderByDescending(share => share.Remainder).ThenBy(share => share.Rule.Index).ToArray();
            var extras = budget - shares.Sum(share => share.Whole);
            Console.WriteLine($"{floor.Map} eligible={cells.Length}, ordinary budget={budget}, boss headroom={floor.Reserved}");
            foreach (var (share, rank) in shares.Select((share, rank) => (share, rank)))
            {
                var count = share.Whole + (rank < extras ? 1 : 0);
                Console.WriteLine($"  #{share.Rule.Index} {share.Rule.MonsterName}: {share.Rule.Count} -> {count}");
                share.Rule.Count = count;
                touchedRules.Add(share.Rule);
            }
        }

        var lich = Rule(rules, plan.Lich.Rule, plan.Lich.Map, "Arch Lich Taedu", true);
        var anchor = new Point(plan.Lich.X, plan.Lich.Y);
        var lichCells = geometry[plan.Lich.Map];
        var oldRegion = lich.Region;
        Require(lich.Count == 1 && lich.Delay == 300 && oldRegion.Description == "Whole Map" &&
            lichCells.Contains(anchor) && oldRegion.GetPoints(50).All(point => !lichCells.Contains(point)),
            "Unexpected D605 boss rule or anchor geometry.");
        Require(oldRegion.Respawns.Count == 3 && IsSpawnOnly(oldRegion), "Unexpected use of the D605 Whole Map region.");
        var anchorRegion = rooms.CreateNewObject();
        anchorRegion.Map = oldRegion.Map;
        anchorRegion.Description = "Arch Lich Taedu Spawn";
        anchorRegion.RegionType = RegionType.Spawn;
        SetPoints(anchorRegion, [anchor]);
        lich.Region = anchorRegion;
        SetPoints(oldRegion, lichCells);
        touchedRules.Add(lich);
        touchedRegions.AddRange([oldRegion, anchorRegion]);
        Console.WriteLine($"{plan.Lich.Map} #{lich.Index} {lich.MonsterName}: anchor ({anchor.X},{anchor.Y}), count={lich.Count}, delay={lich.Delay}m");

        var antCenter = new Point(plan.Ants.X, plan.Ants.Y);
        foreach (var (map, index) in plan.Ants.Rules)
        {
            var ant = Rule(rules, index, map, "Ant Commander", true);
            var region = ant.Region;
            Require(ant.Count == 90 && ant.Delay == 1 && region.Description == "Ant Commander Spawn" &&
                region.RegionType == RegionType.Spawn && region.Respawns.Count == 1 && IsSpawnOnly(region) &&
                region.BitRegion == null && (region.PointRegion?.SequenceEqual([antCenter]) ?? false),
                $"Unexpected ant rule or region on {map}.");
            var points = geometry[map].Where(point => Distance(point, antCenter) <= plan.Ants.Range).ToArray();
            Require(points.Length > 0, $"No eligible Ant Commander points on {map}.");
            SetPoints(region, points);
            ant.Count = plan.Ants.Count;
            ant.Delay = plan.Ants.Delay;
            touchedRules.Add(ant);
            touchedRegions.Add(region);
            Console.WriteLine($"{map} #{ant.Index} {ant.MonsterName}: count 90 -> {ant.Count}, delay 1m -> {ant.Delay}m, region points 1 -> {points.Length}");
        }

        var movements = session.GetCollection<MovementInfo>().Binding.ToDictionary(move => move.Index);
        foreach (var room in plan.ShipRooms)
        {
            Require(!rules.Values.Any(rule => rule.Region?.Map?.FileName == room.Map), $"Unexpected existing spawn on {room.Map}.");
            var map = maps[room.Map];
            var ship = maps["d903"];
            var inbound = movements[room.Inbound];
            var outbound = movements[room.Outbound];
            var shipDoor = CheckMovement(inbound, ship, map);
            var roomDoor = CheckMovement(outbound, map, ship);
            var hubArrivals = movements.Values.Where(move => move.SourceRegion?.Map?.FileName == "d904" &&
                move.DestinationRegion?.Map == ship.Info).SelectMany(move => move.DestinationRegion.GetPoints(ship.Width)).Distinct().ToArray();
            Require(hubArrivals.Length > 0 && hubArrivals.All(point => Reachable(ship, [point]).Contains(shipDoor)),
                $"{room.Map} doorway is cut off from a d904 arrival in d903.");
            Require(Reachable(map, inbound.DestinationRegion.GetPoints(map.Width)).Contains(roomDoor), $"{room.Map} return doorway is cut off.");

            var source = new Point(room.X, room.Y);
            var eligible = geometry[room.Map];
            var originalFootprint = eligible.Where(point => Distance(point, source) <= room.Range).ToArray();
            Require(originalFootprint.Length == 0, $"Expected blocked source footprint on {room.Map}.");
            var nearest = eligible.OrderBy(point => Distance(point, source)).ThenBy(point => point.X).ThenBy(point => point.Y).First();
            var points = eligible.Where(point => Distance(point, nearest) <= room.Range).ToArray();
            var monster = session.GetCollection<MonsterInfo>().Binding.Single(candidate => candidate.MonsterName == room.Monster);
            Require(!monster.IsBoss && rules.Values.Any(rule => rule.Monster == monster && rule.Region?.Map == ship.Info),
                $"Expected existing matching monster on d903 for {room.Map}.");
            var region = rooms.CreateNewObject();
            region.Map = map.Info;
            region.Description = $"{room.Monster} Spawn";
            region.RegionType = RegionType.Spawn;
            SetPoints(region, points);
            var rule = respawns.CreateNewObject();
            rule.Monster = monster;
            rule.Region = region;
            rule.Count = room.Count;
            rule.Delay = room.Delay;
            touchedRules.Add(rule);
            touchedRegions.Add(region);
            Console.WriteLine($"{room.Map} #{rule.Index} {room.Monster}: count={rule.Count}, delay={rule.Delay}m, " +
                $"source ({source.X},{source.Y}) -> ({nearest.X},{nearest.Y}) r{room.Range}, {points.Length} region points");
        }

        if (output == null)
        {
            Console.WriteLine("Preview complete. Supply a new output directory to save the repaired copy.");
            return;
        }

        session.Save(true);
        var readBack = Load(scratch.FullName);
        var savedRules = readBack.GetCollection<RespawnInfo>().Binding.ToDictionary(rule => rule.Index);
        var savedRegions = readBack.GetCollection<MapRegion>().Binding.ToDictionary(region => region.Index);
        Require(rules.Count + plan.ShipRooms.Length == savedRules.Count, "Unexpected respawn rule count.");
        foreach (var rule in touchedRules)
        {
            var saved = savedRules[rule.Index];
            Require(saved.Count == rule.Count && saved.Delay == rule.Delay && saved.Region.Index == rule.Region.Index &&
                saved.Monster.Index == rule.Monster.Index && saved.RespawnIndex == rule.RespawnIndex &&
                saved.EventSpawn == rule.EventSpawn && saved.Announce == rule.Announce && saved.DropSet == rule.DropSet &&
                saved.EasterEventChance == rule.EasterEventChance, $"Rule #{rule.Index} failed round-trip.");
        }
        foreach (var region in touchedRegions)
        {
            var saved = savedRegions[region.Index];
            Require(saved.Map.Index == region.Map.Index && saved.Description == region.Description &&
                saved.RegionType == region.RegionType && saved.Size == region.Size && saved.BitRegion == null &&
                saved.PointRegion.SequenceEqual(region.PointRegion), $"Region #{region.Index} failed round-trip.");
        }

        Directory.CreateDirectory(output);
        var result = Path.Combine(output, "System.db");
        File.Copy(copy, result, overwrite: false);
        Console.WriteLine($"Round-trip passed for {touchedRules.Count} rules and {touchedRegions.Count} regions.");
        Console.WriteLine($"Output {result}, version {readBack.SystemDatabaseVersion}, SHA-256 {Hash(result)}");
    }
    finally
    {
        scratch.Delete(recursive: true);
    }
}

static Session Load(string directory)
{
    var session = new Session(SessionMode.System, directory + Path.DirectorySeparatorChar) { BackUp = false };
    session.Initialize(typeof(MapInfo).Assembly, typeof(Map).Assembly);
    return session;
}

static RespawnInfo Rule(Dictionary<int, RespawnInfo> rules, int index, string map, string monster, bool boss)
{
    var rule = rules[index];
    Require(rule.Region?.Map?.FileName == map && rule.MonsterName == monster && rule.Monster.IsBoss == boss &&
        rule.RespawnIndex == 0 && !rule.EventSpawn && !rule.Announce, $"Unexpected rule #{index}.");
    return rule;
}

static bool IsSpawnOnly(MapRegion region) => region.SourceMovements.Count == 0 && region.DestinationMovements.Count == 0 &&
    region.SafeZones.Count == 0 && region.BindSafeZones.Count == 0 && region.NPCs.Count == 0 && region.QuestTasks.Count == 0;

static void SetPoints(MapRegion region, Point[] points)
{
    region.BitRegion = null;
    region.PointRegion = points;
    region.Size = points.Length;
}

static Map LoadMap(Session session, string name)
{
    var info = session.GetCollection<MapInfo>().Binding.Single(map => map.FileName == name);
    var map = new Map(info);
    map.Load();
    Require(map.ValidCells.Count > 0 && info.Dungeon == null && info.Guards.Count == 0, $"Unexpected map geometry on {name}.");
    return map;
}

static Point CheckMovement(MovementInfo movement, Map from, Map to)
{
    Require(movement.SourceRegion?.Map == from.Info && movement.DestinationRegion?.Map == to.Info && movement.NeedSpawn == null,
        $"Unexpected movement #{movement.Index} from {from.Info.FileName} to {to.Info.FileName}.");
    var source = movement.SourceRegion.GetPoints(from.Width).Single();
    var destination = movement.DestinationRegion.GetPoints(to.Width).Single();
    Require(from.GetCell(source) != null && to.GetCell(destination) != null, $"Blocked endpoint on movement #{movement.Index}.");
    Console.WriteLine($"Movement #{movement.Index} walkable on both ends: {from.Info.FileName} ({source.X},{source.Y}) -> " +
        $"{to.Info.FileName} ({destination.X},{destination.Y})");
    return source;
}

// The pinned maps all have walkable arrivals and space outside the portal buffer.
static Point[] EligibleCells(Session session, Map map)
{
    var info = map.Info;
    var arrivals = new HashSet<Point>();
    var portals = new HashSet<Point>();
    foreach (var region in info.Regions)
    {
        var arrival = region.DestinationMovements.Any(move => move.SourceRegion?.Map != null) || region.BindSafeZones.Count > 0;
        var exit = region.SourceMovements.Any(move => move.DestinationRegion?.Map != null);
        if (!arrival && !exit) continue;
        var points = region.GetPoints(map.Width).Where(point => map.GetCell(point) != null);
        portals.UnionWith(points);
        if (arrival) arrivals.UnionWith(points);
    }
    Require(arrivals.Count > 0, $"No walkable arrival on {info.FileName}.");
    var safe = session.GetCollection<SafeZoneInfo>().Binding.Where(zone => zone.Region?.Map == info && !zone.RedZone)
        .SelectMany(zone => zone.Region.GetPoints(map.Width)).ToHashSet();
    return Reachable(map, arrivals).Where(point => !safe.Contains(point) && portals.All(portal => Distance(point, portal) > 3))
        .OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
}

static HashSet<Point> Reachable(Map map, IEnumerable<Point> start)
{
    var seen = start.Where(point => map.GetCell(point) != null).ToHashSet();
    var toVisit = new Queue<Point>(seen);
    while (toVisit.TryDequeue(out var point))
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var next = new Point(point.X + dx, point.Y + dy);
            if (map.GetCell(next) != null && seen.Add(next)) toVisit.Enqueue(next);
        }
    return seen;
}

static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static void CheckHash(string path, string expected)
{
    var actual = Hash(path);
    Require(actual == expected, $"Unexpected SHA-256 for {path}: {actual} (expected {expected}).");
}

sealed record Plan(string InputSha256, Dictionary<string, string> MapSha256, Ordinary[] Ordinary, Lich Lich, Ants Ants, ShipRoom[] ShipRooms);
sealed record Ordinary(string Map, int Eligible, int Reserved, int Bladesman, int Archer);
sealed record Lich(string Map, int Rule, int X, int Y);
sealed record Ants(int X, int Y, int Range, int Count, int Delay, Dictionary<string, int> Rules);
sealed record ShipRoom(string Map, string Monster, int X, int Y, int Range, int Count, int Delay, int Inbound, int Outbound);
