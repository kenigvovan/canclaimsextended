using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cairo;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace canclaimsextended.src.client.gui.widgets
{
    /// <summary>
    /// Composes a coat of arms with Cairo, layer over layer. Decodes a PNG per layer, so callers go
    /// through <see cref="EmblemCache"/>.
    /// </summary>
    public static class EmblemImage
    {
        /// <summary>The emblem on its own surface, or null when it has no drawable layers.</summary>
        public static ImageSurface Compose(ICoreClientAPI capi, string emblem, int size)
        {
            List<string> layers = EmblemHandler.Parse(emblem);
            if (layers.Count == 0) return null;

            var surface = new ImageSurface(Format.Argb32, size, size);
            var ctx = new Context(surface);
            foreach (string layer in layers)
            {
                DrawLayer(capi, ctx, layer, size);
            }
            ctx.Dispose();
            return surface;
        }

        private static void DrawLayer(ICoreClientAPI capi, Context ctx, string layer, int size)
        {
            ImageSurface img;
            try
            {
                img = LoadPremultiplied(capi, new AssetLocation(EmblemHandler.DOMAIN, EmblemHandler.TexturePath(layer)));
            }
            catch
            {
                // Whitelisted pattern with no file behind it (partial asset copy, resource pack).
                capi.Logger.Warning("[canclaimsextended] emblem layer texture '{0}' could not be loaded", layer);
                return;
            }

            // Nearest keeps the pixel-art outlines sharp.
            var pattern = new SurfacePattern(img) { Filter = Filter.Nearest };
            ctx.Save();
            ctx.Scale((double)size / img.Width, (double)size / img.Height);
            ctx.SetSource(pattern);
            ctx.Paint();
            ctx.Restore();
            pattern.Dispose();
            img.Dispose();
        }

        /// <summary>
        /// The engine decodes PNGs unpremultiplied while Cairo's Argb32 expects premultiplied alpha;
        /// copied as is, transparent pixels bleed their colour into what is under them.
        /// </summary>
        private static ImageSurface LoadPremultiplied(ICoreClientAPI capi, AssetLocation location)
        {
            byte[] data = capi.Assets.Get(location).Data;
            BitmapExternal bitmap = capi.Render.BitmapCreateFromPng(data);
            try
            {
                int[] pixels = bitmap.Pixels;
                for (int i = 0; i < pixels.Length; i++)
                {
                    uint pixel = (uint)pixels[i];
                    uint a = pixel >> 24;
                    if (a == 255) continue;
                    if (a == 0)
                    {
                        pixels[i] = 0;
                        continue;
                    }
                    uint r = ((pixel >> 16) & 0xFF) * a / 255;
                    uint g = ((pixel >> 8) & 0xFF) * a / 255;
                    uint b = (pixel & 0xFF) * a / 255;
                    pixels[i] = (int)((a << 24) | (r << 16) | (g << 8) | b);
                }

                var surface = new ImageSurface(Format.Argb32, bitmap.Width, bitmap.Height);
                // Row by row against the stride: Cairo may pad rows.
                int stride = surface.Stride;
                for (int row = 0; row < bitmap.Height; row++)
                {
                    Marshal.Copy(pixels, row * bitmap.Width, surface.DataPtr + row * stride, bitmap.Width);
                }
                surface.MarkDirty();
                return surface;
            }
            finally
            {
                bitmap.Dispose();
            }
        }
    }
}
