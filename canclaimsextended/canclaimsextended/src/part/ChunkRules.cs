using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// What a chunk has to pass to join or leave a city's territory. Distances are in chunks and
    /// flat: the territory spans the whole height of the world.
    /// </summary>
    public static class ChunkRules
    {
        /// <summary>The first chunk of a city about to be founded.</summary>
        public static ClaimCheck CheckNewCity(ChunkPos chunk)
        {
            ClaimCheck check = CheckChunkFree(chunk);
            return check.Ok ? CheckDistance(chunk, null) : check;
        }

        /// <summary>An ordinary chunk: next to the city and under its chunk limit.</summary>
        public static ClaimCheck CheckClaim(City city, ChunkPos chunk)
        {
            ClaimCheck check = CheckChunkFree(chunk);
            if (!check.Ok) return check;
            if (!TouchesCity(city, chunk))
            {
                return ClaimCheck.Fail("canclaimsextended:chunk_not_adjacent");
            }
            check = CheckChunkLimit(city);
            return check.Ok ? CheckDistance(chunk, city) : check;
        }

        /// <summary>A chunk next to the city bought over its chunk limit, up to the extra limit.</summary>
        public static ClaimCheck CheckExtraClaim(City city, ChunkPos chunk)
        {
            ClaimCheck check = CheckChunkFree(chunk);
            if (!check.Ok) return check;
            if (!TouchesCity(city, chunk))
            {
                return ClaimCheck.Fail("canclaimsextended:chunk_not_adjacent");
            }
            check = CheckExtraChunkLimit(city);
            return check.Ok ? CheckDistance(chunk, city) : check;
        }

        /// <summary>
        /// A chunk apart from the city, no nearer to it than MIN_OUTPOST_DISTANCE_CHUNKS and no
        /// farther than MAX_OUTPOST_DISTANCE_CHUNKS. Counts against the usual chunk limit.
        /// </summary>
        public static ClaimCheck CheckOutpost(City city, ChunkPos chunk)
        {
            ClaimCheck check = CheckChunkFree(chunk);
            if (!check.Ok) return check;
            if (TouchesCity(city, chunk))
            {
                return ClaimCheck.Fail("canclaimsextended:outpost_touches_city");
            }
            Config c = canclaimsextended.config;
            double nearest = city.Chunks.Count == 0 ? double.MaxValue : city.Chunks.Min(own => chunk.DistanceTo(own.Pos));
            if (nearest < c.MIN_OUTPOST_DISTANCE_CHUNKS)
            {
                return ClaimCheck.Fail("canclaimsextended:outpost_too_close", c.MIN_OUTPOST_DISTANCE_CHUNKS);
            }
            if (nearest > c.MAX_OUTPOST_DISTANCE_CHUNKS)
            {
                return ClaimCheck.Fail("canclaimsextended:outpost_too_far", c.MAX_OUTPOST_DISTANCE_CHUNKS);
            }
            check = CheckChunkLimit(city);
            return check.Ok ? CheckDistance(chunk, city) : check;
        }

        /// <summary>
        /// Giving up a chunk: not the last one, no plot in it, and the territory does not fall apart.
        /// </summary>
        public static ClaimCheck CheckUnclaim(City city, ChunkPos chunk)
        {
            if (!city.HasChunk(chunk))
            {
                return ClaimCheck.Fail("canclaimsextended:chunk_not_your_city");
            }
            if (city.ChunkCount <= 1)
            {
                return ClaimCheck.Fail("canclaimsextended:last_city_chunk");
            }
            ClaimCheck check = CheckNoPlotsInChunk(city, chunk);
            if (!check.Ok) return check;
            return StaysConnectedWithout(city, chunk)
                ? ClaimCheck.Success
                : ClaimCheck.Fail("canclaimsextended:unclaim_splits_city");
        }

        public static ClaimCheck CheckChunkFree(ChunkPos chunk)
        {
            return canclaimsextended.dataStorage.GetCityAtChunk(chunk, out City owner)
                ? ClaimCheck.Fail("canclaimsextended:chunk_already_claimed", owner.getPartNameReplaceUnder())
                : ClaimCheck.Success;
        }

        /// <summary>The chunk shares a side with a chunk of the city; corners do not count.</summary>
        public static bool TouchesCity(City city, ChunkPos chunk)
        {
            return chunk.Neighbours4().Any(city.HasChunk);
        }

        /// <summary>Room under the chunk limit; bought extra chunks do not count against it.</summary>
        public static ClaimCheck CheckChunkLimit(City city)
        {
            // A technical (admin) city, or an admin under /cadmin as, has no chunk limits.
            if (ClaimRules.NoCityLimits(city)) return ClaimCheck.Success;
            int max = Settings.GetMaxChunksForCity(city);
            return city.RegularChunksCount >= max
                ? ClaimCheck.Fail("canclaimsextended:max_chunks_claimed", max)
                : ClaimCheck.Success;
        }

        public static ClaimCheck CheckExtraChunkLimit(City city)
        {
            if (ClaimRules.NoCityLimits(city)) return ClaimCheck.Success;
            int max = Settings.GetMaxExtraChunksForCity(city);
            return city.ExtraChunksCount >= max
                ? ClaimCheck.Fail("canclaimsextended:max_extra_chunks", max)
                : ClaimCheck.Success;
        }

        /// <summary>
        /// At least MIN_CHUNK_DISTANCE_FROM_OTHER_CITY free chunks between this one and any chunk of
        /// another city.
        /// </summary>
        public static ClaimCheck CheckDistance(ChunkPos chunk, City ownCity)
        {
            int min = canclaimsextended.config.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY;
            // A chunk |d| away leaves |d| - 1 free between, so |d| <= min is too close.
            for (int dx = -min; dx <= min; dx++)
            {
                for (int dz = -min; dz <= min; dz++)
                {
                    ChunkPos other = new ChunkPos(chunk.X + dx, chunk.Z + dz);
                    if (!canclaimsextended.dataStorage.GetCityAtChunk(other, out City city) || city.Equals(ownCity)) continue;
                    if (chunk.DistanceTo(other) < min)
                    {
                        return ClaimCheck.Fail("canclaimsextended:too_close_to_another_city", city.getPartNameReplaceUnder(), min);
                    }
                }
            }
            return ClaimCheck.Success;
        }

        /// <summary>No plot of the city reaches into the chunk.</summary>
        public static ClaimCheck CheckNoPlotsInChunk(City city, ChunkPos chunk)
        {
            BlockBox column = chunk.Column(canclaimsextended.sapi.WorldManager.MapSizeY);
            Plot inside = canclaimsextended.dataStorage.QueryPlots(column).FirstOrDefault(p => city.Equals(p.getCity()));
            if (inside == null)
            {
                return ClaimCheck.Success;
            }
            string name = inside.GetPartName().Length > 0 ? inside.GetPartName() : inside.Guid.Substring(0, 8);
            return ClaimCheck.Fail("canclaimsextended:chunk_has_plots", name);
        }

        /// <summary>
        /// Whether the territory does not split without this chunk. Outposts make separate pieces,
        /// so the rule is that the number of pieces does not grow; the last chunk of a piece may
        /// always go.
        /// </summary>
        public static bool StaysConnectedWithout(City city, ChunkPos removed)
        {
            var all = new HashSet<ChunkPos>(city.Chunks.Select(c => c.Pos));
            var rest = new HashSet<ChunkPos>(all);
            rest.Remove(removed);
            if (rest.Count <= 1)
            {
                return true;
            }
            return CountPieces(rest) <= CountPieces(all);
        }

        /// <summary>Groups of chunks reachable from each other through shared sides.</summary>
        static int CountPieces(HashSet<ChunkPos> chunks)
        {
            return Connectivity.CountPieces(chunks, c => c.Neighbours4());
        }
    }
}
