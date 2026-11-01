using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Every city chunk inside one map zone. The client drops its chunks of that zone that are not
    /// listed, so a zone always arrives whole.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class LandZonePacket
    {
        /// <summary>CLAIM_CHUNK_SIZE of the server; the client has no config of its own.</summary>
        public int ChunkSize;
        public int ZoneX;
        public int ZoneZ;
        public ClientCityLandData[] Cities;
    }
}
