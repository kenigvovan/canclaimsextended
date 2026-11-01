using canclaimsextended.src.network;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/city set emblem [layers]: the city's coat of arms; without layers it is removed.</summary>
    public class CityCommandEmblem : BaseCommand
    {
        public static TextCommandResult SetEmblem(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out _, out var playerInfo, out var city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SET_EMBLEM, EnumPlayerPermissions.CITY_SET_ALL))
            {
                return NoRight();
            }
            string raw = ((string)args.LastArg ?? "").Trim();
            if (!EmblemHandler.TryParse(raw, out var layers, out string error))
            {
                return ErrorWithParams(error, EmblemHandler.ErrorArg);
            }
            city.Emblem = EmblemHandler.Join(EmblemHandler.Sanitize(layers));
            city.saveToDatabase();
            EmblemSync.BroadcastAll();
            return city.Emblem.Length == 0
                ? TextCommandResult.Success("canclaimsextended:emblem_removed")
                : SuccessWithParams("canclaimsextended:emblem_set", city.Emblem);
        }
    }
}
