using System.Collections.Generic;
using System.Drawing;

namespace Library.Network
{
    public static class GmInspectProtocol
    {
        // Sent in the legacy SelectLanguage packet after StartGame. Old servers ignore it.
        public const string Probe = "ZIRCON_GM_INSPECT_V1";
        public const int ProbeMapIndex = -1;
    }

    public sealed class GmInspectPort
    {
        public int MovementIndex { get; set; }
        public List<Point> SourcePoints { get; set; } = new();
        public int DestinationMapIndex { get; set; } = -1;
        public string DestinationMapName { get; set; }
        public string DestinationMapFileName { get; set; }
        public List<GmInspectDestination> Destinations { get; set; } = new();
        public int MinimumLevel { get; set; }
        public int MaximumLevel { get; set; }
        public RequiredClass RequiredClass { get; set; }
        public string NeedItem { get; set; }
        public int NeedSpawnIndex { get; set; } = -1;
        public bool NeedHole { get; set; }
        public string NeedInstance { get; set; }
        public MovementEffect Effect { get; set; }
        // Diagnostic only; empty does not mean traversal was tested or is allowed.
        public string Reason { get; set; }
    }

    public sealed class GmInspectDestination
    {
        public Point Location { get; set; }
        // Empty: loaded base-map terrain is walkable (occupancy and gates not tested).
        public string Reason { get; set; }
    }

    public sealed class GmInspectNpc
    {
        public int NpcIndex { get; set; }
        public string Name { get; set; }
        public bool HasLocation { get; set; }
        public Point Location { get; set; }
        // False: Location is a configured region point, not an observed NPC position.
        public bool IsSpawned { get; set; }
        public int EntryPageIndex { get; set; } = -1;
        public string EntryPage { get; set; }
        public List<string> Services { get; set; } = new();
    }
}

namespace Library.Network.ClientPackets
{
    public sealed class GmInspectRequest : Packet
    {
        // DB index (not the local list position); -1 is reserved for the capability response.
        public int MapIndex { get; set; }
    }
}

namespace Library.Network.ServerPackets
{
    public sealed class GmInspectResult : Packet
    {
        public GmInspectResult() => ObserverPacket = false;

        // Session permission, not query success. Check Reason and MapIndex as well.
        public bool Allowed { get; set; }
        public int MapIndex { get; set; }
        public string MapName { get; set; }
        public string MapFileName { get; set; }
        public string Reason { get; set; }
        public string SystemDatabaseVersion { get; set; }
        public bool Truncated { get; set; }
        public List<GmInspectPort> Ports { get; set; } = new();
        public List<GmInspectNpc> Npcs { get; set; } = new();
    }
}
