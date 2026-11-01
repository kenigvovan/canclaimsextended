using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    public class CityCommandMembers : BaseCommand
    {
        public static TextCommandResult Invite(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out _, out City city, out var err)) return err;
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (target.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:player_has_city_already");
            }
            if (InvitationHandler.tryGetInvitation(city, target, out _))
            {
                return TextCommandResult.Error("canclaimsextended:player_already_invited");
            }
            if (city.GetSentInvitations().Count >= city.getMaxSentInvitations())
            {
                return ErrorWithParams("canclaimsextended:too_many_sent_invitations", city.getMaxSentInvitations());
            }
            long expires = TimeFunctions.GetEpochSeconds() + canclaimsextended.config.INVITATION_TIMEOUT_HOURS * 3600L;
            Invitation invitation = CityMembership.MakeCityInvitation(city, target, expires);
            if (!InvitationHandler.addNewInvite(invitation))
            {
                // Only the receiver's limit is left to fail.
                return ErrorWithParams("canclaimsextended:player_has_too_many_invitations", target.GetPartName());
            }
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:you_were_invited_to_city",
                city.getPartNameReplaceUnder(), canclaimsextended.config.COMMAND_CITY, city.GetPartName()));
            return SuccessWithParams("canclaimsextended:invitation_sent", target.GetPartName(), canclaimsextended.config.INVITATION_TIMEOUT_HOURS);
        }

        public static TextCommandResult Uninvite(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out _, out City city, out var err)) return err;
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (!InvitationHandler.removeInvitationIfExists(city, target))
            {
                return TextCommandResult.Error("canclaimsextended:no_invitation");
            }
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:invitation_withdrawn", city.getPartNameReplaceUnder()));
            return SuccessWithParams("canclaimsextended:invitation_removed", target.GetPartName());
        }

        /// <summary>Accepts this city's invitation, or simply moves in when the city is open.</summary>
        public static TextCommandResult Join(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (playerInfo.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:you_already_have_city");
            }
            if (!TryFindCity((string)args.LastArg, out City city))
            {
                return TextCommandResult.Error("canclaimsextended:no_such_city");
            }
            if (InvitationHandler.tryGetInvitation(city, playerInfo, out Invitation invitation))
            {
                invitation.accept();
                return TextCommandResult.Success();
            }
            if (!city.openCity)
            {
                return TextCommandResult.Error("canclaimsextended:city_not_open");
            }
            CityMembership.JoinCity(city, playerInfo);
            return TextCommandResult.Success();
        }

        public static TextCommandResult Deny(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!TryFindCity((string)args.LastArg, out City city)
                || !InvitationHandler.tryGetInvitation(city, playerInfo, out Invitation invitation))
            {
                return TextCommandResult.Error("canclaimsextended:no_invitation");
            }
            invitation.deny();
            return SuccessWithParams("canclaimsextended:you_denied_invitation", city.getPartNameReplaceUnder());
        }

        public static TextCommandResult Leave(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (city.isMayor(playerInfo))
            {
                return ErrorWithParams("canclaimsextended:mayor_cannot_leave", canclaimsextended.config.COMMAND_CITY);
            }
            playerInfo.leaveCity();
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.CitizenLeft, playerInfo.GetPartName());
            MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:player_left_city", playerInfo.getPartNameReplaceUnder()));
            return SuccessWithParams("canclaimsextended:you_left_city", city.getPartNameReplaceUnder());
        }

        public static TextCommandResult Kick(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (!city.Equals(target.City))
            {
                return TextCommandResult.Error("canclaimsextended:player_should_be_in_same_city");
            }
            if (target.Equals(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:cannot_kick_yourself");
            }
            if (city.isMayor(target))
            {
                return TextCommandResult.Error("canclaimsextended:cannot_kick_mayor");
            }
            target.leaveCity();
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.CitizenKicked, target.GetPartName(), playerInfo.GetPartName());
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:you_were_kicked", city.getPartNameReplaceUnder()));
            MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:player_was_kicked", target.getPartNameReplaceUnder()));
            return TextCommandResult.Success();
        }

        /// <summary>Who may see sent invitations gets those, anyone else the ones they received.</summary>
        public static TextCommandResult Invites(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            InvitationHandler.findAndDeleteOverdueInvitations();
            City own = AdminActing.CityOf(playerInfo);
            bool asSender = own != null
                && (AdminActing.IsActor(playerInfo) || playerInfo.PlayerPermissionsHandler.HasPermission(EnumPlayerPermissions.SHOW_INVITES_SENT));
            var list = asSender ? own.GetSentInvitations() : playerInfo.getReceivedInvitations();
            if (list.Count == 0)
            {
                return TextCommandResult.Success(asSender ? "canclaimsextended:no_invitations_sent" : "canclaimsextended:no_invitations_received");
            }
            string lines = string.Join("", list.SelectMany(inv => inv.getStatus(playerInfo)));
            return TextCommandResult.Success(Lang.Get(asSender ? "canclaimsextended:invitations_sent" : "canclaimsextended:invitations_received") + "\n" + lines);
        }
    }
}
