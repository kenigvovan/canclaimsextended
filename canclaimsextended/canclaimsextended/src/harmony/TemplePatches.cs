using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.harmony
{
    /// <summary>
    /// Respawn at the city temple. Vanilla OnPlayerRespawn asks the player for a spawn position and
    /// teleports and revives them there; while it runs, the position asked for is the temple's.
    /// Everything else - the teleport, the revive, a radius search - stays vanilla.
    /// </summary>
    public static class TemplePatches
    {
        // OnPlayerRespawn runs on the main thread only.
        static string respawningUid;

        public static void Prefix_OnPlayerRespawn(IServerPlayer player)
        {
            respawningUid = player?.Entity != null && !player.Entity.Alive ? player.PlayerUID : null;
        }

        public static void Finalizer_OnPlayerRespawn()
        {
            respawningUid = null;
        }

        /// <summary>Skips vanilla only for the player being respawned, so a custom spawn is not used up either.</summary>
        public static bool Prefix_GetSpawnPosition(object __instance, ref FuzzyEntityPos __result)
        {
            if (respawningUid == null || __instance is not IServerPlayer player || player.PlayerUID != respawningUid) return true;
            // A jailed player comes back in a cell, whatever temple they picked.
            if (PrisonHandler.TryGetCell(player, out BlockPos cell))
            {
                __result = new FuzzyEntityPos(cell.X + 0.5, cell.Y, cell.Z + 0.5)
                {
                    Radius = 0,
                    UsesLeft = -1
                };
                return false;
            }
            bool found = TempleRespawn.TryFindRespawnPoint(player, out BlockPos point, out bool pickLost);
            if (pickLost) MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:temple_respawn_gone"));
            if (!found) return true;
            // Beside the temple block rather than inside it.
            __result = new FuzzyEntityPos(point.X + 0.5, point.Y + 1, point.Z + 1.5)
            {
                Radius = 0,
                UsesLeft = -1
            };
            return false;
        }
    }
}
