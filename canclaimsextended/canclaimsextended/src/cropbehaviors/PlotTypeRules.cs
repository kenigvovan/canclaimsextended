using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.cropbehaviors
{
    /// <summary>Whether a block lies in a plot of a given type, for the farm and orchard rules.</summary>
    public static class PlotTypeRules
    {
        public static bool IsIn(BlockPos pos, PlotType type)
        {
            return canclaimsextended.dataStorage != null
                && canclaimsextended.dataStorage.GetPlotAt(pos, out Plot plot) && plot.Type == type;
        }

        /// <summary>A crop counts as on a farm when either the farmland or the crop above it is.</summary>
        public static bool IsFarmland(BlockPos farmland)
        {
            return IsIn(farmland, PlotType.FARM) || IsIn(farmland.UpCopy(), PlotType.FARM);
        }
    }
}
