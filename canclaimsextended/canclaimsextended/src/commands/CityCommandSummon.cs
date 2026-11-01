using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.cooldowns;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.economy;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.commands
{
    /// <summary>/city summon ...: points on summon plots that citizens teleport to.</summary>
    public class CityCommandSummon : BaseCommand
    {
        static List<(Plot Plot, PlotDescSummon Desc)> PointsOf(City city) => SummonRules.PointsOf(city);

        /// <summary>/city summon set [name]: the point goes where the caller stands, inside a summon plot.</summary>
        public static TextCommandResult Set(TextCommandCallingArgs args)
        {
            if (!TryResolveSummon(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_SUMMON)) return NoRight();
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            if (!city.Equals(plot.getCity()))
            {
                return TextCommandResult.Error("canclaimsextended:not_your_city_plot");
            }
            if (plot.Type != PlotType.SUMMON || plot.Desc is not PlotDescSummon desc)
            {
                return ErrorWithParams("canclaimsextended:need_summon_plot", canclaimsextended.config.COMMAND_PLOT);
            }
            Vec3d point = player.Entity.Pos.XYZ.Clone();
            if (!PlotDescSummon.IsStandable(canclaimsextended.sapi.World.BlockAccessor, plot, point))
            {
                return TextCommandResult.Error("canclaimsextended:summon_point_no_room");
            }

            string name = args.Parsers[0].IsMissing ? desc.Name : Filter.FilterName((string)args.LastArg);
            if (string.IsNullOrEmpty(name))
            {
                name = FreeDefaultName(city);
            }
            if (PointsOf(city).Any(p => !p.Plot.Equals(plot) && p.Desc.Name == name))
            {
                return ErrorWithParams("canclaimsextended:summon_name_taken", name);
            }
            desc.Point = point;
            desc.Name = name;
            plot.saveToDatabase();
            return SuccessWithParams("canclaimsextended:summon_point_set", name, (int)point.X, (int)point.Y, (int)point.Z);
        }

        public static TextCommandResult List(TextCommandCallingArgs args)
        {
            if (!TryResolveSummon(args, out _, out _, out City city, out var err)) return err;
            var points = PointsOf(city);
            if (points.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:summon_no_points");
            }
            string lines = string.Join("\n", points.Select((p, i) =>
                $"{i + 1}. {p.Desc.Name}: {(int)p.Desc.Point.X} {(int)p.Desc.Point.Y} {(int)p.Desc.Point.Z}"));
            return SuccessWithParams("canclaimsextended:summon_list", lines);
        }

        /// <summary>/city summon tp &lt;name or number&gt;: paid now, teleports after SECONDS_SUMMON_TIME unless the player moves.</summary>
        public static TextCommandResult Teleport(TextCommandCallingArgs args)
        {
            if (!TryResolveSummon(args, out var player, out var playerInfo, out City city, out var err)) return err;
            Config c = canclaimsextended.config;

            long cooldownEnd = CooldownHandler.GetEnd(playerInfo.Guid, CooldownType.SUMMON);
            if (cooldownEnd != 0)
            {
                return ErrorWithParams("canclaimsextended:summon_cooldown", cooldownEnd - TimeFunctions.GetEpochSeconds());
            }

            var points = PointsOf(city);
            string wanted = (string)args.LastArg ?? "";
            var chosen = int.TryParse(wanted, out int number) && number >= 1 && number <= points.Count
                ? points[number - 1]
                : points.FirstOrDefault(p => p.Desc.Name == wanted);
            if (chosen.Plot == null)
            {
                return ErrorWithParams("canclaimsextended:summon_no_such_point", c.COMMAND_CITY);
            }
            Plot plot = chosen.Plot;
            PlotDescSummon desc = chosen.Desc;
            // Set replaces the Vec3d, so the same reference means the point was not moved meanwhile.
            Vec3d pointAtStart = desc.Point;
            Vec3d target = pointAtStart.Clone();

            if (c.SUMMON_MIN_PLAYERS > 0
                && canclaimsextended.sapi.World.GetPlayersAround(target, c.SUMMON_HOR_RANGE, c.SUMMON_VER_RANGE).Length < c.SUMMON_MIN_PLAYERS)
            {
                return ErrorWithParams("canclaimsextended:summon_need_players", c.SUMMON_MIN_PLAYERS);
            }

            decimal cost = PaidAction.IsFree ? 0 : (decimal)c.SUMMON_PAYMENT;
            if (cost > 0)
            {
                TextCommandResult noMoney = PaidAction.CheckFunds(playerInfo.Guid, cost, "canclaimsextended:summon_not_enough_money");
                if (noMoney != null) return noMoney;
                if (canclaimsextended.economyProvider.Withdraw(playerInfo.Guid, cost) != MoneyOperationResult.Success)
                {
                    return TextCommandResult.Error("canclaimsextended:economy_money_transaction_error");
                }
            }

            TeleportationHandler.Add(new TeleportationInfo(playerInfo.Guid, target, player.Entity.Pos.XYZ.Clone(),
                TimeFunctions.GetEpochSeconds() + c.SECONDS_SUMMON_TIME,
                CooldownType.SUMMON, c.SECONDS_SUMMON_COOLDOWN,
                // Still a citizen, the plot still a summon plot with this very point, and room to land.
                () => city.Equals(playerInfo.City) && city.getCityPlots().Contains(plot)
                    && plot.Desc == desc && ReferenceEquals(desc.Point, pointAtStart)
                    && PlotDescSummon.IsStandable(canclaimsextended.sapi.World.BlockAccessor, plot, target),
                cost > 0 ? playerInfo.Guid : null, cost));
            return SuccessWithParams("canclaimsextended:summon_started", desc.Name, c.SECONDS_SUMMON_TIME);
        }

        public static TextCommandResult CancelTeleport(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            return TeleportationHandler.Cancel(playerInfo.Guid)
                ? TextCommandResult.Success("canclaimsextended:summon_cancelled")
                : TextCommandResult.Error("canclaimsextended:summon_nothing_to_cancel");
        }

        /// <summary>Caller with a city, summoning switched on.</summary>
        static bool TryResolveSummon(TextCommandCallingArgs args, out Vintagestory.API.Server.IServerPlayer player,
            out PlayerInfo playerInfo, out City city, out TextCommandResult err)
        {
            if (!TryResolveCity(args, out player, out playerInfo, out city, out err)) return false;
            if (!canclaimsextended.config.SUMMON_ALLOWED)
            {
                err = TextCommandResult.Error("canclaimsextended:summon_not_allowed");
                return false;
            }
            return true;
        }

        static string FreeDefaultName(City city)
        {
            var taken = new HashSet<string>(PointsOf(city).Select(p => p.Desc.Name));
            for (int i = 1; ; i++)
            {
                string name = "point" + i;
                if (!taken.Contains(name)) return name;
            }
        }
    }
}
