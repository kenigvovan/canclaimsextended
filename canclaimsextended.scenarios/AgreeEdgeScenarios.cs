using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.economy;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// What /agree checks again when it is finally given (PaidAction.AskToConfirm: recheck, then the
/// payment, then the action). Ported from CAN_claims AgreeScenarios (O1, O5, AG4); the alliance
/// cases are gone with the alliances. A name taken while the offer waits refuses both the founding
/// and the rename and charges nothing. /city delete makes no offer in this mod (it is confirmed by
/// the city name and runs at once), so the "no longer mayor at /agree" case becomes: a former
/// mayor has neither an offer to confirm nor the right to delete.
/// </summary>
[Trait("Category", "E2E")]
public class AgreeEdgeScenarios : CxScenarioBase
{
    private static string AgreeCmd => "/" + Mod.config.COMMAND_AGREE;

    private static string Account(ITestPlayer player) => player.Player.PlayerUID;

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    /// <summary>A player on fresh land of a new area, founding a city there with enough money.</summary>
    private async Task<City> GivenRivalFounds(FakeMoneyProvider money, string name)
    {
        int area = NewArea();
        ITestPlayer rival = await JoinAt("rival" + area, area);
        money.SetBalance(Account(rival), (decimal)PlotPricing.NewCityCost() + 100);
        City city = await FoundCity(rival, name);
        Assert.Equal(name, city.GetPartName());
        return city;
    }

    // O5, AG4: the name is checked at the command and again at /agree - another player founded a
    // city of that name in between, so /agree founds nothing and takes nothing
    [AtlasScenario]
    public async Task CityCreate_Should_BeRefusedAndChargeNothing_When_NameWasTakenWhileWaiting()
    {
        // Arrange: the founder's offer waits; meanwhile a rival founds a city of the same name
        ResetConfig();
        int area = NewArea();
        string name = CityName(area);
        ITestPlayer founder = await JoinAt("founder" + area, area);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = (decimal)PlotPricing.NewCityCost();
        Assert.True(cost > 0, "precondition: a city costs something");
        decimal start = cost + 100;
        money.SetBalance(Account(founder), start);
        CommandResult offer = await founder.ExecuteCommand($"{CityCmd} create {name}");
        Assert.Equal("canclaimsextended:agree_new_city", offer.Raw.StatusMessage);
        City rivalCity = await GivenRivalFounds(money, name);

        // Act
        CommandResult agree = await founder.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert: the offer was used up, and refused
        Assert.True(agree.Ok, "/agree found no offer: " + agree.Message);
        Assert.False(Info(founder).hasCity(), "a city was founded under a name taken while waiting");
        Assert.Single(Mod.dataStorage.GetCitiesList(), c => c.GetPartName() == name);
        Assert.True(Mod.dataStorage.GetCityByName(name, out City found) && found.Equals(rivalCity), "the name no longer leads to the rival's city");
        Assert.False(Mod.dataStorage.GetChunk(AreaChunk(area), out _), "the founder's chunk was claimed");
        Assert.Equal(start, money.GetBalance(Account(founder)));
        Assert.DoesNotContain(money.Log, l => l.StartsWith("Withdraw " + Account(founder)));
        AssertRefused(await founder.ExecuteCommand(AgreeCmd), "no_agreements_awaits", "a second /agree");
        AssertInvariants();
    }

    // B7, AG4: the same check for a rename - the new name was founded while waiting, the city
    // keeps its name and the treasury its money
    [AtlasScenario]
    public async Task CityRename_Should_BeRefusedAndChargeNothing_When_NameWasTakenWhileWaiting()
    {
        // Arrange: the mayor's rename waits; meanwhile a rival founds a city of the new name
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        string newName = oldName + "new";
        decimal cost = (decimal)Mod.config.CITY_NAME_CHANGE_COST;
        Assert.True(cost > 0, "precondition: a rename costs something");
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, cost + 100);
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} set name {newName}");
        Assert.Equal("canclaimsextended:agree_city_rename", offer.Raw.StatusMessage);
        City rivalCity = await GivenRivalFounds(money, newName);

        // Act
        CommandResult agree = await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        Assert.True(agree.Ok, "/agree found no offer: " + agree.Message);
        Assert.Equal(oldName, s.City.GetPartName());
        Assert.Equal(newName, rivalCity.GetPartName());
        Assert.True(Mod.dataStorage.GetCityByName(oldName, out City found) && found.Equals(s.City), "the city is not found by its old name");
        Assert.Equal(cost + 100, money.GetBalance(s.City.MoneyAccountName));
        Assert.DoesNotContain(money.Log, l => l.StartsWith("Withdraw " + s.City.MoneyAccountName));
        Assert.DoesNotContain(s.City.EventLog, e => e.EventType == EnumCityLogEvent.CityRenamed);
        AssertInvariants();
    }

    // O1, AG4 as this mod has it: /city delete makes no offer, so a mayor replaced by an admin has
    // nothing to /agree to, and the former mayor's own /city delete is refused - the city stays and
    // its treasury is not paid out
    [AtlasScenario]
    public async Task CityDelete_Should_BeRefused_When_PlayerIsNoLongerMayor()
    {
        // Arrange: an admin hands the city to the heir
        CityScene s = await GivenCity("heir");
        ITestPlayer heir = s.Citizens[0];
        string name = s.City.GetPartName();
        ITestPlayer admin = await GivenStranger(s, "admin");
        AllowAdminCommands(admin);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 70);
        money.SetBalance(Account(s.Mayor), 5);
        CommandResult setMayor = await admin.ExecuteCommand($"{CadminCmd} city setmayor {name} {Name(heir)}");
        Assert.True(setMayor.Ok, "precondition: /cadmin city setmayor: " + setMayor.Message);
        Assert.True(s.City.isMayor(Info(heir)), "precondition: the heir is the mayor now");
        Assert.False(s.City.isMayor(Info(s.Mayor)), "precondition: the former mayor is no longer the mayor");

        // Act
        CommandResult delete = await s.Mayor.ExecuteCommand($"{CityCmd} delete {name}");
        CommandResult agree = await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        AssertRefused(delete, "you_dont_have_right_for_that_command", "/city delete by the former mayor");
        AssertRefused(agree, "no_agreements_awaits", "/agree after the refused /city delete");
        Assert.Contains(s.City, Mod.dataStorage.GetCitiesList());
        Assert.True(Mod.dataStorage.GetChunk(AreaChunk(s.Area), out _), "the founding chunk is no longer claimed");
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the former mayor lost the city");
        Assert.Equal(70m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Equal(5m, money.GetBalance(Account(s.Mayor)));
        Assert.DoesNotContain(money.Log, l => l.StartsWith("Transfer " + s.City.MoneyAccountName));
        AssertInvariants();
    }
}
