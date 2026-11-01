using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    public static class OnPlayerDeath
    {
        /// <summary>A criminal killed by a citizen of the city that lists them goes to its prison.</summary>
        public static void Event_OnPlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
        {
            if (canclaimsextended.dataStorage == null || damageSource == null) return;
            // The cause entity is the shooter or thrower too, not only the hand that struck.
            if (damageSource.GetCauseEntity() is not EntityPlayer killerEntity) return;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(byPlayer.PlayerUID, out PlayerInfo victim)) return;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(killerEntity.PlayerUID, out PlayerInfo killer)) return;
            PrisonHandler.TryImprison(victim, killer);
        }
    }
}
