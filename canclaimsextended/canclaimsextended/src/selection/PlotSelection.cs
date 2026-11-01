using System;
using canclaimsextended.src.geometry;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.selection
{
    /// <summary>Two corner blocks a player picked; either may still be missing.</summary>
    public class PlotSelection
    {
        public BlockPos Corner1 { get; set; }
        public BlockPos Corner2 { get; set; }
        /// <summary>Highlight stays on even without the wand in hand (/city sel show).</summary>
        public bool AlwaysShow { get; set; }

        public bool IsComplete => Corner1 != null && Corner2 != null;

        /// <summary>The raw box between the corners, or the single corner block when only one is set.</summary>
        public bool TryGetRawBox(out BlockBox box)
        {
            BlockPos a = Corner1 ?? Corner2;
            BlockPos b = Corner2 ?? Corner1;
            if (a == null)
            {
                box = default;
                return false;
            }
            box = BlockBox.FromCorners(a, b);
            return true;
        }

        /// <summary>Moves one face of the selection; negative n shrinks, never past the opposite face.</summary>
        public void Grow(BlockFacing facing, int n)
        {
            if (!IsComplete) return;
            // The corners are in any order; normalise them so each face belongs to one of them.
            BlockPos lo = new BlockPos(Math.Min(Corner1.X, Corner2.X), Math.Min(Corner1.Y, Corner2.Y), Math.Min(Corner1.Z, Corner2.Z));
            BlockPos hi = new BlockPos(Math.Max(Corner1.X, Corner2.X), Math.Max(Corner1.Y, Corner2.Y), Math.Max(Corner1.Z, Corner2.Z));
            if (facing == BlockFacing.EAST) hi.X = Math.Max(lo.X, hi.X + n);
            else if (facing == BlockFacing.WEST) lo.X = Math.Min(hi.X, lo.X - n);
            else if (facing == BlockFacing.UP) hi.Y = Math.Max(lo.Y, hi.Y + n);
            else if (facing == BlockFacing.DOWN) lo.Y = Math.Min(hi.Y, lo.Y - n);
            else if (facing == BlockFacing.SOUTH) hi.Z = Math.Max(lo.Z, hi.Z + n);
            else if (facing == BlockFacing.NORTH) lo.Z = Math.Min(hi.Z, lo.Z - n);
            Corner1 = lo;
            Corner2 = hi;
        }
    }
}
