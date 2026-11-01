using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    // protobuf-net has no lists inside dictionaries, so a rank travels as one entry.
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiRankEntry
    {
        public string Name = "";
        /// <summary>EnumPlayerPermissions names.</summary>
        public List<string> Permissions = new();
        /// <summary>Names of the citizens holding the rank.</summary>
        public List<string> Holders = new();
    }
}
