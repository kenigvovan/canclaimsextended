using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The end and the new name of a city. B4: /city delete leaves nothing of the city behind
/// (PartDemolition.demolishCity) - owned plots, groups, criminals, sent invitations, ranks, the
/// citizens, the treasury (paid to the mayor) - and its name is free again. Chunks, plots in
/// dataStorage and the confirmation by name are in TerritoryScenarios. B7: /city set name is paid
/// from the treasury, logged, and NEW_PLAYERS_DEFAULT_CITY follows the rename.
/// </summary>
[Trait("Category", "E2E")]
public class CityLifecycleScenarios : CxScenarioBase
{
    private const string GroupName = "grp";
    private const string RankName = "Guard";

    // ---- B4 ----------------------------------------------------------------------------------

    /// <summary>
    /// A city with everything that must go with it: a citizen's plot, a plots group with a plot and
    /// a member, a rank held by a citizen, a criminal, an invitation sent to a stranger.
    /// </summary>
    private sealed record FullCity(CityScene Scene, Plot OwnedPlot, Plot GroupPlot, PlotsGroup Group,
        ITestPlayer Owner, ITestPlayer Ranked, ITestPlayer Crook, ITestPlayer Invitee);

    private async Task<FullCity> GivenFullCity()
    {
        CityScene s = await GivenCity("owner", "ranked");
        ITestPlayer owner = s.Citizens[0], ranked = s.Citizens[1];

        Plot owned = await GivenPlot(s, 0);
        await GivenOwnedPlot(s, owned, owner);

        Plot grouped = await GivenPlot(s, 1);
        await s.Mayor.ExecuteCommand($"{CityCmd} pg create {GroupName}");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg addplot {GroupName}");
        Assert.True(PlotsGroupHandler.TryFind(s.City, GroupName, out PlotsGroup group) && group.Equals(grouped.Group),
            "precondition: the group holds the second plot");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg invite {GroupName} {Name(ranked)}");
        await ranked.ExecuteCommand($"{CityCmd} pg join {GroupName}");
        Assert.True(group.Members.Contains(Info(ranked)), "precondition: the group has a member");

        await s.Mayor.ExecuteCommand($"{CityCmd} rank create {RankName}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {RankName} {Name(ranked)}");
        Assert.True(Info(ranked).hasCityTitle(RankName), "precondition: the citizen holds the rank");

        ITestPlayer crook = await GivenStranger(s, "crook");
        CommandResult criminal = await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(crook)}");
        Assert.True(s.City.Criminals.Contains(crook.Player.PlayerUID), "precondition: /city criminal add: " + criminal.Message);

        ITestPlayer invitee = await GivenStranger(s, "invitee");
        CommandResult invite = await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(invitee)}");
        Assert.True(InvitationHandler.tryGetInvitation(s.City, Info(invitee), out _), "precondition: /city invite: " + invite.Message);

        return new FullCity(s, owned, grouped, group, owner, ranked, crook, invitee);
    }

    // B4: everything that hung on the city goes with it
    [AtlasScenario]
    public async Task Delete_Should_CleanUpEverythingOfTheCity()
    {
        // Arrange
        FullCity f = await GivenFullCity();
        CityScene s = f.Scene;
        string name = s.City.GetPartName();

        // Act
        CommandResult delete = await s.Mayor.ExecuteCommand($"{CityCmd} delete {name}");

        // Assert: the city
        Assert.True(delete.Ok, "/city delete: " + delete.Message);
        Assert.DoesNotContain(s.City, Mod.dataStorage.GetCitiesList());
        // Plots: off the map, off the owner
        Assert.False(Mod.dataStorage.GetPlotByGuid(f.OwnedPlot.Guid, out _), "the owned plot is still known");
        Assert.False(Mod.dataStorage.GetPlotByGuid(f.GroupPlot.Guid, out _), "the group's plot is still known");
        Assert.DoesNotContain(f.OwnedPlot, Info(f.Owner).PlayerPlots);
        Assert.Null(f.OwnedPlot.getPlotOwner());
        Assert.False(Mod.dataStorage.GetChunk(AreaChunk(s.Area), out _), "the founding chunk is still claimed");
        // Groups
        Assert.Empty(s.City.PlotsGroups);
        Assert.Empty(f.Group.Members);
        Assert.Null(PlotsGroupHandler.FindByGuid(f.Group.Guid));
        Assert.Null(f.GroupPlot.Group);
        // Criminals: no city lists the crook
        Assert.DoesNotContain(Mod.dataStorage.GetCitiesList(), c => c.Criminals.Contains(f.Crook.Player.PlayerUID));
        // Invitations sent by the city
        Assert.False(InvitationHandler.tryGetInvitation(s.City, Info(f.Invitee), out _), "the city's invitation outlived the city");
        Assert.Empty(InvitationHandler.getInvitesForReceiver(Info(f.Invitee)));
        // Ranks
        Assert.Empty(Info(f.Ranked).getCityTitles());
        // Citizens
        foreach (ITestPlayer p in new[] { s.Mayor, f.Owner, f.Ranked })
        {
            Assert.False(Info(p).hasCity(), $"{Name(p)} still has the deleted city");
        }
        Assert.Empty(s.City.getCityCitizens());
        AssertInvariants();
    }

    // B4: the invitation of a deleted city cannot make anyone a citizen of nothing
    [AtlasScenario]
    public async Task Invitee_Should_NotJoin_When_TheInvitingCityWasDeleted()
    {
        FullCity f = await GivenFullCity();
        string name = f.Scene.City.GetPartName();
        await f.Scene.Mayor.ExecuteCommand($"{CityCmd} delete {name}");

        CommandResult join = await f.Invitee.ExecuteCommand($"{CityCmd} join {name}");

        Assert.False(join.Ok, "joined a deleted city: " + join.Message);
        Assert.False(Info(f.Invitee).hasCity(), "the invitee became a citizen of the deleted city");
        AssertInvariants();
    }

    // B4: what is left in the treasury goes to the mayor
    [AtlasScenario]
    public async Task Delete_Should_PayTheTreasuryToTheMayor()
    {
        // Arrange
        CityScene s = await GivenCity();
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 70);
        money.SetBalance(s.Mayor.Player.PlayerUID, 5);
        decimal total = money.Total();

        // Act
        CommandResult delete = await s.Mayor.ExecuteCommand($"{CityCmd} delete {s.City.GetPartName()}");

        // Assert
        Assert.True(delete.Ok, "/city delete: " + delete.Message);
        Assert.Equal(75m, money.GetBalance(s.Mayor.Player.PlayerUID));
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Contains($"Transfer {s.City.MoneyAccountName}->{s.Mayor.Player.PlayerUID} 70: Success", money.Log);
        Assert.Equal(total, money.Total());
        AssertInvariants();
    }

    // B4, B1: the name of a deleted city can be founded again, on the same land
    [AtlasScenario]
    public async Task Name_Should_BeFree_When_TheCityWasDeleted()
    {
        // Arrange
        CityScene s = await GivenCity();
        string name = s.City.GetPartName();
        await s.Mayor.ExecuteCommand($"{CityCmd} delete {name}");
        Assert.False(Mod.dataStorage.CityExistsByName(name), "precondition: the name is free");
        await s.Mayor.TeleportTo(AreaOrigin(s.Area));

        // Act
        City again = await FoundCity(s.Mayor, name);

        // Assert
        Assert.NotSame(s.City, again);
        Assert.Equal(name, again.GetPartName());
        Assert.True(again.HasChunk(AreaChunk(s.Area)), "the new city did not get the old founding chunk");
        Assert.True(again.isMayor(Info(s.Mayor)), "the founder is not the mayor of the new city");
        Assert.Empty(again.CustomCityRanks);
        Assert.Empty(again.PlotsGroups);
        Assert.Empty(again.Criminals);
        AssertInvariants();
    }

    // ---- B7 ----------------------------------------------------------------------------------

    private void KeepDefaultCity()
    {
        string before = Mod.config.NEW_PLAYERS_DEFAULT_CITY;
        AddCleanup(() =>
        {
            Mod.config.NEW_PLAYERS_DEFAULT_CITY = before;
            Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        });
    }

    // B7, U4: a rename is paid from the treasury after /agree, is logged, and the default city follows it
    [AtlasScenario]
    public async Task Rename_Should_ChargeTheTreasury_LogIt_And_MoveTheDefaultCity()
    {
        // Arrange: the city is the default city of new players; its treasury holds 100
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        string newName = oldName + "new";
        KeepDefaultCity();
        Mod.config.NEW_PLAYERS_DEFAULT_CITY = oldName;
        decimal cost = (decimal)Mod.config.CITY_NAME_CHANGE_COST;
        Assert.True(cost > 0, "precondition: a rename costs something");
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100);

        // Act
        CommandResult rename = await RunPaid(s.Mayor, $"{CityCmd} set name {newName}",
            () => s.City.GetPartName() == newName, "the city to be renamed");

        // Assert
        Assert.True(rename.Ok, "/city set name: " + rename.Message);
        Assert.Equal(newName, s.City.GetPartName());
        Assert.True(Mod.dataStorage.GetCityByName(newName, out City found) && found.Equals(s.City), "the city is not found by its new name");
        Assert.False(Mod.dataStorage.CityExistsByName(oldName), "the old name is still taken");
        Assert.Equal(100m - cost, money.GetBalance(s.City.MoneyAccountName));
        CityLogEntry? renamed = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.CityRenamed);
        Assert.NotNull(renamed);
        Assert.Equal(new[] { oldName, newName }, renamed!.Args);
        Assert.Equal(newName, Mod.config.NEW_PLAYERS_DEFAULT_CITY);
        AssertInvariants();
    }

    // B7: a treasury that cannot pay keeps the old name
    [AtlasScenario]
    public async Task Rename_Should_BeRefused_When_TreasuryCannotPay()
    {
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        decimal cost = (decimal)Mod.config.CITY_NAME_CHANGE_COST;
        Assert.True(cost > 1, "precondition: a rename costs more than 1");
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, cost - 1);

        CommandResult rename = await s.Mayor.ExecuteCommand($"{CityCmd} set name {oldName}new");

        Assert.False(rename.Ok, "a rename the treasury cannot pay was offered: " + rename.Message);
        Assert.Equal("canclaimsextended:not_enough_money_city", rename.Raw.StatusMessage);
        Assert.Equal(oldName, s.City.GetPartName());
        Assert.Equal(cost - 1, money.GetBalance(s.City.MoneyAccountName));
        Assert.DoesNotContain(s.City.EventLog, e => e.EventType == EnumCityLogEvent.CityRenamed);
    }
}
