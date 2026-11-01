using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Config;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The daily cycle run on demand with /cadmin day (DayTimer.Run): the upkeep formula of
/// PlotPricing.CityDailyPayment (T5), debt and demolition for debt (T6), the technical city that
/// pays nothing (T6), the plot fee and the plot it costs (T4, Z7), and the citizen fee with
/// DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE off (T3). Section T of TESTING_canclaimsextended.md;
/// the fee kick with the flag on is in AdminScenarios.
/// </summary>
[Trait("Category", "E2E")]
public class DayCycleScenarios : CxScenarioBase
{
    private async Task RunDay(ITestPlayer admin)
    {
        AllowAdminCommands(admin);
        CommandResult day = await admin.ExecuteCommand($"{CadminCmd} day");
        Assert.True(day.Ok, "/cadmin day: " + day.Message);
    }

    /// <summary>The prices of the upkeep for this scenario; the old ones come back on Dispose.</summary>
    private void GivenUpkeepPrices(double baseCare, double chunkUpkeep, int blocksPerUnit,
        double defaultUnit, double farmUnit, double noPvpCost, double noMobSpawnCost)
    {
        var c = Mod.config;
        double oldBase = c.CITY_BASE_CARE, oldChunk = c.CHUNK_DAILY_UPKEEP, oldNoPvp = c.PLOT_NO_PVP_FLAG_COST,
            oldNoMob = c.PLOT_NO_MOBSPAWN_FLAG_COST;
        int oldUnit = c.BLOCKS_PER_COST_UNIT;
        Dictionary<string, double> oldUnitCosts = c.PLOT_TYPE_UNIT_COSTS;
        AddCleanup(() =>
        {
            c.CITY_BASE_CARE = oldBase;
            c.CHUNK_DAILY_UPKEEP = oldChunk;
            c.PLOT_NO_PVP_FLAG_COST = oldNoPvp;
            c.PLOT_NO_MOBSPAWN_FLAG_COST = oldNoMob;
            c.BLOCKS_PER_COST_UNIT = oldUnit;
            c.PLOT_TYPE_UNIT_COSTS = oldUnitCosts;
        });

        c.CITY_BASE_CARE = baseCare;
        c.CHUNK_DAILY_UPKEEP = chunkUpkeep;
        c.BLOCKS_PER_COST_UNIT = blocksPerUnit;
        c.PLOT_TYPE_UNIT_COSTS = new Dictionary<string, double>(oldUnitCosts) { ["default"] = defaultUnit, ["farm"] = farmUnit };
        c.PLOT_NO_PVP_FLAG_COST = noPvpCost;
        c.PLOT_NO_MOBSPAWN_FLAG_COST = noMobSpawnCost;
    }

    private static long Units(long volume, int perUnit) => Math.Max(1, (volume + perUnit - 1) / perUnit);

    // ---- T5 ----------------------------------------------------------------------------------

    // T5, G6: the upkeep taken is base + chunks x chunk upkeep + units x unit price of the type + safety flags
    [AtlasScenario]
    public async Task Upkeep_Should_BeBasePlusChunksPlusPlotUnitsPlusFlags_When_DayPasses()
    {
        // Arrange: a default plot with pvp off and nomobspawn on, a farm plot with pvp on and nomobspawn off
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s, 0);
        Plot farm = await GivenPlot(s, 1);
        await RunPaid(s.Mayor, $"{PlotCmd} set type farm", () => farm.Type == PlotType.FARM, "the plot to become a farm");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp on");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set nomobspawn off");
        await StandIn(s.Mayor, plain.Shape.Boxes[0]);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set nomobspawn on");
        Assert.True(!plain.getPermsHandler().pvpFlag && plain.getPermsHandler().noMobSpawnFlag,
            "precondition: the default plot has pvp off and nomobspawn on");
        Assert.True(farm.getPermsHandler().pvpFlag && !farm.getPermsHandler().noMobSpawnFlag,
            "precondition: the farm has pvp on and nomobspawn off");
        Assert.True(s.City.getCityPlots().Count == 2, $"precondition: the city has 2 plots, has {s.City.getCityPlots().Count}");

        GivenUpkeepPrices(baseCare: 2, chunkUpkeep: 0.5, blocksPerUnit: 100,
            defaultUnit: 1, farmUnit: 2, noPvpCost: 3, noMobSpawnCost: 4);
        double expected = 2
            + s.City.ChunkCount * 0.5
            + Units(plain.Volume, 100) * 1 + 3 + 4
            + Units(farm.Volume, 100) * 2;
        decimal expectedTaken = Math.Floor((decimal)expected);
        Assert.True(expectedTaken >= 1, "precondition: the upkeep is at least 1, was " + expected);

        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 1000);

        // Act
        double formula = s.City.GetDayPaymentAmount();
        await RunDay(s.Mayor);

        // Assert
        Assert.True(Math.Abs(expected - formula) < 1e-9, $"CityDailyPayment is {formula}, by hand {expected}");
        Assert.Equal(1000m - expectedTaken, money.GetBalance(s.City.MoneyAccountName));
        Assert.True(s.City.DebtBalance == 0, $"a city that could pay got debt {s.City.DebtBalance}");
        AssertInvariants();
    }

    // ---- T6 ----------------------------------------------------------------------------------

    // T6: an empty treasury turns today's upkeep into debt, and the citizens are told
    [AtlasScenario]
    public async Task Upkeep_Should_BecomeDebt_And_WarnTheCity_When_TreasuryIsEmpty()
    {
        // Arrange
        CityScene s = await GivenCity();
        FakeMoneyProvider money = UseFakeEconomy();
        double today = Math.Floor(s.City.GetDayPaymentAmount());
        Assert.True(today >= 1, "precondition: the upkeep is at least 1, was " + today);
        Assert.True(s.City.DebtBalance == 0, "precondition: no debt");

        // Act
        await RunDay(s.Mayor);

        // Assert
        Assert.Equal(today, s.City.DebtBalance, 2);
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Contains(s.City, Mod.dataStorage.GetCitiesList());
        string warning = Lang.Get("canclaimsextended:city_in_debt", s.City.DebtBalance, Mod.config.CITY_MAX_DEBT);
        await World.Until(() => s.Mayor.Client.ChatLines().Any(l => l.Contains(warning)),
            "the mayor to get city_in_debt", AgreeTimeoutTicks);
        AssertInvariants();
    }

    // T6: debt that goes over CITY_MAX_DEBT demolishes the city, and its people are left without one
    [AtlasScenario]
    public async Task City_Should_BeDemolished_When_DebtGoesOverMax()
    {
        // Arrange: the debt is exactly at the maximum, so today's upkeep takes it over
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        string name = s.City.GetPartName();
        UseFakeEconomy();
        Assert.True(Math.Floor(s.City.GetDayPaymentAmount()) >= 1, "precondition: the upkeep is at least 1");
        s.City.DebtBalance = Mod.config.CITY_MAX_DEBT;

        // Act
        await RunDay(s.Mayor);

        // Assert
        Assert.DoesNotContain(s.City, Mod.dataStorage.GetCitiesList());
        Assert.False(Mod.dataStorage.GetCityByName(name, out _), "the demolished city is still found by name");
        Assert.False(Info(s.Mayor).hasCity(), "the mayor still has the demolished city");
        Assert.False(Info(citizen).hasCity(), "the citizen still has the demolished city");
        Assert.False(Mod.dataStorage.GetChunk(AreaChunk(s.Area), out _), "the chunk of the demolished city is still claimed");
        AssertInvariants();
    }

    // T6: debt that ends exactly at the maximum is not yet over it
    [AtlasScenario]
    public async Task City_Should_Survive_When_DebtEndsExactlyAtMax()
    {
        CityScene s = await GivenCity();
        UseFakeEconomy();
        // Debt grows by the rounded-down upkeep (whole coins only).
        double today = Math.Floor(s.City.GetDayPaymentAmount());
        Assert.True(today >= 1, "precondition: the upkeep is at least 1, was " + today);
        s.City.DebtBalance = Mod.config.CITY_MAX_DEBT - today;

        await RunDay(s.Mayor);

        Assert.Equal(Mod.config.CITY_MAX_DEBT, s.City.DebtBalance, 2);
        Assert.Contains(s.City, Mod.dataStorage.GetCitiesList());
        AssertInvariants();
    }

    // T6, C7: a technical city pays no upkeep
    [AtlasScenario]
    public async Task TechnicalCity_Should_PayNothing_When_DayPasses()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        CommandResult technical = await s.Mayor.ExecuteCommand($"{CadminCmd} city technical {s.City.GetPartName()} on");
        Assert.True(technical.Ok && s.City.isTechnicalCity(), "precondition: /cadmin city technical on: " + technical.Message);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100);
        Assert.True(Math.Floor(s.City.GetDayPaymentAmount()) >= 1, "precondition: an ordinary city would pay");

        // Act
        await RunDay(s.Mayor);

        // Assert
        Assert.Equal(100m, money.GetBalance(s.City.MoneyAccountName));
        Assert.True(s.City.DebtBalance == 0, $"a technical city got debt {s.City.DebtBalance}");
        Assert.DoesNotContain(money.Log, l => l.Contains(s.City.MoneyAccountName));
    }

    // ---- T4 ----------------------------------------------------------------------------------

    // T4, Z7, F7: an owner who cannot pay the plot fee loses the plot; it goes back with the city's
    // rights, and the city log says so
    [AtlasScenario]
    public async Task Plot_Should_ReturnToCityWithCityRights_When_OwnerCannotPayThePlotFee()
    {
        // Arrange: the owner opened the plot to strangers and switched pvp on; the plot fee is 5
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await owner.ExecuteCommand($"{PlotCmd} set perm stranger build on");
        await owner.ExecuteCommand($"{PlotCmd} set pvp on");
        Assert.NotEqual(s.City.getPermsHandler().ToString(), plot.getPermsHandler().ToString());
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        CommandResult fee = await s.Mayor.ExecuteCommand($"{PlotCmd} set fee 5");
        Assert.True(fee.Ok && plot.getCustomTax() == 5, "precondition: /plot set fee 5: " + fee.Message);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100000);
        money.SetBalance(owner.Player.PlayerUID, 0);

        // Act
        await RunDay(s.Mayor);

        // Assert
        Assert.Null(plot.getPlotOwner());
        Assert.DoesNotContain(plot, Info(owner).PlayerPlots);
        Assert.Equal(-1, plot.Price);
        Assert.Equal(s.City.getPermsHandler().ToString(), plot.getPermsHandler().ToString());
        Assert.True(s.City.Equals(Info(owner).City), "losing the plot took the owner out of the city");
        CityLogEntry? unpaid = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.PlotFeeUnpaid);
        Assert.NotNull(unpaid);
        Assert.Equal(new[] { Name(owner), "5" }, unpaid!.Args);
        AssertInvariants();
    }

    // T4: an owner who can pay keeps the plot and the fee goes to the treasury
    [AtlasScenario]
    public async Task PlotFee_Should_BePaidIntoTheTreasury_When_OwnerHasTheMoney()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fee 5");
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100000);
        money.SetBalance(owner.Player.PlayerUID, 20);

        await RunDay(s.Mayor);

        Assert.Equal(15m, money.GetBalance(owner.Player.PlayerUID));
        Assert.Contains($"Transfer {owner.Player.PlayerUID}->{s.City.MoneyAccountName} 5: Success", money.Log);
        Assert.True(Info(owner).Equals(plot.getPlotOwner()), "an owner who paid lost the plot");
        AssertInvariants();
    }

    // ---- T3 ----------------------------------------------------------------------------------

    // T3: with DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE off, who cannot pay stays a citizen
    [AtlasScenario]
    public async Task Citizen_Should_Stay_When_HeCannotPayAndDeletingIsOff()
    {
        // Arrange
        CityScene s = await GivenCity("broke");
        ITestPlayer broke = s.Citizens[0];
        bool before = Mod.config.DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE;
        AddCleanup(() => Mod.config.DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE = before);
        Mod.config.DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE = false;
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 10");
        Assert.Equal(10, s.City.fee);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100000);
        money.SetBalance(broke.Player.PlayerUID, 3);

        // Act
        await RunDay(s.Mayor);

        // Assert
        Assert.True(s.City.Equals(Info(broke).City), "a citizen who could not pay was removed with deleting off");
        Assert.Contains(Info(broke), s.City.getCityCitizens());
        Assert.Equal(3m, money.GetBalance(broke.Player.PlayerUID));
        AssertInvariants();
    }
}
