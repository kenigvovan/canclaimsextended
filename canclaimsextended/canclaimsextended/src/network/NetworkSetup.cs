using canclaimsextended.src.geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>The one channel of the mod, and the map zones plots are sent by.</summary>
    public static class NetworkSetup
    {
        public const string CHANNEL = "canclaimsextended";
        /// <summary>Side of a map zone in blocks, on X and Z.</summary>
        public const int ZONE_SIZE_BLOCKS = 512;
        /// <summary>Zones sent around the player: this many in every direction.</summary>
        public const int SEND_RADIUS = 1;
        /// <summary>A zone further than this from the player is forgotten, so coming back sends it fresh.</summary>
        public const int KEEP_RADIUS = 2;

        // One list for both sides: message types must be registered in the same order.
        static readonly System.Type[] MessageTypes =
        {
            typeof(PlotZonePacket), typeof(PlotUpdatePacket), typeof(PlotRemovedPacket),
            typeof(GuiRequestPacket), typeof(GuiStatePacket),
            typeof(EmblemsPacket),
            // New types go at the end, so the ones above keep their numbers.
            typeof(LandZonePacket), typeof(LandUpdatePacket),
            typeof(BordersStatePacket)
        };

        /// <summary>
        /// The server end of the channel with every handler and sender on it. The client end is
        /// wired in ClientSetup, next to the stores its handlers fill.
        /// </summary>
        public static IServerNetworkChannel WireServer(ICoreServerAPI sapi)
        {
            IServerNetworkChannel channel = RegisterServer(sapi);
            PlotSyncServer.Init(sapi, channel);
            GuiStateServer.Init(channel);
            EmblemSync.Init(sapi, channel);
            return channel;
        }

        public static IServerNetworkChannel RegisterServer(ICoreServerAPI sapi)
        {
            IServerNetworkChannel channel = sapi.Network.RegisterChannel(CHANNEL);
            foreach (System.Type type in MessageTypes) channel.RegisterMessageType(type);
            return channel;
        }

        public static IClientNetworkChannel RegisterClient(ICoreClientAPI capi)
        {
            IClientNetworkChannel channel = capi.Network.RegisterChannel(CHANNEL);
            foreach (System.Type type in MessageTypes) channel.RegisterMessageType(type);
            return channel;
        }

        public static int ZoneOf(double coord)
        {
            return (int)System.Math.Floor(coord / ZONE_SIZE_BLOCKS);
        }

        /// <summary>The zone a chunk of territory lies in; zones are a whole number of chunks wide.</summary>
        public static long ZoneKeyOf(ChunkPos chunk)
        {
            return ZoneKey(ZoneOf(chunk.MinBlockX), ZoneOf(chunk.MinBlockZ));
        }

        public static long ZoneKey(int zx, int zz)
        {
            return ((long)zx << 32) | (uint)zz;
        }

        public static void ZoneOfKey(long key, out int zx, out int zz)
        {
            zx = (int)(key >> 32);
            zz = (int)(uint)key;
        }

        /// <summary>The zone as a box over the whole world height.</summary>
        public static BlockBox ZoneArea(int zx, int zz, int worldHeight)
        {
            int s = ZONE_SIZE_BLOCKS;
            return new BlockBox(zx * s, 0, zz * s, zx * s + s, worldHeight, zz * s + s);
        }
    }
}
