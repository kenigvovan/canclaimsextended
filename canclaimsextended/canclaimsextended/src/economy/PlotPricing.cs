using System;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.economy
{
    /// <summary>
    /// Prices by volume: a plot is counted in units of BLOCKS_PER_COST_UNIT blocks, rounded up,
    /// so even the smallest plot costs one unit.
    /// </summary>
    public static class PlotPricing
    {
        public static long Units(long volume)
        {
            long perUnit = Math.Max(1, canclaimsextended.config.BLOCKS_PER_COST_UNIT);
            return Math.Max(1, (volume + perUnit - 1) / perUnit);
        }

        /// <summary>One-time price of claiming the box.</summary>
        public static double ClaimCost(BlockBox box)
        {
            return Units(box.Volume) * canclaimsextended.config.PLOT_CLAIM_PRICE_PER_UNIT;
        }

        /// <summary>
        /// What a plot's claim price changes by when its volume goes from one value to the other;
        /// negative when it shrinks. Counted on the whole plot, so growing and shrinking in small
        /// steps cannot gain on the rounding of units.
        /// </summary>
        public static double ResizeCost(long volumeBefore, long volumeAfter)
        {
            return (Units(volumeAfter) - Units(volumeBefore)) * canclaimsextended.config.PLOT_CLAIM_PRICE_PER_UNIT;
        }

        /// <summary>What the founder pays for a city with its first chunk.</summary>
        public static double NewCityCost()
        {
            return canclaimsextended.config.NEW_CITY_COST;
        }

        /// <summary>One-time price of a chunk of territory; outposts and extra chunks add their own on top.</summary>
        public static double ChunkClaimCost()
        {
            return canclaimsextended.config.CHUNK_CLAIM_PRICE;
        }

        /// <summary>Daily upkeep of one unit of a plot of this type, see PlotInfo.UnitUpkeep.</summary>
        public static double UnitUpkeep(PlotType type)
        {
            return part.structure.plots.PlotTypes.Of(type).UnitUpkeep();
        }

        public static double DailyUpkeep(Plot plot)
        {
            return Units(plot.Volume) * UnitUpkeep(plot.Type);
        }

        /// <summary>Flat daily charge for the safety flags of a plot; 0 by default.</summary>
        public static double SafetyFlagsCost(Plot plot)
        {
            double sum = 0;
            if (!plot.getPermsHandler().pvpFlag) sum += canclaimsextended.config.PLOT_NO_PVP_FLAG_COST;
            if (plot.getPermsHandler().noMobSpawnFlag) sum += canclaimsextended.config.PLOT_NO_MOBSPAWN_FLAG_COST;
            return sum;
        }

        /// <summary>Everything the city pays in one mod day: the base, the territory, then each plot.</summary>
        public static double CityDailyPayment(City city)
        {
            double sum = canclaimsextended.config.CITY_BASE_CARE
                + city.ChunkCount * canclaimsextended.config.CHUNK_DAILY_UPKEEP;
            foreach (Plot plot in city.getCityPlots())
            {
                sum += DailyUpkeep(plot) + SafetyFlagsCost(plot);
            }
            return sum;
        }
    }
}
