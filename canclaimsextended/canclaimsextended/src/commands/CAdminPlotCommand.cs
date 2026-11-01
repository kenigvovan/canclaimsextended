using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// /cadmin plot ...: the /plot command of the same name, run as the mayor of the city of the plot
    /// the admin stands in (/cadmin as), so free and without limits. Only giving the plot to a
    /// citizen has no mayor's command behind it and is done here.
    /// </summary>
    public class CAdminPlotCommand : BaseCommand
    {
        /// <summary>/cadmin plot create &lt;city&gt;: the selection becomes a plot of that city.</summary>
        public static TextCommandResult Create(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!TryResolveCityArg(args, 0, out City city, out err)) return err;
            return CAdminCommand.RunAs(args, playerInfo, city, canclaimsextended.config.COMMAND_PLOT + " create");
        }

        /// <summary>/cadmin plot delete: back to city land; a plot with an owner is taken back first.</summary>
        public static TextCommandResult Delete(TextCommandCallingArgs args)
        {
            if (!TryResolvePlotHere(args, out PlayerInfo admin, out Plot plot, out var err)) return err;
            if (plot.hasPlotOwner())
            {
                TextCommandResult takeBack = CAdminCommand.RunAs(args, admin, plot.getCity(), canclaimsextended.config.COMMAND_PLOT + " unclaim");
                if (takeBack.Status != EnumCommandStatus.Success) return takeBack;
            }
            return Plot(args, admin, plot, "delete");
        }

        public static TextCommandResult Extend(TextCommandCallingArgs args) => Here(args, "extend");
        /// <summary>/cadmin plot cut [part]: the part the admin stands in, or part n as /plot boxes numbers them.</summary>
        public static TextCommandResult Cut(TextCommandCallingArgs args)
        {
            if (!TryResolvePlotHere(args, out PlayerInfo admin, out Plot plot, out var err)) return err;
            return Plot(args, admin, plot, args.Parsers[0].IsMissing ? "cut" : $"cut {plot.Number} {(int)args[0]}");
        }
        public static TextCommandResult Resize(TextCommandCallingArgs args, string verb) => Here(args, $"{verb} {args[0]} {args[1]}");
        public static TextCommandResult SetType(TextCommandCallingArgs args) => Here(args, "set type " + args[0]);
        public static TextCommandResult SetName(TextCommandCallingArgs args) => Here(args, args.Parsers[0].IsMissing ? "set name" : "set name " + args[0]);
        public static TextCommandResult SetPerm(TextCommandCallingArgs args) => Here(args, $"set perm {args[0]} {args[1]} {args[2]}");
        public static TextCommandResult SetFlag(TextCommandCallingArgs args) => Here(args, $"set {args[0]} {args[1]}");
        public static TextCommandResult SetFee(TextCommandCallingArgs args) => Here(args, "set fee " + args[0]);
        public static TextCommandResult Sell(TextCommandCallingArgs args) => Here(args, "fs " + args[0]);
        public static TextCommandResult NoSell(TextCommandCallingArgs args) => Here(args, "nfs");
        public static TextCommandResult TakeBack(TextCommandCallingArgs args) => Here(args, "unclaim");

        /// <summary>The /plot command with these arguments, as the mayor of the plot under the admin.</summary>
        static TextCommandResult Here(TextCommandCallingArgs args, string plotArgs)
        {
            if (!TryResolvePlotHere(args, out PlayerInfo admin, out Plot plot, out var err)) return err;
            return Plot(args, admin, plot, plotArgs);
        }

        static TextCommandResult Plot(TextCommandCallingArgs args, PlayerInfo admin, Plot plot, string plotArgs)
        {
            return CAdminCommand.RunAs(args, admin, plot.getCity(), canclaimsextended.config.COMMAND_PLOT + " " + plotArgs);
        }

        /// <summary>The admin and the city plot they stand in.</summary>
        static bool TryResolvePlotHere(TextCommandCallingArgs args, out PlayerInfo admin, out Plot plot, out TextCommandResult err)
        {
            plot = null;
            if (!TryResolveCaller(args, out var player, out admin, out err)) return false;
            if (!TryResolveSubPlot(player, out plot, out err)) return false;
            if (!plot.hasCity())
            {
                err = TextCommandResult.Error("canclaimsextended:plot_not_claimed");
                return false;
            }
            return true;
        }

        /// <summary>
        /// /cadmin plot owner &lt;player&gt;: gives the plot to a citizen of its city for nothing; the
        /// previous owner loses it. A mayor can only sell a plot, so this one has no /plot behind it.
        /// </summary>
        public static TextCommandResult SetOwner(TextCommandCallingArgs args)
        {
            if (!TryResolvePlotHere(args, out _, out Plot plot, out var err)) return err;
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out err)) return err;
            City city = plot.getCity();
            // Owners are citizens everywhere else in the mod: fees, rights and selling expect it.
            if (!city.isCitizen(target))
            {
                return ErrorWithParams("canclaimsextended:admin_plot_owner_not_citizen", target.getPartNameReplaceUnder(), city.getPartNameReplaceUnder());
            }
            PlayerInfo previous = plot.getPlotOwner();
            if (target.Equals(previous))
            {
                return TextCommandResult.Error("canclaimsextended:plot_already_yours");
            }
            if (previous != null)
            {
                previous.PlayerPlots.Remove(plot);
                previous.saveToDatabase();
                messages.MessageHandler.sendMsgToPlayerInfo(previous, Lang.Get("canclaimsextended:plot_taken_back_by_admin"));
            }
            plot.setPlotOwner(target);
            plot.Price = -1;
            plot.TimeStampClaimed = auxiliary.TimeFunctions.GetEpochSeconds();
            target.PlayerPlots.Add(plot);
            plot.saveToDatabase();
            target.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            messages.MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:plot_given_by_admin"));
            return SuccessWithParams("canclaimsextended:admin_plot_owner_set", target.getPartNameReplaceUnder());
        }
    }
}
