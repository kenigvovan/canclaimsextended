using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/plot set fee &lt;n&gt;: what the owner of this city plot pays the treasury daily; 0 removes it.</summary>
    public class PlotCommandFee : BaseCommand
    {
        public static TextCommandResult SetFee(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!plot.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            }
            City city = plot.getCity();
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.PLOT_SET_FEE)) return NoRight();
            int fee = (int)args.LastArg;
            int max = (int)canclaimsextended.config.MAX_PLOT_FEE;
            if (fee < 0 || fee > max)
            {
                return ErrorWithParams("canclaimsextended:plot_fee_out_of_range", max);
            }
            plot.setCustomTax(fee);
            plot.saveToDatabase();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return fee == 0
                ? TextCommandResult.Success("canclaimsextended:plot_fee_removed")
                : SuccessWithParams("canclaimsextended:plot_fee_set", fee);
        }
    }
}
