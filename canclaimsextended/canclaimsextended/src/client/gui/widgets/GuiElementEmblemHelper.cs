using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    public static class GuiElementEmblemHelper
    {
        /// <summary>Adds a coat of arms to the composer; keyed per page like any other element.</summary>
        public static GuiComposer AddEmblem(this GuiComposer composer, string emblem, ElementBounds bounds,
                                            string key = null)
        {
            if (!composer.Composed)
            {
                composer.AddStaticElement(new GuiElementEmblem(composer.Api, bounds, emblem), key);
            }
            return composer;
        }
    }
}
