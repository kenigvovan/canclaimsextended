using canclaimsextended.src.part;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/city outpost and /city extraclaim: the chunk under the player outside the usual rules.</summary>
    public class CityCommandPlotsExtra : BaseCommand
    {
        /// <summary>A chunk apart from the city, within the outpost distance of it.</summary>
        public static TextCommandResult ClaimOutpost(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_BUY_OUTPOST)) return NoRight();
            return CityCommand.ClaimChunkFor(player, playerInfo, city, ChunkRules.CheckOutpost,
                canclaimsextended.config.OUTPOST_CHUNK_COST, false, true,
                "canclaimsextended:outpost_claimed", "canclaimsextended:agree_outpost");
        }

        /// <summary>A chunk next to the city over its chunk limit, up to the extra limit of its level.</summary>
        public static TextCommandResult ClaimExtraChunk(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out var playerInfo, out City city, out var err)) return err;
            if (!IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK)) return NoRight();
            return CityCommand.ClaimChunkFor(player, playerInfo, city, ChunkRules.CheckExtraClaim,
                canclaimsextended.config.EXTRA_CHUNK_COST, true, false,
                "canclaimsextended:extra_chunk_claimed", "canclaimsextended:agree_extra_chunk");
        }
    }
}
