using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>/city plotsgroup: groups of citizens with their own rights on some city plots and chunks.</summary>
    public class CityCommandPlotsGroup : BaseCommand
    {
        /// <summary>The caller's city and the group named by the first argument.</summary>
        static bool TryResolveGroup(TextCommandCallingArgs args, out IServerPlayer player, out PlayerInfo playerInfo,
            out City city, out PlotsGroup group, out TextCommandResult err)
        {
            group = null;
            if (!TryResolveCity(args, out player, out playerInfo, out city, out err)) return false;
            if (!PlotsGroupHandler.TryFind(city, (string)args[0], out group))
            {
                err = TextCommandResult.Error("canclaimsextended:plotsgroup_not_found");
                return false;
            }
            return true;
        }

        public static TextCommandResult Create(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_CREATE)) return NoRight();
            string name = Filter.FilterName((string)args[0] ?? "");
            if (name.Length == 0 || name.Length > canclaimsextended.config.MAX_LENGTH_CITY_NAME || !Filter.IsAllowedName(name))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_invalid_name");
            }
            if (PlotsGroupHandler.TryFind(city, name, out _))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_name_taken");
            }
            int max = canclaimsextended.config.MAX_PLOTS_GROUP_PER_CITY;
            if (city.PlotsGroups.Count >= max)
            {
                return ErrorWithParams("canclaimsextended:plotsgroup_too_many", max);
            }
            PlotsGroupHandler.Create(city, name);
            return SuccessWithParams("canclaimsextended:plotsgroup_created", StringFunctions.ReplaceUnderscore(name));
        }

        public static TextCommandResult Delete(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE)) return NoRight();
            PlotsGroupHandler.Delete(group);
            return SuccessWithParams("canclaimsextended:plotsgroup_deleted", group.getPartNameReplaceUnder());
        }

        public static TextCommandResult List(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (city.PlotsGroups.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:plotsgroup_none");
            }
            string lines = string.Join("\n", city.PlotsGroups.Select(g =>
                Lang.Get("canclaimsextended:plotsgroup_list_line", g.getPartNameReplaceUnder(), g.Members.Count, g.Plots.Count(), g.Fee)
                + " " + Lang.Get("canclaimsextended:plotsgroup_chunks_count", g.Chunks.Count())));
            return TextCommandResult.Success(Lang.Get("canclaimsextended:plotsgroup_list") + "\n" + lines);
        }

        public static TextCommandResult Info(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out _, out _, out PlotsGroup group, out var err)) return err;
            string text = Lang.Get("canclaimsextended:plotsgroup_info", group.getPartNameReplaceUnder(),
                StringFunctions.MakeStringPlayersName(group.Members, ", "), group.Plots.Count(), group.Fee)
                + "\n" + Lang.Get("canclaimsextended:plotsgroup_chunks_count", group.Chunks.Count())
                + "\n" + group.Perms.getStringForChat();
            if (group.HasPendingFee)
            {
                text += "\n" + Lang.Get("canclaimsextended:plotsgroup_info_pending_fee", group.PendingFee,
                    TimeFunctions.GetDateFromEpochSeconds(group.PendingFeeAt));
            }
            return TextCommandResult.Success(text);
        }

        /// <summary>The city plot the caller stands in goes into the group.</summary>
        public static TextCommandResult AddPlot(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out var player, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLOT)) return NoRight();
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!city.Equals(plot.getCity()))
            {
                return TextCommandResult.Error("canclaimsextended:not_your_city_plot");
            }
            if (group.Equals(plot.Group))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_plot_already_in");
            }
            PlotsGroupHandler.AddPlot(group, plot);
            return SuccessWithParams("canclaimsextended:plotsgroup_plot_added", group.getPartNameReplaceUnder());
        }

        public static TextCommandResult RemovePlot(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE_PLOT)) return NoRight();
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!city.Equals(plot.getCity()) || plot.Group == null)
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_plot_not_in_group");
            }
            string name = plot.Group.getPartNameReplaceUnder();
            PlotsGroupHandler.RemovePlot(plot);
            return SuccessWithParams("canclaimsextended:plotsgroup_plot_removed", name);
        }

        /// <summary>The chunk of the city the caller stands in goes into the group, with its land outside plots.</summary>
        public static TextCommandResult AddChunk(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out var player, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLOT)) return NoRight();
            if (!canclaimsextended.dataStorage.GetChunk(ChunkUnderPlayer(player), out CityChunk chunk) || !city.Equals(chunk.City))
            {
                return TextCommandResult.Error("canclaimsextended:chunk_not_your_city");
            }
            if (group.Equals(chunk.Group))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_chunk_already_in");
            }
            PlotsGroupHandler.AddChunk(group, chunk);
            return SuccessWithParams("canclaimsextended:plotsgroup_chunk_added", group.getPartNameReplaceUnder(), chunk.Pos.X, chunk.Pos.Z);
        }

        public static TextCommandResult RemoveChunk(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE_PLOT)) return NoRight();
            if (!canclaimsextended.dataStorage.GetChunk(ChunkUnderPlayer(player), out CityChunk chunk) || !city.Equals(chunk.City)
                || chunk.Group == null)
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_chunk_not_in_group");
            }
            string name = chunk.Group.getPartNameReplaceUnder();
            PlotsGroupHandler.RemoveChunk(chunk);
            return SuccessWithParams("canclaimsextended:plotsgroup_chunk_removed", name, chunk.Pos.X, chunk.Pos.Z);
        }

        public static TextCommandResult Invite(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLAYER)) return NoRight();
            if (!TryFindPlayer((string)args[1], out PlayerInfo target, out err)) return err;
            if (!city.isCitizen(target))
            {
                return TextCommandResult.Error("canclaimsextended:player_should_be_in_same_city");
            }
            if (group.Members.Contains(target))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_already_member");
            }
            if (InvitationHandler.tryGetInvitation(group, target, out _))
            {
                return TextCommandResult.Error("canclaimsextended:player_already_invited");
            }
            if (group.GetSentInvitations().Count >= group.getMaxSentInvitations())
            {
                return ErrorWithParams("canclaimsextended:too_many_sent_invitations", group.getMaxSentInvitations());
            }
            long expires = TimeFunctions.GetEpochSeconds() + canclaimsextended.config.INVITATION_TIMEOUT_HOURS * 3600L;
            if (!InvitationHandler.addNewInvite(PlotsGroupHandler.MakeGroupInvitation(group, target, expires)))
            {
                return ErrorWithParams("canclaimsextended:player_has_too_many_invitations", target.GetPartName());
            }
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:plotsgroup_you_were_invited",
                group.getPartNameReplaceUnder(), group.Fee, canclaimsextended.config.COMMAND_CITY, group.GetPartName()));
            return SuccessWithParams("canclaimsextended:invitation_sent", target.GetPartName(), canclaimsextended.config.INVITATION_TIMEOUT_HOURS);
        }

        public static TextCommandResult Uninvite(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLAYER)) return NoRight();
            if (!TryFindPlayer((string)args[1], out PlayerInfo target, out err)) return err;
            if (!InvitationHandler.removeInvitationIfExists(group, target))
            {
                return TextCommandResult.Error("canclaimsextended:no_invitation");
            }
            return SuccessWithParams("canclaimsextended:invitation_removed", target.GetPartName());
        }

        public static TextCommandResult Join(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out _, out PlotsGroup group, out var err)) return err;
            if (!InvitationHandler.tryGetInvitation(group, playerInfo, out Invitation invitation))
            {
                return TextCommandResult.Error("canclaimsextended:no_invitation");
            }
            invitation.accept();
            return TextCommandResult.Success();
        }

        public static TextCommandResult Deny(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out _, out PlotsGroup group, out var err)) return err;
            if (!InvitationHandler.tryGetInvitation(group, playerInfo, out Invitation invitation))
            {
                return TextCommandResult.Error("canclaimsextended:no_invitation");
            }
            invitation.deny();
            return SuccessWithParams("canclaimsextended:you_denied_invitation", group.getPartNameReplaceUnder());
        }

        public static TextCommandResult Kick(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_KICK_PLAYER)) return NoRight();
            if (!TryFindPlayer((string)args[1], out PlayerInfo target, out err)) return err;
            if (!group.Members.Contains(target))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_not_member");
            }
            PlotsGroupHandler.RemoveMember(group, target);
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:plotsgroup_you_were_kicked", group.getPartNameReplaceUnder()));
            return SuccessWithParams("canclaimsextended:plotsgroup_player_kicked", target.GetPartName(), group.getPartNameReplaceUnder());
        }

        public static TextCommandResult Leave(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out _, out PlotsGroup group, out var err)) return err;
            if (!group.Members.Contains(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_not_member");
            }
            PlotsGroupHandler.RemoveMember(group, playerInfo);
            return SuccessWithParams("canclaimsextended:plotsgroup_you_left", group.getPartNameReplaceUnder());
        }

        /// <summary>args: group, use|build|attack, on|off.</summary>
        public static TextCommandResult SetPerm(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_SET)) return NoRight();
            if (!PermsHandler.TryParseType((string)args[1], out PermType type))
            {
                return TextCommandResult.Error("canclaimsextended:wrong_perm_type");
            }
            bool value = (bool)args[2];
            PlotsGroupHandler.SetPerm(group, type, value);
            return SuccessWithParams("canclaimsextended:plotsgroup_perm_set", group.getPartNameReplaceUnder(), (string)args[1], value ? "on" : "off");
        }

        /// <summary>args: group, pvp|fire|blast|nomobspawn, on|off. Each flag has its own right.</summary>
        public static TextCommandResult SetFlag(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            string flag = (string)args[1];
            EnumPlayerPermissions right;
            switch (flag)
            {
                case "pvp": right = EnumPlayerPermissions.CITY_PLOTSGROUP_SET_PVP; break;
                case "fire": right = EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FIRE; break;
                case "blast": right = EnumPlayerPermissions.CITY_PLOTSGROUP_SET_BLAST; break;
                case "nomobspawn": right = EnumPlayerPermissions.CITY_PLOTSGROUP_SET_MOBSPAWN; break;
                default: return TextCommandResult.Error("canclaimsextended:wrong_flag");
            }
            if (!IsMayorOr(playerInfo, city, right)) return NoRight();
            bool value = (bool)args[2];
            PlotsGroupHandler.SetFlag(group, flag, value);
            return SuccessWithParams("canclaimsextended:plotsgroup_flag_set", group.getPartNameReplaceUnder(), flag, value ? "on" : "off");
        }

        public static TextCommandResult SetFee(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out City city, out PlotsGroup group, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FEE)) return NoRight();
            double fee = (double)args[1];
            double max = canclaimsextended.config.MAX_PLOTSGROUP_FEE;
            if (fee < 0 || fee > max)
            {
                return ErrorWithParams("canclaimsextended:plotsgroup_fee_out_of_range", max);
            }
            return PlotsGroupHandler.SetFee(group, fee)
                ? SuccessWithParams("canclaimsextended:plotsgroup_fee_set", group.getPartNameReplaceUnder(), fee)
                : SuccessWithParams("canclaimsextended:plotsgroup_fee_raise_set", group.getPartNameReplaceUnder(), fee,
                    TimeFunctions.GetDateFromEpochSeconds(group.PendingFeeAt));
        }

        public static TextCommandResult AcceptFee(TextCommandCallingArgs args)
        {
            if (!TryResolveGroup(args, out _, out var playerInfo, out _, out PlotsGroup group, out var err)) return err;
            if (!PlotsGroupHandler.AcceptFee(group, playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plotsgroup_no_pending_fee");
            }
            return SuccessWithParams("canclaimsextended:plotsgroup_fee_accepted", group.getPartNameReplaceUnder(), group.PendingFee);
        }
    }
}
