using System;
using canclaimsextended.src.economy;
using canclaimsextended.src.part;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    public class CityCommandMoney : BaseCommand
    {
        public static TextCommandResult Balance(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out _, out City city, out var err)) return err;
            var money = canclaimsextended.economyProvider;
            if (!money.SupportsPlayerWallet)
            {
                return TextCommandResult.Error("canclaimsextended:economy_not_configured");
            }
            return SuccessWithParams("canclaimsextended:city_balance", money.GetBalance(city.MoneyAccountName),
                Math.Floor(city.GetDayPaymentAmount()), city.DebtBalance);
        }

        /// <summary>
        /// With an amount: from the player's account to the city's. Without one: the coins in the
        /// player's hand.
        /// </summary>
        public static TextCommandResult Deposit(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out _, out City city, out var err)) return err;
            if (!CanMoveCoins(out err)) return err;
            var money = canclaimsextended.economyProvider;

            if (!args.Parsers[0].IsMissing)
            {
                int amount = (int)args[0];
                if (amount <= 0)
                {
                    return TextCommandResult.Error("canclaimsextended:economy_bad_amount");
                }
                if (money.GetBalance(player.PlayerUID) < amount)
                {
                    return ErrorWithParams("canclaimsextended:economy_not_enough_money", money.GetBalance(player.PlayerUID));
                }
                if (money.Transfer(player.PlayerUID, city.MoneyAccountName, amount) != MoneyOperationResult.Success)
                {
                    return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
                }
                return SuccessWithParams("canclaimsextended:city_deposited", amount);
            }

            decimal collected = money.TakeCoinItemsFromActiveSlot(player);
            if (collected <= 0)
            {
                return TextCommandResult.Error("canclaimsextended:economy_no_coins_in_hand");
            }
            if (money.Deposit(city.MoneyAccountName, collected) != MoneyOperationResult.Success)
            {
                money.GiveCoinItemsToPlayer(player, collected);
                return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
            }
            return SuccessWithParams("canclaimsextended:city_deposited", collected);
        }

        /// <summary>Coins from the treasury into the player's hands.</summary>
        public static TextCommandResult Withdraw(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out _, out City city, out var err)) return err;
            if (!CanMoveCoins(out err)) return err;
            var money = canclaimsextended.economyProvider;
            int amount = (int)args[0];
            if (amount <= 0)
            {
                return TextCommandResult.Error("canclaimsextended:economy_bad_amount");
            }
            if (money.GetBalance(city.MoneyAccountName) < amount)
            {
                return ErrorWithParams("canclaimsextended:city_not_enough_money", money.GetBalance(city.MoneyAccountName));
            }
            if (money.Withdraw(city.MoneyAccountName, amount) != MoneyOperationResult.Success)
            {
                return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
            }
            money.GiveCoinItemsToPlayer(player, amount);
            return SuccessWithParams("canclaimsextended:city_withdrawn", amount);
        }

        public static TextCommandResult Prices(TextCommandCallingArgs args)
        {
            Config c = canclaimsextended.config;
            long exampleUnits = PlotPricing.Units(16L * 16 * 16);
            // Upkeep of a plain plot, as the prices tab and the editor show it.
            double unitUpkeep = PlotPricing.UnitUpkeep(part.structure.PlotType.DEFAULT);
            return SuccessWithParams("canclaimsextended:city_prices",
                c.BLOCKS_PER_COST_UNIT, c.PLOT_CLAIM_PRICE_PER_UNIT, unitUpkeep,
                exampleUnits,
                exampleUnits * c.PLOT_CLAIM_PRICE_PER_UNIT,
                exampleUnits * unitUpkeep,
                c.NEW_CITY_COST, c.CITY_BASE_CARE, c.CITY_MAX_DEBT,
                c.CHUNK_CLAIM_PRICE, c.CHUNK_DAILY_UPKEEP, c.CLAIM_CHUNK_SIZE);
        }

        /// <summary>Coins move by command only with a virtual economy; bank chests are used in place.</summary>
        static bool CanMoveCoins(out TextCommandResult err)
        {
            var money = canclaimsextended.economyProvider;
            err = !money.SupportsPlayerWallet ? TextCommandResult.Error("canclaimsextended:economy_not_configured")
                : !money.SupportsRemoteCoinTransfer ? TextCommandResult.Error("canclaimsextended:economy_use_bank_chest")
                : null;
            return err == null;
        }
    }
}
