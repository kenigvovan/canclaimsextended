using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    public class PlotCommand : BaseCommand
    {
        public static TextCommandResult HereInfo(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryResolvePlot(player, out Plot plot, out err)) return err;
            return TextCommandResult.Success(string.Join("", plot.getStatus(playerInfo)));
        }
    }
}
