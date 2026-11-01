using System.Collections.Concurrent;
using canclaimsextended.src.messages;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.agreement
{
    /// <summary>One pending "are you sure" per player, confirmed with /agree before it times out.</summary>
    public static class AgreementHandler
    {
        public static ConcurrentDictionary<string, Agreement> agreements = new ConcurrentDictionary<string, Agreement>();

        public static void addNewAgreementOrReplace(Agreement agreement)
        {
            if (agreements.TryRemove(agreement.getPlayerUid(), out Agreement oldAgreement))
            {
                canclaimsextended.sapi.Event.UnregisterCallback(oldAgreement.getTimeoutCallbackId());
            }

            long callbackId = canclaimsextended.sapi.Event.RegisterCallback(dt =>
            {
                string uid = agreement.getPlayerUid();
                if (agreements.TryRemove(uid, out _))
                {
                    MessageHandler.sendMsgToPlayer(canclaimsextended.sapi.World.PlayerByUid(uid) as IServerPlayer,
                        Lang.Get("canclaimsextended:agreement_timeout"));
                }
            }, canclaimsextended.config.AGREEMENT_TIMEOUT_SECONDS * 1000);
            agreement.setTimeoutCallbackId(callbackId);
            agreements[agreement.getPlayerUid()] = agreement;
        }

        public static bool agreeFor(IServerPlayer player)
        {
            if (agreements.TryRemove(player.PlayerUID, out Agreement agreement))
            {
                canclaimsextended.sapi.Event.UnregisterCallback(agreement.getTimeoutCallbackId());
                canclaimsextended.sapi.Event.RegisterCallback(dt => agreement.getOnAgree()?.Invoke(), 0);
                return true;
            }
            return false;
        }

        public static void clearAll()
        {
            // A timeout left registered would fire into the next world (single player).
            foreach (Agreement agreement in agreements.Values)
            {
                canclaimsextended.sapi?.Event.UnregisterCallback(agreement.getTimeoutCallbackId());
            }
            agreements.Clear();
        }
    }
}
