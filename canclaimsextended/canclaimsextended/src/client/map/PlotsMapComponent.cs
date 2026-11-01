using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canclaimsextended.src.client.map
{
    /// <summary>
    /// One texture covering ChunkLen × ChunkLen game chunks of the plots layer. Same scheme as in
    /// claims: tiles are queued and painted into the texture in one framebuffer pass.
    /// </summary>
    public class PlotsMapComponent : MapComponent
    {
        public const int ChunkLen = 4;

        static LoadedTexture tmpTexture;
        static int[] emptyPixels;

        public LoadedTexture Texture;
        readonly Vec3d worldPos;
        Vec2f viewPos = new Vec2f();
        readonly bool[,] chunkSet = new bool[ChunkLen, ChunkLen];
        int[][] pixelsToSet;
        readonly int chunksize;

        public PlotsMapComponent(ICoreClientAPI capi, int baseChunkX, int baseChunkZ) : base(capi)
        {
            chunksize = capi.World.BlockAccessor.ChunkSize;
            worldPos = new Vec3d(baseChunkX * chunksize, 0.0, baseChunkZ * chunksize);
            if (emptyPixels == null)
            {
                int num = ChunkLen * chunksize;
                emptyPixels = new int[num * num];
            }
        }

        public bool IsChunkSet(int dx, int dz) => chunkSet[dx, dz];

        public void setChunk(int dx, int dz, int[] pixels)
        {
            pixelsToSet ??= new int[ChunkLen * ChunkLen][];
            pixelsToSet[dz * ChunkLen + dx] = pixels;
            chunkSet[dx, dz] = true;
        }

        public void unsetChunk(int dx, int dz)
        {
            chunkSet[dx, dz] = false;
        }

        public void FinishSetChunks()
        {
            if (pixelsToSet == null) return;
            if (tmpTexture == null || tmpTexture.Disposed)
            {
                tmpTexture = new LoadedTexture(capi, 0, chunksize, chunksize);
            }
            if (Texture == null || Texture.Disposed)
            {
                int num = ChunkLen * chunksize;
                Texture = new LoadedTexture(capi, 0, num, num);
                capi.Render.LoadOrUpdateTextureFromRgba(emptyPixels, false, 0, ref Texture);
            }
            FrameBufferRef fb = capi.Render.CreateFrameBuffer(Texture);
            for (int i = 0; i < pixelsToSet.Length; i++)
            {
                if (pixelsToSet[i] == null) continue;
                capi.Render.LoadOrUpdateTextureFromRgba(pixelsToSet[i], false, 0, ref tmpTexture);
                // alphaTest -1 turns blending off, so a tile overwrites what was there, transparent included.
                capi.Render.RenderTextureIntoFrameBuffer(0, tmpTexture, 0f, 0f, chunksize, chunksize, fb,
                    chunksize * (i % ChunkLen), chunksize * (i / ChunkLen), -1);
                pixelsToSet[i] = null;
            }
            capi.Render.DestroyFrameBuffer(fb);
            capi.Render.BindTexture2d(Texture.TextureId);
            capi.Render.GlGenerateTex2DMipmaps();
            pixelsToSet = null;
        }

        public override void Render(GuiElementMap map, float dt)
        {
            if (Texture == null || Texture.Disposed) return;
            map.TranslateWorldPosToViewPos(worldPos, ref viewPos);
            capi.Render.Render2DTexture(Texture.TextureId,
                (int)(map.Bounds.renderX + viewPos.X), (int)(map.Bounds.renderY + viewPos.Y),
                (int)(Texture.Width * map.ZoomLevel), (int)(Texture.Height * map.ZoomLevel), 50f);
        }

        public void ActuallyDispose()
        {
            Texture?.Dispose();
        }

        public static void DisposeStatic()
        {
            tmpTexture?.Dispose();
            tmpTexture = null;
            emptyPixels = null;
        }
    }
}
