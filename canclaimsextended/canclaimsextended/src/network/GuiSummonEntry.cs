using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A summon plot of the player's city; IsSet is false until its point is placed.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiSummonEntry
    {
        public string Name = "";
        public int X, Y, Z;
        public bool IsSet;
        public string PlotName = "";
    }
}
