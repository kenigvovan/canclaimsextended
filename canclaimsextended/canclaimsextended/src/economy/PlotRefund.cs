using System;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;

namespace canclaimsextended.src.economy
{
    /// <summary>
    /// Money back into the treasury when land is given up: PLOT_UNCLAIM_REFUND_PERCENT of what the
    /// volume costs to claim now, if the plot was held at least PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS.
    /// Off at 0 percent, and without an economy there is nothing to give back.
    /// </summary>
    public static class PlotRefund
    {
        public static decimal For(long volume, long claimedAt)
        {
            decimal price = PlotPricing.Units(volume) * (decimal)canclaimsextended.config.PLOT_CLAIM_PRICE_PER_UNIT;
            return Share(price, claimedAt);
        }

        /// <summary>The same share of the price of a chunk of territory.</summary>
        public static decimal ForChunk(long claimedAt)
        {
            return Share((decimal)PlotPricing.ChunkClaimCost(), claimedAt);
        }

        static decimal Share(decimal price, long claimedAt)
        {
            decimal percent = (decimal)canclaimsextended.config.PLOT_UNCLAIM_REFUND_PERCENT;
            if (percent <= 0 || !canclaimsextended.economyProvider.SupportsPlayerWallet) return 0;
            if (TimeFunctions.GetEpochSeconds() - claimedAt < canclaimsextended.config.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS) return 0;
            return Math.Floor(price * percent / 100m);
        }

        /// <summary>Pays the refund to the city; the amount paid, 0 when there is none or it failed.</summary>
        public static decimal Pay(City city, long volume, long claimedAt)
        {
            return Deposit(city, For(volume, claimedAt));
        }

        public static decimal PayChunk(City city, long claimedAt)
        {
            return Deposit(city, ForChunk(claimedAt));
        }

        /// <summary>The share of a given claim price, for a plot that shrank: what its claim would cost less now.</summary>
        public static decimal PayPrice(City city, decimal price, long claimedAt)
        {
            return Deposit(city, Share(price, claimedAt));
        }

        static decimal Deposit(City city, decimal amount)
        {
            // A technical city, or an admin under /cadmin as, claims for free, so giving land up must not make money.
            if (amount <= 0 || city.isTechnicalCity() || AdminActing.Active) return 0;
            return canclaimsextended.economyProvider.Deposit(city.MoneyAccountName, amount) == MoneyOperationResult.Success ? amount : 0;
        }
    }
}
