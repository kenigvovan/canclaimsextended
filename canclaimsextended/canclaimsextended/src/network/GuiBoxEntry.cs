using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One box of the plot the player stands in; max is exclusive, as in BlockBox.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiBoxEntry
    {
        public int SizeX, SizeY, SizeZ;
        public long Volume;
        public int MinX, MinY, MinZ;
        public int MaxX, MaxY, MaxZ;
        /// <summary>The player stands in this box.</summary>
        public bool IsHere;
        /// <summary>/plot cut may take this box off: not the last one, and the plot does not fall apart.</summary>
        public bool CanCut;
    }
}
