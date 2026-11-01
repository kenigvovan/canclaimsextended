using System.Collections.Generic;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>A list line: a name, an optional value, and the buttons acting on that one entry.</summary>
    public sealed class ItemRow
    {
        public string Label;
        public string Value = "";
        public double[] ValueColor;
        public List<RowButton> Buttons = new List<RowButton>();
        /// <summary>Shown over the label; without it a cut label shows its full text.</summary>
        public string Tooltip;
        /// <summary>Suffix of the element keys, unique within the page.</summary>
        public string Key;
    }
}
