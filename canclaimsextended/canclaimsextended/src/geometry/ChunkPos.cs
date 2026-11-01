#nullable enable
using System;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.geometry
{
    /// <summary>
    /// One column of city land: CLAIM_CHUNK_SIZE x CLAIM_CHUNK_SIZE blocks over the whole world height.
    /// The size is read from the config each time, so a change takes a restart with an empty database.
    /// </summary>
    public readonly struct ChunkPos : IEquatable<ChunkPos>
    {
        public readonly int X, Z;

        public ChunkPos(int x, int z)
        {
            X = x;
            Z = z;
        }

        public static int Size => canclaimsextended.config.CLAIM_CHUNK_SIZE;

        public static ChunkPos FromBlock(int x, int z)
        {
            // Floor division: -1 belongs to chunk -1, not 0.
            int size = Size;
            return new ChunkPos(FloorDiv(x, size), FloorDiv(z, size));
        }

        public static ChunkPos FromBlock(BlockPos pos) => FromBlock(pos.X, pos.Z);

        public static ChunkPos FromBlock(double x, double z) => FromBlock((int)Math.Floor(x), (int)Math.Floor(z));

        /// <summary>X in the high half, Z in the low one; both keep their sign.</summary>
        public long Key => ((long)X << 32) | (uint)Z;

        public static ChunkPos FromKey(long key) => new ChunkPos((int)(key >> 32), (int)(key & 0xFFFFFFFF));

        public int MinBlockX => X * Size;
        public int MinBlockZ => Z * Size;

        /// <summary>The column as a box from 0 up to worldHeight.</summary>
        public BlockBox Column(int worldHeight)
        {
            int size = Size;
            return new BlockBox(X * size, 0, Z * size, X * size + size, worldHeight, Z * size + size);
        }

        /// <summary>The four chunks sharing a side with this one.</summary>
        public ChunkPos[] Neighbours4()
        {
            return new[] { new ChunkPos(X + 1, Z), new ChunkPos(X - 1, Z), new ChunkPos(X, Z + 1), new ChunkPos(X, Z - 1) };
        }

        /// <summary>Distance in chunks between the nearest edges; 0 for neighbours, diagonal ones included.</summary>
        public double DistanceTo(ChunkPos other)
        {
            long dx = Math.Max(0, Math.Abs((long)X - other.X) - 1);
            long dz = Math.Max(0, Math.Abs((long)Z - other.Z) - 1);
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static int FloorDiv(int v, int d)
        {
            int q = v / d;
            return (v % d != 0 && (v < 0) != (d < 0)) ? q - 1 : q;
        }

        public bool Equals(ChunkPos other) => X == other.X && Z == other.Z;
        public override bool Equals(object? obj) => obj is ChunkPos other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z);
        public static bool operator ==(ChunkPos a, ChunkPos b) => a.Equals(b);
        public static bool operator !=(ChunkPos a, ChunkPos b) => !a.Equals(b);
        public override string ToString() => $"chunk({X},{Z})";
    }
}
