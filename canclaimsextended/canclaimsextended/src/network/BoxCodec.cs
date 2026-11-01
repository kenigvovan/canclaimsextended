using System;
using System.Collections.Generic;
using canclaimsextended.src.geometry;

namespace canclaimsextended.src.network
{
    /// <summary>Boxes on the wire: minX, minY, minZ, maxX, maxY, maxZ in a row, max exclusive.</summary>
    public static class BoxCodec
    {
        public static int[] Encode(IReadOnlyList<BlockBox> boxes)
        {
            int[] flat = new int[boxes.Count * 6];
            for (int i = 0; i < boxes.Count; i++)
            {
                BlockBox b = boxes[i];
                flat[i * 6] = b.MinX; flat[i * 6 + 1] = b.MinY; flat[i * 6 + 2] = b.MinZ;
                flat[i * 6 + 3] = b.MaxX; flat[i * 6 + 4] = b.MaxY; flat[i * 6 + 5] = b.MaxZ;
            }
            return flat;
        }

        /// <summary>Null for an empty or malformed list.</summary>
        public static PlotShape Decode(int[] flat)
        {
            if (flat == null || flat.Length == 0 || flat.Length % 6 != 0) return null;
            var boxes = new List<BlockBox>(flat.Length / 6);
            try
            {
                for (int i = 0; i < flat.Length; i += 6)
                {
                    boxes.Add(new BlockBox(flat[i], flat[i + 1], flat[i + 2], flat[i + 3], flat[i + 4], flat[i + 5]));
                }
                return new PlotShape(boxes);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
