using System;
using System.Collections.Generic;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.timers
{
    /// <summary>
    /// Takes the daily upkeep from every city. Polled every 30 s, so a changed day length applies
    /// without a restart. Cycles missed while the server was off are charged on start, up to
    /// DAY_MAX_MISSED_CYCLES_TO_PROCESS.
    /// </summary>
    public class DayTimer
    {
        const string SAVE_KEY = "canclaimsextended_lastdaystart";

        public static void Start(ICoreServerAPI sapi)
        {
            bool first = true;
            auxiliary.ServerTicks.Every(sapi, _ =>
            {
                CheckNewDay(first);
                first = false;
                PlotsGroupHandler.SettleDueRaises();
            }, 30000);
        }

        /// <summary>
        /// Runs the day if a new cycle has begun. The marker is stored before the run, so a crash in
        /// the middle never charges the same cycle twice.
        /// </summary>
        public static void CheckNewDay(bool afterStart)
        {
            var saveGame = canclaimsextended.sapi.WorldManager.SaveGame;
            long current = TimeFunctions.GetCurrentDayStart(TimeFunctions.GetEpochSeconds());
            long last = saveGame.GetData(SAVE_KEY, -1L);
            if (last < 0)
            {
                // A new world starts counting from now instead of charging for the past.
                saveGame.StoreData(SAVE_KEY, current);
                return;
            }
            if (current <= last) return;

            long runs = 1;
            if (afterStart)
            {
                long passed = Math.Max(1, (current - last) / TimeFunctions.DayLength);
                runs = Math.Min(passed, Math.Max(0, canclaimsextended.config.DAY_MAX_MISSED_CYCLES_TO_PROCESS));
            }
            saveGame.StoreData(SAVE_KEY, current);
            for (long i = 0; i < runs; i++)
            {
                Run();
            }
        }

        public static void Run()
        {
            var toDelete = new List<City>();
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList())
            {
                if (city.isTechnicalCity()) continue;
                try
                {
                    // Fees first: what the citizens pay helps the treasury cover today's upkeep.
                    CollectFees(city);
                    PlotsGroupHandler.CollectFees(city);
                    CollectPlotFees(city);
                    if (ProcessCityCare(city)) toDelete.Add(city);
                }
                catch (Exception e)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] upkeep of {0} failed: {1}", city.GetPartName(), e);
                }
            }
            foreach (City city in toDelete)
            {
                // One city that fails to go must not keep the others, or the new day, from coming.
                try
                {
                    MessageHandler.sendGlobalLang("canclaimsextended:city_demolished_for_debt", city.getPartNameReplaceUnder());
                    PartDemolition.demolishCity(city);
                }
                catch (Exception e)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] demolishing {0} for debt failed: {1}", city.GetPartName(), e);
                }
            }
            MessageHandler.sendGlobalLang("canclaimsextended:new_day");
        }

        /// <summary>
        /// Every citizen but the mayor pays the city fee into the treasury. Who cannot pay is removed
        /// with DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE, only warned without it. Without a real
        /// economy there is nothing to move.
        /// </summary>
        static void CollectFees(City city)
        {
            var money = canclaimsextended.economyProvider;
            if (!money.SupportsPlayerWallet || city.fee <= 0) return;
            decimal fee = city.fee;
            foreach (PlayerInfo citizen in new List<PlayerInfo>(city.getCityCitizens()))
            {
                if (city.isMayor(citizen)) continue;
                if (money.GetBalance(citizen.Guid) >= fee
                    && money.Transfer(citizen.Guid, city.MoneyAccountName, fee) == MoneyOperationResult.Success)
                {
                    continue;
                }
                if (canclaimsextended.config.DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE)
                {
                    citizen.leaveCity();
                    MessageHandler.sendMsgToPlayerInfo(citizen, Lang.Get("canclaimsextended:city_fee_kicked", city.getPartNameReplaceUnder(), fee));
                    MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:city_fee_kicked_city", citizen.getPartNameReplaceUnder()));
                }
                else
                {
                    MessageHandler.sendMsgToPlayerInfo(citizen, Lang.Get("canclaimsextended:city_fee_unpaid", city.getPartNameReplaceUnder(), fee));
                }
            }
        }

        /// <summary>
        /// Owners of city plots with a fee pay it into the treasury. Who cannot pay loses the plot
        /// back to the city. Only with a real economy.
        /// </summary>
        static void CollectPlotFees(City city)
        {
            var money = canclaimsextended.economyProvider;
            if (!money.SupportsPlayerWallet) return;
            bool changed = false;
            foreach (Plot plot in new List<Plot>(city.getCityPlots()))
            {
                if (!plot.hasPlotOwner()) continue;
                decimal fee = (decimal)plot.getCustomTax();
                if (fee <= 0) continue;
                PlayerInfo owner = plot.getPlotOwner();
                // The mayor would only pay themselves.
                if (city.isMayor(owner)) continue;
                if (money.GetBalance(owner.Guid) >= fee
                    && money.Transfer(owner.Guid, city.MoneyAccountName, fee) == MoneyOperationResult.Success)
                {
                    continue;
                }
                owner.PlayerPlots.Remove(plot);
                plot.ReturnToCity();
                plot.saveToDatabase();
                owner.saveToDatabase();
                canclaimsextended.dataStorage.NotifyPlotChanged(plot);
                changed = true;
                MessageHandler.sendMsgToPlayerInfo(owner, Lang.Get("canclaimsextended:plot_fee_unpaid_lost", city.getPartNameReplaceUnder(), fee));
                CityLog.Add(city, EnumCityLogEvent.PlotFeeUnpaid, owner.GetPartName(), fee.ToString());
            }
            if (changed) canclaimsextended.dataStorage.ResetCacheForAll();
        }

        /// <summary>Takes today's upkeep plus old debt. True when the debt passed CITY_MAX_DEBT.</summary>
        static bool ProcessCityCare(City city)
        {
            var money = canclaimsextended.economyProvider;
            double debtBefore = city.DebtBalance;
            // Whole coins only: the check, the payment and the debt all use the rounded-down sum.
            decimal today = Math.Floor((decimal)city.GetDayPaymentAmount());
            decimal due = today + (decimal)city.DebtBalance;
            if (due < 1) return false;

            decimal balance = money.GetBalance(city.MoneyAccountName);
            if (balance >= due)
            {
                if (money.Withdraw(city.MoneyAccountName, due) == MoneyOperationResult.Success)
                {
                    city.DebtBalance = 0;
                }
                else
                {
                    city.DebtBalance += (double)today;
                }
            }
            else
            {
                // Pay what the treasury holds, the rest becomes debt.
                if (balance > 0 && money.Withdraw(city.MoneyAccountName, balance) == MoneyOperationResult.Success)
                {
                    city.DebtBalance = (double)(due - balance);
                }
                else
                {
                    city.DebtBalance += (double)today;
                }
            }
            if (city.DebtBalance != debtBefore)
            {
                city.saveToDatabase();
            }
            if (city.DebtBalance > debtBefore && city.DebtBalance > 0)
            {
                if (city.DebtBalance > canclaimsextended.config.CITY_MAX_DEBT)
                {
                    return true;
                }
                MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:city_in_debt", city.DebtBalance, canclaimsextended.config.CITY_MAX_DEBT));
            }
            return false;
        }
    }
}
