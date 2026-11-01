using canclaimsextended.src.geometry;

namespace canclaimsextended.src.part
{
    /// <summary>One chunk of a city's territory and what it was bought as.</summary>
    public class CityChunk
    {
        public ChunkPos Pos { get; }
        /// <summary>The city holding it; set by DataStorage.AddChunk.</summary>
        public City City { get; internal set; }
        public long TimeStampClaimed { get; set; }
        /// <summary>Bought over the chunk limit (/city extraclaim); not counted against it.</summary>
        public bool ExtraBought { get; set; }
        /// <summary>Bought apart from the city (/city outpost); the start of an island of its own.</summary>
        public bool IsOutpost { get; set; }
        /// <summary>
        /// The plots group the chunk belongs to, null when none. Its members get the group rights on
        /// the land here, and the group's flags apply. Read off the main thread by the patches.
        /// </summary>
        public PlotsGroup Group { get; set; }

        public CityChunk(ChunkPos pos)
        {
            Pos = pos;
        }

        /// <summary>What governs the land of this chunk: the group's land when in a group, else the city's.</summary>
        public structure.Plot Land => Group?.Land ?? City.Land;
    }
}
