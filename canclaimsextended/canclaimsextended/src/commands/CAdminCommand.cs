using System.Text;
using canclaimsextended.src.geometry;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// Server admin tools. The whole /cadmin tree is behind RequireAdminRole; the land commands skip
    /// the rules a city has to follow (touching, limits, money, distance) but never allow overlaps or
    /// plots outside a territory. The other parts of /cadmin are in the CAdminCommand.*.cs files.
    /// </summary>
    public partial class CAdminCommand : BaseCommand
    {
        /*==============================================================================================*/
        /*=====================================CITY=====================================================*/
        /*==============================================================================================*/
        public static TextCommandResult CityDelete(TextCommandCallingArgs args)
        {
            if (!TryResolveCityArg(args, 0, out City city, out var err)) return err;
            MessageHandler.sendGlobalLang("canclaimsextended:city_deleted_by_admin", city.getPartNameReplaceUnder());
            PartDemolition.demolishCity(city);
            return TextCommandResult.Success();
        }

        /// <summary>A city without a mayor (technical), or with the named player as its mayor.</summary>
        public static TextCommandResult CityCreate(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            if (!TryValidateNewCityName((string)args[0], out string name, out err)) return err;
            PlayerInfo mayor = null;
            if (!args.Parsers[1].IsMissing)
            {
                if (!TryFindPlayer((string)args[1], out mayor, out err)) return err;
                if (mayor.hasCity())
                {
                    return TextCommandResult.Error("canclaimsextended:player_has_city_already");
                }
            }
            ChunkPos chunk = ChunkUnderPlayer(player);
            ClaimCheck check = ChunkRules.CheckChunkFree(chunk);
            if (!check.Ok) return FromCheck(check);
            PartInits.initNewCity(mayor, chunk, name);
            return SuccessWithParams("canclaimsextended:city_created", name, chunk.X, chunk.Z,
                canclaimsextended.config.COMMAND_CITY, canclaimsextended.config.COMMAND_PLOT);
        }

        /// <summary>The chunk under the admin joins the city; only that it is free is checked.</summary>
        public static TextCommandResult CityClaim(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            if (!TryResolveCityArg(args, 0, out City city, out err)) return err;
            ChunkPos chunk = ChunkUnderPlayer(player);
            ClaimCheck check = ChunkRules.CheckChunkFree(chunk);
            if (!check.Ok) return FromCheck(check);
            PartInits.AddCityChunk(city, chunk);
            return SuccessWithParams("canclaimsextended:chunk_claimed", chunk.X, chunk.Z);
        }

        /// <summary>
        /// The city's chunk under the admin, even if that splits the city; never the last one, and
        /// never one with a plot in it: a plot outside its territory would be left behind.
        /// </summary>
        public static TextCommandResult CityUnclaim(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            if (!TryResolveCityArg(args, 0, out City city, out err)) return err;
            ChunkPos chunk = ChunkUnderPlayer(player);
            if (!city.HasChunk(chunk))
            {
                return TextCommandResult.Error("canclaimsextended:not_this_city_chunk");
            }
            if (city.ChunkCount <= 1)
            {
                return TextCommandResult.Error("canclaimsextended:last_city_chunk");
            }
            ClaimCheck check = ChunkRules.CheckNoPlotsInChunk(city, chunk);
            if (!check.Ok) return FromCheck(check);
            PartDemolition.unclaimCityChunk(city, chunk);
            return SuccessWithParams("canclaimsextended:chunk_unclaimed", chunk.X, chunk.Z);
        }

        public static TextCommandResult CityBonusChunks(TextCommandCallingArgs args)
        {
            return SetCityBonus(args, (city, amount) => city.setBonusChunks(amount), "canclaimsextended:city_bonus_chunks_set");
        }

        public static TextCommandResult CityBonusPlots(TextCommandCallingArgs args)
        {
            return SetCityBonus(args, (city, amount) => city.setBonusPlots(amount), "canclaimsextended:city_bonus_plots_set");
        }

        /// <summary>/cadmin city bonuschunks|bonusplots &lt;city&gt; &lt;n&gt;: chunks or plots over the city's level.</summary>
        static TextCommandResult SetCityBonus(TextCommandCallingArgs args, System.Action<City, int> set, string doneKey)
        {
            if (!TryResolveCityArg(args, 0, out City city, out var err)) return err;
            int amount = (int)args[1];
            if (amount < 0)
            {
                return TextCommandResult.Error("canclaimsextended:economy_bad_amount");
            }
            set(city, amount);
            city.saveToDatabase();
            return SuccessWithParams(doneKey, city.getPartNameReplaceUnder(), amount);
        }

        public static TextCommandResult CitySetMayor(TextCommandCallingArgs args)
        {
            if (!TryResolveCityArg(args, 0, out City city, out var err)) return err;
            if (!TryFindPlayer((string)args[1], out PlayerInfo newMayor, out err)) return err;
            if (!city.isCitizen(newMayor))
            {
                return TextCommandResult.Error("canclaimsextended:player_should_be_citizen");
            }
            PlayerInfo oldMayor = city.getMayor();
            city.setMayor(newMayor);
            part.citylog.CityLog.Add(city, part.citylog.EnumCityLogEvent.MayorChanged, newMayor.GetPartName());
            if (oldMayor != null)
            {
                RightsHandler.reapplyRights(oldMayor);
            }
            RightsHandler.reapplyRights(newMayor);
            city.saveToDatabase();
            canclaimsextended.dataStorage.ResetCacheForAll();
            MessageHandler.sendLangInCity(city, "canclaimsextended:new_mayor", newMayor.getPartNameReplaceUnder());
            return TextCommandResult.Success();
        }

        /// <summary>
        /// /cadmin city setdefault [city]: NEW_PLAYERS_DEFAULT_CITY, the city new players join on
        /// their first login; without a city there is none. Written to the config at once.
        /// </summary>
        public static TextCommandResult CitySetDefault(TextCommandCallingArgs args)
        {
            Config cfg = canclaimsextended.config;
            if (args.Parsers[0].IsMissing)
            {
                cfg.NEW_PLAYERS_DEFAULT_CITY = "";
                canclaimsextended.sapi.StoreModConfig(cfg, Config.FILE_NAME);
                return TextCommandResult.Success("canclaimsextended:admin_default_city_cleared");
            }
            if (!TryResolveCityArg(args, 0, out City city, out var err)) return err;
            cfg.NEW_PLAYERS_DEFAULT_CITY = city.GetPartName();
            canclaimsextended.sapi.StoreModConfig(cfg, Config.FILE_NAME);
            return SuccessWithParams("canclaimsextended:admin_default_city_set", city.getPartNameReplaceUnder(),
                canclaimsextended.config.COMMAND_CADMIN);
        }

        /// <summary>A technical city pays no upkeep; used for spawn towns and the like.</summary>
        public static TextCommandResult CityTechnical(TextCommandCallingArgs args)
        {
            if (!TryResolveCityArg(args, 0, out City city, out var err)) return err;
            bool on = IsOn(args[1]);
            city.setIsTechnicalCity(on);
            city.saveToDatabase();
            return SuccessWithParams(on ? "canclaimsextended:city_now_technical" : "canclaimsextended:city_not_technical",
                city.getPartNameReplaceUnder());
        }

        /*==============================================================================================*/
        /*=====================================PLOT=====================================================*/
        /*==============================================================================================*/
        public static TextCommandResult PlotInfo(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out var playerInfo, out var err)) return err;
            if (!TryResolveSubPlot(player, out Plot plot, out err)) return err;
            StringBuilder sb = new StringBuilder();
            sb.Append("guid: ").Append(plot.Guid).Append('\n');
            sb.Append("number: ").Append(plot.Number).Append(", type: ").Append(plot.Type).Append(", price: ").Append(plot.Price)
              .Append(", fee: ").Append(plot.getCustomTax()).Append('\n');
            sb.Append("group: ").Append(plot.Group?.GetPartName() ?? "-")
              .Append(", claimed: ").Append(part.citylog.CityLog.When(plot.TimeStampClaimed)).Append('\n');
            // What the type keeps: temple point, prison cells, summon point.
            string desc = plot.Desc?.Serialize() ?? "";
            if (desc.Length > 0) sb.Append("type data: ").Append(desc).Append('\n');
            var boxes = plot.Shape.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                sb.Append("box ").Append(i).Append(": ").Append(boxes[i]).Append('\n');
            }
            sb.Append(string.Join("", plot.getStatus(playerInfo)));
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
