using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Plots as boxes inside the territory: create and its gates (E1, E2), half-open faces (E3),
/// plots stacked in one column (E4), extend (E5), grow and shrink (E6), cut (E7), delete (E8) and
/// the refund (E9). Section E of TESTING_canclaimsextended.md.
///
/// The founding chunk is 16 wide; PlotBox slots 0 and 1 take x 1..7 and 9..15 of it on the same
/// rows of z, so they are two blocks apart.
/// </summary>
[Trait("Category", "E2E")]
public class PlotShapeScenarios : CxScenarioBase
{
    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        int minDim = c.PLOT_MIN_DIM, maxHeight = c.PLOT_MAX_HEIGHT, maxBoxes = c.PLOT_MAX_BOXES;
        long maxVolume = c.PLOT_MAX_VOLUME, minAge = c.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS;
        bool mustTouch = c.PLOT_PARTS_MUST_TOUCH;
        double refundPercent = c.PLOT_UNCLAIM_REFUND_PERCENT, pricePerUnit = c.PLOT_CLAIM_PRICE_PER_UNIT;
        AddCleanup(() =>
        {
            c.PLOT_MIN_DIM = minDim;
            c.PLOT_MAX_HEIGHT = maxHeight;
            c.PLOT_MAX_BOXES = maxBoxes;
            c.PLOT_MAX_VOLUME = maxVolume;
            c.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS = minAge;
            c.PLOT_PARTS_MUST_TOUCH = mustTouch;
            c.PLOT_UNCLAIM_REFUND_PERCENT = refundPercent;
            c.PLOT_CLAIM_PRICE_PER_UNIT = pricePerUnit;
        });
        // The defaults of Config, so a host's own file does not change what the boxes below mean.
        c.PLOT_MIN_DIM = 4;
        c.PLOT_MAX_HEIGHT = 128;
        c.PLOT_MAX_VOLUME = 64 * 64 * 64;
        c.PLOT_MAX_BOXES = 8;
    }

    /// <summary>The real plot holding the block, null when there is none.</summary>
    private static Plot? PlotAtBlock(int x, int y, int z) =>
        Mod.dataStorage.GetSubPlotAt(new BlockPos(x, y, z), out Plot plot) ? plot : null;

    /// <summary>A box in the area's founding chunk, in blocks from the chunk's corner and the origin's height.</summary>
    private BlockBox BoxInChunk(int area, int x0, int y0, int z0, int x1, int y1, int z1)
    {
        ChunkPos chunk = AreaChunk(area);
        int y = AreaOrigin(area).Y;
        return new BlockBox(chunk.MinBlockX + x0, y + y0, chunk.MinBlockZ + z0, chunk.MinBlockX + x1, y + y1, chunk.MinBlockZ + z1);
    }

    /// <summary>The mayor selects the box and runs /plot create; free and at once without an economy.</summary>
    private static async Task<CommandResult> Create(CityScene s, BlockBox box)
    {
        Select(s.Mayor, box);
        return await s.Mayor.ExecuteCommand($"{PlotCmd} create");
    }

    /// <summary>The mayor, standing in the plot, adds the box to it with /plot extend.</summary>
    private static async Task<CommandResult> Extend(CityScene s, BlockBox box)
    {
        Select(s.Mayor, box);
        return await s.Mayor.ExecuteCommand($"{PlotCmd} extend");
    }

    /// <summary>A plot of slots 0 and 1: two boxes two blocks apart. The mayor stands in slot 0.</summary>
    private async Task<Plot> GivenTwoBoxPlot(CityScene s)
    {
        Plot plot = await GivenPlot(s);
        CommandResult extend = await Extend(s, PlotBox(s.Area, 1));
        Assert.True(plot.Shape.Boxes.Count == 2 && PlotIn(PlotBox(s.Area, 1)) == plot,
            "precondition: /plot extend added slot 1 to the plot: " + extend.Message);
        return plot;
    }

    // ---- E1: create ------------------------------------------------------------------------

    // E1: the selection becomes a plot of one box, listed by the city
    [AtlasScenario]
    public async Task Create_Should_MakeOneBoxPlot_When_SelectionIsInsideTerritory()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockBox box = PlotBox(s.Area);

        // Act
        CommandResult result = await Create(s, box);

        // Assert
        Assert.True(result.Ok, "/plot create failed: " + result.Message);
        Plot? plot = PlotIn(box);
        Assert.NotNull(plot);
        Assert.True(s.City.Equals(plot!.getCity()), "the plot does not point to the city");
        Assert.Contains(plot, s.City.getCityPlots());
        Assert.Single(plot.Shape.Boxes);
        Assert.Equal(box, plot.Shape.Boxes[0]);
        Assert.Equal(box.Volume, plot.Volume);
        AssertInvariants();
    }

    // ---- E2: create gates ------------------------------------------------------------------

    // E2: a side under PLOT_MIN_DIM is refused
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_BoxIsTooThin()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockBox thin = BoxInChunk(s.Area, 1, -2, 1, 1 + Mod.config.PLOT_MIN_DIM - 1, 6, 7);

        // Act
        CommandResult result = await Create(s, thin);

        // Assert
        Assert.False(result.Ok, "a box thinner than PLOT_MIN_DIM became a plot: " + result.Message);
        Assert.Null(PlotAtBlock(thin.MinX, thin.MinY, thin.MinZ));
        Assert.Empty(s.City.getCityPlots());
        AssertInvariants();
    }

    // E2: a box higher than PLOT_MAX_HEIGHT is refused
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_BoxIsTooHigh()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockBox box = PlotBox(s.Area);
        Mod.config.PLOT_MAX_HEIGHT = box.SizeY - 1;

        // Act
        CommandResult result = await Create(s, box);

        // Assert
        Assert.False(result.Ok, "a box higher than PLOT_MAX_HEIGHT became a plot: " + result.Message);
        Assert.Null(PlotIn(box));
        Assert.Empty(s.City.getCityPlots());
        AssertInvariants();
    }

    // E2: a box of more than PLOT_MAX_VOLUME blocks is refused
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_BoxIsTooBig()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockBox box = PlotBox(s.Area);
        Mod.config.PLOT_MAX_VOLUME = box.Volume - 1;

        // Act
        CommandResult result = await Create(s, box);

        // Assert
        Assert.False(result.Ok, "a box over PLOT_MAX_VOLUME became a plot: " + result.Message);
        Assert.Null(PlotIn(box));
        Assert.Empty(s.City.getCityPlots());
        AssertInvariants();
    }

    // E2: a box sharing blocks with another plot is refused
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_BoxOverlapsAnotherPlot()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot first = await GivenPlot(s);
        BlockBox slot0 = PlotBox(s.Area);
        // Shifted two blocks east: half of it lies in the first plot.
        BlockBox overlapping = new(slot0.MinX + 2, slot0.MinY, slot0.MinZ, slot0.MaxX + 2, slot0.MaxY, slot0.MaxZ);

        // Act
        CommandResult result = await Create(s, overlapping);

        // Assert
        Assert.False(result.Ok, "a box overlapping a plot became a plot: " + result.Message);
        Assert.Single(s.City.getCityPlots());
        Assert.True(PlotAtBlock(overlapping.MaxX - 1, overlapping.MinY, overlapping.MinZ) == null,
            "the part of the box outside the first plot was claimed");
        Assert.Equal(slot0, Assert.Single(first.Shape.Boxes));
        AssertInvariants();
    }

    // E2: a box with any block outside the city's chunks is refused
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_BoxIsPartlyOutsideTerritory()
    {
        // Arrange
        CityScene s = await GivenCity();
        int size = ChunkPos.Size;
        // From x 12 of the founding chunk to x 4 of the next one, which the city does not hold.
        BlockBox crossing = BoxInChunk(s.Area, size - 4, -2, 1, size + 4, 6, 7);

        // Act
        CommandResult result = await Create(s, crossing);

        // Assert
        Assert.False(result.Ok, "a box reaching outside the territory became a plot: " + result.Message);
        Assert.Null(PlotAtBlock(crossing.MinX, crossing.MinY, crossing.MinZ));
        Assert.Empty(s.City.getCityPlots());
        AssertInvariants();
    }

    // E2: the plot limit of the city's level (plus bonus) is not passed
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_CityHasMaxPlots()
    {
        // Arrange
        CityScene s = await GivenCity();
        await GivenPlot(s, 0);
        // A bonus that brings the limit down to the one plot the city has.
        int levelPlots = Settings.GetCityLevelInfo(s.City.getCityCitizens().Count).AmountOfPlots;
        s.City.setBonusPlots(1 - levelPlots);
        Assert.Equal(1, Settings.GetMaxNumberOfPlotForCity(s.City));
        BlockBox second = PlotBox(s.Area, 1);

        // Act
        CommandResult result = await Create(s, second);

        // Assert
        Assert.False(result.Ok, "a plot over the city's limit was made: " + result.Message);
        Assert.Null(PlotIn(second));
        Assert.Single(s.City.getCityPlots());
        AssertInvariants();
    }

    // ---- E3, E4: faces and columns ---------------------------------------------------------

    // E3: boxes are half-open - plot A ending at x = M and plot B starting at x = M do not overlap,
    // and the block at M - 1 is A's while the one at M is B's
    [AtlasScenario]
    public async Task Create_Should_AllowBothPlots_When_TheyShareOnlyAFace()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockBox west = BoxInChunk(s.Area, 1, -2, 1, 7, 6, 7);
        BlockBox east = BoxInChunk(s.Area, 7, -2, 1, 13, 6, 7);
        Assert.Equal(west.MaxX, east.MinX);

        // Act
        CommandResult first = await Create(s, west);
        CommandResult second = await Create(s, east);

        // Assert
        Assert.True(first.Ok, "the west plot was not created: " + first.Message);
        Assert.True(second.Ok, "the east plot, sharing only the face x = " + west.MaxX + ", was refused: " + second.Message);
        Plot? a = PlotAtBlock(west.MaxX - 1, west.MinY, west.MinZ);
        Plot? b = PlotAtBlock(east.MinX, east.MinY, east.MinZ);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
        Assert.Equal(west, Assert.Single(a!.Shape.Boxes));
        Assert.Equal(east, Assert.Single(b!.Shape.Boxes));
        Assert.Equal(2, s.City.getCityPlots().Count);
        AssertInvariants();
    }

    // E4: a cellar and a house above it in one column are two plots, told apart by height
    [AtlasScenario]
    public async Task Create_Should_AllowStackedPlots_When_TheyShareAColumn()
    {
        // Arrange
        // No lower than PlotBox reaches (origin - 2): a selection is clipped at y = 0, and the
        // spawn of the test world may sit only a few blocks above it.
        CityScene s = await GivenCity();
        BlockBox cellar = BoxInChunk(s.Area, 1, -2, 1, 7, 4, 7);
        BlockBox house = BoxInChunk(s.Area, 1, 4, 1, 7, 10, 7);

        // Act
        CommandResult first = await Create(s, cellar);
        CommandResult second = await Create(s, house);

        // Assert
        Assert.True(first.Ok, "the cellar was not created: " + first.Message);
        Assert.True(second.Ok, "the house above the cellar was refused: " + second.Message);
        Plot? below = PlotAtBlock(cellar.MinX, cellar.MaxY - 1, cellar.MinZ);
        Plot? above = PlotAtBlock(house.MinX, house.MinY, house.MinZ);
        Assert.NotNull(below);
        Assert.NotNull(above);
        Assert.NotEqual(below, above);
        Assert.Equal(cellar, Assert.Single(below!.Shape.Boxes));
        Assert.Equal(house, Assert.Single(above!.Shape.Boxes));
        AssertInvariants();
    }

    // ---- E5: extend ------------------------------------------------------------------------

    // E5: /plot extend adds the selection as a second box of the plot the mayor stands in
    [AtlasScenario]
    public async Task Extend_Should_AddBox_When_SelectionIsFree()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        long before = plot.Volume;
        BlockBox added = PlotBox(s.Area, 1);

        // Act
        CommandResult result = await Extend(s, added);

        // Assert
        Assert.True(result.Ok, "/plot extend failed: " + result.Message);
        Assert.Equal(2, plot.Shape.Boxes.Count);
        Assert.Contains(added, plot.Shape.Boxes);
        Assert.True(PlotIn(added) == plot, "the new box does not lead to the plot");
        Assert.Equal(before + added.Volume, plot.Volume);
        Assert.Single(s.City.getCityPlots());
        AssertInvariants();
    }

    // E5: no more boxes than PLOT_MAX_BOXES
    [AtlasScenario]
    public async Task Extend_Should_BeRefused_When_PlotHasMaxBoxes()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.config.PLOT_MAX_BOXES = 2;
        Plot plot = await GivenTwoBoxPlot(s);
        BlockBox third = PlotBox(s.Area, 2);

        // Act
        CommandResult result = await Extend(s, third);

        // Assert
        Assert.False(result.Ok, "a box over PLOT_MAX_BOXES was added: " + result.Message);
        Assert.Equal(2, plot.Shape.Boxes.Count);
        Assert.Null(PlotIn(third));
        AssertInvariants();
    }

    // E5: with PLOT_PARTS_MUST_TOUCH a detached box is refused, while one sharing a face is taken
    [AtlasScenario]
    public async Task Extend_Should_BeRefused_When_PartsMustTouchAndBoxIsDetached()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.config.PLOT_PARTS_MUST_TOUCH = true;
        Plot plot = await GivenPlot(s);
        BlockBox slot0 = PlotBox(s.Area);
        BlockBox detached = PlotBox(s.Area, 1);
        // The two blocks between slots 0 and 1: shares slot 0's east face.
        BlockBox touching = new(slot0.MaxX, slot0.MinY, slot0.MinZ, detached.MinX, slot0.MaxY, slot0.MaxZ);

        // Act
        CommandResult refused = await Extend(s, detached);

        // Assert
        Assert.False(refused.Ok, "a box not touching the plot was added: " + refused.Message);
        Assert.Single(plot.Shape.Boxes);
        Assert.Null(PlotIn(detached));

        // Control: the refusal is about touching - a box sharing a face goes in.
        // The gap is two blocks wide, under PLOT_MIN_DIM, so the control lowers the minimum.
        Mod.config.PLOT_MIN_DIM = touching.SizeX;
        CommandResult accepted = await Extend(s, touching);
        Assert.True(accepted.Ok, "a box sharing a face with the plot was refused: " + accepted.Message);
        Assert.Equal(2, plot.Shape.Boxes.Count);
        AssertInvariants();
    }

    // ---- E6: grow, shrink ------------------------------------------------------------------

    // E6: grow and shrink move only the named face of the box the mayor stands in
    [AtlasScenario]
    public async Task GrowAndShrink_Should_MoveOneFace()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        BlockBox before = PlotBox(s.Area);

        // Act
        CommandResult grow = await s.Mayor.ExecuteCommand($"{PlotCmd} grow up 2");
        BlockBox grown = Assert.Single(plot.Shape.Boxes);
        CommandResult shrink = await s.Mayor.ExecuteCommand($"{PlotCmd} shrink east 1");
        BlockBox shrunk = Assert.Single(plot.Shape.Boxes);

        // Assert
        Assert.True(grow.Ok, "/plot grow up 2 failed: " + grow.Message);
        Assert.Equal(new BlockBox(before.MinX, before.MinY, before.MinZ, before.MaxX, before.MaxY + 2, before.MaxZ), grown);
        Assert.True(shrink.Ok, "/plot shrink east 1 failed: " + shrink.Message);
        Assert.Equal(new BlockBox(before.MinX, before.MinY, before.MinZ, before.MaxX - 1, before.MaxY + 2, before.MaxZ), shrunk);
        Assert.Null(PlotAtBlock(before.MaxX - 1, before.MinY, before.MinZ));
        Assert.True(PlotAtBlock(before.MinX, before.MaxY + 1, before.MinZ) == plot, "the grown layer is not the plot's");
        AssertInvariants();
    }

    // E6: a box may not grow into another box of its own plot
    [AtlasScenario]
    public async Task Grow_Should_BeRefused_When_ItOverlapsOwnBox()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenTwoBoxPlot(s);
        BlockBox slot0 = PlotBox(s.Area), slot1 = PlotBox(s.Area, 1);

        // Act: three blocks east reach one block into slot 1.
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} grow east 3");

        // Assert
        Assert.False(result.Ok, "a box grew into another box of its plot: " + result.Message);
        Assert.Equal(2, plot.Shape.Boxes.Count);
        Assert.Contains(slot0, plot.Shape.Boxes);
        Assert.Contains(slot1, plot.Shape.Boxes);
        AssertInvariants();
    }

    // E6: a box may not shrink below PLOT_MIN_DIM
    [AtlasScenario]
    public async Task Shrink_Should_BeRefused_When_BoxWouldBeTooThin()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        BlockBox before = PlotBox(s.Area);
        int by = before.SizeX - Mod.config.PLOT_MIN_DIM + 1;

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} shrink east {by}");

        // Assert
        Assert.False(result.Ok, $"a box shrank to {before.SizeX - by} blocks: " + result.Message);
        Assert.Equal(before, Assert.Single(plot.Shape.Boxes));
        AssertInvariants();
    }

    // ---- E7: cut ---------------------------------------------------------------------------

    // E7: /plot cut n part takes one part off; the last part cannot go
    [AtlasScenario]
    public async Task Cut_Should_RemovePart_ButNotTheLastOne()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenTwoBoxPlot(s);
        BlockBox slot0 = PlotBox(s.Area), slot1 = PlotBox(s.Area, 1);
        int part = plot.Shape.Boxes.ToList().IndexOf(slot1) + 1;

        // Act
        CommandResult cut = await s.Mayor.ExecuteCommand($"{PlotCmd} cut {plot.Number} {part}");
        CommandResult last = await s.Mayor.ExecuteCommand($"{PlotCmd} cut {plot.Number} 1");

        // Assert
        Assert.True(cut.Ok, $"/plot cut {plot.Number} {part} failed: " + cut.Message);
        Assert.False(last.Ok, "the last part of the plot was cut: " + last.Message);
        Assert.Equal(slot0, Assert.Single(plot.Shape.Boxes));
        Assert.Null(PlotIn(slot1));
        Assert.True(PlotIn(slot0) == plot, "the remaining part is no longer the plot's");
        Assert.Contains(plot, s.City.getCityPlots());
        AssertInvariants();
    }

    // ---- E8: delete ------------------------------------------------------------------------

    // E8: a plot without an owner goes back to city land
    [AtlasScenario]
    public async Task Delete_Should_RemovePlot_When_ItHasNoOwner()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} delete");

        // Assert
        Assert.True(result.Ok, "/plot delete failed: " + result.Message);
        Assert.Null(PlotIn(PlotBox(s.Area)));
        Assert.False(Mod.dataStorage.GetPlotByGuid(plot.Guid, out _), "the plot is still known by its guid");
        Assert.DoesNotContain(plot, s.City.getCityPlots());
        Assert.True(s.City.HasChunk(AreaChunk(s.Area)), "the chunk went with the plot");
        AssertInvariants();
    }

    // E8: a plot with an owner has to be taken back first
    [AtlasScenario]
    public async Task Delete_Should_BeRefused_When_PlotHasOwner()
    {
        // Arrange
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await StandIn(s.Mayor, PlotBox(s.Area));

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} delete");

        // Assert
        Assert.False(result.Ok, "a plot with an owner was deleted: " + result.Message);
        Assert.True(PlotIn(PlotBox(s.Area)) == plot, "the plot is gone");
        Assert.True(Info(owner).Equals(plot.getPlotOwner()), "the plot lost its owner");
        AssertInvariants();
    }

    // ---- E9: refund ------------------------------------------------------------------------

    /// <summary>A city with money and a paid plot, refunds at 50 percent after <paramref name="minAge"/> seconds.</summary>
    private async Task<(CityScene Scene, FakeMoneyProvider Money, Plot Plot)> GivenPaidPlot(long minAge)
    {
        CityScene s = await GivenCity();
        FakeMoneyProvider money = UseFakeEconomy();
        var c = Mod.config;
        c.PLOT_CLAIM_PRICE_PER_UNIT = 10;
        c.PLOT_UNCLAIM_REFUND_PERCENT = 50;
        c.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS = minAge;
        const decimal start = 1_000_000m;
        money.SetBalance(s.City.MoneyAccountName, start);
        Plot plot = await GivenPlot(s);
        Assert.True(money.GetBalance(s.City.MoneyAccountName) < start, "precondition: the treasury paid for the plot");
        return (s, money, plot);
    }

    // E9: an old enough plot gives PLOT_UNCLAIM_REFUND_PERCENT of its claim price back to the treasury
    [AtlasScenario]
    public async Task Delete_Should_RefundHalf_When_PlotIsOldEnough()
    {
        // Arrange
        var (s, money, plot) = await GivenPaidPlot(minAge: 0);
        decimal before = money.GetBalance(s.City.MoneyAccountName);
        decimal expected = Math.Floor(PlotPricing.Units(plot.Volume) * (decimal)Mod.config.PLOT_CLAIM_PRICE_PER_UNIT * 50m / 100m);
        Assert.True(expected > 0, "precondition: the refund is above zero");

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} delete");

        // Assert
        Assert.True(result.Ok, "/plot delete failed: " + result.Message);
        Assert.Null(PlotIn(PlotBox(s.Area)));
        Assert.Equal(before + expected, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // E9: a plot younger than PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS gives nothing back
    [AtlasScenario]
    public async Task Delete_Should_RefundNothing_When_PlotIsTooYoung()
    {
        // Arrange
        var (s, money, _) = await GivenPaidPlot(minAge: 1_000_000);
        decimal before = money.GetBalance(s.City.MoneyAccountName);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{PlotCmd} delete");

        // Assert
        Assert.True(result.Ok, "/plot delete failed: " + result.Message);
        Assert.Null(PlotIn(PlotBox(s.Area)));
        Assert.Equal(before, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }
}
