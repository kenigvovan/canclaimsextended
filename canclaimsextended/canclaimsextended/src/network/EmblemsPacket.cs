using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// The arms of every city that has some, and the rules the editor offers. Sent whole: it is small
    /// and changes rarely. Lists start empty - protobuf-net appends to whatever is there.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class EmblemsPacket
    {
        /// <summary>City guids; Emblems holds the arms at the same index.</summary>
        public List<string> CityGuids = new();
        public List<string> Emblems = new();
        public List<string> Patterns = new();
        public List<string> Colors = new();
        public int MaxLayers;
    }
}
