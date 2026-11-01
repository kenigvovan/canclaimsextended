using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.part.interfaces;
using Vintagestory.API.Server;

namespace canclaimsextended.src.delayed.invitations
{
    /// <summary>
    /// Pending invitations, kept in the database so a restart does not forget them.
    /// Every invitation is also listed on its sender and its receiver; Detach keeps the three in step.
    /// </summary>
    public static class InvitationHandler
    {
        static ExpiringList<Invitation> invites = new();

        /// <summary>Starts the sweep of overdue invitations, once a minute.</summary>
        public static void Init(ICoreServerAPI sapi)
        {
            invites = new ExpiringList<Invitation>();
            auxiliary.ServerTicks.Every(sapi, _ => findAndDeleteOverdueInvitations(), 60 * 1000);
        }

        public static void findAndDeleteOverdueInvitations()
        {
            invites.ExpireOverdue(inv => inv.getTimeStamp(), inv =>
            {
                DetachFromParties(inv);
                Database()?.deleteInvitation(inv);
            });
        }

        /// <summary>Puts back an invitation read from the database: no limits, nothing written.</summary>
        public static void Restore(Invitation invitation)
        {
            if (tryGetInvitation(invitation.getSender(), invitation.getReceiver(), out _)) return;
            invites.Add(invitation);
            invitation.getReceiver().addReceivedInvitation(invitation);
            invitation.getSender().addSentInvitation(invitation);
        }

        static database.DatabaseHandler Database()
        {
            return canclaimsextended.Db;
        }

        public static bool addNewInvite(Invitation invitation)
        {
            if (tryGetInvitation(invitation.getSender(), invitation.getReceiver(), out _))
                return false;
            if (invitation.getSender().GetSentInvitations().Count >= invitation.getSender().getMaxSentInvitations())
                return false;
            if (invitation.getReceiver().getReceivedInvitations().Count >= invitation.getReceiver().getMaxReceivedInvitations())
                return false;

            invites.Add(invitation);
            invitation.getReceiver().addReceivedInvitation(invitation);
            invitation.getSender().addSentInvitation(invitation);
            Database()?.saveInvitation(invitation);
            return true;
        }

        public static bool removeInvitationIfExists(ISender sender, IReceiver receiver)
        {
            if (!tryGetInvitation(sender, receiver, out Invitation invitation)) return false;
            Detach(invitation);
            return true;
        }

        public static bool tryGetInvitation(ISender sender, IReceiver receiver, out Invitation invitation)
        {
            invitation = invites.Snapshot().FirstOrDefault(it => it.getSender().Equals(sender) && it.getReceiver().Equals(receiver));
            return invitation != null;
        }

        public static List<Invitation> getInvitesForReceiver(IReceiver receiver)
        {
            return invites.Snapshot().Where(it => it.getReceiver().Equals(receiver)).ToList();
        }

        public static void deleteAllInvitationsForReceiver(IReceiver receiver)
        {
            DetachAll(it => it.getReceiver().Equals(receiver));
        }

        public static void deleteAllInvitationsForSender(ISender sender)
        {
            DetachAll(it => it.getSender().Equals(sender));
        }

        static void DetachAll(Func<Invitation, bool> match)
        {
            foreach (var it in invites.Snapshot())
            {
                if (match(it)) Detach(it);
            }
        }

        static void Detach(Invitation invitation)
        {
            invites.Remove(invitation);
            DetachFromParties(invitation);
            Database()?.deleteInvitation(invitation);
        }

        static void DetachFromParties(Invitation invitation)
        {
            invitation.getReceiver().deleteReceivedInvitation(invitation);
            invitation.getSender().deleteSentInvitation(invitation);
        }
    }
}
