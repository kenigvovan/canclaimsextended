using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A city's territory as a client sees it: who, its look, the viewer's rights, and some of its chunks.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClientCityLandData
    {
        public string CityGuid;
        public string CityName = "";
        public int CityColor;
        /// <summary>See PlotMarkerFlags; the rights are those of the viewer on the city land.</summary>
        public int Flags;
        /// <summary>ChunkPos.Key of each chunk; empty when only the city's look changed.</summary>
        public long[] ChunkKeys;
        /// <summary>
        /// The plots group these chunks belong to, empty for the plain city land. A city may come as
        /// several entries, one per group: rights and flags differ there.
        /// </summary>
        public string GroupGuid = "";
        public string GroupName = "";
    }
}
