using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/plot set ...: rights, flags and name of the plot the player stands in.</summary>
    public class PlotCommandSettings : BaseCommand
    {
        const int PLOT_NAME_MAX_LENGTH = 32;

        public static TextCommandResult SetPerm(TextCommandCallingArgs args)
        {
            if (!TryResolvePlotManager(args, out Plot plot, out var err)) return err;
            if (!PermsHandler.TryParseGroup((string)args[0], out PermGroup group)
                || !PermsHandler.TryParseType((string)args[1], out PermType type))
            {
                return TextCommandResult.Error("canclaimsextended:perm_bad_args");
            }
            plot.getPermsHandler().setPerm(group, type, IsOn(args[2]));
            plot.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return SuccessWithParams("canclaimsextended:perm_set", (string)args[0], (string)args[1], (string)args[2]);
        }

        public static TextCommandResult SetFlag(TextCommandCallingArgs args, string flag)
        {
            if (!TryResolvePlotManager(args, out Plot plot, out var err)) return err;
            plot.getPermsHandler().trySetFlag(flag, IsOn(args.LastArg));
            plot.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return SuccessWithParams("canclaimsextended:flag_set", flag, (string)args.LastArg);
        }

        /// <summary>/plot set name [name]; without a name the plot goes back to having none.</summary>
        public static TextCommandResult SetName(TextCommandCallingArgs args)
        {
            if (!TryResolvePlotManager(args, out Plot plot, out var err)) return err;
            string raw = (string)args.LastArg;
            string name = raw == null ? "" : Filter.FilterName(raw);
            if (raw != null && (name.Length == 0 || !Filter.IsAllowedName(name)))
            {
                return TextCommandResult.Error("canclaimsextended:invalid_string");
            }
            if (name.Length > PLOT_NAME_MAX_LENGTH)
            {
                return ErrorWithParams("canclaimsextended:plot_name_too_long", PLOT_NAME_MAX_LENGTH);
            }
            plot.SetPartName(name);
            plot.saveToDatabase();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return name.Length == 0
                ? TextCommandResult.Success("canclaimsextended:plot_name_reset")
                : SuccessWithParams("canclaimsextended:plot_name_set", name);
        }

        /// <summary>
        /// The plot under the caller, if they own it, are the mayor of its city, or hold
        /// PLOT_SET_ALL_CITY_PLOTS in that city.
        /// </summary>
        static bool TryResolvePlotManager(TextCommandCallingArgs args, out Plot plot, out TextCommandResult err)
        {
            plot = null;
            if (!TryResolveCaller(args, out var player, out var playerInfo, out err)) return false;
            // The land of a city is set with /city set.
            if (!TryResolveSubPlot(player, out plot, out err)) return false;
            if (!ManageRules.MayManage(playerInfo, plot))
            {
                err = NoRight();
                return false;
            }
            return true;
        }
    }
}
