using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.rules
{
    public static class PvpRules
    {
        /// <summary>Players may fight on the plot: its own flag, or its city's, which covers plots that have theirs off.</summary>
        public static bool IsPvpOn(Plot plot)
        {
            return plot.getPermsHandler().pvpFlag || (plot.getCity()?.getPermsHandler().pvpFlag ?? false);
        }
    }
}
