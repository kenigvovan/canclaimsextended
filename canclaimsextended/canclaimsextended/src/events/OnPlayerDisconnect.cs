using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    public static class OnPlayerDisconnect
    {
        public static void Event_OnPlayerDisconnect(IServerPlayer player)
        {
            if (canclaimsextended.dataStorage == null) return;
            // Back to general chat: after a relog the player would not expect /cc to be on.
            ChatModes.Set(player.PlayerUID, ChatMode.General);
            TeleportationHandler.Cancel(player.PlayerUID);
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo)) return;
            playerInfo.TimeStampLasOnline = TimeFunctions.GetEpochSeconds();
            playerInfo.saveToDatabase();
        }
    }
}
