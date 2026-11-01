using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.items;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// Small labelled squares over the plots around the player while the claim wand is in hand
    /// (or always, after .cxmarkers): one per flag that is on - pvp, fire, blasts, no mobs - and
    /// three for what the player may do there - use, build, attack, green when allowed, grey when not.
    /// Drawn like vanilla name tags: a world point projected to the screen, so they always face the camera.
    /// </summary>
    public class PlotMarkerRenderer : IRenderer
    {
        const int RANGE = ClientSettings.MARKER_RANGE;
        const int SQUARE = ClientSettings.MARKER_SQUARE;
        const int GAP = ClientSettings.MARKER_GAP;

        readonly ICoreClientAPI capi;
        readonly Dictionary<string, LoadedTexture> textures = new();
        public bool AlwaysShow { get; set; }

        public double RenderOrder => 0.5;
        public int RenderRange => RANGE;

        static readonly (int Bit, string Key, string Fallback, double[] Color)[] FlagMarkers =
        {
            (PlotMarkerFlags.PVP, "marker-pvp", "PvP", new[] { 0.80, 0.15, 0.15, 0.9 }),
            (PlotMarkerFlags.FIRE, "marker-fire", "F", new[] { 0.95, 0.50, 0.10, 0.9 }),
            (PlotMarkerFlags.BLAST, "marker-blast", "B", new[] { 0.90, 0.80, 0.10, 0.9 }),
            (PlotMarkerFlags.NO_MOB_SPAWN, "marker-nomobs", "M", new[] { 0.55, 0.25, 0.75, 0.9 }),
        };
        static readonly (int Bit, string Key, string Fallback)[] RightMarkers =
        {
            (PlotMarkerFlags.CAN_USE, "marker-use", "U"),
            (PlotMarkerFlags.CAN_BUILD, "marker-build", "Bu"),
            (PlotMarkerFlags.CAN_ATTACK, "marker-attack", "A"),
        };
        static readonly double[] Allowed = { 0.20, 0.65, 0.25, 0.9 };
        static readonly double[] Denied = { 0.35, 0.35, 0.35, 0.8 };

        public PlotMarkerRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        bool ShouldShow()
        {
            if (AlwaysShow) return true;
            return capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemClaimWand;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            ClientPlotStore store = ClientSetup.Store;
            var entity = capi.World.Player?.Entity;
            if (store == null || entity == null || !ShouldShow()) return;

            Vec3d eye = entity.Pos.XYZ.Add(entity.LocalEyePos);
            var area = new BlockBox((int)eye.X - RANGE, (int)eye.Y - RANGE, (int)eye.Z - RANGE,
                                    (int)eye.X + RANGE, (int)eye.Y + RANGE, (int)eye.Z + RANGE);
            var eyeBox = new BlockBox((int)Math.Floor(eye.X), (int)Math.Floor(eye.Y), (int)Math.Floor(eye.Z),
                                      (int)Math.Floor(eye.X) + 1, (int)Math.Floor(eye.Y) + 1, (int)Math.Floor(eye.Z) + 1);
            // By the nearest part: the bounds of a plot in pieces may span land that is not the plot.
            var nearest = store.Query(area)
                .Select(p => (Plot: p, Box: NearestBox(p.Shape, eyeBox)))
                .Select(t => (t.Plot, t.Box, Dist: t.Box.DistanceTo(eyeBox)))
                .OrderBy(t => t.Dist)
                .Take(ClientSettings.MARKER_MAX_PLOTS);

            foreach (var (plot, box, _) in nearest)
            {
                if (!Project(Anchor(box, eye), out float x, out float y, out float scale)) continue;
                float gap = (float)GuiElement.scaled(GAP) * scale;
                float top = y - DrawRow(plot.Flags, x, y, scale) - gap;
                if (plot.ForSale)
                {
                    top -= DrawTag(Lang.Get("canclaimsextended:marker-price", plot.Price), "price-" + plot.Price, PriceColor, x, top, scale) + gap;
                }
                DrawNumber(plot, x, top, scale);

                // The other parts carry the number alone, so the pieces of one plot read as one.
                foreach (BlockBox other in plot.Shape.Boxes)
                {
                    if (other == box || other.DistanceTo(eyeBox) > RANGE) continue;
                    if (Project(Anchor(other, eye), out float ox, out float oy, out float oscale))
                    {
                        DrawNumber(plot, ox, oy, oscale);
                    }
                }
            }
        }

        /// <summary>A world point on the screen; false when it is behind the camera.</summary>
        bool Project(Vec3d point, out float x, out float y, out float scale)
        {
            IRenderAPI rapi = capi.Render;
            Vec3d screen = MatrixToolsd.Project(point, rapi.PerspectiveProjectionMat, rapi.PerspectiveViewMat, rapi.FrameWidth, rapi.FrameHeight);
            x = (float)screen.X;
            y = rapi.FrameHeight - (float)screen.Y;
            scale = GameMath.Clamp(8f / (float)Math.Max(1, screen.Z), 0.5f, 1f);
            return screen.Z >= 0;
        }

        static readonly double[] NumberColor = { 0.15, 0.15, 0.20, 0.9 };

        static readonly double[] GroupColor = { 0.20, 0.35, 0.55, 0.9 };

        /// <summary>#n of the plot, with its group's name when it is in one, on every part of it.</summary>
        void DrawNumber(ClientPlot plot, float centerX, float bottomY, float scale)
        {
            if (plot.Number <= 0) return;
            string label = Lang.Get("canclaimsextended:marker-number", plot.Number);
            if (plot.GroupName.Length == 0)
            {
                DrawTag(label, "num-" + plot.Number, NumberColor, centerX, bottomY, scale);
                return;
            }
            label += " · " + ClientText.Pretty(plot.GroupName);
            DrawTag(label, "num-" + plot.Number + "-" + plot.GroupName, GroupColor, centerX, bottomY, scale);
        }

        static readonly double[] PriceColor = { 0.85, 0.65, 0.15, 0.95 };

        /// <summary>A tag of text on a colored plate, centered with its bottom at bottomY; returns its height on screen.</summary>
        float DrawTag(string label, string cacheKey, double[] color, float centerX, float bottomY, float scale)
        {
            if (!textures.TryGetValue(cacheKey, out LoadedTexture tex))
            {
                var background = new TextBackground
                {
                    FillColor = color,
                    Padding = 3,
                    Radius = 3,
                    BorderWidth = 1,
                    BorderColor = new[] { 0.0, 0.0, 0.0, 0.8 }
                };
                tex = capi.Gui.TextTexture.GenTextTexture(label, CairoFont.WhiteSmallText(), background);
                textures[cacheKey] = tex;
            }
            float w = tex.Width * scale;
            float h = tex.Height * scale;
            capi.Render.Render2DTexture(tex.TextureId, centerX - w / 2, bottomY - h, w, h, 20f);
            return h;
        }

        /// <summary>The part of the plot closest to the player; the marker stands over it.</summary>
        static BlockBox NearestBox(PlotShape shape, BlockBox eyeBox)
        {
            BlockBox best = shape.Boxes[0];
            double bestDist = best.DistanceTo(eyeBox);
            for (int i = 1; i < shape.Boxes.Count; i++)
            {
                double dist = shape.Boxes[i].DistanceTo(eyeBox);
                if (dist < bestDist)
                {
                    best = shape.Boxes[i];
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>
        /// Above the middle of the box, at eye height when the eye is within its height range,
        /// otherwise on its top or bottom face - whichever faces the player.
        /// </summary>
        static Vec3d Anchor(BlockBox b, Vec3d eye)
        {
            double y = GameMath.Clamp(eye.Y + 1, b.MinY + 0.5, b.MaxY + 0.5);
            return new Vec3d((b.MinX + b.MaxX) / 2.0, y, (b.MinZ + b.MaxZ) / 2.0);
        }

        /// <summary>Draws the squares with their bottom at y; returns their height on screen.</summary>
        float DrawRow(int flags, float centerX, float y, float scale)
        {
            var row = new List<LoadedTexture>();
            foreach (var m in FlagMarkers)
            {
                if (PlotMarkerFlags.Has(flags, m.Bit)) row.Add(Texture(m.Key, m.Fallback, m.Color));
            }
            foreach (var m in RightMarkers)
            {
                bool allowed = PlotMarkerFlags.Has(flags, m.Bit);
                row.Add(Texture(m.Key + (allowed ? "-yes" : "-no"), m.Key, m.Fallback, allowed ? Allowed : Denied));
            }
            // In screen pixels, so the squares follow the GUI scale like the textures do.
            float size = (float)GuiElement.scaled(SQUARE) * scale;
            float gap = (float)GuiElement.scaled(GAP) * scale;
            float total = row.Count * size + (row.Count - 1) * gap;
            float x = centerX - total / 2;
            foreach (LoadedTexture tex in row)
            {
                capi.Render.Render2DTexture(tex.TextureId, x, y - size, size, size, 20f);
                x += size + gap;
            }
            return size;
        }

        LoadedTexture Texture(string key, string fallback, double[] color) => Texture(key, key, fallback, color);

        /// <summary>A square with a short label, made once and kept.</summary>
        LoadedTexture Texture(string cacheKey, string langKey, string fallback, double[] color)
        {
            if (textures.TryGetValue(cacheKey, out LoadedTexture tex)) return tex;
            string label = Lang.GetIfExists("canclaimsextended:" + langKey) ?? fallback;
            var background = new TextBackground
            {
                FillColor = color,
                Radius = 3,
                BorderWidth = 1,
                BorderColor = new[] { 0.0, 0.0, 0.0, 0.8 }
            };
            int px = (int)GuiElement.scaled(SQUARE);
            tex = capi.Gui.TextTexture.GenTextTexture(label, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                px, px, background, EnumTextOrientation.Center);
            textures[cacheKey] = tex;
            return tex;
        }

        public void Dispose()
        {
            foreach (LoadedTexture tex in textures.Values)
            {
                tex.Dispose();
            }
            textures.Clear();
        }
    }
}
