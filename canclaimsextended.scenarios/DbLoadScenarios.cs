using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Y2: a database edited by hand (fixtures/sql/BrokenRows.sql) - a plot with no boxes, two plots
/// that overlap, a chunk of a city that does not exist - next to good rows. The server must start
/// (a load that fails throws in onModsAndConfigReady and stops it), each bad row is named in the
/// log (SQLiteDatabaseHandler.Load.cs), and the good rows are loaded as stored.
///
/// The log lines are read from World.BootDiagnostics: the load runs before the world is ready, so
/// they are boot entries. Atlas strips the "[canclaimsextended] " prefix, so the checks look for
/// the rest of the line. No player joins and every scenario only reads: order does not matter.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/BrokenRows", TargetPath = "ModConfig")]
public class DbLoadScenarios : AtlasScenarioBase
{
    private bool Logged(EnumLogType level, params string[] parts) =>
        World.BootDiagnostics.Any(e => e.Level == level && parts.All(p => e.Message.Contains(p)));

    private string LogText() =>
        string.Join("\n", World.BootDiagnostics.Select(e => $"{e.Level} [{e.DescribeSource()}] {e.Message}"));

    // Y2: the server starts, the database is open and loaded
    [AtlasScenario]
    public async Task Server_Should_Boot_When_DatabaseHasBrokenRows()
    {
        Fixtures.RequireDb(BrokenRows.Folder);

        // A failed table load is logged as "loading <what> failed: ..." (TryLoad) and then stops the
        // server; booted at all means it did not happen, the log check names the table if it did.
        Assert.NotNull(Mod.Db);
        Assert.False(World.BootDiagnostics.Any(e => e.Message.StartsWith("loading ") && e.Message.Contains(" failed: ")),
            "a whole table failed to load:\n" + LogText());
        await World.Ticks(1);
    }

    // Y2: the good rows are loaded as stored, next to the bad ones
    [AtlasScenario]
    public async Task GoodRows_Should_BeLoaded_When_OtherRowsAreBroken()
    {
        Fixtures.RequireDb(BrokenRows.Folder);

        Assert.True(Mod.dataStorage.GetCityByName(BrokenRows.City, out City city), $"city {BrokenRows.City} was not loaded");
        Assert.Equal(BrokenRows.CityGuid, city.Guid);
        Assert.Equal(BrokenRows.Mayor, city.getMayor()?.GetPartName());
        Assert.True(city.HasChunk(BrokenRows.HomeChunk), $"the city lost its chunk {BrokenRows.HomeChunk}");
        Assert.Equal(1, city.ChunkCount);

        Assert.True(Mod.dataStorage.GetPlotByGuid(BrokenRows.GoodPlot, out Plot good), "the good plot was not loaded");
        Assert.True(city.Equals(good.getCity()), "the good plot lost its city");
        Assert.Contains(good, city.getCityPlots());
        Assert.Equal(BrokenRows.GoodPlotPrice, good.Price);
        Assert.Equal(new[] { BrokenRows.GoodPlotBox }, good.Shape.Boxes.ToArray());
        // In the spatial index too, not only in the guid list.
        Assert.True(Mod.dataStorage.GetSubPlotAt(new Vintagestory.API.MathTools.BlockPos(
            BrokenRows.GoodPlotBox.MinX, BrokenRows.GoodPlotBox.MinY, BrokenRows.GoodPlotBox.MinZ), out Plot found) && good.Equals(found),
            "no plot is found inside the good plot's box");
        await World.Ticks(1);
    }

    // Y2: a plot without boxes is skipped and named in the log
    [AtlasScenario]
    public async Task PlotWithoutBoxes_Should_BeSkippedWithLog()
    {
        Fixtures.RequireDb(BrokenRows.Folder);

        Assert.False(Mod.dataStorage.GetPlotByGuid(BrokenRows.NoBoxPlot, out _), "a plot without boxes was loaded");
        Assert.True(Mod.dataStorage.GetCityByName(BrokenRows.City, out City city), $"city {BrokenRows.City} was not loaded");
        Assert.DoesNotContain(city.getCityPlots(), p => p.Guid == BrokenRows.NoBoxPlot);
        Assert.True(Logged(EnumLogType.Error, BrokenRows.NoBoxPlot, "has no boxes"),
            "nothing in the log names the plot without boxes:\n" + LogText());
        await World.Ticks(1);
    }

    // Y2: a chunk of a city that does not exist is skipped and named in the log
    [AtlasScenario]
    public async Task ChunkOfMissingCity_Should_BeSkippedWithLog()
    {
        Fixtures.RequireDb(BrokenRows.Folder);
        string where = $"{BrokenRows.OrphanChunk.X},{BrokenRows.OrphanChunk.Z}";

        Assert.False(Mod.dataStorage.GetChunk(BrokenRows.OrphanChunk, out _), $"chunk {where} of no city was loaded");
        Assert.True(Logged(EnumLogType.Warning, "chunk " + where, "belongs to no known city"),
            $"nothing in the log names chunk {where}:\n" + LogText());
        await World.Ticks(1);
    }

    // Y2: overlapping plots are named in the log; the plot read first keeps its place.
    // What happens to the second one is the loader's choice (today: kept, the mayor cuts it back);
    // this scenario does not pin it.
    [AtlasScenario]
    public async Task OverlappingPlots_Should_BeLogged_When_Loaded()
    {
        Fixtures.RequireDb(BrokenRows.Folder);

        Assert.True(Logged(EnumLogType.Warning, BrokenRows.OverlapSecond, "overlaps another plot"),
            "nothing in the log names the overlapping plot:\n" + LogText());
        Assert.True(Mod.dataStorage.GetPlotByGuid(BrokenRows.OverlapFirst, out Plot first), "the plot read first was not loaded");
        Assert.True(Mod.dataStorage.GetCityByName(BrokenRows.City, out City city) && city.Equals(first.getCity()),
            "the plot read first lost its city");
        await World.Ticks(1);
    }
}
