using ProtoBuf;

namespace canclaimsextended.src.network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PlotRemovedPacket
    {
        public string Guid;
    }
}
