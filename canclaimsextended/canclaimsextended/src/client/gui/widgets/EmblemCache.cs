using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>
    /// One composed surface per distinct coat of arms. The window recomposes on every click, while
    /// arms change almost never.
    /// </summary>
    public static class EmblemCache
    {
        /// <summary>Larger than any box the arms are drawn into, so they are only ever scaled down.</summary>
        private const int ComposeSize = 128;

        private static readonly Dictionary<string, ImageSurface> surfaces = new();

        public static void Draw(ICoreClientAPI capi, Context ctx, string emblem, double x, double y, double width, double height)
        {
            ImageSurface img = Get(capi, emblem);
            if (img == null) return;

            // Good rather than Nearest: this is a downscale.
            var pattern = new SurfacePattern(img) { Filter = Filter.Good };
            ctx.Save();
            ctx.Translate(x, y);
            ctx.Scale(width / img.Width, height / img.Height);
            ctx.SetSource(pattern);
            ctx.Paint();
            ctx.Restore();
            pattern.Dispose();
        }

        private static ImageSurface Get(ICoreClientAPI capi, string emblem)
        {
            if (string.IsNullOrEmpty(emblem)) return null;
            if (surfaces.TryGetValue(emblem, out ImageSurface cached)) return cached;
            ImageSurface surface = EmblemImage.Compose(capi, emblem, ComposeSize);
            if (surface == null) return null;
            surfaces[emblem] = surface;
            return surface;
        }

        public static void Invalidate()
        {
            foreach (var surface in surfaces.Values) surface?.Dispose();
            surfaces.Clear();
        }
    }
}
