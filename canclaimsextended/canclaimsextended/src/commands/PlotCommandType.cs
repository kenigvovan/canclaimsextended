using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>/plot set type and /plot types. The city pays the one-time price from its treasury.</summary>
    public class PlotCommandType : BaseCommand
    {
        /// <summary>The mayor, or a citizen allowed to manage every city plot. Owners do not: the city pays the upkeep.</summary>
        static bool MaySetType(PlayerInfo playerInfo, Plot plot)
        {
            City city = plot.getCity();
            return city != null && city.Equals(AdminActing.CityOf(playerInfo))
                && IsMayorOr(playerInfo, city, EnumPlayerPermissions.PLOT_SET_ALL_CITY_PLOTS);
        }

        public static TextCommandResult SetType(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!MaySetType(playerInfo, plot)) return NoRight();

            string code = ((string)args[0])?.ToLowerInvariant();
            ClaimCheck check = PlotTypeChanger.Check(plot, code, out PlotInfo info);
            if (!check.Ok) return FromCheck(check);

            City city = plot.getCity();
            decimal cost = (decimal)info.SetCost();
            string typeName = Lang.Get(info.LangKey);
            void Apply()
            {
                PlotTypeChanger.Apply(plot, info);
                part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.PlotTypeChanged, playerInfo.GetPartName(), plot.Number.ToString(), info.Code);
            }
            if (PaidAction.IsFreeFor(city) || cost <= 0)
            {
                Apply();
                return SuccessWithParams("canclaimsextended:plot_type_set", typeName);
            }

            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                () => !canclaimsextended.dataStorage.GetPlotByGuid(plot.Guid, out _) || !city.Equals(plot.getCity()) || !MaySetType(playerInfo, plot)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(PlotTypeChanger.Check(plot, code, out _)),
                Apply,
                () => Lang.Get("canclaimsextended:plot_type_set_paid", typeName, cost));
            return SuccessWithParams("canclaimsextended:agree_plot_type", typeName, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>The types this server offers, with their one-time price and daily upkeep per unit.</summary>
        public static TextCommandResult ListTypes(TextCommandCallingArgs args)
        {
            var sb = new System.Text.StringBuilder(Lang.Get("canclaimsextended:plot_types_header", canclaimsextended.config.BLOCKS_PER_COST_UNIT));
            foreach (string code in PlotTypes.EnabledCodes)
            {
                PlotTypes.TryGet(code, out PlotInfo info);
                sb.Append('\n').Append(Lang.Get("canclaimsextended:plot_types_line", code, Lang.Get(info.LangKey), info.SetCost(), info.UnitUpkeep()));
            }
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
