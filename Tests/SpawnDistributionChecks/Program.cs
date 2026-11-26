using System.Drawing;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using ServerMonster = Server.Models.MonsterObject;

const int BucketColumns = 8;
const int BucketRows = 4;
const int BucketCount = BucketColumns * BucketRows;
const int BichonColumns = 16;
const int BichonRows = 16;
const int RefillPasses = 2;

if (args.Length == 1 && args[0] == "--synthetic-only")
{
    RunSyntheticLegacyRegression();
    return;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project Tests/SpawnDistributionChecks -- <System.db directory> <map directory> [output.csv] [--require-full] [--expect-maps=N] [--legacy]");
    Console.Error.WriteLine("Run --synthetic-only to execute the deterministic legacy-placement demonstration without game data.");
    Environment.ExitCode = 2;
    return;
}

RunSyntheticLegacyRegression();
var dbSource = Path.GetFullPath(args[0]);
var mapRoot = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args.Skip(2).FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "spawn-distribution.csv");
const string mapCountOption = "--expect-maps=";
var expectedMaps = args.SingleOrDefault(arg => arg.StartsWith(mapCountOption, StringComparison.Ordinal));
var sourceDbPath = Path.Combine(dbSource, "System.db");
if (!File.Exists(sourceDbPath)) throw new FileNotFoundException("System.db not found", sourceDbPath);
if (!Directory.Exists(mapRoot)) throw new DirectoryNotFoundException(mapRoot);

var tempRoot = Directory.CreateTempSubdirectory("zircon-spawn-distribution-");
try
{
    var tempDbPath = Path.Combine(tempRoot.FullName, "System.db");
    File.Copy(sourceDbPath, tempDbPath);
    var dbHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tempDbPath))).ToLowerInvariant();

    Config.MapPath = mapRoot;
    Config.DistributeOrdinaryRespawns = !args.Contains("--legacy");
    SEnvir.Now = new DateTime(2026, 11, 26, 23, 59, 59, DateTimeKind.Utc);
    SEnvir.Random = new Random(0x5A17);
    var session = new Session(SessionMode.System, tempRoot.FullName + Path.DirectorySeparatorChar,
        Path.Combine(tempRoot.FullName, "backup") + Path.DirectorySeparatorChar) { BackUp = false };
    session.Initialize(typeof(MapInfo).Assembly, typeof(Map).Assembly);
    BindCollections(session);

    var configuredMapSet = SEnvir.RespawnInfoList.Binding
        .Where(x => x.Region?.Map != null && x.Monster != null)
        .Select(x => x.Region.Map).ToHashSet();
    var mapInfos = SEnvir.MapInfoList.Binding.Where(configuredMapSet.Contains)
        .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
    if (mapInfos.Length == 0 || mapInfos.Length != configuredMapSet.Count ||
        (expectedMaps != null && mapInfos.Length != int.Parse(expectedMaps.AsSpan(mapCountOption.Length))))
        throw new InvalidOperationException($"Unexpected coverage: {mapInfos.Length} registered spawn maps, {configuredMapSet.Count} configured maps, {SEnvir.MapInfoList.Count} registered maps in total ({expectedMaps ?? "no explicit count"}).");

    var distributedProperty = typeof(SpawnInfo).GetProperty("UsesDistributedPlacement",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    var hasDistributionApi = distributedProperty?.PropertyType == typeof(bool);

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
    writer.WriteLine("record_type,map_file,map_description,rule_region,region_type,monster,respawn_index,distributed,reason,configured_count,expected_count,eligible_cells,actual_count,alive_count,count_delta,unfilled,occupied_buckets,bucket_columns,bucket_rows,bucket_counts,duplicate_cells,safezone_hits,redzone_hits,portal_hits,refill_1_added,refill_2_added,bichon_snapshot");

    var loadedMaps = 0;
    var testedRules = 0;
    var rulesWithShortfall = 0;
    var bichonRows = 0;
    foreach (var info in mapInfos)
    {
        SEnvir.Random = new Random(unchecked(0x5A17 + StableSeed(info.FileName)));
        var map = new Map(info);
        map.Load();
        if (map.Width <= 0 || map.Height <= 0 || map.ValidCells.Count == 0)
            throw new InvalidOperationException($"Map did not load usable cells: {info.FileName}");
        foreach (var region in info.Regions) region.CreatePoints(map.Width);
        map.Setup();
        RegisterMapForRuntimeHelpers(info, map);
        ApplySafeZones(info, map);
        InvokeMapHelper("CreateMovements", info);

        var rules = SEnvir.RespawnInfoList.Binding
            .Where(x => x.Region?.Map == info && x.Monster != null)
            .OrderBy(x => x.RespawnIndex).ThenBy(x => x.Region.Description, StringComparer.Ordinal)
            .ThenBy(x => x.Monster.MonsterName, StringComparer.Ordinal).ToArray();
        var samples = new List<RuleRun>(rules.Length);
        var mapOccupied = ExistingSpawnedMonsters(map).Select(x => x.CurrentLocation).ToHashSet();
        var portalCells = PortalCells(info);
        var portalBuffer = PortalBufferCells(portalCells);
        var mapStartTime = SEnvir.Now;
        var groups = rules.Select(rule => new SpawnInfo(rule, null, 0)).ToArray();
        foreach (var spawnInfo in groups)
        {
            var rule = spawnInfo.Info;
            var isDistributed = hasDistributionApi && (bool)distributedProperty.GetValue(spawnInfo);
            var reason = ExclusionReason(rule, isDistributed);
            var configuredExpected = ExpectedSpawnCount(rule, map);
            var effectiveExpected = rule.RespawnIndex == map.RespawnIndex && !rule.EventSpawn
                ? configuredExpected : 0;
            spawnInfo.LastCheck = DateTime.MinValue;
            var placedBefore = ExistingSpawnedMonsters(map).ToHashSet();
            spawnInfo.DoSpawn(false);
            samples.Add(new RuleRun(rule, spawnInfo, isDistributed, reason, configuredExpected, effectiveExpected,
                NewGroupMonsters(map, spawnInfo, placedBefore).ToList(), new int[RefillPasses]));
        }

        // Refill all underfilled groups once per simulated scheduler tick, keeping every
        // previously spawned monster alive so each retry sees the same shared map pool.
        for (var pass = 0; pass < RefillPasses; pass++)
        {
            SEnvir.Now = mapStartTime.AddSeconds(pass + 1);
            foreach (var sample in samples.Where(x => x.SpawnInfo.AliveCount < x.EffectiveExpected))
            {
                sample.SpawnInfo.NextSpawn = DateTime.MinValue;
                var placedBefore = ExistingSpawnedMonsters(map).ToHashSet();
                sample.SpawnInfo.DoSpawn(false);
                var added = NewGroupMonsters(map, sample.SpawnInfo, placedBefore);
                sample.RefillAdded[pass] = added.Length;
                sample.Spawned.AddRange(added);
            }
        }

        foreach (var sample in samples)
        {
            var bucketCounts = new int[BucketCount];
            var duplicateCells = 0;
            var safeZoneHits = 0;
            var redZoneHits = 0;
            var portalHits = 0;
            foreach (var mob in sample.Spawned)
            {
                if (mob.CurrentCell == null) continue;
                bucketCounts[Bucket(mob.CurrentLocation, sample.Rule.Region.PointList)]++;
                if (mob.CurrentCell.SafeZone is { RedZone: false }) safeZoneHits++;
                if (mob.CurrentCell.SafeZone is { RedZone: true }) redZoneHits++;
                if (portalBuffer.Contains(mob.CurrentLocation)) portalHits++;
                if (sample.Distributed && (portalCells.Contains(mob.CurrentLocation) || mob.CurrentCell.Movements?.Count > 0))
                    throw new InvalidOperationException($"Distributed spawn on portal tile: {info.FileName} #{sample.Rule.Index} {mob.CurrentLocation}.");
                if (sample.Distributed && !mapOccupied.Add(mob.CurrentLocation)) duplicateCells++;
                else if (!sample.Distributed) mapOccupied.Add(mob.CurrentLocation);
            }
            sample.Buckets = bucketCounts;
            sample.DuplicateCells = duplicateCells;
            sample.SafeZoneHits = safeZoneHits;
            sample.RedZoneHits = redZoneHits;
            sample.PortalHits = portalHits;
            sample.Actual = sample.SpawnInfo.AliveCount;
            sample.PositionCount = sample.Spawned.Count(x => x.CurrentCell != null);
            if (hasDistributionApi && (sample.PositionCount != sample.Actual ||
                (sample.Distributed && (duplicateCells > 0 || safeZoneHits > 0))))
                throw new InvalidOperationException($"Spawn invariant failed: {info.FileName} #{sample.Rule.Index}; objects={sample.PositionCount}, alive={sample.Actual}, duplicates={duplicateCells}, safe-zone={safeZoneHits}.");
            sample.Unfilled = Math.Max(0, sample.EffectiveExpected - sample.Actual);
            if (sample.Unfilled > 0) rulesWithShortfall++;
            testedRules++;
        }

        foreach (var sample in samples)
        {
            var row = string.Join(',', new[]
            {
                "rule", Csv(info.FileName), Csv(info.Description), Csv(sample.Rule.Region.Description), Csv(sample.Rule.Region.RegionType.ToString()),
                Csv(sample.Rule.Monster.MonsterName), sample.Rule.RespawnIndex.ToString(), sample.Distributed ? "true" : "false",
                Csv(sample.Reason), sample.Rule.Count.ToString(), sample.EffectiveExpected.ToString(), string.Empty, sample.PositionCount.ToString(),
                sample.Actual.ToString(), (sample.PositionCount - sample.Actual).ToString(), sample.Unfilled.ToString(),
                sample.Buckets.Count(x => x > 0).ToString(), BucketColumns.ToString(), BucketRows.ToString(), Csv(string.Join(';', sample.Buckets)),
                sample.DuplicateCells.ToString(), sample.SafeZoneHits.ToString(), sample.RedZoneHits.ToString(), sample.PortalHits.ToString(),
                sample.RefillAdded[0].ToString(), sample.RefillAdded[1].ToString(), string.Empty
            });
            writer.WriteLine(row);
            if (IsBichon(info)) bichonRows++;
        }

        var distributedMapBuckets = new int[BucketCount];
        var distributedMapMobs = samples.Where(x => x.Distributed).SelectMany(x => x.Spawned).Where(x => !x.Dead).ToArray();
        foreach (var mob in distributedMapMobs)
        {
            var col = map.Width == 0 ? 0 : mob.CurrentLocation.X * BucketColumns / map.Width;
            var row = map.Height == 0 ? 0 : mob.CurrentLocation.Y * BucketRows / map.Height;
            distributedMapBuckets[Math.Clamp(row, 0, BucketRows - 1) * BucketColumns + Math.Clamp(col, 0, BucketColumns - 1)]++;
        }
        writer.WriteLine(string.Join(',', new[]
        {
            "map_summary", Csv(info.FileName), Csv(info.Description), Csv(string.Empty), Csv(string.Empty), Csv(string.Empty), "0", "true",
            "distributed_map_pool", string.Empty, string.Empty, string.Empty, distributedMapMobs.Length.ToString(),
            distributedMapMobs.Length.ToString(), "0", string.Empty, distributedMapBuckets.Count(x => x > 0).ToString(), BucketColumns.ToString(), BucketRows.ToString(),
            Csv(string.Join(';', distributedMapBuckets)), "", "", "", "", "", "", ""
        }));

        if (IsBichon(info))
        {
            var eligibleCells = map.ValidCells.Where(x => x.SafeZone is not { RedZone: false } && !portalBuffer.Contains(x.Location)).ToArray();
            var mapBuckets = new int[BichonColumns * BichonRows];
            var minX = eligibleCells.Length == 0 ? 0 : eligibleCells.Min(x => x.Location.X);
            var maxX = eligibleCells.Length == 0 ? 0 : eligibleCells.Max(x => x.Location.X);
            var minY = eligibleCells.Length == 0 ? 0 : eligibleCells.Min(x => x.Location.Y);
            var maxY = eligibleCells.Length == 0 ? 0 : eligibleCells.Max(x => x.Location.Y);
            var eligibleMonsterCount = 0;
            foreach (var mob in samples.SelectMany(sample => sample.Spawned).Where(mob => !mob.Dead))
            {
                if (mob.CurrentCell?.SafeZone is { RedZone: false } || portalBuffer.Contains(mob.CurrentLocation)) continue;
                eligibleMonsterCount++;
                var col = maxX == minX ? 0 : (mob.CurrentLocation.X - minX) * BichonColumns / (maxX - minX + 1);
                var row = maxY == minY ? 0 : (mob.CurrentLocation.Y - minY) * BichonRows / (maxY - minY + 1);
                mapBuckets[Math.Clamp(row, 0, BichonRows - 1) * BichonColumns + Math.Clamp(col, 0, BichonColumns - 1)]++;
            }
            writer.WriteLine(string.Join(',', new[]
            {
                "bichon_map", Csv(info.FileName), Csv(info.Description), Csv(string.Empty), Csv(string.Empty), Csv(string.Empty), "0", "", "eligible_walkable_cells",
                "", "", eligibleCells.Length.ToString(), eligibleMonsterCount.ToString(), "", "", "", mapBuckets.Count(x => x > 0).ToString(),
                BichonColumns.ToString(), BichonRows.ToString(), Csv(string.Join(';', mapBuckets)), "", "", "", "", "", "", "eligible 16x16 occupied bins"
            }));
        }

        if (info.FileName == "0" && hasDistributionApi && Config.DistributeOrdinaryRespawns)
        {
            var target = samples.First(x => x.Distributed && x.Actual > 0);
            for (var pass = 0; pass < 2; pass++)
            {
                var removed = ExistingSpawnedMonsters(map).First(x => ReferenceEquals(x.SpawnInfo, target.SpawnInfo));
                removed.Despawn();
                SEnvir.Now = target.SpawnInfo.NextSpawn.AddSeconds(-1);
                target.SpawnInfo.DoSpawn(false);
                if (target.SpawnInfo.AliveCount != target.Actual - 1)
                    throw new InvalidOperationException("Bichon refill fired before NextSpawn.");
                SEnvir.Now = target.SpawnInfo.NextSpawn;
                target.SpawnInfo.DoSpawn(false);
                var alive = ExistingSpawnedMonsters(map).Where(x => ReferenceEquals(x.SpawnInfo, target.SpawnInfo)).ToArray();
                if (target.SpawnInfo.AliveCount != target.Actual || alive.Length != target.Actual ||
                    alive.Select(x => x.CurrentLocation).Distinct().Count() != alive.Length)
                    throw new InvalidOperationException("Bichon timed refill did not restore the correct number of monsters.");
            }
            Console.WriteLine("PASS: Bichon two timed replacements; no early spawn, no count drift or duplicate locations.");
        }

        foreach (var ob in map.Objects.ToArray()) ob.Despawn();
        RemoveCachedMap(info);
        loadedMaps++;
        if (loadedMaps % 25 == 0) Console.WriteLine($"Processed {loadedMaps}/{mapInfos.Length} maps; rules={testedRules}, rulesWithShortfall={rulesWithShortfall}.");
        map = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    Console.WriteLine($"CSV={outputPath}");
    Console.WriteLine($"System.db sha256={dbHash}; maps={loadedMaps}; rules={testedRules}; rulesWithShortfall={rulesWithShortfall}; BichonRuleRows={bichonRows}; UsesDistributedPlacement={(hasDistributionApi ? "available" : "not present; all current rules reported as legacy")}");
    if (args.Contains("--require-full") && rulesWithShortfall != 0)
    {
        Console.Error.WriteLine($"FAIL: {rulesWithShortfall} eligible rules did not reach their configured targets.");
        Environment.ExitCode = 1;
    }
}
finally
{
    tempRoot.Delete(recursive: true);
}

static void RunSyntheticLegacyRegression()
{
    // Legacy placement samples eligible region points with replacement. Fixed seed
    // reproduces duplicate coordinates and distinct eligible cells left unused.
    var points = Enumerable.Range(0, BucketCount).Select(x => new Point(x, 0)).ToArray();
    var random = new Random(1);
    var selected = Enumerable.Range(0, points.Length).Select(_ => points[random.Next(points.Length)]).ToArray();
    var occupied = selected.Distinct().Count();
    var duplicates = selected.Length - occupied;
    var unfilled = points.Length - occupied;
    if (duplicates == 0 || unfilled == 0)
        throw new InvalidOperationException("Synthetic legacy-placement regression did not reproduce repeated cells.");
    Console.WriteLine($"SYNTHETIC legacy with-replacement placement: requested={points.Length}, occupied={occupied}, duplicatePlacements={duplicates}, unfilled={unfilled}, seed=1");
}

static void BindCollections(Session session)
{
    SEnvir.Session = session;
    SEnvir.MapInfoList = session.GetCollection<MapInfo>();
    SEnvir.InstanceInfoList = session.GetCollection<InstanceInfo>();
    SEnvir.DungeonInfoList = session.GetCollection<DungeonInfo>();
    SEnvir.SafeZoneInfoList = session.GetCollection<SafeZoneInfo>();
    SEnvir.MonsterInfoList = session.GetCollection<MonsterInfo>();
    SEnvir.RespawnInfoList = session.GetCollection<RespawnInfo>();
    SEnvir.MovementInfoList = session.GetCollection<MovementInfo>();
    SEnvir.MapRegionList = session.GetCollection<MapRegion>();
    SEnvir.NPCInfoList = session.GetCollection<NPCInfo>();
    SEnvir.QuestInfoList = session.GetCollection<QuestInfo>();
    SEnvir.BuffInfoList = session.GetCollection<BuffInfo>();
    SEnvir.CurrencyInfoList = session.GetCollection<CurrencyInfo>();
    SEnvir.ItemInfoList = session.GetCollection<ItemInfo>();
    SEnvir.FishingInfoList = session.GetCollection<FishingInfo>();
    SEnvir.DisciplineInfoList = session.GetCollection<DisciplineInfo>();
    SEnvir.FameInfoList = session.GetCollection<FameInfo>();
    SEnvir.GuildInfoList = session.GetCollection<GuildInfo>();
    SEnvir.CastleInfoList = session.GetCollection<CastleInfo>();
    SEnvir.Spawns.Clear();
    var maps = MapCache();
    maps.GetType().GetMethod("Clear")!.Invoke(maps, null);
}

static object MapCache() => typeof(SEnvir).GetField("Maps", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

static void RegisterMapForRuntimeHelpers(MapInfo info, Map map) =>
    MapCache().GetType().GetProperty("Item")!.SetValue(MapCache(), map, new object[] { info });

static void RemoveCachedMap(MapInfo info) =>
    MapCache().GetType().GetMethod("Remove", new[] { typeof(MapInfo) })!.Invoke(MapCache(), new object[] { info });

static void InvokeMapHelper(string methodName, MapInfo info)
{
    var method = typeof(SEnvir).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic,
        binder: null, types: new[] { typeof(InstanceInfo), typeof(byte), typeof(MapInfo) }, modifiers: null);
    method?.Invoke(null, new object[] { null, (byte)0, info });
}

static void ApplySafeZones(MapInfo info, Map map)
{
    foreach (var safeZone in SEnvir.SafeZoneInfoList.Binding.Where(x => x.Region?.Map == info))
    {
        if (safeZone.Region.PointList == null) safeZone.Region.CreatePoints(map.Width);
        foreach (var point in safeZone.Region.PointList)
        {
            var cell = map.GetCell(point);
            if (cell != null) cell.SafeZone = safeZone;
        }
    }
}

static IEnumerable<ServerMonster> ExistingSpawnedMonsters(Map map) => map.Objects.OfType<ServerMonster>().Where(x => !x.Dead);

static HashSet<Point> PortalCells(MapInfo info)
{
    var points = new HashSet<Point>();
    foreach (var region in info.Regions)
    {
        var arrival = (region.DestinationMovements?.Any(move => move.SourceRegion?.Map != null) ?? false)
            || (region.BindSafeZones?.Count ?? 0) > 0;
        var exit = region.SourceMovements?.Any(move => move.DestinationRegion?.Map != null) ?? false;
        if (!arrival && !exit) continue;
        points.UnionWith(region.PointList);
    }
    return points;
}

static HashSet<Point> PortalBufferCells(HashSet<Point> portals)
{
    var points = new HashSet<Point>();
    foreach (var point in portals)
        for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                points.Add(new Point(point.X + x, point.Y + y));
    return points;
}

static ServerMonster[] NewGroupMonsters(Map map, SpawnInfo owner, HashSet<ServerMonster> before) =>
    ExistingSpawnedMonsters(map).Where(x => !before.Contains(x) && ReferenceEquals(x.SpawnInfo, owner)).ToArray();

static int ExpectedSpawnCount(RespawnInfo rule, Map map)
{
    if (!rule.Monster.IsBoss && map.Info.Dungeon != null)
        return (int)Math.Ceiling(Math.Clamp(rule.Count * map.Info.Dungeon.SpawnMultiplier, 0M, int.MaxValue));
    return Math.Max(0, rule.Count);
}

static string ExclusionReason(RespawnInfo rule, bool distributed)
{
    var reasons = new List<string>();
    if (rule.Monster.IsBoss) reasons.Add("boss");
    if (rule.Announce) reasons.Add("announce");
    if (rule.EventSpawn) reasons.Add("event");
    if (rule.RespawnIndex != 0) reasons.Add("linked_or_instance_respawn_index");
    if (rule.Delay >= 1000000) reasons.Add("time_window");
    if (rule.Monster.AI is 30 or 69) reasons.Add($"special_ai_{rule.Monster.AI}");
    if (rule.Monster.Flag is MonsterFlag.CastleDefense or MonsterFlag.CastleObjective or MonsterFlag.Blocker) reasons.Add("castle_or_linked_flag");
    if ((rule.Region?.QuestTasks?.Count ?? 0) > 0) reasons.Add("quest_region");
    if (SEnvir.MovementInfoList?.Binding.Any(move => move.NeedSpawn == rule) == true) reasons.Add("linked_movement");
    if (!distributed && reasons.Count == 0) reasons.Add("other_legacy_rule");
    return reasons.Count == 0 ? "ordinary_area" : string.Join('|', reasons);
}

static bool IsBichon(MapInfo info) =>
    info.FileName == "0" || info.Description?.Contains("Bichon", StringComparison.OrdinalIgnoreCase) == true;

static int Bucket(Point point, IReadOnlyList<Point> region)
{
    if (region == null || region.Count == 0) return 0;
    var minX = region.Min(x => x.X);
    var maxX = region.Max(x => x.X);
    var minY = region.Min(x => x.Y);
    var maxY = region.Max(x => x.Y);
    var column = maxX == minX ? 0 : (point.X - minX) * BucketColumns / (maxX - minX + 1);
    var row = maxY == minY ? 0 : (point.Y - minY) * BucketRows / (maxY - minY + 1);
    return Math.Clamp(row, 0, BucketRows - 1) * BucketColumns + Math.Clamp(column, 0, BucketColumns - 1);
}

static int StableSeed(string text)
{
    unchecked
    {
        uint hash = 2166136261;
        foreach (var value in Encoding.UTF8.GetBytes(text ?? string.Empty)) hash = (hash ^ value) * 16777619;
        return (int)hash;
    }
}

static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

sealed class RuleRun(RespawnInfo rule, SpawnInfo spawnInfo, bool distributed, string reason,
    int configuredExpected, int effectiveExpected, List<ServerMonster> spawned, int[] refillAdded)
{
    public RespawnInfo Rule { get; } = rule;
    public SpawnInfo SpawnInfo { get; } = spawnInfo;
    public bool Distributed { get; } = distributed;
    public string Reason { get; } = reason;
    public int ConfiguredExpected { get; } = configuredExpected;
    public int EffectiveExpected { get; } = effectiveExpected;
    public List<ServerMonster> Spawned { get; } = spawned;
    public int[] RefillAdded { get; } = refillAdded;
    public int Actual { get; set; }
    public int PositionCount { get; set; }
    public int Unfilled { get; set; }
    public int[] Buckets { get; set; } = [];
    public int DuplicateCells { get; set; }
    public int SafeZoneHits { get; set; }
    public int RedZoneHits { get; set; }
    public int PortalHits { get; set; }
}
