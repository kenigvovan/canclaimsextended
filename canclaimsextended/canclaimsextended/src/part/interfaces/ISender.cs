using System.Collections.Generic;
using canclaimsextended.src.delayed.invitations;

namespace canclaimsextended.src.part.interfaces
{
    public interface ISender
    {
        List<Invitation> GetSentInvitations();
        void deleteSentInvitation(Invitation invitation);
        void addSentInvitation(Invitation invitation);
        int getMaxSentInvitations();
        string getNameSender();
    }
}
