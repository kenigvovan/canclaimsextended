using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Every plot touching one map zone. The client drops its plots in that zone that are not in
    /// the list, so a zone always arrives whole.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlotZonePacket
    {
        public int ZoneX;
        public int ZoneZ;
        public ClientPlotData[] Plots;
    }
}
