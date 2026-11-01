using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>
    /// The separator plus row of icon buttons pinned near the bottom of the window. Only for moving
    /// between pages and "back"; actions on a thing belong in its card.
    /// </summary>
    public static class NavRow
    {
        /// <summary>Fraction of the window height the separator sits at; full-height pages stop above it.</summary>
        public const double LineHeightFraction = 0.88;
        private const double ButtonHeightFraction = 0.91;
        private const double ButtonSize = 40;
        private const double ButtonGap = 12;

        public static void Build(GuiComposer compo, ElementBounds main, double firstButtonX, params NavButton[] buttons)
        {
            var line = ElementBounds
                .Fixed(0, main.fixedHeight * LineHeightFraction, main.fixedWidth, 5)
                .WithParent(main);
            compo.AddInset(line);

            double y = main.fixedHeight * ButtonHeightFraction;
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                ElementBounds target = ElementBounds
                    .Fixed(firstButtonX + i * (ButtonSize + ButtonGap), y, ButtonSize, ButtonSize)
                    .WithParent(main);
                compo.AddIconButton(button.Icon, _ => button.OnClick(), target, "nav-" + i);
                Tooltip.Add(compo, button.Tooltip, target, "navtip-" + i);
            }
        }
    }
}
