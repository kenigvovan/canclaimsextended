using System;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>One icon button in the bottom navigation row.</summary>
    public sealed class NavButton
    {
        public string Icon;
        public Action OnClick;
        /// <summary>Already translated.</summary>
        public string Tooltip;

        public NavButton(string icon, Action onClick, string tooltip = null)
        {
            Icon = icon;
            OnClick = onClick;
            Tooltip = tooltip;
        }
    }
}
