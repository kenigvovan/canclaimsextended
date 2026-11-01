using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part.interfaces;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Citizens with their own rights on some plots of the city, paying a daily fee for it. On a
    /// group plot its members get the group's rights instead of the citizen ones.
    /// Invitations into it are sent by the group itself, so it is an ISender.
    /// </summary>
    public class PlotsGroup : Part, ISender
    {
        public City City { get; set; }
        public HashSet<PlayerInfo> Members { get; } = new HashSet<PlayerInfo>();
        /// <summary>Only the CITIZEN row is used: what members may do; the flags go onto the group's plots.</summary>
        public PermsHandler Perms { get; } = new PermsHandler();
        /// <summary>What each member but the mayor pays the city daily right now.</summary>
        public double Fee { get; set; }
        /// <summary>
        /// A raise announced but not yet charged, -1 when there is none. It waits until PendingFeeAt
        /// at the old fee; then those who accepted pay the new one and the rest leave the group.
        /// </summary>
        public double PendingFee { get; set; } = -1;
        public long PendingFeeAt { get; set; }
        /// <summary>Uids of the members who accepted the pending raise.</summary>
        public HashSet<string> PendingFeeAccepted { get; } = new HashSet<string>();

        readonly List<Invitation> sentInvitations = new List<Invitation>();

        public PlotsGroup(string name, string guid) : base(name, guid)
        {
        }

        public bool HasPendingFee => PendingFee >= 0 && PendingFeeAt > 0;

        public void ClearPendingFee()
        {
            PendingFee = -1;
            PendingFeeAt = 0;
            PendingFeeAccepted.Clear();
        }

        public IEnumerable<Plot> Plots => City?.getCityPlots().Where(p => Equals(p.Group)) ?? Enumerable.Empty<Plot>();
        public IEnumerable<CityChunk> Chunks => City?.Chunks.Where(c => Equals(c.Group)) ?? Enumerable.Empty<CityChunk>();
        /// <summary>The group holds land of either kind; only then is its fee charged.</summary>
        public bool HasLand => Plots.Any() || Chunks.Any();

        /// <summary>
        /// The land of the group's chunks, like City.Land: the city's rights for everyone but the
        /// members, the group's flags. Kept in step by PlotsGroupHandler.RefreshLand; null until then.
        /// </summary>
        public Plot Land { get; internal set; }

        public override bool saveToDatabase(bool update = true)
        {
            return canclaimsextended.Db.savePlotsGroup(this);
        }

        // ISender: invitations of citizens into the group.
        public List<Invitation> GetSentInvitations() => sentInvitations;
        public void deleteSentInvitation(Invitation invitation) => sentInvitations.Remove(invitation);
        public void addSentInvitation(Invitation invitation) => sentInvitations.Add(invitation);
        public int getMaxSentInvitations() => canclaimsextended.config.MAX_SENT_INVITATIONS_CITY;
        public string getNameSender() => GetPartName();

        public override int GetHashCode() => Guid.GetHashCode();
        public override bool Equals(object obj) => obj is PlotsGroup other && Guid == other.Guid;
    }
}
