using System.Linq;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Sends the city arms to clients: everything to a player who comes in, everything to everyone
    /// when one changes - banners and the window of any player may show any city.
    /// </summary>
    public static class EmblemSync
    {
        static ICoreServerAPI sapi;
        static IServerNetworkChannel channel;

        public static void Init(ICoreServerAPI api, IServerNetworkChannel serverChannel)
        {
            sapi = api;
            channel = serverChannel;
            api.Event.PlayerNowPlaying += player => channel?.SendPacket(Build(), player);
        }

        /// <summary>Call after a city's arms changed or a city with arms is gone.</summary>
        public static void BroadcastAll()
        {
            if (channel == null || sapi == null) return;
            EmblemsPacket packet = Build();
            foreach (var p in sapi.World.AllOnlinePlayers)
            {
                if (p is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing)
                {
                    channel.SendPacket(packet, sp);
                }
            }
        }

        static EmblemsPacket Build()
        {
            var packet = new EmblemsPacket
            {
                Patterns = EmblemHandler.AvailablePatterns().ToList(),
                Colors = EmblemHandler.AvailableColors().ToList(),
                MaxLayers = EmblemHandler.MaxLayers()
            };
            if (canclaimsextended.dataStorage == null) return packet;
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList())
            {
                if (string.IsNullOrEmpty(city.Emblem)) continue;
                packet.CityGuids.Add(city.Guid);
                packet.Emblems.Add(city.Emblem);
            }
            return packet;
        }
    }
}
