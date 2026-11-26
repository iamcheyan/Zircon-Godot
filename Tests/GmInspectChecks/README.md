# GM inspection protocol checks

Run from the worktree root:

```sh
dotnet run --project Tests/GmInspectChecks --artifacts-path .artifacts/gm-check
```

Optionally pass the absolute path of a pre-change `LibraryCore.dll` after `--` to compare every legacy packet ID and check that the pre-change decoder accepts the opt-in frame. The checks exercise production handlers and wire serialization using an in-memory fixture, not a live server, save or client.

## Client contract

- After successful `StartGame`, send the legacy `C.SelectLanguage` with `Language = GmInspectProtocol.Probe` (`ZIRCON_GM_INSPECT_V1`). No response means unsupported; leave the feature unavailable. Do not send the new query until the capability response (`MapIndex = -1`) has `Allowed = true`. Reset this client-side state on logout/reconnect and probe again for each new game session.
- Send `C.GmInspectRequest { MapIndex = mapInfo.Index }` (database ID, not list position). Match `S.GmInspectResult.MapIndex` to the selected map. `Allowed` means permission, not query success; `Reason` is empty on success, otherwise `NotInGame`, `TestServerRequired`, `AdminRequired`, or `MapNotFound` (still authorized). Probe and query have separate per-connection 500 ms intervals; excess requests are silently dropped.
- See `LibraryCore/Network/GmInspectPackets.cs` for reflected payload fields. Ports are outgoing configured movements and include destination-map gates, not a player-specific evaluation. Destination point reasons: empty means walkable loaded base-map terrain, `BlockedTerrain`, `OutOfBounds`, or `TerrainUnknown`. Port summaries may additionally report `InvalidSourceTerrain`, `MissingDestinationMap`, `NoPoints`, or `CoordinatesUnavailable`. This does not predict actual traversal, occupancy, or instance availability.
- NPC positions are live on the loaded base map when `IsSpawned`, otherwise the first available configured region point when `HasLocation`. Service tags are static evidence from configured page dialog types and storage/teleport actions, not proof of access or service success. Empty tags mean no service was evidenced.
- Missing reference indices are `-1`; missing text (including unavailable `SystemDatabaseVersion`) is empty. Labels are capped at 256 characters. `Truncated` marks omitted rows/points or bounded NPC page traversal: up to 256 rows per list, 64 points per endpoint, and 128 NPC graph visits/entries per NPC.
