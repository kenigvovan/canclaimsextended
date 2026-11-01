using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A prison plot of the player's city and how many cells it has.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiPrisonEntry
    {
        /// <summary>Plot name; empty for an unnamed plot.</summary>
        public string Name = "";
        public int Cells;
        public int MaxCells;
    }
}
