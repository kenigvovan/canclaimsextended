using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Is the build alive at all? Run before anything else; if this fails, the rest of the suite says
/// nothing. Section A of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "Smoke")]
public class SmokeScenarios : CxScenarioBase
{
    // A1, A3: the server boots with the mod, the database loads, nothing logs an error.
    [AtlasScenario]
    public async Task Server_Should_BootWithoutErrors()
    {
        var errors = World.BootDiagnostics
            .Where(e => e.Tick is null && e.Level >= EnumLogType.Error)
            .Select(e => $"[{e.DescribeSource()}] {e.Message}")
            .ToList();

        Assert.True(errors.Count == 0, "errors while booting:\n" + string.Join("\n", errors));
        Assert.NotNull(Mod.config);
        Assert.NotNull(Mod.dataStorage);
        Assert.NotNull(Mod.Db);
        await World.Ticks(1);
    }

    // A1: every harmony patch fits the game version. A patch that no longer fits is only logged
    // and its protection silently stops - this turns those warnings into a failure.
    [AtlasScenario]
    public async Task HarmonyPatches_Should_AllApply()
    {
        string[] markers = { "patch skipped", "patch of", "client patch failed" };
        var warnings = World.BootDiagnostics
            .Where(e => markers.Any(m => e.Message.Contains(m)))
            .Select(e => e.Message)
            .ToList();

        Assert.True(warnings.Count == 0, "patches not applied: " + string.Join("; ", warnings));
        await World.Ticks(1);
    }

    // B1, E1, F2: the core flow end to end - a city is founded, a plot made, a citizen buys it.
    [AtlasScenario]
    public async Task CoreFlow_Should_Work_When_CityIsFoundedAndPlotBought()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];

        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, citizen);

        Assert.True(Info(citizen).Equals(plot.getPlotOwner()), "the citizen must own the plot");
        AssertInvariants();
    }
}
