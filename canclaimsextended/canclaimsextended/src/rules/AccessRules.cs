using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.rules
{
    /// <summary>What a player may do on a plot: use, build or attack, by how they stand to it.</summary>
    public static class AccessRules
    {
        /// <summary>Whether the player may do this at that block. Free land allows everything.</summary>
        public static bool Check(IServerPlayer byPlayer, BlockPos pos, PermType permType)
        {
            if (!canclaimsextended.dataStorage.GetPlotAt(pos, out Plot plot))
            {
                return true;
            }
            if (!canclaimsextended.dataStorage.GetPlayerByUid(byPlayer.PlayerUID, out PlayerInfo playerInfo))
            {
                return false;
            }
            var cache = playerInfo.PlayerCache;
            if (ReferenceEquals(cache.getLastPlot(), plot) && cache.getCache()[(int)permType].HasValue)
            {
                return cache.getCache()[(int)permType].Value;
            }
            cache.setPlot(plot);
            bool allowed = EvalPermission(playerInfo, plot, permType);
            cache.getCache()[(int)permType] = allowed;
            return allowed;
        }

        /// <summary>Same as <see cref="Check"/> for an entity position, tested by the block it is in.</summary>
        public static bool CheckAt(IServerPlayer byPlayer, Vec3d pos, PermType permType)
        {
            return Check(byPlayer, new BlockPos((int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z)), permType);
        }

        /// <summary>
        /// The permission without touching the player's cache: for checks made on someone's behalf
        /// (a fire they lit) at a place they are not looking at.
        /// </summary>
        public static bool CanWithoutCache(PlayerInfo playerInfo, Plot plot, PermType permType)
        {
            return EvalPermission(playerInfo, plot, permType);
        }

        /// <summary>How this player stands to this plot; the closest tie wins.</summary>
        public static PlotRelation getPlotRelationForPlayerInfo(PlayerInfo playerInfo, Plot plot)
        {
            if (plot.hasCity() && plot.getCity().isMayor(playerInfo))
            {
                return PlotRelation.MANAGABLE_OWNER;
            }
            if (plot.hasPlotOwner() && plot.getPlotOwner().Equals(playerInfo))
            {
                return PlotRelation.PLOT_OWNER;
            }
            if (plot.Group != null && plot.Group.Members.Contains(playerInfo))
            {
                return PlotRelation.GROUP_MEMBER;
            }
            if (plot.hasPlotOwner() && plot.getPlotOwner().Friends.Contains(playerInfo))
            {
                return PlotRelation.COMRADE;
            }
            if (plot.hasCity() && plot.getCity().isCitizen(playerInfo))
            {
                return PlotRelation.CITIZEN;
            }
            return PlotRelation.STRANGER;
        }

        /// <summary>The right the player has here without counting friendship.</summary>
        static bool RightWithoutFriendship(PlayerInfo playerInfo, Plot plot, PermType permType)
        {
            PermsHandler perms = plot.getPermsHandler();
            if (plot.hasCity() && plot.getCity().isCitizen(playerInfo)) return perms.getPerm(PermGroup.CITIZEN, permType);
            return perms.getPerm(PermGroup.STRANGER, permType);
        }

        private static bool EvalPermission(PlayerInfo playerInfo, Plot plot, PermType permType)
        {
            PermsHandler perms = plot.getPermsHandler();
            switch (getPlotRelationForPlayerInfo(playerInfo, plot))
            {
                case PlotRelation.MANAGABLE_OWNER:
                case PlotRelation.PLOT_OWNER:
                    return true;
                case PlotRelation.COMRADE:
                    // Friendship adds the friend's rights, it never takes the citizen's away.
                    return perms.getPerm(PermGroup.COMRADE, permType)
                        || RightWithoutFriendship(playerInfo, plot, permType);
                case PlotRelation.GROUP_MEMBER:
                    // The group's rights replace the citizen ones on its plots.
                    return plot.Group.Perms.getPerm(PermGroup.CITIZEN, permType);
                case PlotRelation.CITIZEN:
                    return perms.getPerm(PermGroup.CITIZEN, permType);
                default:
                    return perms.getPerm(PermGroup.STRANGER, permType);
            }
        }
    }
}
