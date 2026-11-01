using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// Plots owned by citizens: put up for sale by the mayor (city land) or the owner (their own),
    /// bought by a citizen of the same city, given back to the city by the owner.
    /// </summary>
    public class PlotCommandOwnership : BaseCommand
    {
        /// <summary>Who may sell this plot: its owner, or the mayor when the city holds it.</summary>
        static bool MaySell(PlayerInfo playerInfo, Plot plot)
        {
            if (plot.hasPlotOwner()) return plot.getPlotOwner().Equals(playerInfo);
            return plot.getCity().isMayor(playerInfo);
        }

        public static TextCommandResult SetForSale(TextCommandCallingArgs args)
        {
            if (!TryResolveCityPlot(args, out _, out var playerInfo, out Plot plot, out var err)) return err;
            if (!MaySell(playerInfo, plot))
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_yours_to_sell");
            }
            int price = (int)args[0];
            if (price < 0)
            {
                return TextCommandResult.Error("canclaimsextended:plot_price_negative");
            }
            plot.Price = price;
            plot.saveToDatabase();
            // The price tag over the plot and on the map comes from this.
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return SuccessWithParams("canclaimsextended:plot_set_for_sale", price);
        }

        public static TextCommandResult SetNotForSale(TextCommandCallingArgs args)
        {
            if (!TryResolveCityPlot(args, out _, out var playerInfo, out Plot plot, out var err)) return err;
            if (!MaySell(playerInfo, plot))
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_yours_to_sell");
            }
            if (!plot.IsForSale)
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_for_sale");
            }
            plot.Price = -1;
            plot.saveToDatabase();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return TextCommandResult.Success("canclaimsextended:plot_sale_cancelled");
        }

        /// <summary>
        /// Without an argument shows the price. The purchase itself needs the price typed back,
        /// so the buyer never pays a price changed after they looked.
        /// </summary>
        public static TextCommandResult Claim(TextCommandCallingArgs args)
        {
            if (!TryResolveCityPlot(args, out _, out var playerInfo, out Plot plot, out var err)) return err;
            City city = plot.getCity();
            if (!plot.IsForSale)
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_for_sale");
            }
            if (plot.hasPlotOwner() && plot.getPlotOwner().Equals(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plot_already_yours");
            }
            if (!city.isCitizen(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plot_buy_not_citizen");
            }
            // City land is the mayor's to manage already; buying it would only move coins in a circle.
            if (!plot.hasPlotOwner() && city.isMayor(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plot_buy_mayor");
            }
            if (args.Parsers[0].IsMissing || (int)args[0] != plot.Price)
            {
                return ErrorWithParams("canclaimsextended:plot_buy_confirm", plot.Price, canclaimsextended.config.COMMAND_PLOT);
            }
            decimal price = plot.Price;
            if (canclaimsextended.economyProvider.GetBalance(playerInfo.Guid) < price)
            {
                return TextCommandResult.Error("canclaimsextended:not_enough_money");
            }

            PlayerInfo seller = plot.getPlotOwner();
            string toAccount = seller != null ? seller.Guid : city.MoneyAccountName;
            if (price > 0 && canclaimsextended.economyProvider.Transfer(playerInfo.Guid, toAccount, price) != MoneyOperationResult.Success)
            {
                return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
            }

            if (seller != null)
            {
                seller.PlayerPlots.Remove(plot);
                seller.saveToDatabase();
                MessageHandler.sendMsgToPlayerInfo(seller, Lang.Get("canclaimsextended:plot_sold_by_you", playerInfo.GetPartName(), price));
            }
            plot.setPlotOwner(playerInfo);
            plot.Price = -1;
            plot.TimeStampClaimed = TimeFunctions.GetEpochSeconds();
            playerInfo.PlayerPlots.Add(plot);
            plot.saveToDatabase();
            playerInfo.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.PlotSold, playerInfo.GetPartName(), plot.Number.ToString(),
                price.ToString(), seller?.GetPartName() ?? city.GetPartName());
            return SuccessWithParams("canclaimsextended:plot_bought", price);
        }

        /// <summary>The owner gives the plot back to the city; the mayor may take it back too.</summary>
        public static TextCommandResult Unclaim(TextCommandCallingArgs args)
        {
            if (!TryResolveCityPlot(args, out _, out var playerInfo, out Plot plot, out var err)) return err;
            if (!plot.hasPlotOwner())
            {
                return TextCommandResult.Error("canclaimsextended:plot_has_no_owner");
            }
            PlayerInfo owner = plot.getPlotOwner();
            if (!owner.Equals(playerInfo) && !plot.getCity().isMayor(playerInfo))
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_yours");
            }
            owner.PlayerPlots.Remove(plot);
            plot.ReturnToCity();
            plot.saveToDatabase();
            owner.saveToDatabase();
            if (!owner.Equals(playerInfo))
            {
                MessageHandler.sendMsgToPlayerInfo(owner, Lang.Get(AdminActing.Active ? "canclaimsextended:plot_taken_back_by_admin" : "canclaimsextended:plot_taken_back_by_mayor"));
            }
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            return TextCommandResult.Success("canclaimsextended:plot_given_back");
        }

        /// <summary>The caller and the city plot they stand in.</summary>
        static bool TryResolveCityPlot(TextCommandCallingArgs args, out IServerPlayer player, out PlayerInfo playerInfo,
            out Plot plot, out TextCommandResult err)
        {
            plot = null;
            if (!TryResolveCaller(args, out player, out playerInfo, out err)) return false;
            if (!TryResolveSubPlot(player, out plot, out err)) return false;
            if (!plot.hasCity())
            {
                err = TextCommandResult.Error("canclaimsextended:plot_not_claimed");
                return false;
            }
            return true;
        }
    }
}
