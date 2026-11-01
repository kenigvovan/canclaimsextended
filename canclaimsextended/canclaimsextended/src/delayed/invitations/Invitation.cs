using System;
using System.Collections.Generic;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.interfaces;
using Vintagestory.API.Config;

namespace canclaimsextended.src.delayed.invitations
{
    public class Invitation : IGetStatus
    {
        ISender sender;
        IReceiver receiver;
        long timeoutStamp;
        Action onApproval;
        Action onDissent;

        public Invitation(ISender sender, IReceiver receiver, long timeoutStamp, Action onApproval, Action onDissent)
        {
            this.sender = sender;
            this.receiver = receiver;
            this.timeoutStamp = timeoutStamp;
            this.onApproval = onApproval;
            this.onDissent = onDissent;
        }

        public ISender getSender() => sender;
        public IReceiver getReceiver() => receiver;
        public long getTimeStamp() => timeoutStamp;

        public void accept()
        {
            InvitationHandler.removeInvitationIfExists(sender, receiver);
            onApproval?.Invoke();
        }

        public void deny()
        {
            InvitationHandler.removeInvitationIfExists(sender, receiver);
            onDissent?.Invoke();
        }

        public List<string> getStatus(PlayerInfo forPlayer = null)
        {
            return new List<string>
            {
                Lang.Get("canclaimsextended:invitation_line", sender.getNameSender(), receiver.getNameReceiver(),
                    TimeFunctions.GetDateFromEpochSeconds(timeoutStamp)) + "\n"
            };
        }
    }
}
