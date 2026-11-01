using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One plot was added or changed; the client adds it or replaces it by guid.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlotUpdatePacket
    {
        public ClientPlotData Plot;
    }
}
