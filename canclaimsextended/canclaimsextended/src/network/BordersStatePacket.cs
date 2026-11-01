using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>/city borders on or off: territory borders drawn without the wand in hand.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BordersStatePacket
    {
        public bool Show;
    }
}
