using System.Collections.Generic;
using canclaimsextended.src.delayed.invitations;

namespace canclaimsextended.src.part.interfaces
{
    public interface IReceiver
    {
        List<Invitation> getReceivedInvitations();
        void deleteReceivedInvitation(Invitation invitation);
        void addReceivedInvitation(Invitation invitation);
        int getMaxReceivedInvitations();
        string getNameReceiver();
    }
}
