using System.Collections.Generic;
using canclaimsextended.src.part.interfaces;

namespace canclaimsextended.src.delayed.invitations
{
    /// <summary>
    /// What accepting and refusing an invitation does lives in its callbacks, which are not stored.
    /// Each kind (sender type, receiver type) registers how to build one, and the same builder is
    /// used to send it and to bring it back after a restart.
    /// </summary>
    public static class InvitationKinds
    {
        public delegate Invitation Factory(ISender sender, IReceiver receiver, long expires);

        static readonly Dictionary<(string Sender, string Receiver), Factory> factories = new();

        public static void Register(string senderType, string receiverType, Factory factory)
        {
            factories[(senderType, receiverType)] = factory;
        }

        public static bool TryCreate(ISender sender, IReceiver receiver, long expires, out Invitation invitation)
        {
            invitation = null;
            if (!factories.TryGetValue((InvitationParty.TypeOf(sender), InvitationParty.TypeOf(receiver)), out Factory factory))
            {
                return false;
            }
            invitation = factory(sender, receiver, expires);
            return invitation != null;
        }
    }
}
