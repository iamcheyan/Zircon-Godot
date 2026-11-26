using Library;
using Library.Network;
using Library.Network.ServerPackets;
using Library.SystemModels;
using Server.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Server.Envir
{
    // Definition and already-loaded base-world reads only. Never call GetMap, CreatePoints,
    // the movement evaluator, or NPC interaction from this inspection path.
    internal static class GmInspection
    {
        private const int RowLimit = 256;
        private const int PointLimit = 64;
        private const int NpcGraphLimit = 128;
        private const int TextLimit = 256;

        internal static string DatabaseVersion() => Text(SEnvir.Session?.GetCollection<SystemDatabaseInfo>().Binding
            .FirstOrDefault(x => x.Name == MirDB.Session.SystemDatabaseInfoName)?.Version);

        internal static void PopulateMap(GmInspectResult result)
        {
            MapInfo info = SEnvir.MapInfoList?.Binding.FirstOrDefault(x => x.Index == result.MapIndex);
            if (info == null)
            {
                result.Reason = "MapNotFound";
                return;
            }

            result.MapName = Text(info.Description);
            result.MapFileName = Text(info.FileName);
            Map loaded = SEnvir.GetLoadedBaseMap(info);
            if (info.Regions == null) return;

            var liveNpcs = new Dictionary<NPCInfo, Point>();
            if (loaded != null)
            {
                foreach (NPCObject npc in loaded.NPCs)
                    if (npc.NPCInfo != null)
                        liveNpcs.TryAdd(npc.NPCInfo, npc.CurrentLocation);
            }

            foreach (MapRegion region in info.Regions)
            {
                if (region.SourceMovements != null)
                {
                    foreach (MovementInfo movement in region.SourceMovements)
                    {
                        if (result.Ports.Count == RowLimit)
                        {
                            result.Truncated = true;
                            break;
                        }
                        result.Ports.Add(Port(movement, loaded, result));
                    }
                }
                if (region.NPCs != null)
                {
                    foreach (NPCInfo npc in region.NPCs)
                    {
                        if (result.Npcs.Count == RowLimit)
                        {
                            result.Truncated = true;
                            break;
                        }
                        result.Npcs.Add(Npc(npc, loaded, liveNpcs, result));
                    }
                }
            }
        }

        private static GmInspectPort Port(MovementInfo movement, Map sourceMap, GmInspectResult result)
        {
            MapRegion source = movement.SourceRegion;
            MapRegion destination = movement.DestinationRegion;
            MapInfo target = destination?.Map;
            Map loadedTarget = SEnvir.GetLoadedBaseMap(target);
            var port = new GmInspectPort
            {
                MovementIndex = movement.Index,
                DestinationMapIndex = target?.Index ?? -1,
                DestinationMapName = Text(target?.Description),
                DestinationMapFileName = Text(target?.FileName),
                MinimumLevel = target?.MinimumLevel ?? 0,
                MaximumLevel = target?.MaximumLevel ?? 0,
                RequiredClass = target?.RequiredClass ?? RequiredClass.None,
                NeedItem = Text(movement.NeedItem?.ItemName),
                NeedSpawnIndex = movement.NeedSpawn?.Index ?? -1,
                NeedHole = movement.NeedHole,
                NeedInstance = Text(movement.NeedInstance?.Name),
                Effect = movement.Effect,
            };

            // Source registration uses PointRegion; destination selection uses PointList when present.
            CopyPoints(source?.PointRegion ?? System.Array.Empty<Point>(), result, port.SourcePoints.Add);
            CopyPoints(Points(destination, loadedTarget), result, point => port.Destinations.Add(new GmInspectDestination
            {
                Location = point,
                Reason = TerrainReason(loadedTarget, point),
            }));

            port.Reason = target == null ? "MissingDestinationMap"
                : port.SourcePoints.Count == 0 ? "NoPoints"
                : port.Destinations.Count == 0 ? MissingPointsReason(destination)
                : port.Destinations.Any(point => point.Reason == "BlockedTerrain") ? "BlockedTerrain"
                : port.Destinations.Any(point => point.Reason == "OutOfBounds") ? "OutOfBounds"
                : sourceMap != null && port.SourcePoints.Any(point => sourceMap.GetCell(point) == null) ? "InvalidSourceTerrain"
                : loadedTarget == null ? "TerrainUnknown"
                : string.Empty;
            return port;
        }

        private static string TerrainReason(Map map, Point point)
        {
            if (map == null) return "TerrainUnknown";
            if (point.X < 0 || point.Y < 0 || point.X >= map.Width || point.Y >= map.Height) return "OutOfBounds";
            return map.GetCell(point) == null ? "BlockedTerrain" : string.Empty;
        }

        private static IEnumerable<Point> Points(MapRegion region, Map loaded)
        {
            if (region == null) yield break;
            if (region.PointList != null)
            {
                foreach (Point point in region.PointList) yield return point;
            }
            else if (region.BitRegion != null && loaded?.Width > 0)
            {
                for (int i = 0; i < region.BitRegion.Length; i++)
                    if (region.BitRegion[i]) yield return new Point(i % loaded.Width, i / loaded.Width);
            }
            else if (region.PointRegion != null)
            {
                foreach (Point point in region.PointRegion) yield return point;
            }
        }

        private static string MissingPointsReason(MapRegion region) =>
            region?.BitRegion != null && region.PointList == null ? "CoordinatesUnavailable" : "NoPoints";

        private static void CopyPoints(IEnumerable<Point> points, GmInspectResult result, System.Action<Point> add)
        {
            int count = 0;
            foreach (Point point in points)
            {
                if (count++ == PointLimit)
                {
                    result.Truncated = true;
                    break;
                }
                add(point);
            }
        }

        private static GmInspectNpc Npc(NPCInfo info, Map loaded, Dictionary<NPCInfo, Point> liveNpcs, GmInspectResult result)
        {
            bool spawned = liveNpcs.TryGetValue(info, out Point location);
            bool hasLocation = spawned;
            if (!spawned)
            {
                using var points = Points(info.Region, loaded).GetEnumerator();
                hasLocation = points.MoveNext();
                if (hasLocation) location = points.Current;
            }

            return new GmInspectNpc
            {
                NpcIndex = info.Index,
                Name = Text(info.NPCName),
                HasLocation = hasLocation,
                Location = location,
                IsSpawned = spawned,
                EntryPageIndex = info.EntryPage?.Index ?? -1,
                EntryPage = Text(info.EntryPage?.Description),
                Services = Services(info.EntryPage, result),
            };
        }

        private static List<string> Services(NPCPage entry, GmInspectResult result)
        {
            var seen = new HashSet<NPCPage>();
            var pending = new Queue<NPCPage>();
            var services = new SortedSet<string>(System.StringComparer.Ordinal);
            int remaining = NpcGraphLimit;
            AddPage(entry);
            while (pending.TryDequeue(out NPCPage page))
            {
                bool hasDialog = !string.IsNullOrEmpty(page.Say);
                if (hasDialog && page.DialogType != NPCDialogType.None)
                    services.Add(page.DialogType.ToString());

                if (page.Actions != null)
                    foreach (NPCAction action in page.Actions)
                    {
                        if (!Spend()) break;
                        if (action.ActionType == NPCActionType.Storage) services.Add("Storage");
                        if (action.ActionType == NPCActionType.Teleport && (action.MapParameter1 != null || action.InstanceParameter1 != null))
                            services.Add("Teleport");
                    }

                if (!hasDialog || page.DialogType is NPCDialogType.RollDie or NPCDialogType.RollYut)
                    AddPage(page.SuccessPage);
                if (hasDialog && page.Buttons != null)
                    foreach (NPCButton button in page.Buttons)
                    {
                        if (!Spend()) break;
                        AddPage(button.DestinationPage);
                    }
                if (page.Checks != null)
                    foreach (NPCCheck check in page.Checks)
                    {
                        if (!Spend()) break;
                        AddPage(check.FailPage);
                    }
            }
            return services.ToList();

            void AddPage(NPCPage page)
            {
                if (page != null && !seen.Contains(page) && Spend())
                {
                    seen.Add(page);
                    pending.Enqueue(page);
                }
            }

            bool Spend()
            {
                if (remaining > 0)
                {
                    remaining--;
                    return true;
                }
                result.Truncated = true;
                return false;
            }
        }

        private static string Text(string value)
        {
            if (value == null) return string.Empty;
            if (value.Length <= TextLimit) return value;
            int length = char.IsHighSurrogate(value[TextLimit - 1]) ? TextLimit - 1 : TextLimit;
            return value[..length];
        }
    }
}
