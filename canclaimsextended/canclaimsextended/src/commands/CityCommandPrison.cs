using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.commands
{
    /// <summary>/city criminal ..., /city prison ... and /citizen prison ...: see PrisonHandler.</summary>
    public class CityCommandPrison : BaseCommand
    {
        /*==============================================================================================*/
        /*=====================================CRIMINALS================================================*/
        /*==============================================================================================*/
        public static TextCommandResult CriminalAdd(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_ADD_CRIMINAL, EnumPlayerPermissions.CITY_CRIMINAL_ALL)) return NoRight();
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (city.isCitizen(target))
            {
                return TextCommandResult.Error("canclaimsextended:criminal_is_citizen");
            }
            if (!city.Criminals.Add(target.Guid))
            {
                return ErrorWithParams("canclaimsextended:criminal_already", target.GetPartName());
            }
            canclaimsextended.Db.saveCriminal(city, target.Guid);
            MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:you_are_criminal", city.getPartNameReplaceUnder()));
            return SuccessWithParams("canclaimsextended:criminal_added", target.GetPartName());
        }

        public static TextCommandResult CriminalRemove(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_REMOVE_CRIMINAL, EnumPlayerPermissions.CITY_CRIMINAL_ALL)) return NoRight();
            if (!TryFindPlayer((string)args.LastArg, out PlayerInfo target, out err)) return err;
            if (!city.Criminals.Remove(target.Guid))
            {
                return ErrorWithParams("canclaimsextended:criminal_not_listed", target.GetPartName());
            }
            canclaimsextended.Db.deleteCriminal(city, target.Guid);
            // A sentence already given still runs out on its own.
            return SuccessWithParams("canclaimsextended:criminal_removed", target.GetPartName());
        }

        public static TextCommandResult CriminalList(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CRIMINAL_ALL, EnumPlayerPermissions.CITY_ADD_CRIMINAL,
                EnumPlayerPermissions.CITY_REMOVE_CRIMINAL, EnumPlayerPermissions.CITY_PRISON_LIST, EnumPlayerPermissions.CITY_PRISON_ALL)) return NoRight();
            if (city.Criminals.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:criminals_none");
            }
            string names = string.Join(", ", city.Criminals.Select(uid =>
                canclaimsextended.dataStorage.GetPlayerByUid(uid, out PlayerInfo p) ? p.GetPartName() : uid));
            return SuccessWithParams("canclaimsextended:criminals_list", names);
        }

        /*==============================================================================================*/
        /*=====================================PRISON===================================================*/
        /*==============================================================================================*/
        /// <summary>A cell where the mayor stands, inside a prison plot of the city.</summary>
        public static TextCommandResult AddCell(TextCommandCallingArgs args)
        {
            if (!TryResolvePrison(args, out var player, out var playerInfo, out City city, out Plot plot, out PlotDescPrison desc, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PRISON_ADD_CELL, EnumPlayerPermissions.CITY_PRISON_ALL)) return NoRight();
            BlockPos pos = player.Entity.Pos.AsBlockPos;
            if (!plot.Shape.Contains(pos.X, pos.Y, pos.Z))
            {
                return TextCommandResult.Error("canclaimsextended:prison_cell_outside");
            }
            int max = canclaimsextended.config.MAX_CELLS_PER_PRISON;
            if (desc.Cells.Count >= max)
            {
                return ErrorWithParams("canclaimsextended:prison_cells_max", max);
            }
            if (desc.Cells.Any(c => c.Equals(pos)))
            {
                return TextCommandResult.Error("canclaimsextended:prison_cell_exists");
            }
            desc.Cells.Add(pos.Copy());
            plot.saveToDatabase();
            return SuccessWithParams("canclaimsextended:prison_cell_added", pos.X, pos.Y, pos.Z, desc.Cells.Count, max);
        }

        /// <summary>Removes the cell nearest to the mayor, within a few blocks.</summary>
        public static TextCommandResult RemoveCell(TextCommandCallingArgs args)
        {
            if (!TryResolvePrison(args, out var player, out var playerInfo, out City city, out Plot plot, out PlotDescPrison desc, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PRISON_REMOVE_CELL, EnumPlayerPermissions.CITY_PRISON_ALL)) return NoRight();
            Vec3d at = player.Entity.Pos.XYZ;
            BlockPos nearest = desc.Cells.OrderBy(c => c.DistanceSqTo(at.X, at.Y, at.Z)).FirstOrDefault();
            if (nearest == null || nearest.DistanceSqTo(at.X, at.Y, at.Z) > 9)
            {
                return TextCommandResult.Error("canclaimsextended:prison_no_cell_here");
            }
            desc.Cells.Remove(nearest);
            plot.saveToDatabase();
            return SuccessWithParams("canclaimsextended:prison_cell_removed", nearest.X, nearest.Y, nearest.Z);
        }

        public static TextCommandResult ListPrisons(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_PRISON_LIST, EnumPlayerPermissions.CITY_PRISON_ALL)) return NoRight();
            var lines = city.getCityPlots()
                .Where(p => p.Type == PlotType.PRISON && p.Desc is PlotDescPrison)
                .Select(p =>
                {
                    var desc = (PlotDescPrison)p.Desc;
                    string cells = desc.Cells.Count == 0
                        ? Lang.Get("canclaimsextended:prison_no_cells")
                        : string.Join("; ", desc.Cells.Select(c => c.X + " " + c.Y + " " + c.Z));
                    string name = p.GetPartName().Length > 0 ? p.GetPartName() : Lang.Get("canclaimsextended:plot_type_prison");
                    return name + ": " + cells;
                })
                .ToList();
            if (lines.Count == 0)
            {
                return TextCommandResult.Error("canclaimsextended:city_has_no_prison");
            }
            return TextCommandResult.Success(string.Join("\n", lines));
        }

        /*==============================================================================================*/
        /*=====================================PRISONER=================================================*/
        /*==============================================================================================*/
        public static TextCommandResult PrisonerInfo(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!playerInfo.isPrisoned())
            {
                return TextCommandResult.Success("canclaimsextended:you_are_not_prisoned");
            }
            long left = System.Math.Max(0, playerInfo.PrisonReleaseAt - TimeFunctions.GetEpochSeconds());
            long hours = (left + 3599) / 3600;
            return SuccessWithParams("canclaimsextended:prison_status", hours, PrisonHandler.RansomPrice(playerInfo), canclaimsextended.config.COMMAND_CITIZEN);
        }

        /// <summary>Pays the ransom to the city that holds the prisoner and walks out.</summary>
        public static TextCommandResult Payout(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            if (!playerInfo.isPrisoned())
            {
                return TextCommandResult.Error("canclaimsextended:you_are_not_prisoned");
            }
            IMoneyProvider money = canclaimsextended.economyProvider;
            if (!money.SupportsPlayerWallet)
            {
                return TextCommandResult.Error("canclaimsextended:economy_not_configured");
            }
            decimal price = (decimal)PrisonHandler.RansomPrice(playerInfo);
            if (money.GetBalance(playerInfo.Guid) < price)
            {
                return TextCommandResult.Error("canclaimsextended:not_enough_money");
            }
            PrisonHandler.TryGetPrison(playerInfo, out Plot prison, out _);
            City holder = prison?.getCity();
            MoneyOperationResult result = holder != null
                ? money.Transfer(playerInfo.Guid, holder.MoneyAccountName, price)
                : money.Withdraw(playerInfo.Guid, price);
            if (result != MoneyOperationResult.Success)
            {
                return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
            }
            if (holder != null)
            {
                MessageHandler.sendMsgInCity(holder, Lang.Get("canclaimsextended:ransom_paid_city", playerInfo.GetPartName(), price));
            }
            PrisonHandler.Release(playerInfo, true);
            return SuccessWithParams("canclaimsextended:ransom_paid", price);
        }

        /*==============================================================================================*/
        /*=====================================HELPERS==================================================*/
        /*==============================================================================================*/
        /// <summary>The caller's city and the prison plot of that city they stand in.</summary>
        static bool TryResolvePrison(TextCommandCallingArgs args, out Vintagestory.API.Server.IServerPlayer player, out PlayerInfo playerInfo,
            out City city, out Plot plot, out PlotDescPrison desc, out TextCommandResult err)
        {
            plot = null;
            desc = null;
            if (!TryResolveCity(args, out player, out playerInfo, out city, out err)) return false;
            if (!TryResolveSubPlot(player, out plot, out err)) return false;
            if (!city.Equals(plot.getCity()) || plot.Type != PlotType.PRISON || plot.Desc is not PlotDescPrison d)
            {
                err = TextCommandResult.Error("canclaimsextended:not_prison_plot");
                return false;
            }
            desc = d;
            return true;
        }
    }
}
