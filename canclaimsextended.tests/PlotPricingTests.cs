using canclaimsextended.src;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    // Prices by volume. Cases: TESTING_canclaimsextended.md E1, E5, E6, D2 (selection_price), T5.
    // PlotPricing reads the static config.
    [Collection("ModStatics")]
    public class PlotPricingTests : IDisposable
    {
        private const double PricePerUnit = 1.25;
        private readonly Config previousConfig = Mod.config;

        public PlotPricingTests()
        {
            Mod.config = new Config { BLOCKS_PER_COST_UNIT = 1024, PLOT_CLAIM_PRICE_PER_UNIT = PricePerUnit };
        }

        public void Dispose()
        {
            Mod.config = previousConfig;
        }

        // E1: ceil(volume / BLOCKS_PER_COST_UNIT), and never less than one unit.
        [Theory]
        [InlineData(0L, 1L)]
        [InlineData(1L, 1L)]
        [InlineData(1024L, 1L)]
        [InlineData(1025L, 2L)]
        [InlineData(2048L, 2L)]
        [InlineData(2049L, 3L)]
        public void Units_Should_RoundUp_When_VolumeIsNotAWholeUnit(long volume, long expected)
        {
            Assert.Equal(expected, PlotPricing.Units(volume));
        }

        // A unit of 0 blocks (a hand-edited config) must not divide by zero.
        [Fact]
        public void Units_Should_CountEveryBlock_When_BlocksPerUnitIsZero()
        {
            Mod.config.BLOCKS_PER_COST_UNIT = 0;

            Assert.Equal(7L, PlotPricing.Units(7));
        }

        // E1: an 8x8x8 box is 512 blocks, one unit.
        [Fact]
        public void ClaimCost_Should_BeUnitsTimesPrice_When_BoxIsSmall()
        {
            Assert.Equal(PricePerUnit, PlotPricing.ClaimCost(new BlockBox(0, 0, 0, 8, 8, 8)), 6);
        }

        // E1: 16x16x16 = 4096 blocks = 4 units.
        [Fact]
        public void ClaimCost_Should_CountWholeUnits_When_BoxSpansSeveral()
        {
            Assert.Equal(4 * PricePerUnit, PlotPricing.ClaimCost(new BlockBox(0, 0, 0, 16, 16, 16)), 6);
        }

        // E6: growing costs, shrinking gives back, keeping the volume costs nothing.
        [Theory]
        [InlineData(1024L, 2048L, 1.25)]
        [InlineData(2048L, 1024L, -1.25)]
        [InlineData(1500L, 1500L, 0.0)]
        [InlineData(1025L, 2048L, 0.0)] // both two units
        public void ResizeCost_Should_HaveTheSignOfTheChange(long before, long after, double expected)
        {
            Assert.Equal(expected, PlotPricing.ResizeCost(before, after), 6);
        }

        // E5, E6: growing in many small steps costs the same as one big step.
        [Fact]
        public void ResizeCost_Should_NotDependOnSteps_When_GrowingInSmallSteps()
        {
            double stepped = 0;
            for (long v = 1000; v < 5000; v += 100)
            {
                stepped += PlotPricing.ResizeCost(v, v + 100);
            }

            Assert.Equal(PlotPricing.ResizeCost(1000, 5000), stepped, 6);
        }

        // E6: a grow and shrink round, in any steps, earns nothing.
        [Fact]
        public void ResizeCost_Should_SumToZero_When_PlotGrowsAndShrinksBack()
        {
            long[] volumes = { 1000, 1030, 1100, 2047, 2049, 1500, 1023, 1000 };
            double total = 0;
            for (int i = 1; i < volumes.Length; i++)
            {
                total += PlotPricing.ResizeCost(volumes[i - 1], volumes[i]);
            }

            Assert.Equal(0.0, total, 6);
        }

        // E6: a shrink never gives back more than the grow cost.
        [Fact]
        public void ResizeCost_Should_NotPayOut_When_ShrinkingInsideOneUnit()
        {
            Assert.Equal(0.0, PlotPricing.ResizeCost(1100, 1025), 6);
        }
    }
}
