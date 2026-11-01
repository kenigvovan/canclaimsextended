using System;
using System.Collections.Generic;
using canclaimsextended.src.geometry;
using canclaimsextended.src.network;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// The land of a city as the client knows it, or of one plots group of the city: the same
    /// city, its own rights and flags.
    /// </summary>
    public class ClientCityLand
    {
        /// <summary>Guid of the city; equal for the city's own land and its groups'.</summary>
        public string Guid;
        public string Name = "";
        public int Color;
        /// <summary>See PlotMarkerFlags; the rights are the player's own on this land.</summary>
        public int Flags;
        /// <summary>Empty for the city's own land.</summary>
        public string GroupGuid = "";
        public string GroupName = "";
    }

    /// <summary>
    /// The city chunks the server has sent. Chunk positions are kept as ChunkPos keys; the size of
    /// a chunk comes with every packet, as the client has no config.
    /// </summary>
    public class ClientLandStore
    {
        readonly Dictionary<long, ClientCityLand> chunks = new();
        readonly Dictionary<string, ClientCityLand> cities = new();

        /// <summary>Blocks on a side of a chunk; 0 until the first packet.</summary>
        public int ChunkSize { get; private set; }

        /// <summary>Area whose picture is out of date, over the whole height.</summary>
        public event Action<BlockBox> AreaChanged;

        public bool TryGetCityAtBlock(double x, double z, out ClientCityLand city)
        {
            city = null;
            if (ChunkSize <= 0) return false;
            int cx = (int)Math.Floor(x / ChunkSize), cz = (int)Math.Floor(z / ChunkSize);
            return chunks.TryGetValue(new ChunkPos(cx, cz).Key, out city);
        }

        public bool TryGetCityAtChunk(int cx, int cz, out ClientCityLand city)
        {
            return chunks.TryGetValue(new ChunkPos(cx, cz).Key, out city);
        }

        public void ApplyZone(LandZonePacket packet)
        {
            SetChunkSize(packet.ChunkSize);
            if (ChunkSize <= 0) return;
            int zoneChunks = NetworkSetup.ZONE_SIZE_BLOCKS / ChunkSize;
            int cx0 = packet.ZoneX * zoneChunks, cz0 = packet.ZoneZ * zoneChunks;
            // Anything in the zone the server no longer lists is gone.
            for (int cx = cx0; cx < cx0 + zoneChunks; cx++)
            {
                for (int cz = cz0; cz < cz0 + zoneChunks; cz++)
                {
                    chunks.Remove(new ChunkPos(cx, cz).Key);
                }
            }
            if (packet.Cities != null)
            {
                foreach (ClientCityLandData data in packet.Cities)
                {
                    AddCity(data, false);
                }
            }
            int s = NetworkSetup.ZONE_SIZE_BLOCKS;
            AreaChanged?.Invoke(new BlockBox(packet.ZoneX * s, 0, packet.ZoneZ * s, packet.ZoneX * s + s, 1, packet.ZoneZ * s + s));
        }

        public void ApplyUpdate(LandUpdatePacket packet)
        {
            SetChunkSize(packet.ChunkSize);
            if (ChunkSize <= 0) return;
            if (packet.RemovedChunks != null)
            {
                foreach (long key in packet.RemovedChunks)
                {
                    if (chunks.Remove(key)) AreaChanged?.Invoke(Column(key));
                }
            }
            if (packet.City != null)
            {
                AddCity(packet.City, true);
            }
        }

        public void Clear()
        {
            chunks.Clear();
            cities.Clear();
        }

        /// <summary>Updates the city's look; with lookChanged, everything of it on the map is redrawn.</summary>
        void AddCity(ClientCityLandData data, bool lookChanged)
        {
            if (string.IsNullOrEmpty(data.CityGuid)) return;
            string landKey = data.CityGuid + "/" + (data.GroupGuid ?? "");
            if (!cities.TryGetValue(landKey, out ClientCityLand city))
            {
                city = new ClientCityLand { Guid = data.CityGuid, GroupGuid = data.GroupGuid ?? "" };
                cities[landKey] = city;
            }
            city.Name = data.CityName ?? "";
            city.Color = data.CityColor;
            city.Flags = data.Flags;
            city.GroupName = data.GroupName ?? "";
            if (data.ChunkKeys != null)
            {
                foreach (long key in data.ChunkKeys)
                {
                    chunks[key] = city;
                    if (lookChanged) AreaChanged?.Invoke(Column(key));
                }
            }
            if (lookChanged && (data.ChunkKeys == null || data.ChunkKeys.Length == 0))
            {
                foreach (var it in chunks)
                {
                    if (it.Value == city) AreaChanged?.Invoke(Column(it.Key));
                }
            }
        }

        void SetChunkSize(int size)
        {
            if (size <= 0 || size == ChunkSize) return;
            // Another size means another server or config: what was kept no longer fits.
            if (ChunkSize != 0) chunks.Clear();
            ChunkSize = size;
        }

        /// <summary>The chunk's ground area; height does not matter to the map.</summary>
        BlockBox Column(long key)
        {
            ChunkPos c = ChunkPos.FromKey(key);
            return new BlockBox(c.X * ChunkSize, 0, c.Z * ChunkSize, c.X * ChunkSize + ChunkSize, 1, c.Z * ChunkSize + ChunkSize);
        }
    }
}
