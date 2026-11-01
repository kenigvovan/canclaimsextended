#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace canclaimsextended.src.geometry
{
    /// <summary>
    /// Spatial index of plots by their boxes, bucketed into 32x32x32 cells. Thread-safe: lookups come from
    /// fire/liquid patches off the main thread too. Plots are compared by reference.
    /// Overlap between different plots is not checked here, callers query before adding.
    /// </summary>
    public sealed class PlotIndex<TPlot> where TPlot : class
    {
        public const int CELL_SHIFT = 5;

        private readonly Dictionary<long, List<Entry>> cells = new();
        private readonly Dictionary<TPlot, PlotShape> shapes = new(ReferenceEqualityComparer.Instance);
        private readonly ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.NoRecursion);

        private readonly struct Entry
        {
            public readonly BlockBox Box;
            public readonly TPlot Plot;

            public Entry(BlockBox box, TPlot plot)
            {
                Box = box;
                Plot = plot;
            }
        }

        public void Add(TPlot plot, PlotShape shape)
        {
            rwLock.EnterWriteLock();
            try
            {
                if (shapes.ContainsKey(plot))
                {
                    throw new InvalidOperationException("Plot is already in the index");
                }
                AddUnlocked(plot, shape);
            }
            finally { rwLock.ExitWriteLock(); }
        }

        public bool Remove(TPlot plot)
        {
            rwLock.EnterWriteLock();
            try { return RemoveUnlocked(plot); }
            finally { rwLock.ExitWriteLock(); }
        }

        /// <summary>Swaps the plot's shape in one step, so readers never see it missing.</summary>
        public void Reshape(TPlot plot, PlotShape newShape)
        {
            rwLock.EnterWriteLock();
            try
            {
                if (!RemoveUnlocked(plot))
                {
                    throw new InvalidOperationException("Plot is not in the index");
                }
                AddUnlocked(plot, newShape);
            }
            finally { rwLock.ExitWriteLock(); }
        }

        public bool TryFind(int x, int y, int z, [MaybeNullWhen(false)] out TPlot plot)
        {
            rwLock.EnterReadLock();
            try
            {
                if (cells.TryGetValue(CellKey(x >> CELL_SHIFT, y >> CELL_SHIFT, z >> CELL_SHIFT), out var list))
                {
                    foreach (Entry e in list)
                    {
                        if (e.Box.Contains(x, y, z))
                        {
                            plot = e.Plot;
                            return true;
                        }
                    }
                }
                plot = null;
                return false;
            }
            finally { rwLock.ExitReadLock(); }
        }

        /// <summary>Plots that share at least one block with the box.</summary>
        public List<TPlot> Query(BlockBox box)
        {
            rwLock.EnterReadLock();
            try { return QueryUnlocked(box, null); }
            finally { rwLock.ExitReadLock(); }
        }

        public void Clear()
        {
            rwLock.EnterWriteLock();
            try
            {
                cells.Clear();
                shapes.Clear();
            }
            finally { rwLock.ExitWriteLock(); }
        }

        /// <summary>Plots already in seen are skipped; the ones found are added to it.</summary>
        private List<TPlot> QueryUnlocked(BlockBox box, HashSet<TPlot>? seen)
        {
            seen ??= new HashSet<TPlot>(ReferenceEqualityComparer.Instance);
            var result = new List<TPlot>();
            if (box.IsEmpty)
            {
                return result;
            }
            ForEachCell(box, key =>
            {
                if (!cells.TryGetValue(key, out var list))
                {
                    return;
                }
                foreach (Entry e in list)
                {
                    if (e.Box.Intersects(box) && seen.Add(e.Plot))
                    {
                        result.Add(e.Plot);
                    }
                }
            });
            return result;
        }

        private void AddUnlocked(TPlot plot, PlotShape shape)
        {
            shapes[plot] = shape;
            foreach (BlockBox b in shape.Boxes)
            {
                var entry = new Entry(b, plot);
                ForEachCell(b, key =>
                {
                    if (!cells.TryGetValue(key, out var list))
                    {
                        list = new List<Entry>(1);
                        cells[key] = list;
                    }
                    list.Add(entry);
                });
            }
        }

        private bool RemoveUnlocked(TPlot plot)
        {
            if (!shapes.Remove(plot, out PlotShape? shape))
            {
                return false;
            }
            foreach (BlockBox b in shape.Boxes)
            {
                ForEachCell(b, key =>
                {
                    if (!cells.TryGetValue(key, out var list))
                    {
                        return;
                    }
                    list.RemoveAll(e => ReferenceEquals(e.Plot, plot));
                    if (list.Count == 0)
                    {
                        cells.Remove(key);
                    }
                });
            }
            return true;
        }

        private static void ForEachCell(BlockBox box, Action<long> action)
        {
            // Max is exclusive, so the last block is Max - 1.
            int cx0 = box.MinX >> CELL_SHIFT, cx1 = (box.MaxX - 1) >> CELL_SHIFT;
            int cy0 = box.MinY >> CELL_SHIFT, cy1 = (box.MaxY - 1) >> CELL_SHIFT;
            int cz0 = box.MinZ >> CELL_SHIFT, cz1 = (box.MaxZ - 1) >> CELL_SHIFT;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        action(CellKey(cx, cy, cz));
                    }
                }
            }
        }

        /// <summary>21 bits for X and Z cells (±33M blocks), 22 bits for Y.</summary>
        private static long CellKey(int cx, int cy, int cz)
        {
            return ((long)(cx & 0x1FFFFF) << 43) | ((long)(cz & 0x1FFFFF) << 22) | (long)(cy & 0x3FFFFF);
        }
    }
}
