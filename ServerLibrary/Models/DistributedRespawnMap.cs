using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Library;
using Server.Envir;

namespace Server.Models;

/// <summary>One shared placement pool per map; rules retain ownership, quotas and timers.</summary>
internal sealed class DistributedRespawnMap
{
    internal const int BucketSize = 16;
    private const int PortalBuffer = 3;
    private readonly Dictionary<(int X, int Y), Bucket> _buckets = new();
    private readonly PriorityQueue<Bucket, (double Density, int Tie)> _available = new();
    private readonly Map _map;
    private readonly Dictionary<SpawnInfo, int> _targets = new();
    private int _capacity;
    private int _registeredCount = -1;
    private DateTime _sampleTime;
    private bool _sampled;

    private sealed class Bucket
    {
        public readonly List<Cell> Cells = new();
        public int Living, Cursor;
        public double Density => (Living + 1d) / Cells.Count;
    }

    public DistributedRespawnMap(Map map)
    {
        _map = map;
        if (map.ValidCells.Count == 0) return;

        var fixedBossCells = new HashSet<Point>();
        var portalCells = new HashSet<Point>();
        var portalBuffer = new HashSet<Point>();
        var arrivals = new List<Cell>();
        foreach (var region in map.Info.Regions)
        {
            if (region.PointList == null) continue;
            if (region.PointList.Count == 1 && region.Respawns?.Any(rule =>
                rule.Count > 0 && !rule.EventSpawn && rule.RespawnIndex == map.RespawnIndex && rule.Monster?.IsBoss == true) == true)
                fixedBossCells.Add(region.PointList[0]);
            bool arrival = (region.DestinationMovements?.Any(move => move.SourceRegion?.Map != null) ?? false)
                || (region.BindSafeZones?.Count ?? 0) > 0;
            bool exit = (region.SourceMovements?.Any(move => move.DestinationRegion?.Map != null) ?? false);
            if (!arrival && !exit) continue;
            foreach (var point in region.PointList)
            {
                if (map.GetCell(point) is not { } cell) continue;
                if (arrival) arrivals.Add(cell);
                portalCells.Add(point);
                for (int x = -PortalBuffer; x <= PortalBuffer; x++)
                for (int y = -PortalBuffer; y <= PortalBuffer; y++)
                    portalBuffer.Add(new Point(point.X + x, point.Y + y));
            }
        }

        // Traverse terrain, not the spawn filter; safety zones and doors are walkable.
        var visited = new bool[map.Width, map.Height];
        var pending = new Queue<Cell>();
        void Visit(Cell cell)
        {
            if (cell == null || visited[cell.Location.X, cell.Location.Y]) return;
            visited[cell.Location.X, cell.Location.Y] = true;
            pending.Enqueue(cell);
        }
        List<Cell> Drain()
        {
            var result = new List<Cell>();
            while (pending.TryDequeue(out var cell))
            {
                result.Add(cell);
                for (int d = 0; d < 8; d++) Visit(map.GetCell(Functions.Move(cell.Location, (MirDirection)d)));
            }
            return result;
        }
        foreach (var cell in arrivals)
        {
            if (visited[cell.Location.X, cell.Location.Y]) continue;
            Visit(cell);
            AddComponent(Drain());
        }
        if (arrivals.Count == 0)
        {
            // Without a known arrival, use the largest continuous area instead of spawning on isolated tiles.
            var terrain = new List<Cell>();
            foreach (var cell in map.ValidCells)
            {
                if (visited[cell.Location.X, cell.Location.Y]) continue;
                Visit(cell);
                var component = Drain();
                if (component.Count > terrain.Count) terrain = component;
            }
            AddComponent(terrain);
        }

        void AddComponent(List<Cell> terrain)
        {
            bool buffered = terrain.Any(cell => cell.SafeZone is not { RedZone: false } && !portalBuffer.Contains(cell.Location));
            foreach (var cell in terrain)
            {
                // Small linked rooms can fit wholly within the buffer; keep them playable,
                // but still never spawn on an arrival, exit or a protected town cell.
                if (cell.SafeZone is { RedZone: false } || portalCells.Contains(cell.Location) ||
                    fixedBossCells.Contains(cell.Location) || cell.Movements?.Count > 0 ||
                    (buffered && portalBuffer.Contains(cell.Location))) continue;
                var key = Key(cell.Location);
                if (!_buckets.TryGetValue(key, out var bucket)) _buckets.Add(key, bucket = new Bucket());
                bucket.Cells.Add(cell);
                _capacity++;
            }
        }
    }

    public int GetTarget(SpawnInfo rule, List<SpawnInfo> rules)
    {
        if (_registeredCount != rules.Count)
        {
            _registeredCount = rules.Count;
            _targets.Clear();
            long total = rules.Sum(r => (long)r.ConfiguredTarget);
            if (total > _capacity)
            {
                // A cramped map cannot meet all quotas without overlap. Allocate seats
                // proportionally, with deterministic remainder ties, not first-rule-wins.
                var shares = rules.Select(r => (Rule: r, Numerator: (long)r.ConfiguredTarget * _capacity)).ToArray();
                int left = _capacity;
                foreach (var share in shares)
                {
                    int count = (int)(share.Numerator / total);
                    _targets.Add(share.Rule, count);
                    left -= count;
                }
                foreach (var share in shares.OrderByDescending(s => s.Numerator % total).ThenBy(s => s.Rule.Info.Index).Take(left))
                    _targets[share.Rule]++;
                SEnvir.Log($"[Respawn] {_map.Info.FileName}: {total} ordinary spawn slots share {_capacity} eligible cells; targets scaled proportionally.");
            }
        }
        return _targets.TryGetValue(rule, out int capped) ? capped : rule.ConfiguredTarget;
    }

    public bool TrySpawn(MonsterObject monster)
    {
        if (!_sampled || _sampleTime != SEnvir.Now)
        {
            _sampled = true;
            _sampleTime = SEnvir.Now;
            _available.Clear();
            foreach (var bucket in _buckets.Values) { bucket.Living = 0; bucket.Cursor = 0; }
            foreach (var ob in _map.Objects)
                if (ob is MonsterObject { Dead: false, DistributedSpawnOrigin: not null } && _buckets.TryGetValue(Key(ob.CurrentLocation), out var bucket))
                    bucket.Living++;
            foreach (var bucket in _buckets.Values) Enqueue(bucket);
        }

        while (_available.TryDequeue(out var bucket, out _))
        {
            while (bucket.Cursor < bucket.Cells.Count)
            {
                int i = bucket.Cursor;
                // Partial Fisher–Yates: randomize each new candidate without cloning or pre-shuffling the whole map.
                int j = SEnvir.Random.Next(i, bucket.Cells.Count);
                var cell = bucket.Cells[j];
                (bucket.Cells[j], bucket.Cells[i]) = (bucket.Cells[i], cell);
                bucket.Cursor++;
                if (cell.SafeZone is { RedZone: false } || cell.Movements?.Count > 0 ||
                    cell.Objects?.Any(ob => ob.Blocking || ob is MonsterObject { Dead: false }) == true) continue;
                if (!monster.Spawn(_map, cell.Location)) continue;
                monster.DistributedSpawnOrigin = cell.Location;
                bucket.Living++;
                if (bucket.Cursor < bucket.Cells.Count) Enqueue(bucket);
                return true;
            }
        }
        return false;
    }

    private void Enqueue(Bucket bucket) => _available.Enqueue(bucket, (bucket.Density, SEnvir.Random.Next()));
    private static (int, int) Key(Point p) => (p.X / BucketSize, p.Y / BucketSize);
}
