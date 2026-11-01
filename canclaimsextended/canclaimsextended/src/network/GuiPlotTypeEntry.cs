using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A plot type the server offers; the client names it by code from its own lang.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiPlotTypeEntry
    {
        public string Code = "";
        /// <summary>One-time price of setting it, paid by the city.</summary>
        public string SetCost = "";
        /// <summary>Daily upkeep of one unit of volume.</summary>
        public string UnitUpkeep = "";
    }
}
