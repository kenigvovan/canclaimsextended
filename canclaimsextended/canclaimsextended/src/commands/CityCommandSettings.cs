using System;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// /city set ...: rights and flags of the city. Rights and flags are copied onto every plot
    /// of the city that has no owner; an owner keeps what they set on their own plot.
    /// </summary>
    public class CityCommandSettings : BaseCommand
    {
        const int INV_MSG_MAX_LENGTH = 100;

        public static TextCommandResult Info(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out _, out City city, out var err)) return err;
            return TextCommandResult.Success(city.getPermsHandler().getStringForChat());
        }

        /// <summary>/city set perm &lt;group&gt; &lt;type&gt; &lt;on|off&gt;</summary>
        public static TextCommandResult SetPerm(TextCommandCallingArgs args)
        {
            if (!TryResolveCityManager(args, EnumPlayerPermissions.CITY_SET_PLOT_ACCESS_PERMISSIONS, out City city, out var err)) return err;
            if (!PermsHandler.TryParseGroup((string)args[0], out PermGroup group)
                || !PermsHandler.TryParseType((string)args[1], out PermType type))
            {
                return TextCommandResult.Error("canclaimsextended:perm_bad_args");
            }
            bool val = IsOn(args[2]);
            ApplyToCity(city, perms => perms.setPerm(group, type, val));
            return SuccessWithParams("canclaimsextended:perm_set", (string)args[0], (string)args[1], (string)args[2]);
        }

        /// <summary>/city set pvp|fire|blast|nomobspawn &lt;on|off&gt;</summary>
        public static TextCommandResult SetFlag(TextCommandCallingArgs args, string flag, EnumPlayerPermissions permission)
        {
            if (!TryResolveCityManager(args, permission, out City city, out var err)) return err;
            bool val = IsOn(args.LastArg);
            ApplyToCity(city, perms => perms.trySetFlag(flag, val));
            return SuccessWithParams("canclaimsextended:flag_set", flag, (string)args.LastArg);
        }

        /// <summary>Changes the city's rules and those of its plots without an owner, then saves.</summary>
        static void ApplyToCity(City city, Action<PermsHandler> change)
        {
            change(city.getPermsHandler());
            foreach (Plot plot in city.getCityPlots())
            {
                if (plot.hasPlotOwner()) continue;
                change(plot.getPermsHandler());
                plot.saveToDatabase();
            }
            city.saveToDatabase();
            // The land of the groups' chunks keeps the city's rights for those outside the group.
            PlotsGroupHandler.RefreshLands(city);
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyCityChanged(city);
        }

        public static TextCommandResult SetOpen(TextCommandCallingArgs args)
        {
            if (!TryResolveCityManager(args, EnumPlayerPermissions.CITY_SET_OPEN_STATE, out City city, out var err)) return err;
            city.openCity = IsOn(args.LastArg);
            city.saveToDatabase();
            return TextCommandResult.Success(city.openCity ? "canclaimsextended:city_now_open" : "canclaimsextended:city_now_closed");
        }

        /// <summary>/city set invmsg [text]; without text the message is removed.</summary>
        public static TextCommandResult SetInvMsg(TextCommandCallingArgs args)
        {
            if (!TryResolveCityManager(args, EnumPlayerPermissions.CITY_SET_INV_MSG, out City city, out var err)) return err;
            string raw = (string)args.LastArg;
            if (string.IsNullOrWhiteSpace(raw))
            {
                city.invMsg = "";
                city.saveToDatabase();
                return TextCommandResult.Success("canclaimsextended:city_inv_msg_reset");
            }
            string text = Filter.FilterNameWithSpaces(raw);
            if (text.Length == 0 || !Filter.IsAllowedName(text))
            {
                return TextCommandResult.Error("canclaimsextended:invalid_string");
            }
            if (text.Length > INV_MSG_MAX_LENGTH)
            {
                return ErrorWithParams("canclaimsextended:inv_msg_too_long", INV_MSG_MAX_LENGTH);
            }
            city.invMsg = text;
            city.saveToDatabase();
            return TextCommandResult.Success("canclaimsextended:city_inv_msg_set");
        }

        /// <summary>/city set fee &lt;n&gt;: what each citizen but the mayor pays daily; 0 removes it.</summary>
        public static TextCommandResult SetFee(TextCommandCallingArgs args)
        {
            if (!TryResolveCityManager(args, EnumPlayerPermissions.CITY_SET_GLOBAL_FEE, out City city, out var err)) return err;
            int fee = (int)args.LastArg;
            int max = (int)canclaimsextended.config.MAX_CITY_FEE;
            if (fee < 0 || fee > max)
            {
                return ErrorWithParams("canclaimsextended:city_fee_out_of_range", max);
            }
            city.fee = fee;
            city.saveToDatabase();
            CityLog.Add(city, EnumCityLogEvent.FeeChanged, args.Caller.Player?.PlayerName ?? "", fee.ToString());
            return SuccessWithParams("canclaimsextended:city_fee_set", fee);
        }

        /// <summary>The caller's city, if they are its mayor or hold CITY_SET_ALL or the given right.</summary>
        static bool TryResolveCityManager(TextCommandCallingArgs args, EnumPlayerPermissions permission, out City city, out TextCommandResult err)
        {
            return TryResolveManagedCity(args, out _, out city, out err, EnumPlayerPermissions.CITY_SET_ALL, permission);
        }
    }
}
