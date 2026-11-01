using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.perms;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// A flag set on one plot stays on that plot: the neighbour plot, the city, the city land and a
/// plot of another city keep theirs, and the plot's own other values stay too. /city set flag
/// reaches the city, its land and the plots without an owner only. Mirrors CAN_claims
/// FlagIsolationScenarios (FI3-FI5); section I of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class FlagIsolationScenarios : CxScenarioBase
{
    private static readonly string[] Groups = { "citizen", "stranger", "friend" };
    private static readonly string[] Types = { "use", "build", "attack" };
    private static readonly string[] Flags = { "pvp", "fire", "blast", "nomobspawn" };

    /// <summary>All 13 values of a rules table (4 flags, 3 groups x 3 rights), by name.</summary>
    private static Dictionary<string, bool> Snapshot(PermsHandler h)
    {
        var s = new Dictionary<string, bool>
        {
            ["pvp"] = h.pvpFlag,
            ["fire"] = h.fireFlag,
            ["blast"] = h.blastFlag,
            ["nomobspawn"] = h.noMobSpawnFlag,
        };
        bool[][] byGroup = { h.CitizenPerms, h.StrangerPerms, h.ComradePerms };
        for (int g = 0; g < Groups.Length; g++)
            for (int t = 0; t < Types.Length; t++)
                s[Groups[g] + ":" + Types[t]] = byGroup[g][t];
        return s;
    }

    private static string[] Changed(Dictionary<string, bool> before, Dictionary<string, bool> after) =>
        before.Keys.Where(k => before[k] != after[k]).ToArray();

    private static bool Flag(PermsHandler h, string flag) => Snapshot(h)[flag];

    private static string Word(bool on) => on ? "on" : "off";

    /// <summary>The land of the city: a block of its chunk between the plot boxes.</summary>
    private Plot CityLand(CityScene s)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos land = new(chunk.MinBlockX + 8, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 8);
        Assert.True(Mod.dataStorage.GetPlotAt(land, out Plot here) && here.IsCityLand, "precondition: the block is city land");
        return here;
    }

    // FI3: a plot command changes its own value and nothing else on the plot
    [AtlasTheory]
    [InlineData("pvp")]
    [InlineData("fire")]
    [InlineData("blast")]
    [InlineData("nomobspawn")]
    [InlineData("perm stranger attack")]
    public async Task PlotCommand_Should_ChangeOnlyItsOwnValue(string setting)
    {
        // Arrange: the value goes to the opposite of what it is
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        string key = setting.StartsWith("perm ") ? setting["perm ".Length..].Replace(' ', ':') : setting;
        Dictionary<string, bool> before = Snapshot(plot.getPermsHandler());

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{PlotCmd} set {setting} {Word(!before[key])}");

        // Assert
        Assert.True(set.Ok, $"/plot set {setting}: {set.Message}");
        Assert.Equal(new[] { key }, Changed(before, Snapshot(plot.getPermsHandler())));
    }

    // FI3: a plot's flag leaves the neighbour plot, the city, its land and a plot of another city alone
    [AtlasTheory]
    [InlineData("pvp")]
    [InlineData("fire")]
    [InlineData("blast")]
    [InlineData("nomobspawn")]
    public async Task PlotFlag_Should_NotTouchNeighbourCityLandOrAnotherCity(string flag)
    {
        // Arrange: two plots of one city, and a plot of a second city
        CityScene other = await GivenCity();
        Plot foreign = await GivenPlot(other);
        CityScene s = await GivenCity();
        Plot neighbour = await GivenPlot(s, 1);
        Plot plot = await GivenPlot(s, 0);
        Plot land = CityLand(s);
        var neighbourBefore = Snapshot(neighbour.getPermsHandler());
        var cityBefore = Snapshot(s.City.getPermsHandler());
        var landBefore = Snapshot(land.getPermsHandler());
        var foreignBefore = Snapshot(foreign.getPermsHandler());
        var otherCityBefore = Snapshot(other.City.getPermsHandler());
        bool was = Flag(plot.getPermsHandler(), flag);

        // Act: the mayor stands in slot 0 since GivenPlot
        CommandResult set = await s.Mayor.ExecuteCommand($"{PlotCmd} set {flag} {Word(!was)}");

        // Assert
        Assert.True(set.Ok, $"/plot set {flag}: {set.Message}");
        Assert.True(Flag(plot.getPermsHandler(), flag) == !was, $"the plot's {flag} did not change");
        Assert.Empty(Changed(neighbourBefore, Snapshot(neighbour.getPermsHandler())));
        Assert.Empty(Changed(cityBefore, Snapshot(s.City.getPermsHandler())));
        Assert.Empty(Changed(landBefore, Snapshot(land.getPermsHandler())));
        Assert.Empty(Changed(foreignBefore, Snapshot(foreign.getPermsHandler())));
        Assert.Empty(Changed(otherCityBefore, Snapshot(other.City.getPermsHandler())));
    }

    // FI3, H2: /city set flag reaches the city, its land and the unowned plot; the owned plot and
    // another city keep theirs
    [AtlasTheory]
    [InlineData("pvp")]
    [InlineData("fire")]
    [InlineData("blast")]
    [InlineData("nomobspawn")]
    public async Task CityFlag_Should_ReachUnownedPlotsOnly(string flag)
    {
        // Arrange
        CityScene other = await GivenCity();
        Plot foreign = await GivenPlot(other);
        CityScene s = await GivenCity("owner");
        Plot unowned = await GivenPlot(s, 0);
        Plot owned = await GivenPlot(s, 1);
        await GivenOwnedPlot(s, owned, s.Citizens[0]);
        Plot land = CityLand(s);
        bool was = Flag(s.City.getPermsHandler(), flag);
        Assert.True(Flag(unowned.getPermsHandler(), flag) == was && Flag(owned.getPermsHandler(), flag) == was,
            $"precondition: both plots have the city's {flag}");
        var foreignBefore = Snapshot(foreign.getPermsHandler());
        var otherCityBefore = Snapshot(other.City.getPermsHandler());

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set {flag} {Word(!was)}");

        // Assert
        Assert.True(set.Ok, $"/city set {flag}: {set.Message}");
        Assert.True(Flag(s.City.getPermsHandler(), flag) == !was, $"the city's {flag} did not change");
        Assert.True(Flag(land.getPermsHandler(), flag) == !was, $"the city land's {flag} did not follow the city");
        Assert.True(Flag(unowned.getPermsHandler(), flag) == !was, $"the unowned plot's {flag} did not follow the city");
        Assert.True(Flag(owned.getPermsHandler(), flag) == was, $"/city set {flag} reached a plot with an owner");
        Assert.Empty(Changed(foreignBefore, Snapshot(foreign.getPermsHandler())));
        Assert.Empty(Changed(otherCityBefore, Snapshot(other.City.getPermsHandler())));
    }

    // FI3: /city set flag changes only that flag on the city and the unowned plot
    [AtlasScenario]
    public async Task CityFlag_Should_ChangeOnlyItsOwnValue()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        var cityBefore = Snapshot(s.City.getPermsHandler());
        var plotBefore = Snapshot(plot.getPermsHandler());

        await s.Mayor.ExecuteCommand($"{CityCmd} set fire {Word(!cityBefore["fire"])}");

        Assert.Equal(new[] { "fire" }, Changed(cityBefore, Snapshot(s.City.getPermsHandler())));
        Assert.Equal(new[] { "fire" }, Changed(plotBefore, Snapshot(plot.getPermsHandler())));
    }

    // FI4: a mistyped state is refused and changes nothing
    [AtlasScenario]
    public async Task PlotFlag_Should_StayAsItWas_When_TheStateIsMistyped()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire on");
        Assert.True(plot.getPermsHandler().fireFlag, "precondition: fire is on");
        var before = Snapshot(plot.getPermsHandler());

        CommandResult mistyped = await s.Mayor.ExecuteCommand($"{PlotCmd} set fire onn");

        Assert.False(mistyped.Ok, "a mistyped state was reported as done");
        Assert.Empty(Changed(before, Snapshot(plot.getPermsHandler())));
    }

    // FI4 for the city: the same with /city set
    [AtlasScenario]
    public async Task CityFlag_Should_StayAsItWas_When_TheStateIsMistyped()
    {
        CityScene s = await GivenCity();
        await s.Mayor.ExecuteCommand($"{CityCmd} set blast on");
        Assert.True(s.City.getPermsHandler().blastFlag, "precondition: blast is on");
        var before = Snapshot(s.City.getPermsHandler());

        CommandResult mistyped = await s.Mayor.ExecuteCommand($"{CityCmd} set blast offf");

        Assert.False(mistyped.Ok, "a mistyped state was reported as done");
        Assert.Empty(Changed(before, Snapshot(s.City.getPermsHandler())));
    }

    // FI5: the flags stay on the plot when a citizen buys it
    [AtlasScenario]
    public async Task Flags_Should_StayOnThePlot_When_ACitizenBuysIt()
    {
        // Arrange: no mobs, blasts on, fire on - not what a new plot has
        CityScene s = await GivenCity("buyer");
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set nomobspawn on");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set blast on");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire on");
        var before = Snapshot(plot.getPermsHandler());
        Assert.True(before["nomobspawn"] && before["blast"] && before["fire"], "precondition: the flags are on");

        // Act
        await GivenOwnedPlot(s, plot, s.Citizens[0]);

        // Assert
        Assert.Empty(Changed(before, Snapshot(plot.getPermsHandler())).Where(Flags.Contains));
        AssertInvariants();
    }
}
