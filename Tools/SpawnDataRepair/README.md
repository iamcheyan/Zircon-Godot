# Spawn data repair for the 2026.10.18.8 snapshot

Offline, snapshot-specific MirDB migration. `repair.json` pins the input DB and map files by SHA-256. The tool opens only a private copy, defaults to preview, and refuses an existing output directory. It writes `System.db` only after reloading the private copy and checking the edited fields. No service, `Users.db`, map file, or canonical DB is modified.

From the source worktree, using .NET 10 and an existing copy of the baseline DB:

```sh
maps=/home/tetsuya/development/zircon/Debug/ServerCore/Map
input=../repair-db/System.before.db
dotnet run --project Tools/SpawnDataRepair -- "$input" "$maps"
dotnet run --project Tools/SpawnDataRepair -- "$input" "$maps" ../repair-db/rebuilt-284
dotnet run --project Tests/SpawnDistributionChecks -- \
  ../repair-db/rebuilt-284 "$maps" ../repair-db/rebuilt-284.csv --require-full --expect-maps=284
```

The row and coordinate changes are deterministic. MirDB advances `SystemDatabaseInfo.Version` using the local calendar date, so replaying on a different day changes the output hash.

## Findings and decisions

- **D604 and D605:** Current maps are 50×50, not the legacy respawn geometry. Their respective walkable, reachable counts are 242 and 229; excluding the three-tile arrival/exit buffer leaves 151 and 180 eligible cells before any boss reservation. Preserve the old ordinary species ratios by largest-remainder apportionment. D604 becomes 107 Bone Bladesmen + 44 Bone Archers (from 300 + 125); leave its two Skeleton Enforcers unchanged. D605 becomes 130 + 49 (from 400 + 150), reserving one of the 180 slots for Taedu. D605's old `Whole Map` region has 60 points, none walkable; rebuild it from the eligible cells for the two ordinary rules, and point rule #4735 to a new singleton region at `(29,28)`. The anchor is in the interior chamber, tied for maximum walking distance (14 steps) from the four portal/arrival tiles. The existing Zircon rule and the archived RespawnInfo export support one boss at 300 minutes but do **not** supply a valid historical coordinate on this map. `(29,28)` is a geometry-based replacement. The server's fixed-boss reservation keeps this interior cell out of the ordinary pool independent of spawn order.
- **D8101–D8104 and D8201–D8204:** Archived Mud3 `Mon_Def/MidBoss.gen` lines 101–109 explicitly specify `蚂蚁将军` at `(100,100)`, range 90, **count 1, interval 120** for all eight maps. `MonGen.txt` line 9 loads `MidBoss.gen`. Each north/east pair uses byte-identical map geometry. Steps 2 and 4 have a blocked center; steps 1 and 3 currently stack 90 bosses on a single valid cell. On all eight maps, intersect the source's centered ±90 footprint (inclusive tile range) with walkable, arrival-reachable cells outside protected zones and the portal buffer. Restore the source count and delay. Regions have 7,250 / 7,275 / 6,448 / 7,403 eligible points on steps 1–4 respectively, mirrored in both branches. No Ant Soldier or Armoured Ant rules are edited.
- **D9031/D9032:** Archived Mud3 `Mon_Def/!Lv12_SinGiSun.gen` lines 63 and 65 specify one `爆毒神魔` and one `神舰守卫`, respectively, each at 20 minutes. Both identities agree with the existing d903 spawns (`Otherworld Poison Demon` and `Otherworld Ship Guard`, not `Otherworld Ship Guard 1`). Neither archived footprint intersects eligible terrain on the installed 140×140 maps. Recenter to the nearest eligible cell in Chebyshev distance, breaking ties by X then Y, retaining the source radius: D9031 `(10,10) r5` → `(23,23) r5` (34 region points); D9032 `(15,15) r15` → `(38,25) r15` (118 region points). These are ordinary rules: the configured regions are used by legacy placement, while distributed placement uses the room's eligible map-wide pool.

Source paths are relative to `Mir3-Research` at `758e006a`: Mud3 text is under `docs/research/ei-ui-layout/sources/hero-kill-mud3-2026-09-25/mud3/Envir/` (GB18030); D605's old rule is in `docs/database/data/RespawnInfo.2.md` (#4735). See also `docs/map-migration-comparison.md` for the D605 dimensions.

## Ship connection check

Both endpoints of each of the four links are walkable, and both room-door sources are reachable from both walkable d904 arrivals in `d903` (`(53,52)` and `(48,42)`). Use the exact lowercase `d903.map` linked from `MapInfo`, **not** the different `D903.map` in the same directory.

| Movement | Source | Destination |
| --- | --- | --- |
| #4739 | d903 `(144,179)` | D9031 `(17,29)` |
| #4740 | D9031 `(17,29)` | d903 `(144,180)` |
| #4780 | d903 `(72,60)` | D9032 `(33,42)` |
| #4781 | D9032 `(32,43)` | d903 `(72,60)` |

D9031 shares its landing and return-door tile; D9032 returns onto the corresponding d903 outbound tile. These are existing links, verified by offline geometry, not changed or tested with a client. Separately, **D9022 → d903 movement #4733** has a blocked destination `(196,161)` in region #10998. It does not invalidate access from d904; this migration leaves all movements unchanged.
