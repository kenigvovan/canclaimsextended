using System.Collections.Generic;
using System.Globalization;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.commands
{
    /// <summary>/city set name|color|dailymsg|prefix|aftername: how the city and its people are shown.</summary>
    public class CityCommandIdentity : BaseCommand
    {
        const int DAILY_MSG_MAX_LENGTH = 100;

        /// <summary>/city set name &lt;new&gt;: paid from the treasury, after /agree when there is an economy.</summary>
        public static TextCommandResult SetName(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_ALL, EnumPlayerPermissions.CITY_SET_NAME)) return NoRight();
            if (!TryValidateNewCityName((string)args.LastArg, out string name, out err)) return err;

            void Apply()
            {
                string oldName = city.GetPartName();
                bool wasDefault = events.OnPlayerJoin.TryFindDefaultCity(out City defaultCity) && city.Equals(defaultCity);
                if (!canclaimsextended.dataStorage.ChangeCityName(city, name)) return;
                // The config holds the default city by name; it follows the rename.
                if (wasDefault)
                {
                    canclaimsextended.config.NEW_PLAYERS_DEFAULT_CITY = name;
                    canclaimsextended.sapi.StoreModConfig(canclaimsextended.config, Config.FILE_NAME);
                }
                canclaimsextended.economyProvider.UpdateAccount(city.MoneyAccountName, new Dictionary<string, object> { { "lastknownname", name } });
                city.saveToDatabase();
                CityLog.Add(city, EnumCityLogEvent.CityRenamed, oldName, name);
                canclaimsextended.dataStorage.NotifyCityChanged(city);
                MessageHandler.sendGlobalMsg(Lang.Get("canclaimsextended:city_renamed", StringFunctions.ReplaceUnderscore(oldName), StringFunctions.ReplaceUnderscore(name)));
            }
            decimal cost = (decimal)canclaimsextended.config.CITY_NAME_CHANGE_COST;
            if (PaidAction.IsFreeFor(city) || cost <= 0)
            {
                Apply();
                return TextCommandResult.Success();
            }
            TextCommandResult noMoney = PaidAction.CheckFunds(city.MoneyAccountName, cost, "canclaimsextended:not_enough_money_city");
            if (noMoney != null) return noMoney;
            PaidAction.AskToConfirm(player, city.MoneyAccountName, cost,
                () => !city.Equals(playerInfo.City) || canclaimsextended.dataStorage.CityExistsByName(name) ? PaidAction.Outdated() : null,
                Apply,
                () => Lang.Get("canclaimsextended:city_renamed_paid", cost));
            return SuccessWithParams("canclaimsextended:agree_city_rename", name, cost, canclaimsextended.config.COMMAND_AGREE);
        }

        /// <summary>/city set color &lt;name or #rrggbb&gt;: the color of the city on the map.</summary>
        public static TextCommandResult SetColor(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_ALL, EnumPlayerPermissions.CITY_SET_PLOTS_COLOR)) return NoRight();
            if (!TryParseColor((string)args.LastArg, out int color))
            {
                return TextCommandResult.Error("canclaimsextended:city_color_bad");
            }
            city.cityColor = color;
            city.saveToDatabase();
            canclaimsextended.dataStorage.NotifyCityChanged(city);
            return SuccessWithParams("canclaimsextended:city_color_set", (string)args.LastArg);
        }

        /// <summary>
        /// A known color name ("red", "gold") or "#rrggbb", stored as claims stores it: ARGB with the
        /// bytes reversed, the order the map draws.
        /// </summary>
        public static bool TryParseColor(string raw, out int color)
        {
            color = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            int argb;
            if (raw.StartsWith("#"))
            {
                if (raw.Length != 7 || !int.TryParse(raw.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
                argb = unchecked((int)0xFF000000) | rgb;
            }
            else
            {
                System.Drawing.Color named = System.Drawing.Color.FromName(raw);
                if (!named.IsKnownColor) return false;
                argb = named.ToArgb();
            }
            color = ColorUtil.ReverseColorBytes(argb);
            return true;
        }

        /// <summary>/city set dailymsg [text]: shown to citizens when they log in; empty removes it.</summary>
        public static TextCommandResult SetDailyMsg(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_ALL, EnumPlayerPermissions.CITY_SET_DAILY_MSG)) return NoRight();
            string raw = (string)args.LastArg;
            if (string.IsNullOrWhiteSpace(raw))
            {
                city.DailyMsg = "";
                city.saveToDatabase();
                return TextCommandResult.Success("canclaimsextended:city_daily_msg_reset");
            }
            string text = Filter.FilterNameWithSpaces(raw);
            if (text.Length == 0 || !Filter.IsAllowedName(text))
            {
                return TextCommandResult.Error("canclaimsextended:invalid_string");
            }
            if (text.Length > DAILY_MSG_MAX_LENGTH)
            {
                return ErrorWithParams("canclaimsextended:inv_msg_too_long", DAILY_MSG_MAX_LENGTH);
            }
            city.DailyMsg = text;
            city.saveToDatabase();
            return TextCommandResult.Success("canclaimsextended:city_daily_msg_set");
        }

        /// <summary>/city set prefix &lt;player&gt; [text]: a title in front of a citizen's name in chat.</summary>
        public static TextCommandResult SetPrefix(TextCommandCallingArgs args) => SetTitle(args, true);

        /// <summary>/city set aftername &lt;player&gt; [text]: a title after a citizen's name in chat.</summary>
        public static TextCommandResult SetAfterName(TextCommandCallingArgs args) => SetTitle(args, false);

        static TextCommandResult SetTitle(TextCommandCallingArgs args, bool prefix)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_OTHERS_PREFIX)) return NoRight();
            if (!TryFindPlayer((string)args[0], out PlayerInfo target, out err)) return err;
            if (!city.isCitizen(target))
            {
                return TextCommandResult.Error("canclaimsextended:player_should_be_in_same_city");
            }
            string raw = args.Parsers.Count > 1 && !args.Parsers[1].IsMissing ? (string)args[1] : "";
            string text = Filter.FilterNameWithSpaces(raw ?? "");
            if (text.Length > 0 && !Filter.IsAllowedName(text))
            {
                return TextCommandResult.Error("canclaimsextended:invalid_string");
            }
            int max = canclaimsextended.config.MAX_CITIZEN_TITLE_LENGTH;
            if (text.Length > max)
            {
                return ErrorWithParams("canclaimsextended:title_too_long", max);
            }
            if (prefix) target.Prefix = text;
            else target.AfterName = text;
            target.saveToDatabase();
            return text.Length == 0
                ? SuccessWithParams("canclaimsextended:title_removed", target.GetPartName())
                : SuccessWithParams("canclaimsextended:title_set", target.GetPartName(), text);
        }
    }
}
