using canclaimsextended.src;
using canclaimsextended.src.geometry;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    // Boxes, plot shapes and territory chunks. Cases: TESTING_canclaimsextended.md C1, C2, C5, C6,
    // E2, E3, E4, E5, E6, E7. ChunkPos reads CLAIM_CHUNK_SIZE from the static config.
    [Collection("ModStatics")]
    public class GeometryTests : IDisposable
    {
        private readonly Config previousConfig = Mod.config;

        public GeometryTests()
        {
            Mod.config = new Config { CLAIM_CHUNK_SIZE = 16 };
        }

        public void Dispose()
        {
            Mod.config = previousConfig;
        }

        private static BlockBox Cube(int min, int max) => new BlockBox(min, min, min, max, max, max);

        // ---- BlockBox ------------------------------------------------------------------------

        // E3: max is exclusive.
        [Theory]
        [InlineData(0, 0, 0, true)]
        [InlineData(9, 9, 9, true)]
        [InlineData(10, 0, 0, false)]
        [InlineData(0, 10, 0, false)]
        [InlineData(0, 0, 10, false)]
        [InlineData(-1, 0, 0, false)]
        public void Contains_Should_BeHalfOpen_When_BlockIsOnTheEdge(int x, int y, int z, bool expected)
        {
            Assert.Equal(expected, Cube(0, 10).Contains(x, y, z));
        }

        // E3: an entity is in the block it stands in, negative coordinates floor down.
        [Theory]
        [InlineData(9.99, 0.5, 0.5, true)]
        [InlineData(-0.5, 0.5, 0.5, false)]
        [InlineData(10.0, 0.5, 0.5, false)]
        public void Contains_Should_FloorThePosition_When_PositionIsFractional(double x, double y, double z, bool expected)
        {
            Assert.Equal(expected, Cube(0, 10).Contains(x, y, z));
        }

        [Fact]
        public void Ctor_Should_Throw_When_MaxIsBelowMin()
        {
            Assert.Throws<ArgumentException>(() => new BlockBox(5, 0, 0, 4, 1, 1));
        }

        [Fact]
        public void IsEmpty_Should_BeTrue_When_OneSideIsZero()
        {
            var flat = new BlockBox(0, 0, 0, 10, 0, 10);

            Assert.True(flat.IsEmpty);
            Assert.Equal(0, flat.Volume);
        }

        // E3: plot A up to x = 10 and plot B from x = 10 do not overlap.
        [Fact]
        public void Intersects_Should_BeFalse_When_BoxesOnlyShareAFace()
        {
            var a = new BlockBox(0, 0, 0, 10, 10, 10);
            var b = new BlockBox(10, 0, 0, 20, 10, 10);

            Assert.False(a.Intersects(b));
            Assert.False(b.Intersects(a));
        }

        // E2
        [Fact]
        public void Intersects_Should_BeTrue_When_BoxesShareABlock()
        {
            Assert.True(Cube(0, 10).Intersects(Cube(9, 20)));
        }

        // E5: a face of non-zero area counts.
        [Fact]
        public void Touches_Should_BeTrue_When_BoxesShareAFace()
        {
            var a = new BlockBox(0, 0, 0, 10, 10, 10);

            Assert.True(a.Touches(new BlockBox(10, 0, 0, 20, 10, 10)));   // east
            Assert.True(a.Touches(new BlockBox(0, 10, 0, 10, 20, 10)));   // above
            Assert.True(a.Touches(new BlockBox(0, 0, -5, 10, 10, 0)));    // north
            Assert.True(a.Touches(new BlockBox(5, 0, 10, 6, 1, 11)));     // a small part of the face
        }

        // E5: an edge or a corner is not a face.
        [Fact]
        public void Touches_Should_BeFalse_When_BoxesShareOnlyAnEdgeOrACorner()
        {
            var a = new BlockBox(0, 0, 0, 10, 10, 10);

            Assert.False(a.Touches(new BlockBox(10, 10, 0, 20, 20, 10)));  // edge
            Assert.False(a.Touches(new BlockBox(10, 10, 10, 20, 20, 20))); // corner
        }

        [Fact]
        public void Touches_Should_BeFalse_When_BoxesIntersectOrHaveAGap()
        {
            var a = new BlockBox(0, 0, 0, 10, 10, 10);

            Assert.False(a.Touches(new BlockBox(5, 0, 0, 15, 10, 10)));
            Assert.False(a.Touches(new BlockBox(11, 0, 0, 20, 10, 10)));
        }

        // E7: the pieces left after a cut cover exactly the rest of the box.
        [Fact]
        public void Subtract_Should_LeaveSixDisjointPiecesOfTheRightVolume_When_CutIsInTheMiddle()
        {
            BlockBox box = Cube(0, 10);
            BlockBox cut = Cube(4, 6);

            List<BlockBox> pieces = box.Subtract(cut);

            Assert.Equal(6, pieces.Count);
            Assert.Equal(box.Volume - cut.Volume, pieces.Sum(p => p.Volume));
            Assert.All(pieces, p => Assert.False(p.Intersects(cut), $"{p} overlaps the cut"));
            Assert.All(pieces, p => Assert.True(box.Contains(p), $"{p} is outside the box"));
            for (int i = 0; i < pieces.Count; i++)
                for (int j = 0; j < i; j++)
                    Assert.False(pieces[i].Intersects(pieces[j]), $"{pieces[i]} overlaps {pieces[j]}");
        }

        [Fact]
        public void Subtract_Should_ReturnTheBox_When_CutMissesIt()
        {
            BlockBox box = Cube(0, 10);

            Assert.Equal(new[] { box }, box.Subtract(Cube(20, 30)));
        }

        [Fact]
        public void Subtract_Should_ReturnNothing_When_CutCoversTheBox()
        {
            Assert.Empty(Cube(2, 4).Subtract(Cube(0, 10)));
        }

        // D2, E1: both corner blocks are included, in any order.
        [Fact]
        public void FromCorners_Should_IncludeBothCorners_When_GivenInAnyOrder()
        {
            BlockBox box = BlockBox.FromCorners(5, 5, 5, 2, 3, 4);

            Assert.Equal(new BlockBox(2, 3, 4, 6, 6, 6), box);
            Assert.True(box.Contains(5, 5, 5));
            Assert.True(box.Contains(2, 3, 4));
        }

        [Fact]
        public void FromCorners_Should_BeOneBlock_When_CornersAreTheSame()
        {
            Assert.Equal(1, BlockBox.FromCorners(-3, 7, 9, -3, 7, 9).Volume);
        }

        [Theory]
        [InlineData(10, 0, 0, 0.0)] // touching
        [InlineData(5, 0, 0, 0.0)]  // overlapping
        [InlineData(13, 0, 0, 3.0)] // gap of 3 along x
        [InlineData(13, 14, 0, 5.0)] // gaps of 3 and 4
        public void DistanceTo_Should_BeTheGapBetweenTheBoxes(int minX, int minY, int minZ, double expected)
        {
            BlockBox a = Cube(0, 10);
            var b = new BlockBox(minX, minY, minZ, minX + 10, minY + 10, minZ + 10);

            Assert.Equal(expected, a.DistanceTo(b), 6);
            Assert.Equal(expected, b.DistanceTo(a), 6);
        }

        // ---- PlotShape -----------------------------------------------------------------------

        [Fact]
        public void PlotShape_Should_Throw_When_ThereAreNoBoxes()
        {
            Assert.Throws<ArgumentException>(() => new PlotShape(Array.Empty<BlockBox>()));
        }

        // E2, Y2: two boxes of one plot never overlap.
        [Fact]
        public void PlotShape_Should_Throw_When_BoxesOverlap()
        {
            Assert.Throws<ArgumentException>(() => new PlotShape(new[] { Cube(0, 10), Cube(5, 15) }));
        }

        [Fact]
        public void PlotShape_Should_Throw_When_ABoxIsEmpty()
        {
            Assert.Throws<ArgumentException>(() => new PlotShape(new BlockBox(0, 0, 0, 10, 0, 10)));
        }

        // E4, E5: an L of two boxes; the corner its bounds cover is not in the plot.
        [Fact]
        public void PlotShape_Contains_Should_CheckEveryBox_When_ShapeIsNotABox()
        {
            var shape = new PlotShape(new[] { new BlockBox(0, 0, 0, 10, 10, 10), new BlockBox(10, 0, 0, 20, 10, 5) });

            Assert.True(shape.Contains(5, 5, 5));
            Assert.True(shape.Contains(15, 5, 2));
            Assert.False(shape.Contains(15, 5, 7));   // inside the bounds, outside both boxes
            Assert.False(shape.Contains(20, 5, 2));   // max is exclusive
            Assert.Equal(1000 + 500, shape.Volume);
        }

        [Fact]
        public void PlotShape_Touches_Should_BeTrue_When_TwoShapesShareAFace()
        {
            var a = new PlotShape(Cube(0, 10));
            var b = new PlotShape(new BlockBox(10, 0, 0, 20, 10, 10));

            Assert.True(a.Touches(b));
            Assert.False(a.Intersects(b));
        }

        [Fact]
        public void PlotShape_Touches_Should_BeFalse_When_ShapesIntersectOrMeetAtAnEdge()
        {
            var a = new PlotShape(Cube(0, 10));

            Assert.False(a.Touches(new PlotShape(Cube(5, 15))));
            Assert.False(a.Touches(new PlotShape(new BlockBox(10, 10, 0, 20, 20, 10))));
        }

        // E5, E6, E7: plot_cut_splits / PLOT_PARTS_MUST_TOUCH.
        [Fact]
        public void IsConnected_Should_BeTrue_When_ShapeIsOneBox()
        {
            Assert.True(new PlotShape(Cube(0, 4)).IsConnected());
        }

        [Fact]
        public void IsConnected_Should_BeTrue_When_BoxesFormAChainOfFaces()
        {
            var shape = new PlotShape(new[]
            {
                new BlockBox(0, 0, 0, 4, 4, 4),
                new BlockBox(4, 0, 0, 8, 4, 4),
                new BlockBox(8, 0, 0, 12, 4, 4)
            });

            Assert.True(shape.IsConnected());
        }

        [Fact]
        public void IsConnected_Should_BeFalse_When_BoxesAreApartOrMeetOnlyAtAnEdge()
        {
            Assert.False(new PlotShape(new[] { Cube(0, 4), new BlockBox(10, 0, 0, 14, 4, 4) }).IsConnected());
            Assert.False(new PlotShape(new[] { Cube(0, 4), new BlockBox(4, 4, 0, 8, 8, 4) }).IsConnected());
        }

        // E5: extend makes a new shape; the old one stays as it was.
        [Fact]
        public void WithBox_Should_ReturnANewShapeWithTheBox_When_BoxFits()
        {
            var shape = new PlotShape(Cube(0, 4));

            PlotShape extended = shape.WithBox(new BlockBox(4, 0, 0, 8, 4, 4));

            Assert.Single(shape.Boxes);
            Assert.Equal(2, extended.Boxes.Count);
            Assert.Equal(new BlockBox(0, 0, 0, 8, 4, 4), extended.Bounds);
        }

        [Fact]
        public void WithBox_Should_Throw_When_BoxOverlapsTheShape()
        {
            var shape = new PlotShape(Cube(0, 4));

            Assert.Throws<ArgumentException>(() => shape.WithBox(Cube(2, 6)));
        }

        // ---- ChunkPos ------------------------------------------------------------------------

        // C1, C2: floor division, -1 is in chunk -1.
        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(15, 15, 0, 0)]
        [InlineData(16, -1, 1, -1)]
        [InlineData(-1, -16, -1, -1)]
        [InlineData(-16, -17, -1, -2)]
        public void FromBlock_Should_FloorDivide_When_Size16(int x, int z, int cx, int cz)
        {
            Assert.Equal(new ChunkPos(cx, cz), ChunkPos.FromBlock(x, z));
        }

        // C6
        [Theory]
        [InlineData(-33, 31, -2, 0)]
        [InlineData(32, -32, 1, -1)]
        public void FromBlock_Should_UseTheConfiguredSize_When_Size32(int x, int z, int cx, int cz)
        {
            Mod.config.CLAIM_CHUNK_SIZE = 32;

            Assert.Equal(new ChunkPos(cx, cz), ChunkPos.FromBlock(x, z));
        }

        [Fact]
        public void FromBlock_Should_FloorThePosition_When_PositionIsFractional()
        {
            Assert.Equal(new ChunkPos(-1, 0), ChunkPos.FromBlock(-0.5, 15.9));
        }

        // Y1: the key of the chunk index keeps both signs.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, -1)]
        [InlineData(5, -7)]
        [InlineData(-7, 5)]
        [InlineData(int.MaxValue, int.MinValue)]
        [InlineData(int.MinValue, int.MaxValue)]
        public void Key_Should_RoundTrip_When_CoordinatesHaveAnySign(int x, int z)
        {
            var pos = new ChunkPos(x, z);

            Assert.Equal(pos, ChunkPos.FromKey(pos.Key));
        }

        [Fact]
        public void Key_Should_Differ_When_CoordinatesAreSwapped()
        {
            Assert.NotEqual(new ChunkPos(1, -1).Key, new ChunkPos(-1, 1).Key);
        }

        // C2, C5: edges between chunks, neighbours (diagonal too) are 0 apart.
        [Theory]
        [InlineData(1, 0, 0.0)]
        [InlineData(1, 1, 0.0)]
        [InlineData(3, 0, 2.0)]
        [InlineData(-3, 0, 2.0)]
        [InlineData(4, 5, 5.0)]
        public void DistanceTo_Should_CountChunksBetweenTheEdges(int x, int z, double expected)
        {
            Assert.Equal(expected, new ChunkPos(0, 0).DistanceTo(new ChunkPos(x, z)), 6);
        }

        [Fact]
        public void Column_Should_SpanTheChunkOverTheWorldHeight()
        {
            Assert.Equal(new BlockBox(-16, 0, 32, 0, 256, 48), new ChunkPos(-1, 2).Column(256));
        }
    }
}
