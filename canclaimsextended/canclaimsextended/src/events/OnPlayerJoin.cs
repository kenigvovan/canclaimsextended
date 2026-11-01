using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.rights;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    public static class OnPlayerJoin
    {
        public static void Event_OnPlayerJoin(IServerPlayer player)
        {
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo))
            {
                playerInfo = new PlayerInfo(player.PlayerName, player.PlayerUID)
                {
                    TimeStampFirstJoined = TimeFunctions.GetEpochSeconds()
                };
                canclaimsextended.dataStorage.AddPlayer(playerInfo);
                JoinDefaultCityOnFirstLogin(playerInfo);
            }
            else if (!player.PlayerName.Equals(playerInfo.GetPartName()))
            {
                canclaimsextended.dataStorage.ChangePlayerName(playerInfo, player.PlayerName);
                playerInfo.SetPartName(player.PlayerName);
            }
            playerInfo.TimeStampLasOnline = TimeFunctions.GetEpochSeconds();
            playerInfo.PlayerCache.setPlot(null);
            RightsHandler.reapplyRights(playerInfo);
            playerInfo.saveToDatabase();
            // Freed or moved back into the prison; served sentences end here for those who were offline.
            PrisonHandler.CheckPlayer(player, playerInfo);
            if (playerInfo.hasCity() && !string.IsNullOrEmpty(playerInfo.City.DailyMsg))
            {
                messages.MessageHandler.sendMsgToPlayer(player, Vintagestory.API.Config.Lang.Get("canclaimsextended:city_daily_msg",
                    playerInfo.City.getPartNameReplaceUnder(), playerInfo.City.DailyMsg));
            }
        }

        /// <summary>
        /// Makes the player a citizen of NEW_PLAYERS_DEFAULT_CITY. Done for a player new to the mod's
        /// database, and by /cadmin player joindefault to try it. The lang key of why it was not
        /// done (its argument is the configured name), null when it was.
        /// </summary>
        internal static string JoinDefaultCity(PlayerInfo playerInfo, out string name)
        {
            name = canclaimsextended.config.NEW_PLAYERS_DEFAULT_CITY?.Trim() ?? "";
            if (name.Length == 0) return "canclaimsextended:default_city_not_set";
            if (!TryFindDefaultCity(out City city)) return DEFAULT_CITY_NOT_FOUND;
            if (playerInfo.hasCity()) return "canclaimsextended:default_city_player_has_city";
            CityMembership.JoinCity(city, playerInfo);
            return null;
        }

        const string DEFAULT_CITY_NOT_FOUND = "canclaimsextended:default_city_not_found";

        /// <summary>The city NEW_PLAYERS_DEFAULT_CITY names, if it exists. Names keep underscores for spaces.</summary>
        public static bool TryFindDefaultCity(out City city)
        {
            city = null;
            string name = canclaimsextended.config?.NEW_PLAYERS_DEFAULT_CITY?.Trim() ?? "";
            if (name.Length == 0 || canclaimsextended.dataStorage == null) return false;
            return canclaimsextended.dataStorage.GetCityByName(name, out city)
                || canclaimsextended.dataStorage.GetCityByName(name.Replace(" ", "_"), out city);
        }

        /// <summary>On a first login only a missing city is worth a word in the log; the rest is normal.</summary>
        static void JoinDefaultCityOnFirstLogin(PlayerInfo playerInfo)
        {
            if (JoinDefaultCity(playerInfo, out string name) == DEFAULT_CITY_NOT_FOUND)
            {
                canclaimsextended.sapi.Logger.Warning("[canclaimsextended] NEW_PLAYERS_DEFAULT_CITY: no city named '{0}'.", name);
            }
        }
    }
}
