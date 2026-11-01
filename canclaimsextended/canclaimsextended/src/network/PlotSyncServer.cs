using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Keeps each client's copy of the plots around it current: the zones around the player are
    /// sent whole when they come into range, and changes to plots in sent zones follow one by one.
    /// Every packet is built for its receiver, since it carries their rights on the plot.
    /// </summary>
    public static class PlotSyncServer
    {
        static ICoreServerAPI sapi;
        static IServerNetworkChannel channel;
        static readonly Dictionary<string, HashSet<long>> sentZones = new();
        static readonly Dictionary<string, long> lastZone = new();

        public static void Init(ICoreServerAPI api, IServerNetworkChannel serverChannel)
        {
            sapi = api;
            sentZones.Clear();
            lastZone.Clear();
            channel = serverChannel;
            auxiliary.ServerTicks.Every(api, _ => Tick(), 1000);
            api.Event.PlayerJoin += Forget;
            api.Event.PlayerDisconnect += Forget;

            // The events are static and outlive a single player session; never subscribe twice.
            DataStorage.PlotAdded -= OnPlotUpsert;
            DataStorage.PlotChanged -= OnPlotUpsert;
            DataStorage.PlotRemoved -= OnPlotRemoved;
            DataStorage.PlayerRightsChanged -= Forget;
            DataStorage.ChunkClaimed -= OnChunkClaimed;
            DataStorage.ChunkUnclaimed -= OnChunkUnclaimed;
            DataStorage.CityChanged -= OnCityChanged;
            DataStorage.ChunkChanged -= OnChunkClaimed;
            DataStorage.ChunkChanged += OnChunkClaimed;
            DataStorage.PlotAdded += OnPlotUpsert;
            DataStorage.PlotChanged += OnPlotUpsert;
            DataStorage.PlotRemoved += OnPlotRemoved;
            DataStorage.PlayerRightsChanged += Forget;
            DataStorage.ChunkClaimed += OnChunkClaimed;
            DataStorage.ChunkUnclaimed += OnChunkUnclaimed;
            DataStorage.CityChanged += OnCityChanged;
        }

        /// <summary>Whether the client draws territory borders without the wand; the client draws them itself.</summary>
        public static void SendBordersState(IServerPlayer player, bool show)
        {
            channel?.SendPacket(new BordersStatePacket { Show = show }, player);
        }

        static void Forget(IServerPlayer player)
        {
            Forget(player.PlayerUID);
        }

        /// <summary>The next tick sends this player every zone around them again.</summary>
        static void Forget(string playerUid)
        {
            sentZones.Remove(playerUid);
            lastZone.Remove(playerUid);
        }

        static void Tick()
        {
            if (canclaimsextended.dataStorage == null) return;
            foreach (IPlayer p in sapi.World.AllOnlinePlayers)
            {
                if (p is not IServerPlayer player || player.ConnectionState != EnumClientState.Playing || player.Entity == null) continue;
                int zx = NetworkSetup.ZoneOf(player.Entity.Pos.X);
                int zz = NetworkSetup.ZoneOf(player.Entity.Pos.Z);
                long key = NetworkSetup.ZoneKey(zx, zz);
                if (lastZone.TryGetValue(player.PlayerUID, out long last) && last == key) continue;
                lastZone[player.PlayerUID] = key;

                if (!sentZones.TryGetValue(player.PlayerUID, out var sent))
                {
                    sent = new HashSet<long>();
                    sentZones[player.PlayerUID] = sent;
                }
                sent.RemoveWhere(k =>
                {
                    NetworkSetup.ZoneOfKey(k, out int kx, out int kz);
                    return Math.Abs(kx - zx) > NetworkSetup.KEEP_RADIUS || Math.Abs(kz - zz) > NetworkSetup.KEEP_RADIUS;
                });
                int r = NetworkSetup.SEND_RADIUS;
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (sent.Add(NetworkSetup.ZoneKey(zx + dx, zz + dz)))
                        {
                            SendZone(player, zx + dx, zz + dz);
                        }
                    }
                }
            }
        }

        static void SendZone(IServerPlayer player, int zx, int zz)
        {
            BlockBox area = NetworkSetup.ZoneArea(zx, zz, sapi.WorldManager.MapSizeY);
            PlayerInfo viewer = ViewerOf(player);
            channel.SendPacket(new PlotZonePacket
            {
                ZoneX = zx,
                ZoneZ = zz,
                Plots = canclaimsextended.dataStorage.QueryPlots(area).Select(p => ToData(p, viewer)).ToArray()
            }, player);

            // The zone is a whole number of chunks wide, so every chunk lies in exactly one zone.
            // One entry per land: the city's own, and one per group with chunks here.
            var byLand = new Dictionary<Plot, List<long>>(ReferenceEqualityComparer.Instance);
            foreach (ChunkPos pos in DataStorage.ChunksIn(area))
            {
                if (!canclaimsextended.dataStorage.GetChunk(pos, out CityChunk chunk)) continue;
                if (!byLand.TryGetValue(chunk.Land, out var keys))
                {
                    keys = new List<long>();
                    byLand[chunk.Land] = keys;
                }
                keys.Add(pos.Key);
            }
            channel.SendPacket(new LandZonePacket
            {
                ChunkSize = ChunkPos.Size,
                ZoneX = zx,
                ZoneZ = zz,
                Cities = byLand.Select(it => ToLandData(it.Key, viewer, it.Value.ToArray())).ToArray()
            }, player);
        }

        /// <summary>Players who hold any zone the plot touches.</summary>
        static List<IServerPlayer> Watchers(Plot plot)
        {
            BlockBox b = plot.Bounds;
            var zones = new List<long>();
            for (int zx = NetworkSetup.ZoneOf(b.MinX); zx <= NetworkSetup.ZoneOf(b.MaxX - 1); zx++)
            {
                for (int zz = NetworkSetup.ZoneOf(b.MinZ); zz <= NetworkSetup.ZoneOf(b.MaxZ - 1); zz++)
                {
                    zones.Add(NetworkSetup.ZoneKey(zx, zz));
                }
            }
            return WatchersOf(zones);
        }

        /// <summary>Players who hold any of these zones.</summary>
        static List<IServerPlayer> WatchersOf(ICollection<long> zones)
        {
            var result = new List<IServerPlayer>();
            foreach (var it in sentZones)
            {
                if (zones.Any(it.Value.Contains) && sapi.World.PlayerByUid(it.Key) is IServerPlayer player)
                {
                    result.Add(player);
                }
            }
            return result;
        }

        static void OnPlotUpsert(Plot plot)
        {
            if (channel == null) return;
            foreach (IServerPlayer player in Watchers(plot))
            {
                channel.SendPacket(new PlotUpdatePacket { Plot = ToData(plot, ViewerOf(player)) }, player);
            }
        }

        static void OnPlotRemoved(Plot plot)
        {
            if (channel == null) return;
            List<IServerPlayer> to = Watchers(plot);
            if (to.Count == 0) return;
            channel.SendPacket(new PlotRemovedPacket { Guid = plot.Guid }, to.ToArray());
        }

        /// <summary>A chunk joined a city, or moved into or out of a group: sent with the land that governs it now.</summary>
        static void OnChunkClaimed(City city, ChunkPos pos)
        {
            if (channel == null || !canclaimsextended.dataStorage.GetChunk(pos, out CityChunk chunk)) return;
            foreach (IServerPlayer player in WatchersOf(new[] { NetworkSetup.ZoneKeyOf(pos) }))
            {
                channel.SendPacket(new LandUpdatePacket
                {
                    ChunkSize = ChunkPos.Size,
                    City = ToLandData(chunk.Land, ViewerOf(player), new[] { pos.Key })
                }, player);
            }
        }

        static void OnChunkUnclaimed(City city, ChunkPos chunk)
        {
            if (channel == null) return;
            List<IServerPlayer> to = WatchersOf(new[] { NetworkSetup.ZoneKeyOf(chunk) });
            if (to.Count == 0) return;
            channel.SendPacket(new LandUpdatePacket { ChunkSize = ChunkPos.Size, RemovedChunks = new[] { chunk.Key } }, to.ToArray());
        }

        /// <summary>A new name, color or rights of the city: its look goes to everyone holding a zone of it.</summary>
        static void OnCityChanged(City city)
        {
            if (channel == null) return;
            var zones = new HashSet<long>(city.Chunks.Select(c => NetworkSetup.ZoneKeyOf(c.Pos)));
            var lands = new List<Plot> { city.Land };
            lands.AddRange(city.PlotsGroups.Where(g => g.Land != null).Select(g => g.Land));
            foreach (IServerPlayer player in WatchersOf(zones))
            {
                PlayerInfo viewer = ViewerOf(player);
                foreach (Plot land in lands)
                {
                    channel.SendPacket(new LandUpdatePacket
                    {
                        ChunkSize = ChunkPos.Size,
                        City = ToLandData(land, viewer, new long[0])
                    }, player);
                }
            }
        }

        /// <summary>The land of a city or of one of its groups, as the viewer sees it.</summary>
        static ClientCityLandData ToLandData(Plot land, PlayerInfo viewer, long[] chunkKeys)
        {
            City city = land.getCity();
            return new ClientCityLandData
            {
                CityGuid = city.Guid,
                CityName = city.GetPartName(),
                CityColor = city.cityColor,
                Flags = FlagsFor(land, viewer),
                ChunkKeys = chunkKeys,
                GroupGuid = land.Group?.Guid ?? "",
                GroupName = land.Group?.GetPartName() ?? ""
            };
        }

        static PlayerInfo ViewerOf(IServerPlayer player)
        {
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo viewer);
            return viewer;
        }

        /// <summary>The plot's flags, and what the viewer may do there; no rights when the viewer is unknown.</summary>
        static int FlagsFor(Plot plot, PlayerInfo viewer)
        {
            PermsHandler perms = plot.getPermsHandler();
            int flags = 0;
            // As damage checks it: the city's flag covers plots that have theirs off.
            if (PvpRules.IsPvpOn(plot)) flags |= PlotMarkerFlags.PVP;
            if (perms.fireFlag) flags |= PlotMarkerFlags.FIRE;
            if (perms.blastFlag) flags |= PlotMarkerFlags.BLAST;
            if (perms.noMobSpawnFlag) flags |= PlotMarkerFlags.NO_MOB_SPAWN;
            if (viewer != null)
            {
                if (AccessRules.CanWithoutCache(viewer, plot, PermType.USE_PERM)) flags |= PlotMarkerFlags.CAN_USE;
                if (AccessRules.CanWithoutCache(viewer, plot, PermType.BUILD_AND_DESTROY_PERM)) flags |= PlotMarkerFlags.CAN_BUILD;
                if (AccessRules.CanWithoutCache(viewer, plot, PermType.ATTACK_ANIMALS_PERM)) flags |= PlotMarkerFlags.CAN_ATTACK;
                if (viewer.City != null && viewer.City.Equals(plot.getCity())) flags |= PlotMarkerFlags.OWN_CITY;
            }
            return flags;
        }

        static ClientPlotData ToData(Plot plot, PlayerInfo viewer)
        {
            City city = plot.getCity();
            return new ClientPlotData
            {
                Guid = plot.Guid,
                Name = plot.GetPartName(),
                CityGuid = city?.Guid ?? "",
                CityName = city?.GetPartName() ?? "",
                CityColor = city?.cityColor ?? 0,
                OwnerName = plot.getPlotOwner()?.GetPartName() ?? "",
                Boxes = BoxCodec.Encode(plot.Shape.Boxes),
                Flags = FlagsFor(plot, viewer),
                ForSale = plot.IsForSale,
                Price = plot.IsForSale ? plot.Price : 0,
                Number = plot.Number,
                GroupName = plot.Group?.GetPartName() ?? ""
            };
        }
    }
}
