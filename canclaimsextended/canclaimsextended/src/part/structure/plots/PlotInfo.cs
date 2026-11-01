using System;

namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>One plot type as players see it: its code, its enum value and how its description is made.</summary>
    public class PlotInfo
    {
        public string Code { get; }
        public PlotType Type { get; }
        readonly Func<PlotDesc> createDesc;

        public PlotInfo(string code, PlotType type, Func<PlotDesc> createDesc)
        {
            Code = code;
            Type = type;
            this.createDesc = createDesc;
        }

        public PlotDesc CreateDesc() => createDesc();

        /// <summary>Lang key of the type's name, "canclaimsextended:plot_type_" + code.</summary>
        public string LangKey => "canclaimsextended:plot_type_" + Code;

        /// <summary>Daily upkeep of one unit of volume; types missing in PLOT_TYPE_UNIT_COSTS cost as DEFAULT_PLOT_COST.</summary>
        public double UnitUpkeep()
        {
            var costs = canclaimsextended.config.PLOT_TYPE_UNIT_COSTS;
            return costs != null && costs.TryGetValue(Code, out double cost) ? cost : canclaimsextended.config.DEFAULT_PLOT_COST;
        }

        /// <summary>One-time price of setting a plot to this type; 0 when the type is missing in PLOT_TYPE_SET_COSTS.</summary>
        public double SetCost()
        {
            var costs = canclaimsextended.config.PLOT_TYPE_SET_COSTS;
            return costs != null && costs.TryGetValue(Code, out double cost) ? cost : 0;
        }
    }
}
