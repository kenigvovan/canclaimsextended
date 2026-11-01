using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>One PLOT_TYPE_ONLY_BLOCKS key: its plot types and how many loaded blocks it matches.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiBlockRuleEntry
    {
        public string Key = "";
        public List<string> Types = new();
        public int Blocks;
    }
}
