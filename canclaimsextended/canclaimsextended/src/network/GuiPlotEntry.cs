using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A plot of the player's city they may reshape from afar.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiPlotEntry
    {
        public int Number;
        public string Name = "";
        public long Volume;
        /// <summary>The player's selection could be added to this plot now.</summary>
        public bool CanExtend;
        /// <summary>At least one part may be taken off, see GuiBoxEntry.CanCut.</summary>
        public bool CanCut;
        /// <summary>In the order /plot boxes and /plot cut number them, from 1.</summary>
        public List<GuiBoxEntry> Parts = new();
    }
}
