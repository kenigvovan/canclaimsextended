using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.rules
{
    /// <summary>Which plot or chunk a player is at, the same way for the commands, the window and the selection.</summary>
    public static class PlotLookup
    {
        /// <summary>A real plot around the player's feet, else one they stand on.</summary>
        public static bool TryGetSubPlotAtFeet(IServerPlayer player, out Plot plot)
        {
            BlockPos feet = player.Entity.Pos.AsBlockPos;
            return canclaimsextended.dataStorage.GetSubPlotAt(feet, out plot)
                || canclaimsextended.dataStorage.GetSubPlotAt(feet.DownCopy(), out plot);
        }

        /// <summary>
        /// What governs where the player is: a plot around their feet, else one they stand on, else
        /// one under the crosshair, else the land of the city whose territory they are in. Use
        /// <see cref="TryGetSubPlotUnderPlayer"/> where only a real plot will do.
        /// </summary>
        public static bool TryGetPlotUnderPlayer(IServerPlayer player, out Plot plot)
        {
            if (TryGetSubPlotUnderPlayer(player, out plot)) return true;
            return canclaimsextended.dataStorage.GetPlotAt(player.Entity.Pos.AsBlockPos, out plot);
        }

        /// <summary>The same places as <see cref="TryGetPlotUnderPlayer"/>, real plots only.</summary>
        public static bool TryGetSubPlotUnderPlayer(IServerPlayer player, out Plot plot)
        {
            if (TryGetSubPlotAtFeet(player, out plot)) return true;
            BlockPos aimed = player.CurrentBlockSelection?.Position;
            return aimed != null && canclaimsextended.dataStorage.GetSubPlotAt(aimed, out plot);
        }

        /// <summary>The chunk of territory the player stands in.</summary>
        public static ChunkPos ChunkUnderPlayer(IServerPlayer player)
        {
            return ChunkPos.FromBlock(player.Entity.Pos.AsBlockPos);
        }
    }
}
