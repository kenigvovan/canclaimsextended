using System;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>A small icon button at the right end of a list row.</summary>
    public sealed class RowButton
    {
        public string Icon;
        public Action OnClick;
        /// <summary>Already translated.</summary>
        public string Tooltip;

        public RowButton(string icon, Action onClick, string tooltip)
        {
            Icon = icon;
            OnClick = onClick;
            Tooltip = tooltip;
        }
    }
}
