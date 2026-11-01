using System;
using System.Collections.Generic;
using canclaimsextended.src.geometry;
using canclaimsextended.src.items;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// Lines on the edges of city territory around the player while the claim wand is in hand (or
    /// always, after /city borders on), like a chunk grid but only where the land changes hands.
    /// The mesh is built from ClientLandStore and rebuilt only when the player moves to another
    /// chunk or height step, or the land around changes; each frame just draws it.
    /// </summary>
    public class BorderRenderer : IRenderer
    {
        const int RADIUS = ClientSettings.BORDER_RADIUS_CHUNKS;
        const int STEP = ClientSettings.BORDER_STEP;
        // Lines sit this far inside their chunk, so two cities meeting on one edge draw side by side.
        const float INSET = 0.05f;
        // As vanilla wireframes: the shader draws such vertices a little closer, over the blocks.
        const int WIREFRAME_FLAGS = 256;

        readonly ICoreClientAPI capi;
        readonly ClientLandStore land;
        readonly Matrixf mvMat = new Matrixf();
        MeshRef mesh;
        bool dirty = true;
        int builtX, builtZ, builtY, builtSize;
        // Mesh coordinates are relative to this block, so floats stay small far from the origin.
        int originX, originY, originZ;

        /// <summary>Set by the server's /city borders.</summary>
        public bool AlwaysShow { get; set; }

        public double RenderOrder => 0.5;
        public int RenderRange => (RADIUS + 1) * Math.Max(land.ChunkSize, 1);

        public BorderRenderer(ICoreClientAPI capi, ClientLandStore land)
        {
            this.capi = capi;
            this.land = land;
            land.AreaChanged += OnAreaChanged;
        }

        void OnAreaChanged(BlockBox area)
        {
            dirty = true;
        }

        bool ShouldShow()
        {
            if (AlwaysShow) return true;
            return capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemClaimWand;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            var entity = capi.World.Player?.Entity;
            int size = land.ChunkSize;
            if (entity == null || size <= 0 || !ShouldShow()) return;

            int cx = (int)Math.Floor(entity.Pos.X / size);
            int cz = (int)Math.Floor(entity.Pos.Z / size);
            int baseY = (int)Math.Floor(entity.Pos.Y / STEP) * STEP;
            if (dirty || cx != builtX || cz != builtZ || baseY != builtY || size != builtSize)
            {
                Rebuild(cx, cz, baseY, size);
            }
            if (mesh == null) return;

            IShaderProgram program = capi.Shader.GetProgram((int)EnumShaderProgram.Wireframe);
            program.Use();
            capi.Render.LineWidth = ClientSettings.BORDER_LINE_WIDTH;
            capi.Render.GLEnableDepthTest();
            capi.Render.GLDepthMask(false);
            capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
            Vec3d cam = entity.CameraPos;
            mvMat.Identity().Set(capi.Render.CameraMatrixOrigin).Translate(originX - cam.X, originY - cam.Y, originZ - cam.Z);
            program.Uniform("origin", 0f, 0f, 0f);
            program.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            program.UniformMatrix("modelViewMatrix", mvMat.Values);
            // The vertex colors carry own city and other cities apart.
            program.Uniform("colorIn", ColorUtil.WhiteArgbVec);
            capi.Render.RenderMesh(mesh);
            program.Stop();
            capi.Render.LineWidth = 1.6f;
            capi.Render.GLDepthMask(true);
        }

        void Rebuild(int cx, int cz, int baseY, int size)
        {
            dirty = false;
            builtX = cx;
            builtZ = cz;
            builtY = baseY;
            builtSize = size;
            originX = cx * size;
            originZ = cz * size;
            originY = Math.Max(0, baseY - ClientSettings.BORDER_DOWN);
            int topY = Math.Min(capi.World.BlockAccessor.MapSizeY, baseY + ClientSettings.BORDER_UP);

            var xyz = new List<float>();
            var colors = new List<int>();
            if (topY > originY)
            {
                for (int dx = -RADIUS; dx <= RADIUS; dx++)
                {
                    for (int dz = -RADIUS; dz <= RADIUS; dz++)
                    {
                        AddChunkEdges(cx + dx, cz + dz, size, topY - originY, xyz, colors);
                    }
                }
            }

            mesh?.Dispose();
            mesh = null;
            int count = colors.Count;
            if (count == 0) return;
            var data = new MeshData(count, count, false, false, true, true);
            data.SetMode(EnumDrawMode.Lines);
            for (int i = 0; i < count; i++)
            {
                data.AddVertexSkipTex(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2], colors[i]);
                data.AddIndex(i);
            }
            for (int i = 0; i < count; i++) data.Flags[i] = WIREFRAME_FLAGS;
            mesh = capi.Render.UploadMesh(data);
        }

        /// <summary>
        /// The sides of one city chunk whose neighbour is another city's or nobody's. Groups of a
        /// city are other ClientCityLand objects with the same Guid, so they draw no line between.
        /// </summary>
        void AddChunkEdges(int x, int z, int size, int height, List<float> xyz, List<int> colors)
        {
            if (!land.TryGetCityAtChunk(x, z, out ClientCityLand city)) return;
            int color = PlotMarkerFlags.Has(city.Flags, PlotMarkerFlags.OWN_CITY) ? ClientSettings.BORDER_COLOR_OWN : ClientSettings.BORDER_COLOR_OTHER;
            // Chunk corner relative to the mesh origin.
            float x0 = x * size - originX, z0 = z * size - originZ;
            float x1 = x0 + size, z1 = z0 + size;
            if (!SameCity(x + 1, z, city)) AddSide(x1 - INSET, z0, x1 - INSET, z1, height, color, xyz, colors);
            if (!SameCity(x - 1, z, city)) AddSide(x0 + INSET, z0, x0 + INSET, z1, height, color, xyz, colors);
            if (!SameCity(x, z + 1, city)) AddSide(x0, z1 - INSET, x1, z1 - INSET, height, color, xyz, colors);
            if (!SameCity(x, z - 1, city)) AddSide(x0, z0 + INSET, x1, z0 + INSET, height, color, xyz, colors);
        }

        bool SameCity(int x, int z, ClientCityLand city)
        {
            return land.TryGetCityAtChunk(x, z, out ClientCityLand other) && other.Guid == city.Guid;
        }

        /// <summary>A grid on one side: across every STEP blocks of height, upright every STEP blocks along.</summary>
        static void AddSide(float ax, float az, float bx, float bz, int height, int color, List<float> xyz, List<int> colors)
        {
            for (int y = 0; y <= height; y += STEP)
            {
                AddLine(ax, y, az, bx, y, bz, color, xyz, colors);
            }
            float length = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az));
            for (float t = 0; t <= length + 0.01f; t += STEP)
            {
                float f = length > 0 ? t / length : 0;
                float px = ax + (bx - ax) * f, pz = az + (bz - az) * f;
                AddLine(px, 0, pz, px, height, pz, color, xyz, colors);
            }
        }

        static void AddLine(float ax, float ay, float az, float bx, float by, float bz, int color, List<float> xyz, List<int> colors)
        {
            xyz.Add(ax); xyz.Add(ay); xyz.Add(az);
            xyz.Add(bx); xyz.Add(by); xyz.Add(bz);
            colors.Add(color);
            colors.Add(color);
        }

        public void Dispose()
        {
            land.AreaChanged -= OnAreaChanged;
            mesh?.Dispose();
            mesh = null;
        }
    }
}
