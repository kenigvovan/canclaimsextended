namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>One caption/value line of a card.</summary>
    public sealed class CardRow
    {
        public string Label;
        public string Value;
        /// <summary>Null falls back to the shared accent colour.</summary>
        public double[] ValueColor;
        public string Tooltip;
        /// <summary>Suffix of the element keys, unique within the page.</summary>
        public string Key;
    }
}
