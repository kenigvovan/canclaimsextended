using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;

namespace canclaimsextended.src.rules
{
    public static class SummonRules
    {
        /// <summary>Summon points of the city that are set, in plot order.</summary>
        public static List<(Plot Plot, PlotDescSummon Desc)> PointsOf(City city)
        {
            return city.getCityPlots()
                .Where(p => p.Type == PlotType.SUMMON && p.Desc is PlotDescSummon d && d.Point != null)
                .Select(p => (p, (PlotDescSummon)p.Desc))
                .ToList();
        }
    }
}
