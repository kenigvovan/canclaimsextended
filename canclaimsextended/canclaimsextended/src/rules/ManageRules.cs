using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;

namespace canclaimsextended.src.rules
{
    /// <summary>Who may manage a city or a plot: the same checks for the commands and the window.</summary>
    public static class ManageRules
    {
        /// <summary>The mayor of the city, or a player holding any of the given permissions.</summary>
        public static bool IsMayorOr(PlayerInfo playerInfo, City city, params EnumPlayerPermissions[] anyPermission)
        {
            if (city.isMayor(playerInfo)) return true;
            foreach (var permission in anyPermission)
            {
                if (playerInfo.PlayerPermissionsHandler.HasPermission(permission)) return true;
            }
            return false;
        }

        /// <summary>The plot's owner, or the mayor of its city, or a citizen holding PLOT_SET_ALL_CITY_PLOTS.</summary>
        public static bool MayManage(PlayerInfo playerInfo, Plot plot)
        {
            if (plot.hasPlotOwner() && plot.getPlotOwner().Equals(playerInfo)) return true;
            return plot.hasCity() && plot.getCity().Equals(AdminActing.CityOf(playerInfo))
                && IsMayorOr(playerInfo, plot.getCity(), EnumPlayerPermissions.PLOT_SET_ALL_CITY_PLOTS);
        }

        /// <summary>The mayor of the plot's city, or a citizen of it allowed to make plots for the city.</summary>
        public static bool MayReshape(PlayerInfo playerInfo, Plot plot)
        {
            City city = plot.getCity();
            if (city == null) return false;
            return city.isMayor(playerInfo)
                || (city.isCitizen(playerInfo) && IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CREATE_PLOT));
        }

        /// <summary>
        /// Whether /cadmin plot extend would take the box: the /plot extend check, run as the city's
        /// mayor like the command itself, so the admin tab shows its button exactly when it works.
        /// </summary>
        public static ClaimCheck CheckAdminExtend(PlayerInfo admin, Plot plot, BlockBox box)
        {
            if (!plot.hasCity()) return ClaimCheck.Fail("canclaimsextended:plot_not_claimed");
            // Already acting (the window is built as some city's mayor): the limits are off already
            // and the check does not depend on whose mayor the admin is.
            if (AdminActing.Active) return ClaimRules.CheckExtend(plot, box);
            return AdminActing.Run(admin, plot.getCity(), () => ClaimRules.CheckExtend(plot, box));
        }

        /// <summary>A city by the typed name.</summary>
        public static bool TryFindCity(string rawName, out City city)
        {
            return canclaimsextended.dataStorage.GetCityByName(Filter.FilterName(rawName ?? ""), out city);
        }
    }
}
