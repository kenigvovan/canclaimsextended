using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.scenarios;

/// <summary>
/// Criminals and the prison: who sees the list (Z6, K8), joining the city clears the record (Z8,
/// P8), and the admin's jail and release (X4a), which also stand in for a kill in other cases.
/// </summary>
[Trait("Category", "E2E")]
public class PrisonScenarios : CxScenarioBase
{
    /// <summary>A prison plot with one cell where the mayor stands.</summary>
    private async Task<Plot> GivenPrison(CityScene s)
    {
        Plot plot = await GivenPlot(s);
        await RunPaid(s.Mayor, $"{PlotCmd} set type prison", () => plot.Type == PlotType.PRISON, "the plot to become a prison");
        CommandResult cell = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");
        Assert.True(cell.Ok, "precondition: a cell was added: " + cell.Message);
        return plot;
    }

    // K8, Z6: a citizen without a prison or criminal right does not see the list
    [AtlasScenario]
    public async Task CriminalList_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("citizen");

        CommandResult result = await s.Citizens[0].ExecuteCommand($"{CityCmd} criminal list");

        Assert.False(result.Ok, "a citizen without rights read the criminal list: " + result.Message);
    }

    // K8: the mayor does
    [AtlasScenario]
    public async Task CriminalList_Should_BeShown_When_MayorAsks()
    {
        CityScene s = await GivenCity();

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} criminal list");

        Assert.True(result.Ok, "the mayor could not read the criminal list: " + result.Message);
    }

    // Z8, P8: a criminal of the city who joins it is a criminal no more
    [AtlasScenario]
    public async Task Criminal_Should_BeCleared_When_HeJoinsTheCity()
    {
        // Arrange
        CityScene s = await GivenCity();
        ITestPlayer outlaw = await GivenStranger(s, "outlaw");
        await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(outlaw)}");
        Assert.Contains(outlaw.Player.PlayerUID, s.City.Criminals);

        // Act
        await AddCitizen(s.City, s.Mayor, outlaw);

        // Assert
        Assert.DoesNotContain(outlaw.Player.PlayerUID, s.City.Criminals);
        AssertInvariants();
    }

    // X4a: /cadmin player jail puts an online player into a cell of the city's prison at once
    [AtlasScenario]
    public async Task AdminJail_Should_PutPlayerIntoTheCell()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot prison = await GivenPrison(s);
        ITestPlayer prisoner = await GivenStranger(s, "prisoner");
        AllowAdminCommands(s.Mayor);

        // Act
        CommandResult jail = await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(prisoner)} {s.City.GetPartName()} 2");

        // Assert
        Assert.True(jail.Ok, "/cadmin player jail: " + jail.Message);
        Assert.True(Info(prisoner).isPrisoned(), "the player is not a prisoner");
        Assert.Equal(prison.Guid, Info(prisoner).PrisonPlotGuid);
        await World.Until(() => prison.Shape.Contains(prisoner.Position.X, prisoner.Position.Y, prisoner.Position.Z),
            "the prisoner to be moved into the prison", 100);
    }

    // X4a: the given minutes set the sentence, not PRISON_SENTENCE_HOURS
    [AtlasScenario]
    public async Task AdminJail_Should_UseTheGivenMinutes()
    {
        CityScene s = await GivenCity();
        await GivenPrison(s);
        ITestPlayer prisoner = await GivenStranger(s, "shortterm");
        AllowAdminCommands(s.Mayor);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(prisoner)} {s.City.GetPartName()} 2");

        long left = Info(prisoner).PrisonReleaseAt - now;
        Assert.InRange(left, 110, 130);
    }

    // X4a: release ends the sentence now
    [AtlasScenario]
    public async Task AdminRelease_Should_FreeThePrisoner()
    {
        CityScene s = await GivenCity();
        await GivenPrison(s);
        ITestPlayer prisoner = await GivenStranger(s, "freed");
        AllowAdminCommands(s.Mayor);
        await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(prisoner)} {s.City.GetPartName()} 30");
        Assert.True(Info(prisoner).isPrisoned(), "precondition: jailed");

        CommandResult release = await s.Mayor.ExecuteCommand($"{CadminCmd} player release {Name(prisoner)}");

        Assert.True(release.Ok, "/cadmin player release: " + release.Message);
        Assert.False(Info(prisoner).isPrisoned(), "the player is still a prisoner");
    }

    // X4a: a city without a prison with cells is refused, and nobody is jailed
    [AtlasScenario]
    public async Task AdminJail_Should_BeRefused_When_CityHasNoPrison()
    {
        CityScene s = await GivenCity();
        ITestPlayer player = await GivenStranger(s, "lucky");
        AllowAdminCommands(s.Mayor);

        CommandResult jail = await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(player)} {s.City.GetPartName()} 2");

        Assert.False(jail.Ok, "jailed into a city without a prison");
        Assert.False(Info(player).isPrisoned(), "the player became a prisoner without a prison");
    }
}
