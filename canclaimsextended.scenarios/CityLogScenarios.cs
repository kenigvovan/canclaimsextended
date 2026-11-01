using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.scenarios;

/// <summary>
/// The city log: who may read it (Z6, CITY_SEE_LOG) and what lands in it (Z13: plot sales, type
/// changes, reshapes, ranks). Section U4 of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class CityLogScenarios : CxScenarioBase
{
    private static CityLogEntry? Last(City city, EnumCityLogEvent type) =>
        city.EventLog.LastOrDefault(e => e.EventType == type);

    /// <summary>The mayor makes a rank holding one right and gives it to the player.</summary>
    private static async Task GiveRank(CityScene s, ITestPlayer to, string rank, string permission)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create {rank}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm {rank} {permission}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {rank} {Name(to)}");
        Assert.True(Info(to).hasCityTitle(rank), $"precondition: {Name(to)} holds rank {rank}");
    }

    // Z6, U4: a citizen without the right does not read the log
    [AtlasScenario]
    public async Task Log_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("citizen");

        CommandResult result = await s.Citizens[0].ExecuteCommand($"{CityCmd} log");

        Assert.False(result.Ok, "a citizen without CITY_SEE_LOG read the log: " + result.Message);
    }

    // Z6, U4: the mayor always may
    [AtlasScenario]
    public async Task Log_Should_BeShown_When_MayorAsks()
    {
        CityScene s = await GivenCity();

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} log");

        Assert.True(result.Ok, "the mayor could not read the log: " + result.Message);
    }

    // Z2 + Z6: CITY_SEE_LOG can be handed out with a rank, and then the citizen reads the log
    [AtlasScenario]
    public async Task Log_Should_BeShown_When_CitizenGotTheRightByRank()
    {
        CityScene s = await GivenCity("reader");
        ITestPlayer reader = s.Citizens[0];

        await GiveRank(s, reader, "Scribe", "CITY_SEE_LOG");
        CommandResult result = await reader.ExecuteCommand($"{CityCmd} log");

        Assert.True(result.Ok, "the rank's CITY_SEE_LOG did not let the citizen read the log: " + result.Message);
    }

    // Z13: giving and taking a rank are logged, with who did it
    [AtlasScenario]
    public async Task RankChanges_Should_BeLogged()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];

        await GiveRank(s, citizen, "Guard", "CITY_INVITE");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank revoke Guard {Name(citizen)}");

        CityLogEntry? granted = Last(s.City, EnumCityLogEvent.RankGranted);
        CityLogEntry? revoked = Last(s.City, EnumCityLogEvent.RankRevoked);
        Assert.NotNull(granted);
        Assert.NotNull(revoked);
        Assert.Equal(new[] { Name(s.Mayor), "Guard", Name(citizen) }, granted!.Args);
        Assert.Equal(new[] { Name(s.Mayor), "Guard", Name(citizen) }, revoked!.Args);
    }

    // Z13: a plot sale is logged with the buyer, the plot, the price and the seller
    [AtlasScenario]
    public async Task PlotSale_Should_BeLogged()
    {
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);

        await GivenOwnedPlot(s, plot, buyer, price: 0);

        CityLogEntry? sold = Last(s.City, EnumCityLogEvent.PlotSold);
        Assert.NotNull(sold);
        Assert.Equal(new[] { Name(buyer), plot.Number.ToString(), "0", s.City.GetPartName() }, sold!.Args);
    }

    // Z13: a type change is logged with the type's code
    [AtlasScenario]
    public async Task PlotTypeChange_Should_BeLogged()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        await RunPaid(s.Mayor, $"{PlotCmd} set type farm", () => plot.Type == PlotType.FARM, "the plot to become a farm");

        CityLogEntry? changed = Last(s.City, EnumCityLogEvent.PlotTypeChanged);
        Assert.NotNull(changed);
        Assert.Equal(new[] { Name(s.Mayor), plot.Number.ToString(), "farm" }, changed!.Args);
    }

    // Z13: grow, shrink and cut are logged with the volume after
    [AtlasScenario]
    public async Task PlotReshape_Should_BeLogged()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        long before = plot.Volume;

        await RunPaid(s.Mayor, $"{PlotCmd} shrink up 1", () => plot.Volume < before, "the plot to shrink");

        CityLogEntry? reshaped = Last(s.City, EnumCityLogEvent.PlotReshaped);
        Assert.NotNull(reshaped);
        Assert.Equal(new[] { Name(s.Mayor), plot.Number.ToString(), plot.Volume.ToString() }, reshaped!.Args);
    }
}
