using Vintagestory.API.Server;

namespace canclaimsextended.src.rights
{
    public static class AdminRole
    {
        /// <summary>The player's role is one of ROLE_CODES_WITH_ADMIN_RIGHTS.</summary>
        public static bool IsAdmin(IServerPlayer player)
        {
            return player?.Role != null && canclaimsextended.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Contains(player.Role.Code);
        }
    }
}
