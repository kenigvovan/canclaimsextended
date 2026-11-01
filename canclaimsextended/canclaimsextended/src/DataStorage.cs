using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src
{
    /// <summary>
    /// Everything loaded on the server: cities, players and plots. Plot lookups by position may come
    /// from fire and liquid patches off the main thread; the index is locked for that, the
    /// dictionaries are concurrent.
    /// </summary>
    public class DataStorage
    {
        protected ConcurrentDictionary<string, City> guidToCityDict = new ConcurrentDictionary<string, City>();
        protected ConcurrentDictionary<string, City> nameToCityDict = new ConcurrentDictionary<string, City>();

        protected ConcurrentDictionary<string, PlayerInfo> nameToPlayerDict = new ConcurrentDictionary<string, PlayerInfo>();
        protected ConcurrentDictionary<string, PlayerInfo> uidToPlayerDict = new ConcurrentDictionary<string, PlayerInfo>();

        // Where plots are; guidToPlot is the list of what exists. Both change together, only here.
        protected PlotIndex<Plot> plotIndex = new PlotIndex<Plot>();
        protected ConcurrentDictionary<string, Plot> guidToPlot = new ConcurrentDictionary<string, Plot>();

        /// <summary>Server-wide flags over plots; filled from the database on load.</summary>
        public WorldInfo World { get; } = new WorldInfo();

        /*==============================================================================================*/
        /*=====================================EVENTS===================================================*/
        /*==============================================================================================*/
        /// <summary>Raised on the server after the index changed; clients are told from here.</summary>
        public static event Action<Plot> PlotAdded;
        /// <summary>Raised with the plot as it was, its shape still set.</summary>
        public static event Action<Plot> PlotRemoved;
        /// <summary>Shape, name, owner, rights or city look of a plot changed.</summary>
        public static event Action<Plot> PlotChanged;
        /// <summary>Raised with a player uid when what that player may do on plots changed.</summary>
        public static event Action<string> PlayerRightsChanged;

        /// <summary>
        /// Call after changing what clients draw of a plot: its name, owner, flags or rights, or its
        /// city's name or color.
        /// </summary>
        public void NotifyPlotChanged(Plot plot)
        {
            PlotChanged?.Invoke(plot);
        }
        /// <summary>Raised when the look or the rights of a city's land changed: clients redraw its territory.</summary>
        public static event Action<City> CityChanged;

        /// <summary>
        /// The same for every plot of the city and for its land: city-wide rights, flags, name or
        /// color changed.
        /// </summary>
        public void NotifyCityChanged(City city)
        {
            foreach (Plot plot in city.getCityPlots().ToList())
            {
                PlotChanged?.Invoke(plot);
            }
            CityChanged?.Invoke(city);
        }
        /// <summary>
        /// Call when a player's standing towards plots changed rather than the plots: joining or
        /// leaving a city, being made or unmade someone's friend.
        /// </summary>
        public void NotifyPlayerRightsChanged(PlayerInfo player)
        {
            if (player != null) PlayerRightsChanged?.Invoke(player.Guid);
        }

        /*==============================================================================================*/
        /*=====================================PLOTS====================================================*/
        /*==============================================================================================*/
        /// <summary>
        /// What governs the block: the plot it is in, else the land of the city whose territory it
        /// is in (City.Land), else nothing. Check Plot.IsCityLand where only a real plot will do, or
        /// use GetSubPlotAt.
        /// </summary>
        public bool GetPlotAt(int x, int y, int z, out Plot plot)
        {
            if (plotIndex.TryFind(x, y, z, out plot)) return true;
            return TryGetLand(ChunkPos.FromBlock(x, z), out plot);
        }
        public bool GetPlotAt(BlockPos pos, out Plot plot)
        {
            return GetPlotAt(pos.X, pos.Y, pos.Z, out plot);
        }
        public bool GetPlotAt(Vec3d pos, out Plot plot)
        {
            return GetPlotAt((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z), out plot);
        }
        /// <summary>Only a real plot, never the land of a city.</summary>
        public bool GetSubPlotAt(BlockPos pos, out Plot plot)
        {
            return plotIndex.TryFind(pos.X, pos.Y, pos.Z, out plot);
        }
        bool TryGetLand(ChunkPos pos, out Plot land)
        {
            if (chunks.TryGetValue(pos.Key, out CityChunk chunk))
            {
                land = chunk.Land;
                return true;
            }
            land = null;
            return false;
        }
        public bool GetPlotByGuid(string guid, out Plot plot)
        {
            return guidToPlot.TryGetValue(guid, out plot);
        }
        public bool AddPlot(Plot plot)
        {
            if (!guidToPlot.TryAdd(plot.Guid, plot))
            {
                return false;
            }
            plotIndex.Add(plot, plot.Shape);
            PlotAdded?.Invoke(plot);
            return true;
        }
        public bool RemovePlot(Plot plot)
        {
            if (!guidToPlot.TryRemove(plot.Guid, out _))
            {
                return false;
            }
            plotIndex.Remove(plot);
            PlotRemoved?.Invoke(plot);
            return true;
        }
        public void ReshapePlot(Plot plot, PlotShape newShape)
        {
            plotIndex.Reshape(plot, newShape);
            plot.Shape = newShape;
            PlotChanged?.Invoke(plot);
        }
        /// <summary>Plots that share at least one block with the box.</summary>
        public List<Plot> QueryPlots(BlockBox box)
        {
            return plotIndex.Query(box);
        }
        /// <summary>
        /// The plots in the box plus the land of every city or group whose territory the box
        /// reaches: all that a blast in the box could hurt.
        /// </summary>
        public List<Plot> QueryPlotsAndLand(BlockBox box)
        {
            List<Plot> result = plotIndex.Query(box);
            var seen = new HashSet<Plot>(ReferenceEqualityComparer.Instance);
            foreach (ChunkPos pos in ChunksIn(box))
            {
                if (chunks.TryGetValue(pos.Key, out CityChunk chunk) && seen.Add(chunk.Land))
                {
                    result.Add(chunk.Land);
                }
            }
            return result;
        }

        /*==============================================================================================*/
        /*=====================================TERRITORY================================================*/
        /*==============================================================================================*/
        // Chunk key -> the chunk with its city. Read off the main thread by the patches, so concurrent.
        protected ConcurrentDictionary<long, CityChunk> chunks = new ConcurrentDictionary<long, CityChunk>();

        /// <summary>Raised on the server after a chunk joined or left a city's territory.</summary>
        public static event Action<City, ChunkPos> ChunkClaimed;
        public static event Action<City, ChunkPos> ChunkUnclaimed;
        /// <summary>A chunk stays with its city but is governed differently now: it joined or left a group.</summary>
        public static event Action<City, ChunkPos> ChunkChanged;

        public void NotifyChunkChanged(CityChunk chunk)
        {
            ChunkChanged?.Invoke(chunk.City, chunk.Pos);
        }

        public bool GetChunk(ChunkPos pos, out CityChunk chunk)
        {
            return chunks.TryGetValue(pos.Key, out chunk);
        }
        public bool GetCityAtChunk(ChunkPos pos, out City city)
        {
            city = chunks.TryGetValue(pos.Key, out CityChunk chunk) ? chunk.City : null;
            return city != null;
        }
        public bool GetCityAt(BlockPos pos, out City city)
        {
            return GetCityAtChunk(ChunkPos.FromBlock(pos), out city);
        }
        /// <summary>False when the chunk already belongs to a city; then nothing is changed.</summary>
        public bool AddChunk(City city, CityChunk chunk)
        {
            // Its city before it is visible: a lookup off the main thread may find it at once.
            chunk.City = city;
            if (!chunks.TryAdd(chunk.Pos.Key, chunk))
            {
                return false;
            }
            city.AddChunkEntry(chunk);
            ChunkClaimed?.Invoke(city, chunk.Pos);
            return true;
        }
        public bool RemoveChunk(City city, ChunkPos pos)
        {
            if (!chunks.TryGetValue(pos.Key, out CityChunk held) || !city.Equals(held.City))
            {
                return false;
            }
            chunks.TryRemove(pos.Key, out _);
            city.RemoveChunkEntry(pos);
            ChunkUnclaimed?.Invoke(city, pos);
            return true;
        }
        /// <summary>Every chunk the box reaches into, whoever holds it.</summary>
        public static IEnumerable<ChunkPos> ChunksIn(BlockBox box)
        {
            if (box.IsEmpty) yield break;
            ChunkPos min = ChunkPos.FromBlock(box.MinX, box.MinZ);
            // Max is exclusive.
            ChunkPos max = ChunkPos.FromBlock(box.MaxX - 1, box.MaxZ - 1);
            for (int cx = min.X; cx <= max.X; cx++)
            {
                for (int cz = min.Z; cz <= max.Z; cz++)
                {
                    yield return new ChunkPos(cx, cz);
                }
            }
        }

        /*==============================================================================================*/
        /*=====================================PLAYERS==================================================*/
        /*==============================================================================================*/
        public bool GetPlayerByUid(string uid, out PlayerInfo playerInfo)
        {
            return uidToPlayerDict.TryGetValue(uid, out playerInfo);
        }
        public bool AddPlayer(PlayerInfo player)
        {
            if (uidToPlayerDict.TryAdd(player.Guid, player))
            {
                nameToPlayerDict[player.GetPartName()] = player;
                return true;
            }
            return false;
        }
        public bool GetPlayerByName(string name, out PlayerInfo playerInfo)
        {
            return nameToPlayerDict.TryGetValue(name, out playerInfo);
        }
        public void ChangePlayerName(PlayerInfo player, string newName)
        {
            nameToPlayerDict.TryRemove(player.GetPartName(), out _);
            nameToPlayerDict[newName] = player;
        }
        public void ResetCacheForAll()
        {
            foreach (var it in uidToPlayerDict.Values)
            {
                it.PlayerCache.Reset();
            }
        }
        /// <summary>Drops cached answers of everyone whose cache points at this plot.</summary>
        public void ClearCacheForPlayersInPlot(Plot plot)
        {
            foreach (var it in uidToPlayerDict.Values)
            {
                if (ReferenceEquals(it.PlayerCache.getLastPlot(), plot))
                {
                    it.PlayerCache.setPlot(null);
                }
            }
        }

        /*==============================================================================================*/
        /*=====================================CITY=====================================================*/
        /*==============================================================================================*/
        public bool GetCityByName(string name, out City city)
        {
            return nameToCityDict.TryGetValue(name, out city);
        }
        public bool GetCityByGUID(string cityGUID, out City city)
        {
            return guidToCityDict.TryGetValue(cityGUID, out city);
        }
        /// <summary>False when the guid or the name is taken; then nothing is added.</summary>
        public bool AddCity(City city)
        {
            if (!guidToCityDict.TryAdd(city.Guid, city))
            {
                return false;
            }
            if (!nameToCityDict.TryAdd(city.GetPartName(), city))
            {
                guidToCityDict.TryRemove(city.Guid, out _);
                return false;
            }
            return true;
        }
        public bool RemoveCityByGUID(string cityGUID)
        {
            if (guidToCityDict.TryRemove(cityGUID, out City city))
            {
                return nameToCityDict.TryRemove(city.GetPartName(), out _);
            }
            return false;
        }
        public bool CityExistsByName(string name)
        {
            return nameToCityDict.ContainsKey(name);
        }
        /// <summary>Moves the city to its new name in the name index; false when the name is taken.</summary>
        public bool ChangeCityName(City city, string newName)
        {
            if (!nameToCityDict.TryAdd(newName, city))
            {
                return false;
            }
            nameToCityDict.TryRemove(city.GetPartName(), out _);
            city.SetPartName(newName);
            return true;
        }
        public List<City> GetCitiesList()
        {
            return guidToCityDict.Values.ToList();
        }
    }
}
