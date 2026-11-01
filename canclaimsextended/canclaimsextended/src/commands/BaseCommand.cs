using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    public class BaseCommand
    {
        /// <summary>
        /// Precondition body: allows the command if the calling player has ANY of the given permissions.
        /// Use inside .WithPreCondition: <c>(args) => BaseCommand.RequirePermission(args, EnumPlayerPermissions.X)</c>
        /// </summary>
        public static TextCommandResult RequirePermission(TextCommandCallingArgs args, params EnumPlayerPermissions[] anyPermission)
        {
            if (args.Caller.Player is IServerPlayer player
                && canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo))
            {
                // An admin under /cadmin as may do what the mayor may.
                if (AdminActing.IsActor(playerInfo)) return TextCommandResult.Success();
                foreach (var permission in anyPermission)
                {
                    if (playerInfo.PlayerPermissionsHandler.HasPermission(permission))
                    {
                        return TextCommandResult.Success();
                    }
                }
                return NoRight();
            }
            return NoPlayerInfo();
        }

        /// <summary>
        /// Precondition body: allows the command only for roles listed in ROLE_CODES_WITH_ADMIN_RIGHTS.
        /// Use inside .WithPreCondition: <c>(args) => BaseCommand.RequireAdminRole(args)</c>
        /// </summary>
        public static TextCommandResult RequireAdminRole(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is IServerPlayer player)
            {
                return AdminRole.IsAdmin(player) ? TextCommandResult.Success() : NoRight();
            }
            return NoPlayerInfo();
        }

        public static bool TryResolveCaller(TextCommandCallingArgs args, out IServerPlayer player, out PlayerInfo playerInfo, out TextCommandResult err)
        {
            player = args.Caller.Player as IServerPlayer;
            if (player == null)
            {
                playerInfo = null;
                err = NoPlayerInfo();
                return false;
            }
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out playerInfo))
            {
                err = NoPlayerInfo();
                return false;
            }
            err = null;
            return true;
        }

        /// <summary>The caller and the city they live in.</summary>
        public static bool TryResolveCity(TextCommandCallingArgs args, out IServerPlayer player, out PlayerInfo playerInfo, out City city, out TextCommandResult err)
        {
            city = null;
            if (!TryResolveCaller(args, out player, out playerInfo, out err)) return false;
            // Under /cadmin as, the city the admin acts for.
            city = AdminActing.CityOf(playerInfo);
            if (city == null)
            {
                err = TextCommandResult.Error("canclaimsextended:you_dont_have_city");
                return false;
            }
            return true;
        }

        /// <summary>The caller and their city, if they are its mayor or hold any of the given rights.</summary>
        public static bool TryResolveManagedCity(TextCommandCallingArgs args, out PlayerInfo playerInfo, out City city,
            out TextCommandResult err, params EnumPlayerPermissions[] anyPermission)
        {
            if (!TryResolveCity(args, out _, out playerInfo, out city, out err)) return false;
            if (!IsMayorOr(playerInfo, city, anyPermission))
            {
                err = NoRight();
                return false;
            }
            return true;
        }

        /// <summary>The city named by argument <paramref name="index"/>, with the error for an unknown name.</summary>
        public static bool TryResolveCityArg(TextCommandCallingArgs args, int index, out City city, out TextCommandResult err)
        {
            if (TryFindCity((string)args[index], out city))
            {
                err = null;
                return true;
            }
            err = TextCommandResult.Error("canclaimsextended:no_such_city");
            return false;
        }

        /// <inheritdoc cref="ManageRules.IsMayorOr"/>
        public static bool IsMayorOr(PlayerInfo playerInfo, City city, params EnumPlayerPermissions[] anyPermission)
            => ManageRules.IsMayorOr(playerInfo, city, anyPermission);

        /// <summary>The on/off argument of a switch command.</summary>
        public static bool IsOn(object arg) => (string)arg == "on";

        // Keys, not text: the game translates a one-line result into the caller's own language.
        public static TextCommandResult NoRight()
        {
            return TextCommandResult.Error("canclaimsextended:you_dont_have_right_for_that_command");
        }

        public static TextCommandResult NoPlayerInfo()
        {
            return TextCommandResult.Error("canclaimsextended:no_such_player_info");
        }

        /// <inheritdoc cref="PlotLookup.TryGetPlotUnderPlayer"/>
        public static bool TryGetPlotUnderPlayer(IServerPlayer player, out Plot plot) => PlotLookup.TryGetPlotUnderPlayer(player, out plot);

        /// <inheritdoc cref="PlotLookup.TryGetSubPlotUnderPlayer"/>
        public static bool TryGetSubPlotUnderPlayer(IServerPlayer player, out Plot plot) => PlotLookup.TryGetSubPlotUnderPlayer(player, out plot);

        /// <summary><see cref="TryGetPlotUnderPlayer"/> with the error for "nothing claimed here".</summary>
        public static bool TryResolvePlot(IServerPlayer player, out Plot plot, out TextCommandResult err)
        {
            if (TryGetPlotUnderPlayer(player, out plot))
            {
                err = null;
                return true;
            }
            err = TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            return false;
        }

        /// <summary>
        /// <see cref="TryGetSubPlotUnderPlayer"/> with its errors: on the land of a city the player is
        /// told it is city land rather than a plot.
        /// </summary>
        public static bool TryResolveSubPlot(IServerPlayer player, out Plot plot, out TextCommandResult err)
        {
            if (TryGetSubPlotUnderPlayer(player, out plot))
            {
                err = null;
                return true;
            }
            if (!canclaimsextended.dataStorage.GetChunk(ChunkUnderPlayer(player), out CityChunk chunk))
            {
                err = TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            }
            else if (chunk.Group != null)
            {
                err = ErrorWithParams("canclaimsextended:not_a_plot_group_land", chunk.City.getPartNameReplaceUnder(),
                    chunk.Group.getPartNameReplaceUnder(), canclaimsextended.config.COMMAND_CITY, chunk.Group.GetPartName());
            }
            else
            {
                err = ErrorWithParams("canclaimsextended:not_a_plot_city_land", chunk.City.getPartNameReplaceUnder(), canclaimsextended.config.COMMAND_CITY);
            }
            return false;
        }

        /// <inheritdoc cref="PlotLookup.ChunkUnderPlayer"/>
        public static geometry.ChunkPos ChunkUnderPlayer(IServerPlayer player) => PlotLookup.ChunkUnderPlayer(player);

        /// <summary>A known player by the typed name.</summary>
        public static bool TryFindPlayer(string rawName, out PlayerInfo target, out TextCommandResult err)
        {
            target = null;
            string name = Filter.FilterName(rawName ?? "");
            if (name.Length > 0 && canclaimsextended.dataStorage.GetPlayerByName(name, out target))
            {
                err = null;
                return true;
            }
            err = TextCommandResult.Error("canclaimsextended:invalid_player_name");
            return false;
        }

        /// <inheritdoc cref="ManageRules.TryFindCity"/>
        public static bool TryFindCity(string rawName, out City city) => ManageRules.TryFindCity(rawName, out city);

        /// <summary>A plot of the player's own city by its number, typed as n or #n. Names are not unique.</summary>
        public static bool TryFindCityPlot(PlayerInfo playerInfo, string raw, out Plot plot, out TextCommandResult err)
        {
            plot = null;
            string typed = (raw ?? "").TrimStart('#');
            City own = AdminActing.CityOf(playerInfo);
            if (own != null && int.TryParse(typed, out int number))
            {
                foreach (Plot p in own.getCityPlots())
                {
                    if (p.Number == number)
                    {
                        plot = p;
                        break;
                    }
                }
            }
            err = plot == null ? ErrorWithParams("canclaimsextended:plot_number_not_found", typed) : null;
            return plot != null;
        }

        /// <summary>The typed name filtered and checked as the name of a new city.</summary>
        public static bool TryValidateNewCityName(string rawName, out string name, out TextCommandResult err)
        {
            name = Filter.FilterName(rawName ?? "");
            err = null;
            if (name.Length == 0 || !Filter.IsAllowedName(name))
            {
                err = TextCommandResult.Error("canclaimsextended:invalid_new_city_name");
            }
            else if (name.Length > canclaimsextended.config.MAX_LENGTH_CITY_NAME)
            {
                err = TextCommandResult.Error("canclaimsextended:city_name_is_too_long");
            }
            else if (canclaimsextended.dataStorage.CityExistsByName(name))
            {
                err = TextCommandResult.Error("canclaimsextended:city_name_is_already_taken");
            }
            return err == null;
        }

        public static TextCommandResult SuccessWithParams(string msg, params object[] msgParams)
        {
            return new TextCommandResult
            {
                Status = EnumCommandStatus.Success,
                MessageParams = msgParams,
                StatusMessage = msg
            };
        }

        public static TextCommandResult ErrorWithParams(string msg, params object[] msgParams)
        {
            return new TextCommandResult
            {
                Status = EnumCommandStatus.Error,
                MessageParams = msgParams,
                StatusMessage = msg
            };
        }

        public static TextCommandResult FromCheck(ClaimCheck check)
        {
            return ErrorWithParams(check.LangKey, check.Args);
        }
    }
}
