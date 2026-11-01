using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.blocks;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The temple block and the one point a temple plot remembers (J7, Z9): breaking the registered
/// block hands the point to another temple block left on the plot, and only the last one clears
/// it. And J1: outside a temple plot a survival player cannot place the block.
///
/// The block hooks are called directly (as the world calls them on a place or a break): a test
/// player cannot be made to hold and click a block.
/// </summary>
[Trait("Category", "E2E")]
public class TempleScenarios : CxScenarioBase
{
    private const string TempleCode = "canclaimsextended:cantempleblock";
    // One of the vanilla gravestone shapes the block's "types" list offers.
    private const string TempleType = "gravestone-1";

    /// <summary>A plot of the city made a temple by the mayor, who stands in it.</summary>
    private async Task<Plot> GivenTemplePlot(CityScene s)
    {
        Plot plot = await GivenPlot(s);
        CommandResult result = await RunPaid(s.Mayor, $"{PlotCmd} set type temple", () => plot.Type == PlotType.TEMPLE, "the plot to become a temple");
        Assert.True(plot.Desc is PlotDescTemple, "precondition: /plot set type temple made a temple plot: " + result.Message);
        return plot;
    }

    private static Block TempleBlockType()
    {
        Block block = Mod.sapi.World.GetBlock(new AssetLocation(TempleCode));
        Assert.True(block is TempleBlock, $"precondition: {TempleCode} is a TempleBlock, got {block?.GetType().Name ?? "null"}");
        return block!;
    }

    private static ItemStack TempleStack(Block block)
    {
        var stack = new ItemStack(block);
        stack.Attributes.SetString("type", TempleType);
        return stack;
    }

    /// <summary>Puts a temple block at the position and runs its placement hook, as the world does after a placement.</summary>
    private static void Place(Block block, BlockPos pos)
    {
        IWorldAccessor world = Mod.sapi.World;
        world.BlockAccessor.SetBlock(block.BlockId, pos);
        block.OnBlockPlaced(world, pos, TempleStack(block));
        Assert.True(world.BlockAccessor.GetBlock(pos) is TempleBlock, $"precondition: a temple block stands at {pos}");
    }

    /// <summary>Breaks the temple block at the position, without a player (as a block update would).</summary>
    private static void Break(Block block, BlockPos pos)
    {
        IWorldAccessor world = Mod.sapi.World;
        block.OnBlockBroken(world, pos, null);
        Assert.False(world.BlockAccessor.GetBlock(pos) is TempleBlock, $"precondition: the temple block at {pos} is gone");
    }

    private static BlockPos? PointOf(Plot plot) => ((PlotDescTemple)plot.Desc).Point;

    /// <summary>Two blocks inside the plot box, at the player's feet height, apart from where the mayor stands.</summary>
    private (BlockPos A, BlockPos B) TwoSpots(CityScene s)
    {
        BlockBox box = PlotBox(s.Area);
        BlockPos middle = Middle(box);
        return (new BlockPos(box.MinX + 1, middle.Y, box.MinZ + 1), new BlockPos(box.MaxX - 2, middle.Y, box.MaxZ - 2));
    }

    // J7: the block placed last is the plot's point
    [AtlasScenario]
    public async Task Point_Should_BeTheLastPlacedBlock_When_TwoBlocksArePlaced()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenTemplePlot(s);
        Block temple = TempleBlockType();
        (BlockPos a, BlockPos b) = TwoSpots(s);

        // Act
        Place(temple, a);
        Place(temple, b);

        // Assert
        Assert.True(b.Equals(PointOf(plot)), $"the point must be B {b}, is {PointOf(plot)?.ToString() ?? "null"}");
        await World.Ticks(1);
    }

    // J7, Z9: breaking the registered block B moves the point to the block A left on the plot;
    // breaking the last one clears it
    [AtlasScenario]
    public async Task Point_Should_MoveToTheRemainingBlock_When_RegisteredBlockIsBroken()
    {
        // Arrange: A, then B - the point is B
        CityScene s = await GivenCity();
        Plot plot = await GivenTemplePlot(s);
        Block temple = TempleBlockType();
        (BlockPos a, BlockPos b) = TwoSpots(s);
        Place(temple, a);
        Place(temple, b);
        Assert.True(b.Equals(PointOf(plot)), $"precondition: the point is B {b}, is {PointOf(plot)?.ToString() ?? "null"}");

        // Act 1: break B
        Break(temple, b);

        // Assert 1
        Assert.True(a.Equals(PointOf(plot)), $"after B was broken the point must be A {a}, is {PointOf(plot)?.ToString() ?? "null"}");

        // Act 2: break A, the last one
        Break(temple, a);

        // Assert 2
        Assert.True(PointOf(plot) == null, $"with no temple block left the plot still has the point {PointOf(plot)}");
        AssertInvariants();
    }

    // J7: breaking a block that is not the registered one leaves the point alone
    [AtlasScenario]
    public async Task Point_Should_StayOnB_When_UnregisteredBlockAIsBroken()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenTemplePlot(s);
        Block temple = TempleBlockType();
        (BlockPos a, BlockPos b) = TwoSpots(s);
        Place(temple, a);
        Place(temple, b);

        // Act
        Break(temple, a);

        // Assert
        Assert.True(b.Equals(PointOf(plot)), $"breaking A moved the point away from B {b} to {PointOf(plot)?.ToString() ?? "null"}");
        await World.Ticks(1);
    }

    // J1: in survival the block is refused on a plot that is not a temple
    [AtlasScenario]
    public async Task TryPlaceBlock_Should_Refuse_When_PlotIsNoTemple()
    {
        // Arrange: a plain plot of the mayor's own city, the mayor in survival
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        Assert.True(plot.Type != PlotType.TEMPLE, "precondition: a new plot is not a temple");
        IWorldPlayerData data = s.Mayor.Player.WorldData;
        EnumGameMode modeBefore = data.CurrentGameMode;
        data.CurrentGameMode = EnumGameMode.Survival;
        AddCleanup(() => data.CurrentGameMode = modeBefore);
        Block temple = TempleBlockType();
        (BlockPos a, _) = TwoSpots(s);
        var sel = new BlockSelection { Position = a, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        string failureCode = "";

        // Act
        bool placed = temple.TryPlaceBlock(Mod.sapi.World, s.Mayor.Player, TempleStack(temple), sel, ref failureCode);

        // Assert
        Assert.False(placed, "a temple block was placed on a plot that is not a temple");
        Assert.True(failureCode == "__ignore__", "unexpected failure code: " + failureCode);
        Assert.False(Mod.sapi.World.BlockAccessor.GetBlock(a) is TempleBlock, "a temple block stands on a plain plot");
        await World.Ticks(1);
    }
}
