# SpawnDistributionChecks

Offline regression executable for the ServerLibrary respawn path. It copies the supplied `System.db` into a temporary directory, opens that copy with MirDB, and reads map files from the supplied map directory. The source database is never opened for writing. Each of the 282 maps that has configured respawn rules is constructed and processed individually; spawned objects remain alive until all rules and up to two immediate refill attempts for that map have completed, then the map is released.

Run from the Zircon checkout (or pass absolute paths):

```sh
dotnet run --project Tests/SpawnDistributionChecks -- \
  Debug/ServerCore/Database Debug/ServerCore/Map \
  .artifacts/uniform-spawns/spawn-distribution.csv
```

The CSV has one row per configured rule, with the configured and effective expected counts, actual positioned count beside the tracked `AliveCount` and their delta, unfilled count, 32 region-relative bucket counts (8 by 4), collision/safe-zone/RedZone/portal-buffer observations, and the number added by each of two scheduler-tick refill passes. Each map also has a `map_summary` row with its shared distributed pool in 32 map-wide buckets (8 by 4). All rules on a map share one live monster pool during initial spawning and refills. Capacity shortfalls are reported and do not fail the run. RedZone cells are allowed. Bichon has an additional map snapshot row with occupied eligible map bins in a 16 by 16 grid; eligible cells are walkable cells outside protected safe zones and the movement/portal buffer. `System.db` SHA-256, map/rule totals, API availability, and shortfall totals are printed to stdout.

When `SpawnInfo.UsesDistributedPlacement` exists, the runner reads it through reflection and marks each rule accordingly. Distributed placements are checked against the shared map occupancy pool for repeated cells and safe-zone/portal locations. Legacy rules remain measured and reported. If the API is absent, the runner can still produce the full current legacy baseline and reports that all rules are legacy. The deterministic synthetic regression runs first; it demonstrates the original with-replacement behavior (32 requests, seed 1) and reports duplicate placements and unfilled distinct positions.

Run only the deterministic synthetic demonstration with:

```sh
dotnet run --project Tests/SpawnDistributionChecks -- --synthetic-only
```
