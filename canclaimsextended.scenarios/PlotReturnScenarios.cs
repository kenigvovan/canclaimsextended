using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;

namespace canclaimsextended.scenarios;

/// <summary>
/// Z7, F7: a plot that goes back to the city takes the city's rights and flags, not the ones its
/// last owner set (Plot.ReturnToCity). Three ways back: the owner gives it up, the mayor takes it,
/// the owner leaves the city.
/// </summary>
[Trait("Category", "E2E")]
public class PlotReturnScenarios : CxScenarioBase
{
    /// <summary>A citizen's plot where the owner opened building to strangers and switched pvp on.</summary>
    private async Task<(CityScene Scene, Plot Plot, ITestPlayer Owner)> GivenCustomisedPlot()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);

        await owner.ExecuteCommand($"{PlotCmd} set perm stranger build on");
        await owner.ExecuteCommand($"{PlotCmd} set pvp on");
        PermsHandler perms = plot.getPermsHandler();
        Assert.True(perms.getPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM) && perms.pvpFlag,
            "precondition: the owner changed the plot's rights and flags");
        Assert.False(s.City.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: the city does not let strangers build");
        return (s, plot, owner);
    }

    private static void AssertCityRules(CityScene s, Plot plot)
    {
        PermsHandler city = s.City.getPermsHandler(), now = plot.getPermsHandler();
        Assert.Null(plot.getPlotOwner());
        Assert.Equal(-1, plot.Price);
        Assert.Equal(city.ToString(), now.ToString());
    }

    // Z7: /plot unclaim by the owner
    [AtlasScenario]
    public async Task Plot_Should_TakeCityRules_When_OwnerGivesItUp()
    {
        var (s, plot, owner) = await GivenCustomisedPlot();

        await owner.ExecuteCommand($"{PlotCmd} unclaim");

        AssertCityRules(s, plot);
        AssertInvariants();
    }

    // Z7: /plot unclaim by the mayor on a citizen's plot
    [AtlasScenario]
    public async Task Plot_Should_TakeCityRules_When_MayorTakesItBack()
    {
        var (s, plot, _) = await GivenCustomisedPlot();
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);

        await s.Mayor.ExecuteCommand($"{PlotCmd} unclaim");

        AssertCityRules(s, plot);
        AssertInvariants();
    }

    // Z7: the owner leaves the city (PlayerInfo.leaveCity)
    [AtlasScenario]
    public async Task Plot_Should_TakeCityRules_When_OwnerLeavesTheCity()
    {
        var (s, plot, owner) = await GivenCustomisedPlot();

        await owner.ExecuteCommand($"{CityCmd} leave");

        Assert.False(Info(owner).hasCity(), "precondition: the owner left");
        AssertCityRules(s, plot);
        AssertInvariants();
    }

    // F7: a stranger who could build while the owner allowed it cannot any more - the answer the
    // player's cache holds for this plot must not outlive the change
    [AtlasScenario]
    public async Task Stranger_Should_LoseBuildRight_When_PlotReturnsToCity()
    {
        // Arrange: the stranger stands in the plot and has been allowed once (cached)
        var (s, plot, owner) = await GivenCustomisedPlot();
        ITestPlayer stranger = await GivenStranger(s);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        await stranger.TeleportTo(spot);
        Assert.True(AccessRules.Check(stranger.Player, spot, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: the owner's rules let the stranger build");

        // Act
        await owner.ExecuteCommand($"{PlotCmd} unclaim");

        // Assert
        Assert.False(AccessRules.Check(stranger.Player, spot, PermType.BUILD_AND_DESTROY_PERM),
            "the stranger may still build on the plot that went back to the city");
    }
}
