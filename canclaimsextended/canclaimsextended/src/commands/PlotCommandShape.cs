using System.Collections.Generic;
using System.Linq;
using System.Text;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// Plots inside the city's territory, made of one or more boxes: made from the selection
    /// (create), given up (delete), a box added (extend) or taken off (cut), the list.
    /// </summary>
    public class PlotCommandShape : BaseCommand
    {
        const int PLOT_NAME_MAX_LENGTH = 32;

        /// <summary>/plot create [name]: the selection becomes a plot of the caller's city; the treasury pays.</summary>
        public static TextCommandResult Create(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CREATE_PLOT)) return NoRight();
            if (!canclaimsextended.selections.TryGetClaimBox(player, out BlockBox box))
            {
                return ErrorWithParams("canclaimsextended:selection_needed", canclaimsextended.config.COMMAND_CITY);
            }
            string name = "";
            if (!args.Parsers[0].IsMissing)
            {
                name = Filter.FilterName((string)args[0] ?? "");
                if (name.Length == 0 || !Filter.IsAllowedName(name))
                {
                    return TextCommandResult.Error("canclaimsextended:invalid_string");
                }
                if (name.Length > PLOT_NAME_MAX_LENGTH)
                {
                    return ErrorWithParams("canclaimsextended:plot_name_too_long", PLOT_NAME_MAX_LENGTH);
                }
            }
            ClaimCheck check = ClaimRules.CheckPlotClaim(city, box);
            if (!check.Ok) return FromCheck(check);

            void Apply()
            {
                Plot plot = PartInits.AddCityPlot(city, box);
                if (name.Length > 0)
                {
                    plot.SetPartName(name);
                    plot.saveToDatabase();
                    canclaimsextended.dataStorage.NotifyPlotChanged(plot);
                }
                part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.PlotClaimed, playerInfo.GetPartName(), box.Volume.ToString());
                canclaimsextended.selections.Clear(player);
            }
            if (PaidAction.IsFreeFor(city))
            {
                Apply();
                return SuccessWithParams("canclaimsextended:plot_has_been_claimed", box.SizeX, box.SizeY, box.SizeZ, box.Volume);
            }

            decimal cost = (decimal)PlotPricing.ClaimCost(box);
            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                () => !city.Equals(playerInfo.City) || !canclaimsextended.dataStorage.GetCityByGUID(city.Guid, out _)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(ClaimRules.CheckPlotClaim(city, box)),
                Apply,
                () => Lang.Get("canclaimsextended:plot_has_been_claimed_paid", box.SizeX, box.SizeY, box.SizeZ, box.Volume, cost));
            return SuccessWithParams("canclaimsextended:agree_claim", box.Volume, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>
        /// /plot delete: the plot under the caller goes back to being city land. A plot with an owner
        /// has to be taken back first, so nobody loses land by surprise.
        /// </summary>
        public static TextCommandResult Delete(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!city.Equals(plot.getCity()))
            {
                return TextCommandResult.Error("canclaimsextended:not_your_city_plot");
            }
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_DELETE_PLOT)) return NoRight();
            if (plot.hasPlotOwner())
            {
                return ErrorWithParams("canclaimsextended:plot_delete_has_owner", canclaimsextended.config.COMMAND_PLOT);
            }
            long volume = plot.Volume;
            PartDemolition.demolishCityPlot(plot);
            decimal refund = PlotRefund.Pay(city, volume, plot.TimeStampClaimed);
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.PlotUnclaimed, playerInfo.GetPartName(), volume.ToString());
            return refund > 0
                ? SuccessWithParams("canclaimsextended:plot_has_been_unclaimed_refund", volume, refund)
                : SuccessWithParams("canclaimsextended:plot_has_been_unclaimed", volume);
        }

        /// <summary>/plot extend [#n]: the selection becomes part of plot #n of the caller's city, or of the plot they stand in.</summary>
        public static TextCommandResult Extend(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            Plot plot;
            if (args.Parsers[0].IsMissing)
            {
                if (!TryResolveSubPlot(player, out plot, out err)) return err;
            }
            else if (!TryFindCityPlot(playerInfo, (string)args[0], out plot, out err)) return err;
            if (!plot.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            }
            if (!ManageRules.MayReshape(playerInfo, plot)) return NoRight();
            if (!canclaimsextended.selections.TryGetClaimBox(player, out BlockBox box))
            {
                return ErrorWithParams("canclaimsextended:selection_needed", canclaimsextended.config.COMMAND_CITY);
            }
            ClaimCheck check = ClaimRules.CheckExtend(plot, box);
            if (!check.Ok) return FromCheck(check);

            City city = plot.getCity();
            void Apply()
            {
                canclaimsextended.dataStorage.ReshapePlot(plot, plot.Shape.WithBox(box));
                plot.saveToDatabase();
                canclaimsextended.dataStorage.ResetCacheForAll();
                canclaimsextended.selections.Clear(player);
                LogReshape(city, playerInfo, plot);
            }
            if (PaidAction.IsFreeFor(city))
            {
                Apply();
                return SuccessWithParams("canclaimsextended:plot_extended", box.SizeX, box.SizeY, box.SizeZ, box.Volume, plot.Volume);
            }

            decimal cost = (decimal)PlotPricing.ClaimCost(box);
            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                // The plot may have changed hands or shape while the player thought.
                () => !canclaimsextended.dataStorage.GetPlotByGuid(plot.Guid, out _) || !city.Equals(plot.getCity()) || !ManageRules.MayReshape(playerInfo, plot)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(ClaimRules.CheckExtend(plot, box)),
                Apply,
                () => Lang.Get("canclaimsextended:plot_extended_paid", box.SizeX, box.SizeY, box.SizeZ, box.Volume, plot.Volume, cost));
            return SuccessWithParams("canclaimsextended:agree_extend", box.Volume, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>
        /// /plot cut [#n part]: takes off part `part` (as /plot boxes numbers them) of plot #n of the
        /// caller's city, or the box the player stands in. Part of its price may come back, as with
        /// /city unclaim.
        /// </summary>
        public static TextCommandResult Cut(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            Plot plot;
            int index;
            if (args.Parsers[0].IsMissing)
            {
                if (!TryGetBoxUnderPlayer(player, out plot, out index) || !plot.hasCity())
                {
                    return TextCommandResult.Error("canclaimsextended:plot_not_claimed");
                }
            }
            else
            {
                if (!TryFindCityPlot(playerInfo, (string)args[0], out plot, out err)) return err;
                index = args.Parsers[1].IsMissing ? -1 : (int)args[1] - 1;
                if (index < 0 || index >= plot.Shape.Boxes.Count)
                {
                    return ErrorWithParams("canclaimsextended:plot_part_not_found", plot.Shape.Boxes.Count, canclaimsextended.config.COMMAND_PLOT);
                }
            }
            if (!ManageRules.MayReshape(playerInfo, plot)) return NoRight();
            IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
            if (boxes.Count <= 1)
            {
                return ErrorWithParams("canclaimsextended:plot_cut_last_box", canclaimsextended.config.COMMAND_PLOT);
            }
            BlockBox removed = boxes[index];
            PlotShape newShape = new PlotShape(boxes.Where((_, i) => i != index));
            if (!ClaimRules.ShapeStaysValid(newShape))
            {
                return TextCommandResult.Error("canclaimsextended:plot_cut_splits");
            }
            canclaimsextended.dataStorage.ReshapePlot(plot, newShape);
            plot.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            LogReshape(plot.getCity(), playerInfo, plot);
            // The box itself has no claim time; the plot's is the closest there is.
            decimal refund = PlotRefund.Pay(plot.getCity(), removed.Volume, plot.TimeStampClaimed);
            return refund > 0
                ? SuccessWithParams("canclaimsextended:plot_cut_refund", removed.Volume, plot.Volume, refund)
                : SuccessWithParams("canclaimsextended:plot_cut", removed.Volume, plot.Volume);
        }

        /// <summary>
        /// /plot grow|shrink &lt;direction&gt; &lt;blocks&gt;: moves one face of the box the player stands
        /// in. Growing is paid like a claim of what the plot gains; shrinking may return part of it.
        /// </summary>
        public static TextCommandResult Resize(TextCommandCallingArgs args, int sign)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryGetBoxUnderPlayer(player, out Plot plot, out int index) || !plot.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            }
            if (!ManageRules.MayReshape(playerInfo, plot)) return NoRight();
            string direction = ((string)args[0] ?? "").ToLowerInvariant();
            int amount = (int)args[1];
            BlockBox oldBox = plot.Shape.Boxes[index];
            if (amount <= 0 || !ClaimRules.TryMoveFace(oldBox, direction, sign * amount, out BlockBox newBox))
            {
                return TextCommandResult.Error("canclaimsextended:plot_resize_bad");
            }
            ClaimCheck check = ClaimRules.CheckResize(plot, index, newBox);
            if (!check.Ok) return FromCheck(check);

            City city = plot.getCity();
            long volumeBefore = plot.Volume;
            long volumeAfter = volumeBefore - oldBox.Volume + newBox.Volume;
            double priceChange = PlotPricing.ResizeCost(volumeBefore, volumeAfter);
            string sizeText = $"{newBox.SizeX} x {newBox.SizeY} x {newBox.SizeZ}";

            // The box is found again by its old value: the plot may have changed while the player thought.
            bool Apply()
            {
                int at = IndexOf(plot, oldBox);
                if (at < 0) return false;
                canclaimsextended.dataStorage.ReshapePlot(plot, ClaimRules.WithBoxReplaced(plot.Shape, at, newBox));
                plot.saveToDatabase();
                canclaimsextended.dataStorage.ResetCacheForAll();
                LogReshape(city, playerInfo, plot);
                return true;
            }

            if (priceChange <= 0 || PaidAction.IsFreeFor(city))
            {
                Apply();
                decimal refund = priceChange < 0 ? PlotRefund.PayPrice(city, (decimal)-priceChange, plot.TimeStampClaimed) : 0;
                return refund > 0
                    ? SuccessWithParams("canclaimsextended:plot_resized_refund", sizeText, plot.Volume, refund)
                    : SuccessWithParams("canclaimsextended:plot_resized", sizeText, plot.Volume);
            }

            decimal cost = (decimal)priceChange;
            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                () => !canclaimsextended.dataStorage.GetPlotByGuid(plot.Guid, out _) || !city.Equals(plot.getCity())
                      || !ManageRules.MayReshape(playerInfo, plot) || !plot.Shape.Boxes.Contains(oldBox)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(ClaimRules.CheckResize(plot, IndexOf(plot, oldBox), newBox)),
                () => Apply(),
                () => Lang.Get("canclaimsextended:plot_resized_paid", sizeText, plot.Volume, cost));
            return SuccessWithParams("canclaimsextended:agree_resize", sizeText, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>Extend, cut, grow and shrink: who, which plot, and its volume now.</summary>
        static void LogReshape(City city, PlayerInfo playerInfo, Plot plot)
        {
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.PlotReshaped, playerInfo.GetPartName(), plot.Number.ToString(), plot.Volume.ToString());
        }

        static int IndexOf(Plot plot, BlockBox box)
        {
            for (int i = 0; i < plot.Shape.Boxes.Count; i++)
            {
                if (plot.Shape.Boxes[i] == box) return i;
            }
            return -1;
        }

        public static TextCommandResult Boxes(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            if (!TryGetBoxUnderPlayer(player, out Plot plot, out int current))
            {
                return TextCommandResult.Error("canclaimsextended:plot_not_claimed");
            }
            IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
            StringBuilder sb = new StringBuilder(Lang.Get("canclaimsextended:plot_boxes_header", boxes.Count, plot.Volume));
            for (int i = 0; i < boxes.Count; i++)
            {
                BlockBox b = boxes[i];
                sb.Append('\n').Append(Lang.Get(i == current ? "canclaimsextended:plot_box_line_here" : "canclaimsextended:plot_box_line",
                    i + 1, b.SizeX, b.SizeY, b.SizeZ, b.Volume, b.MinX, b.MinY, b.MinZ, b.MaxX - 1, b.MaxY - 1, b.MaxZ - 1));
            }
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>Same places as TryGetPlotUnderPlayer, plus which box of the plot holds that block.</summary>
        internal static bool TryGetBoxUnderPlayer(IServerPlayer player, out Plot plot, out int index)
        {
            BlockPos feet = player.Entity.Pos.AsBlockPos;
            foreach (BlockPos pos in new[] { feet, feet.DownCopy(), player.CurrentBlockSelection?.Position })
            {
                if (pos == null || !canclaimsextended.dataStorage.GetSubPlotAt(pos, out plot)) continue;
                IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Contains(pos))
                    {
                        index = i;
                        return true;
                    }
                }
            }
            plot = null;
            index = -1;
            return false;
        }
    }
}
