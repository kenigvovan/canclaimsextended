using System;
using System.Collections.Generic;
using System.Linq;

namespace canclaimsextended.src.geometry
{
    /// <summary>
    /// Immutable set of non-overlapping, non-empty boxes that make up one plot.
    /// </summary>
    public sealed class PlotShape
    {
        private readonly BlockBox[] boxes;

        public IReadOnlyList<BlockBox> Boxes => boxes;
        public BlockBox Bounds { get; }
        public long Volume { get; }

        public PlotShape(BlockBox box) : this(new[] { box })
        {
        }

        public PlotShape(IEnumerable<BlockBox> boxes)
        {
            this.boxes = boxes.ToArray();
            if (this.boxes.Length == 0)
            {
                throw new ArgumentException("Plot shape needs at least one box");
            }
            BlockBox bounds = this.boxes[0];
            long volume = 0;
            for (int i = 0; i < this.boxes.Length; i++)
            {
                BlockBox b = this.boxes[i];
                if (b.IsEmpty)
                {
                    throw new ArgumentException($"Empty box in plot shape: {b}");
                }
                for (int j = 0; j < i; j++)
                {
                    if (b.Intersects(this.boxes[j]))
                    {
                        throw new ArgumentException($"Boxes of one plot overlap: {this.boxes[j]} and {b}");
                    }
                }
                bounds = bounds.Union(b);
                volume += b.Volume;
            }
            Bounds = bounds;
            Volume = volume;
        }

        public bool Contains(int x, int y, int z)
        {
            if (!Bounds.Contains(x, y, z))
            {
                return false;
            }
            foreach (BlockBox b in boxes)
            {
                if (b.Contains(x, y, z))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Contains(double x, double y, double z)
        {
            return Contains((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
        }

        public bool Intersects(BlockBox box)
        {
            if (!Bounds.Intersects(box))
            {
                return false;
            }
            foreach (BlockBox b in boxes)
            {
                if (b.Intersects(box))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Intersects(PlotShape other)
        {
            if (!Bounds.Intersects(other.Bounds))
            {
                return false;
            }
            foreach (BlockBox b in other.boxes)
            {
                if (Intersects(b))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The shapes don't intersect and at least one pair of their boxes shares a face.
        /// </summary>
        public bool Touches(PlotShape other)
        {
            if (!Bounds.Expand(1).Intersects(other.Bounds) || Intersects(other))
            {
                return false;
            }
            foreach (BlockBox a in boxes)
            {
                foreach (BlockBox b in other.boxes)
                {
                    if (a.Touches(b))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Every box can be reached from every other one through shared faces.</summary>
        public bool IsConnected()
        {
            if (boxes.Length == 1)
            {
                return true;
            }
            return Connectivity.CountPieces(boxes, b => boxes.Where(b.Touches)) == 1;
        }

        public PlotShape WithBox(BlockBox box)
        {
            return new PlotShape(boxes.Append(box));
        }

        public override string ToString() => string.Join(" + ", boxes);
    }
}
