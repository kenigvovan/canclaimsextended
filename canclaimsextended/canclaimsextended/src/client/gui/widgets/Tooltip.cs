using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>Hover labels sized to their own text, each under its own key.</summary>
    public static class Tooltip
    {
        private const double Padding = 16;
        private const double MaxWidth = 320;
        private const double MinHoverHeight = 25;
        // Only rows thinner than this are grown: growing 17px card rows made two tooltips overlap.
        private const double MinUsableHeight = 10;

        public static void Add(GuiComposer compo, string text, ElementBounds bounds, string key)
        {
            if (string.IsNullOrEmpty(text)) return;

            CairoFont font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);
            double width = font.GetTextExtents(text).Width + Padding;
            if (width > MaxWidth) width = MaxWidth;

            ElementBounds area = bounds;
            if (area.fixedHeight < MinUsableHeight)
            {
                area = bounds.FlatCopy();
                area.fixedHeight = MinHoverHeight;
            }

            compo.AddHoverText(text, font, (int)width, area, key);
        }
    }
}
