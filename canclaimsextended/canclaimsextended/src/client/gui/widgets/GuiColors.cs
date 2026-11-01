namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>The palette every page shares, as in claims.</summary>
    public static class GuiColors
    {
        /// <summary>Heading of a card.</summary>
        public static readonly double[] Section = { 0.95, 0.84, 0.52, 1.0 };

        /// <summary>Muted grey for the caption of a caption/value row, and for empty states.</summary>
        public static readonly double[] Label = { 0.74, 0.74, 0.74, 1.0 };

        /// <summary>Warm accent for the value itself.</summary>
        public static readonly double[] Value = { 1.00, 0.88, 0.45, 1.0 };

        public static readonly double[] Danger = { 0.90, 0.35, 0.30, 1.0 };
        public static readonly double[] Success = { 0.45, 0.80, 0.45, 1.0 };
    }
}
