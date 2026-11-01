using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One PriceSettings key with its value, invariant culture; the client names it from its lang.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiPriceSettingEntry
    {
        public string Key = "";
        public string Group = "";
        public string Value = "";
        public string Min = "";
        public string Max = "";
        public bool Integer;
    }
}
