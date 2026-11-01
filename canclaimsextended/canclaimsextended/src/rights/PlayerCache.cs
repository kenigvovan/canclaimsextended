using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.rights
{
    public class PlayerCache
    {
        Plot lastPlot;
        bool?[] playerCache = new bool?[3]; // use, build, attack in the last plot

        public Plot getLastPlot()
        {
            return lastPlot;
        }
        public void Reset()
        {
            for (int i = 0; i < playerCache.Length; i++)
            {
                playerCache[i] = null;
            }
        }
        public bool?[] getCache()
        {
            return playerCache;
        }
        /// <summary>
        /// Points the cache at a plot. Answers cached for the previous plot are dropped: they were
        /// computed against its permissions.
        /// </summary>
        public void setPlot(Plot plot)
        {
            if (!ReferenceEquals(lastPlot, plot)) Reset();
            this.lastPlot = plot;
        }
    }
}
