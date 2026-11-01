using canclaimsextended.src.part;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/cadmin as: commands run as the mayor of a city (see AdminActing).</summary>
    public partial class CAdminCommand
    {
        /// <summary>
        /// /cadmin as &lt;city&gt; &lt;command&gt;: one /city or /plot command run as the city's mayor (see
        /// AdminActing). Works for a city without a mayor too.
        /// </summary>
        public static TextCommandResult ActAs(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!TryResolveCityArg(args, 0, out City city, out err)) return err;
            Config cfg = canclaimsextended.config;
            string command = ((string)args[1] ?? "").Trim().TrimStart('/');
            string root = command.Split(' ')[0].ToLowerInvariant();
            // Only the commands the mayor has; anything else gains nothing and /cadmin must not nest.
            if (root != cfg.COMMAND_CITY.ToLowerInvariant() && root != cfg.COMMAND_PLOT.ToLowerInvariant())
            {
                return ErrorWithParams("canclaimsextended:admin_as_only_city_plot", cfg.COMMAND_CITY, cfg.COMMAND_PLOT);
            }
            return RunAs(args, playerInfo, city, command);
        }

        /// <summary>
        /// Runs a chat command (without the /) as the mayor of the city. Shared by /cadmin as and the
        /// /cadmin plot commands, which are the /plot ones run for the city of the plot.
        /// </summary>
        internal static TextCommandResult RunAs(TextCommandCallingArgs args, PlayerInfo admin, City city, string command)
        {
            if (AdminActing.Active)
            {
                return TextCommandResult.Error("canclaimsextended:admin_as_nested");
            }
            canclaimsextended.sapi.Logger.Audit("[canclaimsextended] {0} as mayor of {1}: /{2}", admin.GetPartName(), city.GetPartName(), command);
            TextCommandResult result = null;
            AdminActing.Run(admin, city, () =>
            {
                // Runs at once on this thread (as the game's /executeas relies on), inside the scope.
                canclaimsextended.sapi.ChatCommands.ExecuteUnparsed("/" + command, new TextCommandCallingArgs { Caller = args.Caller },
                    r => result = r);
                return true;
            });
            return result ?? TextCommandResult.Error("canclaimsextended:admin_as_no_result");
        }
    }
}
