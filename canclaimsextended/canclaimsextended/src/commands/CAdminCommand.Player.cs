using System.Linq;
using System.Text;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    public partial class CAdminCommand
    {
        public static TextCommandResult PlayerInfoCmd(TextCommandCallingArgs args)
        {
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out var err)) return err;
            StringBuilder sb = new StringBuilder();
            sb.Append(target.GetPartName()).Append(" (").Append(target.Guid).Append(")\n");
            sb.Append(string.Join("", target.getStatus(null)));
            if (target.hasCity() && target.City.isMayor(target))
            {
                sb.Append(Lang.Get("canclaimsextended:admin_is_mayor")).Append('\n');
            }
            sb.Append(Lang.Get("canclaimsextended:admin_owned_plots", target.PlayerPlots.Count)).Append('\n');
            sb.Append(Lang.Get("canclaimsextended:admin_permissions",
                string.Join(", ", target.PlayerPermissionsHandler.GetPermissions().Select(p => p.ToString()))));
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>Removes the player from their city. A mayor has to be replaced first.</summary>
        public static TextCommandResult PlayerKick(TextCommandCallingArgs args)
        {
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out var err)) return err;
            City city = target.City;
            if (city == null)
            {
                return TextCommandResult.Error("canclaimsextended:player_has_no_city");
            }
            if (city.isMayor(target))
            {
                return ErrorWithParams("canclaimsextended:admin_kick_mayor", canclaimsextended.config.COMMAND_CADMIN);
            }
            target.leaveCity();
            canclaimsextended.dataStorage.ResetCacheForAll();
            MessageHandler.sendLangInCity(city, "canclaimsextended:player_was_kicked", target.getPartNameReplaceUnder());
            MessageHandler.sendLangToPlayerInfo(target, "canclaimsextended:you_were_kicked", city.getPartNameReplaceUnder());
            return TextCommandResult.Success();
        }

        /// <summary>
        /// /cadmin player jail &lt;player&gt; &lt;city&gt; [minutes]: into a prison of the city, as if killed
        /// by one of its citizens; for PRISON_SENTENCE_HOURS without minutes.
        /// </summary>
        public static TextCommandResult PlayerJail(TextCommandCallingArgs args)
        {
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out var err)) return err;
            if (!TryResolveCityArg(args, 1, out City city, out err)) return err;
            if (target.isPrisoned())
            {
                return ErrorWithParams("canclaimsextended:admin_already_jailed", target.getPartNameReplaceUnder());
            }
            long seconds = args.Parsers[2].IsMissing
                ? System.Math.Max(1, canclaimsextended.config.PRISON_SENTENCE_HOURS) * 3600L
                : (int)args[2] * 60L;
            if (seconds <= 0)
            {
                return TextCommandResult.Error("canclaimsextended:admin_jail_bad_minutes");
            }
            if (!PrisonHandler.Jail(target, city, seconds, true))
            {
                return ErrorWithParams("canclaimsextended:admin_jail_no_prison", city.getPartNameReplaceUnder());
            }
            return SuccessWithParams("canclaimsextended:admin_jailed", target.getPartNameReplaceUnder(), city.getPartNameReplaceUnder(), seconds / 60);
        }

        /// <summary>/cadmin player release &lt;player&gt;: ends the sentence now, as when it runs out.</summary>
        public static TextCommandResult PlayerRelease(TextCommandCallingArgs args)
        {
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out var err)) return err;
            if (!target.isPrisoned())
            {
                return ErrorWithParams("canclaimsextended:admin_not_jailed", target.getPartNameReplaceUnder());
            }
            PrisonHandler.Release(target, true);
            return SuccessWithParams("canclaimsextended:admin_released", target.getPartNameReplaceUnder());
        }

        /// <summary>
        /// /cadmin player joindefault [player]: what a player new to the mod gets on the first login,
        /// NEW_PLAYERS_DEFAULT_CITY, for the named player or the caller; says why when it cannot.
        /// </summary>
        public static TextCommandResult PlayerJoinDefault(TextCommandCallingArgs args)
        {
            PlayerInfo target;
            if (args.Parsers[0].IsMissing)
            {
                if (!TryResolveCaller(args, out _, out target, out var callerErr)) return callerErr;
            }
            else if (!TryFindPlayer((string)args[0], out target, out var findErr)) return findErr;

            string problem = events.OnPlayerJoin.JoinDefaultCity(target, out string name);
            if (problem != null)
            {
                return ErrorWithParams(problem, name, target.getPartNameReplaceUnder());
            }
            canclaimsextended.dataStorage.ResetCacheForAll();
            return SuccessWithParams("canclaimsextended:default_city_joined", target.getPartNameReplaceUnder(), target.City.getPartNameReplaceUnder());
        }
    }
}
