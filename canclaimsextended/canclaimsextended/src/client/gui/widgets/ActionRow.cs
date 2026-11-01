using System;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>The strip of icon buttons a card ends with, laid out left to right.</summary>
    public sealed class ActionRow
    {
        private readonly GuiComposer compo;
        private ElementBounds slot;

        public ActionRow(GuiComposer compo, ElementBounds firstSlot)
        {
            this.compo = compo;
            this.slot = firstSlot;
        }

        /// <summary>An icon button that runs <paramref name="onClick"/>, named by its tooltip.</summary>
        public ElementBounds Add(string icon, string key, Action onClick, string tooltip)
        {
            ElementBounds bounds = slot;
            compo.AddIconButton(icon, _ => onClick(), bounds, key);
            Tooltip.Add(compo, tooltip, bounds, "tip-" + key);
            slot = Card.NextAction(bounds);
            return bounds;
        }
    }
}
