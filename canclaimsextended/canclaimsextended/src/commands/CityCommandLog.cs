using System;
using System.Text;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>/city log [page]: the city's history, newest first.</summary>
    public class CityCommandLog : BaseCommand
    {
        const int PAGE_SIZE = 10;

        public static TextCommandResult Show(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SEE_LOG)) return NoRight();
            var log = city.EventLog;
            if (log.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:gui-city-log-empty");
            }
            int pages = (log.Count + PAGE_SIZE - 1) / PAGE_SIZE;
            int page = args.Parsers[0].IsMissing ? 1 : (int)args[0];
            page = Math.Clamp(page, 1, pages);

            var sb = new StringBuilder(Lang.Get("canclaimsextended:city_log_header", city.getPartNameReplaceUnder(), page, pages));
            int newest = log.Count - 1 - (page - 1) * PAGE_SIZE;
            for (int i = newest; i > newest - PAGE_SIZE && i >= 0; i--)
            {
                var entry = log[i];
                sb.Append('\n').Append(CityLog.Format(entry.Timestamp, entry.EventType.ToString(), entry.Args));
            }
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
