using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// A change in a zone the client holds: chunks added to a city (with the city's look), chunks
    /// that left their city, or only a new look of the city.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class LandUpdatePacket
    {
        public int ChunkSize;
        public ClientCityLandData City;
        public long[] RemovedChunks;
    }
}
