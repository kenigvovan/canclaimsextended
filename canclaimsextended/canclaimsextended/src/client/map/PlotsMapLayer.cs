using System;
using System.Collections.Generic;
using System.Text;
using canclaimsextended.src.geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canclaimsextended.src.client.map
{
    /// <summary>
    /// City territory and plots on the world map. Territory is a faint fill in the city's color
    /// with a thick edge where it meets other land. Plots are drawn over it, cut at the player's
    /// height: a column shows the plot that crosses the slice (ClientSettings.MAP_SLICE_*) closest
    /// to the player, edged thinly where the neighbour pixel shows a different plot, and a faint
    /// hatch when the column has plots only above or below it.
    /// </summary>
    public class PlotsMapLayer : MapLayer
    {
        readonly Dictionary<(int, int), PlotsMapComponent> components = new();
        readonly Queue<(int, int)> dirtyQueue = new();
        readonly HashSet<(int, int)> dirtySet = new();
        readonly object dirtyLock = new();
        int chunksize;
        int[] emptyChunkPixels;
        int layerY = int.MinValue;
        ClientPlotStore store;
        ClientLandStore land;
        Vec3d hoverPos = new Vec3d();
        // Pixels of the territory edge, counted inwards.
        const int LandEdge = 2;

        public override string Title => "Plots";
        public override string LayerGroupCode => "canclaimsextended";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

        public PlotsMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
        }

        public override void OnLoaded()
        {
            chunksize = api.World.BlockAccessor.ChunkSize;
            emptyChunkPixels = new int[chunksize * chunksize];
            store = ClientSetup.Store;
            if (store != null)
            {
                store.AreaChanged += MarkAreaDirty;
            }
            land = ClientSetup.Land;
            if (land != null)
            {
                land.AreaChanged += MarkAreaDirty;
            }
        }

        /// <summary>Queues every game chunk the area covers on X and Z, plus the ring the edges reach into.</summary>
        void MarkAreaDirty(BlockBox area)
        {
            if (chunksize == 0) return;
            int cx0 = FloorDiv(area.MinX - LandEdge, chunksize), cx1 = FloorDiv(area.MaxX + LandEdge - 1, chunksize);
            int cz0 = FloorDiv(area.MinZ - LandEdge, chunksize), cz1 = FloorDiv(area.MaxZ + LandEdge - 1, chunksize);
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    QueueChunk(cx, cz);
                }
            }
        }

        void QueueChunk(int cx, int cz)
        {
            lock (dirtyLock)
            {
                if (dirtySet.Add((cx, cz))) dirtyQueue.Enqueue((cx, cz));
            }
        }

        public override void OnTick(float dt)
        {
            if (chunksize == 0 || store == null) return;
            if (api is not ICoreClientAPI capi || capi.World.Player?.Entity == null) return;

            int step = ClientSettings.MAP_LAYER_STEP;
            int y = FloorDiv((int)capi.World.Player.Entity.Pos.Y, step) * step;
            if (y != layerY)
            {
                layerY = y;
                foreach (ClientPlot plot in store.All())
                {
                    MarkAreaDirty(plot.Shape.Bounds);
                }
                // Tiles painted for the old height must go too, even where no plot is now.
                foreach (var it in components)
                {
                    for (int dx = 0; dx < PlotsMapComponent.ChunkLen; dx++)
                    {
                        for (int dz = 0; dz < PlotsMapComponent.ChunkLen; dz++)
                        {
                            if (it.Value.IsChunkSet(dx, dz))
                            {
                                QueueChunk(it.Key.Item1 * PlotsMapComponent.ChunkLen + dx, it.Key.Item2 * PlotsMapComponent.ChunkLen + dz);
                            }
                        }
                    }
                }
            }

            var modified = new HashSet<PlotsMapComponent>();
            for (int painted = 0; painted < ClientSettings.MAP_CHUNK_PAINTS_PER_TICK; painted++)
            {
                (int cx, int cz) chunk;
                lock (dirtyLock)
                {
                    if (dirtyQueue.Count == 0) break;
                    chunk = dirtyQueue.Dequeue();
                    dirtySet.Remove(chunk);
                }
                int mx = FloorDiv(chunk.cx, PlotsMapComponent.ChunkLen);
                int mz = FloorDiv(chunk.cz, PlotsMapComponent.ChunkLen);
                int dx = chunk.cx - mx * PlotsMapComponent.ChunkLen;
                int dz = chunk.cz - mz * PlotsMapComponent.ChunkLen;
                components.TryGetValue((mx, mz), out PlotsMapComponent comp);

                int[] pixels = PaintChunk(chunk.cx, chunk.cz);
                bool cleared = false;
                if (pixels == null)
                {
                    if (comp == null || !comp.IsChunkSet(dx, dz)) continue;
                    pixels = emptyChunkPixels;
                    cleared = true;
                }
                if (comp == null)
                {
                    comp = new PlotsMapComponent(capi, mx * PlotsMapComponent.ChunkLen, mz * PlotsMapComponent.ChunkLen);
                    components[(mx, mz)] = comp;
                }
                comp.setChunk(dx, dz, pixels);
                if (cleared) comp.unsetChunk(dx, dz);
                modified.Add(comp);
            }
            foreach (PlotsMapComponent comp in modified)
            {
                comp.FinishSetChunks();
            }
        }

        /// <summary>Pixels of one game chunk, row = Z, column = X; null when nothing is drawn there.</summary>
        int[] PaintChunk(int cx, int cz)
        {
            int x0 = cx * chunksize, z0 = cz * chunksize;
            // One block wider than the chunk: the edge needs to know the neighbour pixel.
            List<ClientPlot> candidates = store.Query(new BlockBox(x0 - 1, 0, z0 - 1, x0 + chunksize + 1, store.WorldHeight, z0 + chunksize + 1));

            // Picks for the chunk with a ring, so every edge test is a lookup. The ring is as wide
            // as the territory edge; plots need only its inner pixel.
            int ring = LandEdge;
            int w = chunksize + 2 * ring;
            var cities = new ClientCityLand[w * w];
            bool anyLand = false;
            if (land != null && land.ChunkSize > 0)
            {
                for (int i = 0; i < w; i++)
                {
                    for (int j = 0; j < w; j++)
                    {
                        land.TryGetCityAtBlock(x0 + j - ring, z0 + i - ring, out cities[i * w + j]);
                        anyLand |= cities[i * w + j] != null;
                    }
                }
            }
            if (candidates.Count == 0 && !anyLand) return null;

            var picked = new ClientPlot[w * w];
            var hidden = new bool[w * w];
            if (candidates.Count > 0)
            {
                for (int i = ring - 1; i <= w - ring; i++)
                {
                    for (int j = ring - 1; j <= w - ring; j++)
                    {
                        picked[i * w + j] = Pick(x0 + j - ring, z0 + i - ring, candidates, out hidden[i * w + j]);
                    }
                }
            }

            int[] pixels = null;
            for (int i = 0; i < chunksize; i++)
            {
                for (int j = 0; j < chunksize; j++)
                {
                    int x = x0 + j, z = z0 + i;
                    int g = (i + ring) * w + (j + ring);
                    ClientPlot plot = picked[g];
                    ClientCityLand city = cities[g];
                    int color;
                    if (plot != null)
                    {
                        // Thinner and lighter than the territory edge, so the two read apart.
                        bool edge = picked[g - 1] != plot || picked[g + 1] != plot
                                 || picked[g - w] != plot || picked[g + w] != plot;
                        color = WithAlpha(plot.TintColor, edge ? 0xB0 : 0x60);
                    }
                    else if (city != null)
                    {
                        color = WithAlpha(city.Color, IsLandEdge(cities, w, g, city) ? 0xE0 : 0x40);
                    }
                    else if (hidden[g] && ((x + z) & 3) == 0)
                    {
                        color = WithAlpha(unchecked((int)0xFFFFFFFF), 0x50);
                    }
                    else
                    {
                        continue;
                    }
                    pixels ??= new int[chunksize * chunksize];
                    pixels[i * chunksize + j] = color;
                }
            }
            return pixels;
        }

        /// <summary>
        /// Whether another city or free land lies within LandEdge pixels on X or Z. The groups of
        /// one city are the same city here: their chunks are told apart on hover only.
        /// </summary>
        static bool IsLandEdge(ClientCityLand[] cities, int w, int g, ClientCityLand city)
        {
            for (int d = 1; d <= LandEdge; d++)
            {
                if (!SameCity(cities[g - d], city) || !SameCity(cities[g + d], city)
                    || !SameCity(cities[g - d * w], city) || !SameCity(cities[g + d * w], city))
                {
                    return true;
                }
            }
            return false;
        }

        static bool SameCity(ClientCityLand a, ClientCityLand b) => a != null && b != null && a.Guid == b.Guid;

        /// <summary>
        /// The plot drawn at this column: of the boxes crossing the slice, the one nearest to the
        /// player's height. hiddenOnly tells that the column has plots, but none in the slice.
        /// </summary>
        ClientPlot Pick(int x, int z, List<ClientPlot> candidates, out bool hiddenOnly)
        {
            hiddenOnly = false;
            int sliceMin = layerY - ClientSettings.MAP_SLICE_DOWN, sliceMax = layerY + ClientSettings.MAP_SLICE_UP;
            ClientPlot best = null;
            int bestDist = int.MaxValue;
            foreach (ClientPlot plot in candidates)
            {
                foreach (BlockBox b in plot.Shape.Boxes)
                {
                    if (x < b.MinX || x >= b.MaxX || z < b.MinZ || z >= b.MaxZ) continue;
                    if (b.MaxY <= sliceMin || b.MinY >= sliceMax)
                    {
                        hiddenOnly = true;
                        continue;
                    }
                    int dist = layerY < b.MinY ? b.MinY - layerY : layerY >= b.MaxY ? layerY - (b.MaxY - 1) : 0;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = plot;
                    }
                }
            }
            if (best != null) hiddenOnly = false;
            return best;
        }

        public override void Render(GuiElementMap mapElem, float dt)
        {
            if (!Active) return;
            foreach (PlotsMapComponent comp in components.Values)
            {
                comp.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            if (store == null || chunksize == 0) return;
            mapElem.TranslateViewPosToWorldPos(
                new Vec2f((float)(args.X - mapElem.Bounds.renderX), (float)(args.Y - mapElem.Bounds.renderY)),
                ref hoverPos);
            int x = (int)Math.Floor(hoverPos.X), z = (int)Math.Floor(hoverPos.Z);
            List<ClientPlot> candidates = store.Query(new BlockBox(x, 0, z, x + 1, store.WorldHeight, z + 1));
            ClientPlot plot = Pick(x, z, candidates, out _);
            if (plot == null)
            {
                if (land != null && land.TryGetCityAtBlock(x, z, out ClientCityLand city))
                {
                    hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_city_land", ClientText.Pretty(city.Name)));
                    if (city.GroupName.Length > 0)
                    {
                        hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_group", ClientText.Pretty(city.GroupName)));
                    }
                }
                return;
            }
            if (plot.CityName.Length > 0)
            {
                hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_city", ClientText.Pretty(plot.CityName)));
            }
            string number = plot.Number > 0 ? Lang.Get("canclaimsextended:marker-number", plot.Number) : "";
            if (plot.Name.Length > 0 || number.Length > 0)
            {
                hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_plot", (number + " " + plot.Name).Trim()));
            }
            if (plot.OwnerName.Length > 0)
            {
                hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_owner", plot.OwnerName));
            }
            if (plot.GroupName.Length > 0)
            {
                hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_group", ClientText.Pretty(plot.GroupName)));
            }
            if (plot.ForSale)
            {
                hoverText.AppendLine(Lang.Get("canclaimsextended:map_hover_price", plot.Price));
            }
        }

        public override void Dispose()
        {
            if (store != null) store.AreaChanged -= MarkAreaDirty;
            if (land != null) land.AreaChanged -= MarkAreaDirty;
            foreach (PlotsMapComponent comp in components.Values)
            {
                comp.ActuallyDispose();
            }
            components.Clear();
            PlotsMapComponent.DisposeStatic();
            base.Dispose();
        }

        static int WithAlpha(int color, int alpha)
        {
            return (color & 0x00FFFFFF) | (alpha << 24);
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }
    }
}
