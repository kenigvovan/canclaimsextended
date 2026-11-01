using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One event of the city log.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiLogEntry
    {
        public long Timestamp;
        /// <summary>EnumCityLogEvent name; the client builds the text from it.</summary>
        public string Event = "";
        public List<string> Args = new();
    }
}
