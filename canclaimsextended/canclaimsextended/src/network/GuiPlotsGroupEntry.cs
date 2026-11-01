using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One plots group of the player's city, for the groups page.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiPlotsGroupEntry
    {
        public string Name = "";
        public List<string> Members = new();
        public int Plots;
        public string Fee = "";
        public bool HasPendingFee;
        public string PendingFee = "";
        public long PendingHoursLeft;
        /// <summary>The viewer accepted the pending raise.</summary>
        public bool IAccepted;
        public bool IsMember;
        /// <summary>Members' rights: use, build, attack. Empty by default, see GuiStatePacket.CityPerms.</summary>
        public bool[] Perms = new bool[0];
        public bool Pvp, Fire, Blast, NoMobSpawn;
        /// <summary>Chunks of territory in the group.</summary>
        public int Chunks;
        /// <summary>#n of each plot in the group. Lists default to empty: protobuf-net appends to them.</summary>
        public List<int> PlotNumbers = new();
        /// <summary>"x, z" of each chunk in the group.</summary>
        public List<string> ChunkCoords = new();
    }
}
