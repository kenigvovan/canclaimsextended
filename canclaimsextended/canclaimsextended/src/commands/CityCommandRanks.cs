using System;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// City ranks: named sets of rights the mayor hands to citizens. A rank carries only rights from
    /// AVAILABLE_CITY_PERMISSIONS, and only rights the editor holds - otherwise a deputy allowed to
    /// edit ranks could write themselves the treasury.
    /// </summary>
    public class CityCommandRanks : BaseCommand
    {
        const int RANK_NAME_MAX_LENGTH = 24;

        public static TextCommandResult Create(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_CREATE_CITY_RANK, out _, out City city, out var err)) return err;
            string name = Filter.FilterName((string)args[0]);
            if (name.Length == 0 || !Filter.IsAllowedName(name))
            {
                return TextCommandResult.Error("canclaimsextended:invalid_rank_name");
            }
            if (name.Length > RANK_NAME_MAX_LENGTH)
            {
                return ErrorWithParams("canclaimsextended:rank_name_too_long", RANK_NAME_MAX_LENGTH);
            }
            if (!city.AddNewCityRank(name, new CustomCityRank { Name = name }))
            {
                return ErrorWithParams("canclaimsextended:rank_already_exists", name);
            }
            city.saveToDatabase();
            return SuccessWithParams("canclaimsextended:rank_created", name);
        }

        public static TextCommandResult Delete(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_DELETE_CITY_RANK, out _, out City city, out var err)) return err;
            string name = (string)args[0];
            if (!city.RemoveCityRank(name))
            {
                return ErrorWithParams("canclaimsextended:no_such_rank", name);
            }
            // Holders lose the rank and its rights now, not at their next login.
            foreach (PlayerInfo holder in city.getCityCitizens().Where(c => c.hasCityTitle(name)).ToArray())
            {
                holder.removeCityTitle(name);
                RightsHandler.reapplyRights(holder);
                holder.saveToDatabase();
            }
            city.saveToDatabase();
            return SuccessWithParams("canclaimsextended:rank_deleted", name);
        }

        public static TextCommandResult AddPerm(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_ADD_PERMISSION_TO_RANK, out PlayerInfo editor, out City city, out var err)) return err;
            if (!city.CustomCityRanks.TryGetValue((string)args[0], out CustomCityRank rank))
            {
                return ErrorWithParams("canclaimsextended:no_such_rank", (string)args[0]);
            }
            if (!TryParsePermission((string)args[1], out EnumPlayerPermissions perm))
            {
                return ErrorWithParams("canclaimsextended:rank_bad_permission", string.Join(", ", canclaimsextended.config.AVAILABLE_CITY_PERMISSIONS));
            }
            if (!city.isMayor(editor) && !editor.PlayerPermissionsHandler.HasPermission(perm))
            {
                return TextCommandResult.Error("canclaimsextended:rank_perm_not_yours");
            }
            if (!rank.Permissions.Add(perm))
            {
                return ErrorWithParams("canclaimsextended:rank_has_perm", rank.Name, perm.ToString());
            }
            ReapplyHolders(city, rank.Name);
            city.saveToDatabase();
            return SuccessWithParams("canclaimsextended:rank_perm_added", perm.ToString(), rank.Name);
        }

        public static TextCommandResult RemovePerm(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_REMOVE_PERMISSION_FROM_RANK, out _, out City city, out var err)) return err;
            if (!city.CustomCityRanks.TryGetValue((string)args[0], out CustomCityRank rank))
            {
                return ErrorWithParams("canclaimsextended:no_such_rank", (string)args[0]);
            }
            // Any defined right may be taken away, also one the host has since dropped from the list.
            if (!Enum.TryParse((string)args[1], true, out EnumPlayerPermissions perm) || !Enum.IsDefined(perm)
                || !rank.Permissions.Remove(perm))
            {
                return ErrorWithParams("canclaimsextended:rank_has_no_perm", rank.Name, (string)args[1]);
            }
            ReapplyHolders(city, rank.Name);
            city.saveToDatabase();
            return SuccessWithParams("canclaimsextended:rank_perm_removed", perm.ToString(), rank.Name);
        }

        public static TextCommandResult Grant(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_SET_RANK, out PlayerInfo granter, out City city, out var err)) return err;
            if (!city.CustomCityRanks.TryGetValue((string)args[0], out CustomCityRank rank))
            {
                return ErrorWithParams("canclaimsextended:no_such_rank", (string)args[0]);
            }
            if (!TryResolveCitizen(city, (string)args[1], out PlayerInfo target, out err)) return err;
            if (target.hasCityTitle(rank.Name))
            {
                return ErrorWithParams("canclaimsextended:player_already_has_rank", target.GetPartName(), rank.Name);
            }
            // Handed out only by someone holding everything it grants, so nobody gives themselves more.
            if (!city.isMayor(granter) && rank.Permissions.Any(p => !granter.PlayerPermissionsHandler.HasPermission(p)))
            {
                return TextCommandResult.Error("canclaimsextended:rank_perm_not_yours");
            }
            city.GrantPlayerRank(rank.Name, target);
            target.addCityTitle(rank.Name);
            RightsHandler.reapplyRights(target);
            target.saveToDatabase();
            city.saveToDatabase();
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.RankGranted, granter.GetPartName(), rank.Name, target.GetPartName());
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:you_got_rank", rank.Name));
            return SuccessWithParams("canclaimsextended:rank_granted", rank.Name, target.GetPartName());
        }

        public static TextCommandResult Revoke(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_REMOVE_RANK, out PlayerInfo revoker, out City city, out var err)) return err;
            string rankName = (string)args[0];
            if (!TryResolveCitizen(city, (string)args[1], out PlayerInfo target, out err)) return err;
            if (!target.hasCityTitle(rankName))
            {
                return ErrorWithParams("canclaimsextended:player_has_no_rank", target.GetPartName(), rankName);
            }
            city.RevokePlayerRank(rankName, target);
            target.removeCityTitle(rankName);
            RightsHandler.reapplyRights(target);
            target.saveToDatabase();
            city.saveToDatabase();
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.RankRevoked, revoker.GetPartName(), rankName, target.GetPartName());
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:you_lost_rank", rankName));
            return SuccessWithParams("canclaimsextended:rank_revoked", rankName, target.GetPartName());
        }

        /// <summary>Ranks of the caller's city, with how many hold each, and the caller's own.</summary>
        public static TextCommandResult List(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out PlayerInfo playerInfo, out City city, out var err)) return err;
            string ranks = city.CustomCityRanks.Count == 0
                ? Lang.Get("canclaimsextended:rank_none")
                : string.Join(", ", city.CustomCityRanks.Values.Select(r => r.Name + " (" + r.CitizensNames.Count + ")"));
            string own = playerInfo.getCityTitles().Count == 0
                ? Lang.Get("canclaimsextended:rank_none")
                : string.Join(", ", playerInfo.getCityTitles());
            return SuccessWithParams("canclaimsextended:rank_list", ranks, own);
        }

        public static TextCommandResult Info(TextCommandCallingArgs args)
        {
            if (!TryResolveRankManager(args, EnumPlayerPermissions.CITY_SEE_CITY_RANKS, out _, out City city, out var err)) return err;
            if (!city.CustomCityRanks.TryGetValue((string)args[0], out CustomCityRank rank))
            {
                return ErrorWithParams("canclaimsextended:no_such_rank", (string)args[0]);
            }
            string perms = rank.Permissions.Count == 0 ? Lang.Get("canclaimsextended:rank_none") : string.Join(", ", rank.Permissions);
            string holders = rank.CitizensNames.Count == 0 ? Lang.Get("canclaimsextended:rank_none") : string.Join(", ", rank.CitizensNames);
            return SuccessWithParams("canclaimsextended:rank_info", rank.Name, perms, holders);
        }

        /// <summary>The caller and their city, if they are its mayor or hold the given right.</summary>
        static bool TryResolveRankManager(TextCommandCallingArgs args, EnumPlayerPermissions permission,
            out PlayerInfo playerInfo, out City city, out TextCommandResult err)
        {
            return TryResolveManagedCity(args, out playerInfo, out city, out err, permission);
        }

        static bool TryResolveCitizen(City city, string rawName, out PlayerInfo target, out TextCommandResult err)
        {
            if (!TryFindPlayer(rawName, out target, out err)) return false;
            if (!city.isCitizen(target))
            {
                err = TextCommandResult.Error("canclaimsextended:player_should_be_citizen");
                return false;
            }
            return true;
        }

        /// <summary>A right the host lets ranks carry; names are matched without case.</summary>
        static bool TryParsePermission(string text, out EnumPlayerPermissions perm)
        {
            return Enum.TryParse(text, true, out perm) && Enum.IsDefined(perm)
                && canclaimsextended.config.AVAILABLE_CITY_PERMISSIONS.Contains(perm);
        }

        static void ReapplyHolders(City city, string rankName)
        {
            foreach (PlayerInfo citizen in city.getCityCitizens())
            {
                if (citizen.hasCityTitle(rankName))
                {
                    RightsHandler.reapplyRights(citizen);
                }
            }
        }
    }
}
