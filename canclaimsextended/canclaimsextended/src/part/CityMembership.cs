using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.messages;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// A player becoming a citizen: from the commands, the default city of new players and city
    /// invitations restored after a restart.
    /// </summary>
    public static class CityMembership
    {
        /// <summary>Builds a city invitation; also brings one back after a restart.</summary>
        public static Invitation MakeCityInvitation(interfaces.ISender sender, interfaces.IReceiver receiver, long expires)
        {
            if (sender is not City city || receiver is not PlayerInfo target) return null;
            return new Invitation(city, target, expires,
                () => JoinCity(city, target),
                () => MessageHandler.sendMsgToPlayerInfo(city.getMayor(),
                    Lang.Get("canclaimsextended:invitation_denied", target.GetPartName(), city.getPartNameReplaceUnder())));
        }

        /// <summary>Makes the player a citizen. Called on accepting an invitation and for open cities.</summary>
        public static void JoinCity(City city, PlayerInfo player)
        {
            // The player may have joined or founded another city while the invitation waited.
            if (player.hasCity())
            {
                MessageHandler.sendMsgToPlayerInfo(player, Lang.Get("canclaimsextended:you_already_have_city"));
                return;
            }
            if (!canclaimsextended.dataStorage.GetCityByGUID(city.Guid, out _))
            {
                MessageHandler.sendMsgToPlayerInfo(player, Lang.Get("canclaimsextended:no_such_city"));
                return;
            }
            InvitationHandler.deleteAllInvitationsForReceiver(player);
            city.getCityCitizens().Add(player);
            // A city cannot hold its own citizen as a criminal; joining it clears the record.
            if (city.Criminals.Remove(player.Guid))
            {
                canclaimsextended.Db.deleteCriminal(city, player.Guid);
            }
            // setCity reapplies the rights.
            player.setCity(city);
            player.PlayerCache.setPlot(null);
            city.saveToDatabase();
            player.saveToDatabase();
            MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:player_joined_city", player.getPartNameReplaceUnder()));
            citylog.CityLog.Add(city, citylog.EnumCityLogEvent.CitizenJoined, player.GetPartName());
        }
    }
}
