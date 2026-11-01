using System.Linq;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    public class CityCommand : BaseCommand
    {
        public static TextCommandResult CreateNewCity(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (playerInfo.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:you_already_have_city");
            }
            if (!TryValidateNewCityName((string)args.LastArg, out string name, out err)) return err;

            // The first chunk is the one the founder stands in; plots come later with /plot create.
            ChunkPos chunk = ChunkUnderPlayer(player);
            ClaimCheck check = ChunkRules.CheckNewCity(chunk);
            if (!check.Ok) return FromCheck(check);

            void Apply()
            {
                PartInits.initNewCity(playerInfo, chunk, name);
            }
            if (PaidAction.IsFree)
            {
                Apply();
                return SuccessWithParams("canclaimsextended:city_created", name, chunk.X, chunk.Z,
                    canclaimsextended.config.COMMAND_CITY, canclaimsextended.config.COMMAND_PLOT);
            }

            decimal cost = (decimal)PlotPricing.NewCityCost();
            TextCommandResult noMoney = PaidAction.CheckFunds(player.PlayerUID, cost, "canclaimsextended:not_enough_money_new_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, player.PlayerUID, cost,
                () => playerInfo.hasCity() || canclaimsextended.dataStorage.CityExistsByName(name)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(ChunkRules.CheckNewCity(chunk)),
                Apply,
                () => Lang.Get("canclaimsextended:city_created", name, chunk.X, chunk.Z,
                    canclaimsextended.config.COMMAND_CITY, canclaimsextended.config.COMMAND_PLOT));
            return SuccessWithParams("canclaimsextended:agree_new_city", name, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>Asks for the city name as a confirmation; a stray /city delete does nothing.</summary>
        public static TextCommandResult DeleteCity(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!city.isMayor(playerInfo)) return NoRight();
            if ((string)args.LastArg != city.GetPartName())
            {
                return ErrorWithParams("canclaimsextended:city_delete_confirm", canclaimsextended.config.COMMAND_CITY, city.GetPartName());
            }
            MessageHandler.sendGlobalMsg(Lang.Get("canclaimsextended:city_has_been_demolished", city.getPartNameReplaceUnder(), playerInfo.getPartNameReplaceUnder()));
            PartDemolition.demolishCity(city);
            return TextCommandResult.Success();
        }

        /// <summary>The chunk the player stands in joins the city's territory; the treasury pays.</summary>
        public static TextCommandResult ClaimChunk(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            return ClaimChunkFor(player, playerInfo, city, ChunkRules.CheckClaim, 0, false, false,
                "canclaimsextended:chunk_claimed", "canclaimsextended:agree_chunk_claim");
        }

        /// <summary>
        /// Claim of the chunk under the player with its own check, surcharge and mark; shared with
        /// /city outpost and /city extraclaim.
        /// </summary>
        internal static TextCommandResult ClaimChunkFor(IServerPlayer player, PlayerInfo playerInfo, City city,
            System.Func<City, ChunkPos, ClaimCheck> rule, double surcharge, bool extra, bool outpost, string doneKey, string agreeKey)
        {
            ChunkPos chunk = ChunkUnderPlayer(player);
            ClaimCheck check = rule(city, chunk);
            if (!check.Ok) return FromCheck(check);

            void Apply()
            {
                PartInits.AddCityChunk(city, chunk, extra, outpost);
                part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.ChunkClaimed, playerInfo.GetPartName(), chunk.X + "," + chunk.Z);
            }
            if (PaidAction.IsFreeFor(city))
            {
                Apply();
                return SuccessWithParams(doneKey, chunk.X, chunk.Z);
            }

            decimal cost = (decimal)(PlotPricing.ChunkClaimCost() + surcharge);
            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                () => !city.Equals(playerInfo.City) || !canclaimsextended.dataStorage.GetCityByGUID(city.Guid, out _)
                    ? PaidAction.Outdated()
                    : PaidAction.Problem(rule(city, chunk)),
                Apply,
                () => Lang.Get(doneKey, chunk.X, chunk.Z) + " " + Lang.Get("canclaimsextended:chunk_paid", cost));
            return SuccessWithParams(agreeKey, chunk.X, chunk.Z, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>The chunk the player stands in leaves the territory; it must hold no plot.</summary>
        public static TextCommandResult UnclaimChunk(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            ChunkPos chunk = ChunkUnderPlayer(player);
            ClaimCheck check = ChunkRules.CheckUnclaim(city, chunk);
            if (!check.Ok) return FromCheck(check);
            city.TryGetChunk(chunk, out CityChunk held);
            PartDemolition.unclaimCityChunk(city, chunk);
            decimal refund = PlotRefund.PayChunk(city, held.TimeStampClaimed);
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.ChunkUnclaimed, playerInfo.GetPartName(), chunk.X + "," + chunk.Z);
            return refund > 0
                ? SuccessWithParams("canclaimsextended:chunk_unclaimed_refund", chunk.X, chunk.Z, refund)
                : SuccessWithParams("canclaimsextended:chunk_unclaimed", chunk.X, chunk.Z);
        }

        public static TextCommandResult CityHere(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryGetPlotUnderPlayer(player, out Plot plot) || !plot.hasCity())
            {
                return TextCommandResult.Error("canclaimsextended:no_city_here");
            }
            return TextCommandResult.Success(string.Join("", plot.getCity().getStatus(playerInfo)));
        }

        /// <summary>A city by name, the caller's own when no name is given.</summary>
        public static TextCommandResult CityInfo(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out _, out var playerInfo, out var err)) return err;
            City city;
            if (args.Parsers[0].IsMissing)
            {
                city = AdminActing.CityOf(playerInfo);
                if (city == null)
                {
                    return TextCommandResult.Error("canclaimsextended:you_dont_have_city");
                }
            }
            else if (!TryFindCity((string)args.LastArg, out city))
            {
                return TextCommandResult.Error("canclaimsextended:no_such_city");
            }
            return TextCommandResult.Success(string.Join("", city.getStatus(playerInfo)));
        }

        public static TextCommandResult ListCities(TextCommandCallingArgs args)
        {
            string names = string.Join(", ", canclaimsextended.dataStorage.GetCitiesList().Select(c => c.getPartNameReplaceUnder()));
            return SuccessWithParams("canclaimsextended:cities_list", names);
        }
    }
}
