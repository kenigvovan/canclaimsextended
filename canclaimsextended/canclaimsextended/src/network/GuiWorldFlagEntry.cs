using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One world flag for the admin page, by its /cadmin world set code.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiWorldFlagEntry
    {
        public string Code = "";
        public bool Value;
    }
}
