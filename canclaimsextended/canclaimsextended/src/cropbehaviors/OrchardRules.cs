using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canclaimsextended.src.cropbehaviors
{
    /// <summary>
    /// With FRUIT_ONLY_ON_ORCHARD_PLOTS on, fruit trees outside an orchard plot still blossom but
    /// never carry fruit: Fruiting/Ripe go back to Empty right after the vanilla root tick.
    /// </summary>
    public static class OrchardRules
    {
        /// <summary>Whether planting here deserves the "will only blossom" warning.</summary>
        public static bool ShouldWarnOnPlanting(BlockPos pos)
        {
            if (canclaimsextended.dataStorage == null) return false;
            if (!canclaimsextended.dataStorage.GetPlotAt(pos, out Plot plot))
            {
                return canclaimsextended.config.ORCHARD_WARN_OUTSIDE_CITY;
            }
            return plot.Type != PlotType.ORCHARD;
        }

        /// <summary>lastStateChangeTotalDays is left alone, so the tree keeps its seasonal schedule.</summary>
        public static void StripFruitOutsideOrchard(FruitTreeRootBH rootBh)
        {
            if (!canclaimsextended.config.FRUIT_ONLY_ON_ORCHARD_PLOTS) return;
            if (rootBh?.propsByType == null) return;
            BlockEntity be = rootBh.Blockentity;
            if (be?.Pos == null || !HasFruit(rootBh)) return;
            if (PlotTypeRules.IsIn(be.Pos, PlotType.ORCHARD)) return;

            foreach (var props in rootBh.propsByType.Values)
            {
                if (props.State != EnumFruitTreeState.Fruiting && props.State != EnumFruitTreeState.Ripe) continue;
                props.State = EnumFruitTreeState.Empty;
                props.workingState = EnumFruitTreeState.Empty;
            }
            be.MarkDirty(true);
        }

        static bool HasFruit(FruitTreeRootBH rootBh)
        {
            foreach (var props in rootBh.propsByType.Values)
            {
                if (props.State == EnumFruitTreeState.Fruiting || props.State == EnumFruitTreeState.Ripe) return true;
            }
            return false;
        }
    }
}
