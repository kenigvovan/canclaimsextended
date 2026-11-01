using System.Collections.Generic;
using Cairo;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>A coat of arms scaled to the element's bounds, or a blank plate when there is none.</summary>
    public class GuiElementEmblem : GuiElement
    {
        private readonly List<string> layers;

        public GuiElementEmblem(ICoreClientAPI capi, ElementBounds bounds, string emblem)
            : base(capi, bounds)
        {
            // Unknown layers are dropped here, so a bad string can never turn into an asset path.
            layers = EmblemHandler.Parse(emblem);
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
            if (layers.Count == 0)
            {
                DrawPlaceholder(ctx);
                return;
            }
            EmblemCache.Draw(api as ICoreClientAPI, ctx, EmblemHandler.Join(layers),
                Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight);
        }

        /// <summary>Reads as "no arms yet" rather than as a rendering failure.</summary>
        private void DrawPlaceholder(Context ctx)
        {
            ctx.Save();
            RoundRectangle(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight, 2.0);
            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.25);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.12);
            ctx.LineWidth = 1.0;
            ctx.Stroke();
            ctx.Restore();
        }
    }
}
