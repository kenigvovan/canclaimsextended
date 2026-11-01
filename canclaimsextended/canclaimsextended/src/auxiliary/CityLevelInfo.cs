namespace canclaimsextended.src.auxiliary
{
    public class CityLevelInfo
    {
        /// <summary>How many plots may be handed out inside the territory.</summary>
        public int AmountOfPlots { get; set; }
        /// <summary>Total volume of all plots of the city, in blocks.</summary>
        public long MaxVolume { get; set; }
        /// <summary>How many summon plots the city may have.</summary>
        public int SummonPoints { get; set; }
        /// <summary>How many chunks of territory the city may hold.</summary>
        public int MaxChunks { get; set; }
        /// <summary>How many chunks the city may buy over MaxChunks.</summary>
        public int MaxExtraChunks { get; set; }

        /// <param name="maxChunks">Below 0, as in a levels file written before the field: twice amountOfPlots.</param>
        /// <param name="maxExtraChunks">Below 0, as in a levels file written before the field: a quarter of the chunks.</param>
        public CityLevelInfo(int amountOfPlots, long maxVolume, int summonPoints = 0, int maxChunks = -1, int maxExtraChunks = -1)
        {
            AmountOfPlots = amountOfPlots;
            MaxVolume = maxVolume;
            SummonPoints = summonPoints;
            MaxChunks = maxChunks >= 0 ? maxChunks : amountOfPlots * 2;
            MaxExtraChunks = maxExtraChunks >= 0 ? maxExtraChunks : MaxChunks / 4;
        }
    }
}
