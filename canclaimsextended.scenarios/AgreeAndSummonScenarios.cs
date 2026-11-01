using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.economy;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Paid actions with an economy (T1): nothing happens before /agree, and /agree checks everything
/// again before it takes money (PaidAction). Summon points and the paid, delayed teleport to them
/// (L1, L2, L3, L4). Sections L and T of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class AgreeAndSummonScenarios : CxScenarioBase
{
    private const decimal Payment = 5;
    private const string PointName = "Square";
    // The teleport timer runs every 250 ms; a few seconds of ticks.
    private const int TeleportTicks = 150;

    private static string AgreeCmd => "/" + Mod.config.COMMAND_AGREE;

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        bool allowed = c.SUMMON_ALLOWED, cancelOnMove = c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION;
        double payment = c.SUMMON_PAYMENT;
        int minPlayers = c.SUMMON_MIN_PLAYERS, time = c.SECONDS_SUMMON_TIME, cooldown = c.SECONDS_SUMMON_COOLDOWN;
        AddCleanup(() =>
        {
            c.SUMMON_ALLOWED = allowed;
            c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = cancelOnMove;
            c.SUMMON_PAYMENT = payment;
            c.SUMMON_MIN_PLAYERS = minPlayers;
            c.SECONDS_SUMMON_TIME = time;
            c.SECONDS_SUMMON_COOLDOWN = cooldown;
        });
        c.SUMMON_ALLOWED = true;
        c.SUMMON_PAYMENT = (double)Payment;
        c.SUMMON_MIN_PLAYERS = 0;
        c.SECONDS_SUMMON_TIME = 1;
        c.SECONDS_SUMMON_COOLDOWN = 600;
        c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = false;
    }

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static string Account(ITestPlayer player) => player.Player.PlayerUID;

    // ---- T1: paid actions wait for /agree --------------------------------------------------

    // T1: with an economy /city create only makes an offer; /agree founds the city and charges it
    [AtlasScenario]
    public async Task CityCreate_Should_WaitForAgree_And_ChargeOnAgree()
    {
        // Arrange: a founder on fresh land with the price and 100 more
        ResetConfig();
        int area = NewArea();
        string name = CityName(area);
        ITestPlayer founder = await JoinAt("founder" + area, area);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = (decimal)PlotPricing.NewCityCost();
        money.SetBalance(Account(founder), cost + 100);

        // Act 1: the offer
        CommandResult offer = await founder.ExecuteCommand($"{CityCmd} create {name}");
        await World.Ticks(AgreeTimeoutTicks);

        // Assert 1: nothing yet
        Assert.True(offer.Ok, "/city create with an economy: " + offer.Message);
        Assert.Equal("canclaimsextended:agree_new_city", offer.Raw.StatusMessage);
        Assert.False(Info(founder).hasCity(), "the city was founded before /agree");
        Assert.False(Mod.dataStorage.CityExistsByName(name), "a city of that name exists before /agree");
        Assert.Equal(cost + 100, money.GetBalance(Account(founder)));

        // Act 2: the agreement
        CommandResult agree = await founder.ExecuteCommand(AgreeCmd);
        await World.Until(() => Info(founder).hasCity(), "the city to be founded on /agree", AgreeTimeoutTicks);

        // Assert 2
        Assert.True(agree.Ok, "/agree: " + agree.Message);
        Assert.Equal(name, Info(founder).City.GetPartName());
        Assert.Equal(100m, money.GetBalance(Account(founder)));
        AssertInvariants();
    }

    // T1: /plot create with an economy waits for /agree; the treasury pays the claim price on it
    [AtlasScenario]
    public async Task PlotCreate_Should_WaitForAgree_And_ChargeTheTreasuryOnAgree()
    {
        // Arrange: a selection the mayor stands in, a rich treasury
        CityScene s = await GivenCity();
        var box = PlotBox(s.Area);
        Select(s.Mayor, box);
        await StandIn(s.Mayor, box);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100000);
        decimal cost = (decimal)PlotPricing.ClaimCost(box);
        int plotsBefore = s.City.getCityPlots().Count;

        // Act 1: the offer
        CommandResult offer = await s.Mayor.ExecuteCommand($"{PlotCmd} create");
        await World.Ticks(AgreeTimeoutTicks);

        // Assert 1: nothing yet
        Assert.True(offer.Ok, "/plot create with an economy: " + offer.Message);
        Assert.Equal("canclaimsextended:agree_claim", offer.Raw.StatusMessage);
        Assert.Null(PlotIn(box));
        Assert.Equal(plotsBefore, s.City.getCityPlots().Count);
        Assert.Equal(100000m, money.GetBalance(s.City.MoneyAccountName));

        // Act 2
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Until(() => PlotIn(box) != null, "the plot to be created on /agree", AgreeTimeoutTicks);

        // Assert 2
        Assert.True(s.City.Equals(PlotIn(box)!.getCity()), "the plot does not belong to the city");
        Assert.Equal(100000m - cost, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // T1, PaidAction: the condition changed while waiting (the founder joined a city) - /agree
    // founds nothing and takes nothing
    [AtlasScenario]
    public async Task Agree_Should_BeRefusedAndChargeNothing_When_ConditionChangedWhileWaiting()
    {
        // Arrange: an open city to join, and a founder on his own land asking to found one
        CityScene s = await GivenCity();
        int area = NewArea();
        string name = CityName(area);
        ITestPlayer founder = await JoinAt("turncoat" + area, area);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = (decimal)PlotPricing.NewCityCost() + 100;
        money.SetBalance(Account(founder), start);
        CommandResult offer = await founder.ExecuteCommand($"{CityCmd} create {name}");
        Assert.Equal("canclaimsextended:agree_new_city", offer.Raw.StatusMessage);
        await AddCitizen(s.City, s.Mayor, founder);

        // Act
        await founder.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        Assert.True(s.City.Equals(Info(founder).City), "the founder is no longer a citizen of the city he joined");
        Assert.False(Mod.dataStorage.CityExistsByName(name), "a city was founded on an outdated offer");
        Assert.Equal(start, money.GetBalance(Account(founder)));
        Assert.DoesNotContain(money.Log, l => l.StartsWith("Withdraw " + Account(founder)));
        AssertInvariants();
    }

    // T1, AGREEMENT_TIMEOUT_SECONDS: an offer left too long is gone; /agree finds nothing
    [AtlasScenario]
    public async Task Agree_Should_DoNothing_When_OfferTimedOut()
    {
        ResetConfig();
        Mod.config.AGREEMENT_TIMEOUT_SECONDS = 1;
        int area = NewArea();
        string name = CityName(area);
        ITestPlayer founder = await JoinAt("slow" + area, area);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = (decimal)PlotPricing.NewCityCost() + 100;
        money.SetBalance(Account(founder), start);
        await founder.ExecuteCommand($"{CityCmd} create {name}");
        await World.Ticks(90); // about three seconds

        CommandResult agree = await founder.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        AssertRefused(agree, "no_agreements_awaits", "/agree after the timeout");
        Assert.False(Info(founder).hasCity(), "a timed-out offer founded the city");
        Assert.Equal(start, money.GetBalance(Account(founder)));
    }

    // ---- L: summon -------------------------------------------------------------------------

    /// <summary>
    /// Lets a city of one citizen hold a summon plot (/cadmin level set 1 summon 1, put back after
    /// the scenario) and makes box 0 a summon plot with the mayor standing in it, feet and head free.
    /// </summary>
    private async Task<Plot> GivenSummonPlot(CityScene s)
    {
        Assert.True(Settings.cityLevelsDict.ContainsKey(1), "precondition: a city level for 1 citizen");
        Assert.Single(s.City.getCityCitizens());
        int previous = Settings.cityLevelsDict[1].SummonPoints;
        AddCleanup(() => Settings.TrySetLevelField(1, "summon", previous, out _, out _));
        AllowAdminCommands(s.Mayor);
        CommandResult level = await s.Mayor.ExecuteCommand($"{CadminCmd} level set 1 summon 1");
        Assert.True(level.Ok, "precondition: /cadmin level set 1 summon 1: " + level.Message);

        Plot plot = await GivenPlot(s);
        await RunPaid(s.Mayor, $"{PlotCmd} set type summon", () => plot.Type == PlotType.SUMMON, "the plot to become a summon plot");
        Assert.Equal(PlotType.SUMMON, plot.Type);

        BlockPos feet = Middle(plot.Shape.Boxes[0]);
        World.SetBlock("game:air", feet);
        World.SetBlock("game:air", feet.UpCopy());
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        return plot;
    }

    /// <summary>A summon point named <see cref="PointName"/> where the mayor stands; returns where it is.</summary>
    private async Task<Vec3d> GivenSummonPoint(CityScene s)
    {
        Plot plot = await GivenSummonPlot(s);
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} summon set {PointName}");
        Assert.True(set.Ok, "precondition: /city summon set: " + set.Message);
        var desc = Assert.IsType<PlotDescSummon>(plot.Desc);
        Assert.NotNull(desc.Point);
        return desc.Point.Clone();
    }

    /// <summary>A spot of the area well away from the summon plot (box 0), on no plot.</summary>
    private BlockPos Away(CityScene s, int slot = 3) => Middle(PlotBox(s.Area, slot));

    private static bool At(ITestPlayer player, Vec3d point) =>
        Math.Abs(player.Position.X - Math.Floor(point.X)) <= 1 && Math.Abs(player.Position.Z - Math.Floor(point.Z)) <= 1;

    private static bool Waiting(ITestPlayer player) => TeleportationHandler.IsWaiting(Account(player));

    // L1: the point is set where the mayor stands, under the given name
    [AtlasScenario]
    public async Task SummonSet_Should_PlaceThePoint_When_MayorStandsInSummonPlot()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenSummonPlot(s);
        BlockPos here = s.Mayor.Position;

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} summon set {PointName}");

        Assert.True(set.Ok, "/city summon set: " + set.Message);
        var desc = Assert.IsType<PlotDescSummon>(plot.Desc);
        Assert.Equal(PointName, desc.Name);
        Assert.NotNull(desc.Point);
        Assert.Equal(here.X, (int)Math.Floor(desc.Point.X));
        Assert.Equal(here.Z, (int)Math.Floor(desc.Point.Z));
        AssertInvariants();
    }

    // L1: without a name the point is called point1
    [AtlasScenario]
    public async Task SummonSet_Should_NameThePointPoint1_When_NoNameIsGiven()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenSummonPlot(s);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} summon set");

        Assert.True(set.Ok, "/city summon set without a name: " + set.Message);
        Assert.Equal("point1", Assert.IsType<PlotDescSummon>(plot.Desc).Name);
    }

    // L1: a plain plot takes no summon point
    [AtlasScenario]
    public async Task SummonSet_Should_BeRefused_When_PlotIsNoSummonPlot()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} summon set {PointName}");

        AssertRefused(set, "need_summon_plot", "a summon point on a plain plot");
        Assert.IsNotType<PlotDescSummon>(plot.Desc);
    }

    // L2: /city summon tp takes SUMMON_PAYMENT at once and teleports after SECONDS_SUMMON_TIME
    [AtlasScenario]
    public async Task SummonTp_Should_ChargeAndTeleport_AfterTheWait()
    {
        // Arrange: the point, the mayor away from it with 100
        CityScene s = await GivenCity();
        Vec3d point = await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 2;
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(s.Mayor), 100);

        // Act
        CommandResult tp = await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");

        // Assert: paid and waiting, then there
        Assert.True(tp.Ok, "/city summon tp: " + tp.Message);
        Assert.Equal(100m - Payment, money.GetBalance(Account(s.Mayor)));
        Assert.True(Waiting(s.Mayor), "the teleport is not waiting for SECONDS_SUMMON_TIME");
        Assert.False(At(s.Mayor, point), "teleported before SECONDS_SUMMON_TIME");
        await World.Until(() => At(s.Mayor, point), "the mayor to arrive at the summon point", TeleportTicks);
        Assert.False(Waiting(s.Mayor), "the teleport is still waiting after it happened");
        Assert.Equal(100m - Payment, money.GetBalance(Account(s.Mayor)));
        AssertInvariants();
    }

    // L2: a point can also be chosen by its number in the list
    [AtlasScenario]
    public async Task SummonTp_Should_FindThePoint_ByItsNumber()
    {
        CityScene s = await GivenCity();
        Vec3d point = await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));

        CommandResult tp = await s.Mayor.ExecuteCommand($"{CityCmd} summon tp 1");

        Assert.True(tp.Ok, "/city summon tp 1: " + tp.Message);
        await World.Until(() => At(s.Mayor, point), "the mayor to arrive at point 1", TeleportTicks);
    }

    // L4: again before the cooldown is over is refused, and charges nothing
    [AtlasScenario]
    public async Task SummonTp_Should_BeRefused_DuringTheCooldown()
    {
        // Arrange: one summon done
        CityScene s = await GivenCity();
        Vec3d point = await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(s.Mayor), 100);
        await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");
        await World.Until(() => At(s.Mayor, point), "the first summon", TeleportTicks);
        await s.Mayor.TeleportTo(Away(s));

        // Act
        CommandResult again = await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");

        // Assert
        AssertRefused(again, "summon_cooldown", "a summon during the cooldown");
        Assert.False(Waiting(s.Mayor), "a teleport was booked during the cooldown");
        Assert.Equal(100m - Payment, money.GetBalance(Account(s.Mayor)));
    }

    // L3: moving while waiting cancels the summon and gives the payment back
    [AtlasScenario]
    public async Task SummonTp_Should_BeCancelledAndRefunded_When_PlayerMoves()
    {
        // Arrange: a long wait, movement cancels
        CityScene s = await GivenCity();
        Vec3d point = await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        await World.Ticks(10); // settle where the summon starts from
        Mod.config.SECONDS_SUMMON_TIME = 60;
        Mod.config.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = true;
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(s.Mayor), 100);
        await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");
        Assert.True(Waiting(s.Mayor), "precondition: the summon is waiting");
        Assert.Equal(100m - Payment, money.GetBalance(Account(s.Mayor)));

        // Act: a few blocks away
        await s.Mayor.TeleportTo(Away(s, slot: 2));
        await World.Until(() => !Waiting(s.Mayor), "the move to cancel the summon", TeleportTicks);

        // Assert
        Assert.Equal(100m, money.GetBalance(Account(s.Mayor)));
        Assert.False(At(s.Mayor, point), "teleported although the player moved");
    }

    // L3: /city summon cancel gives the payment back
    [AtlasScenario]
    public async Task SummonCancel_Should_RefundThePayment()
    {
        CityScene s = await GivenCity();
        await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 60;
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(s.Mayor), 100);
        await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");
        Assert.True(Waiting(s.Mayor), "precondition: the summon is waiting");

        CommandResult cancel = await s.Mayor.ExecuteCommand($"{CityCmd} summon cancel");

        Assert.True(cancel.Ok, "/city summon cancel: " + cancel.Message);
        Assert.False(Waiting(s.Mayor), "the summon still waits after cancel");
        Assert.Equal(100m, money.GetBalance(Account(s.Mayor)));
    }

    // L4: without the payment nothing is booked and nothing taken
    [AtlasScenario]
    public async Task SummonTp_Should_BeRefused_When_PlayerCannotPay()
    {
        CityScene s = await GivenCity();
        await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(s.Mayor), Payment - 1);

        CommandResult tp = await s.Mayor.ExecuteCommand($"{CityCmd} summon tp {PointName}");

        AssertRefused(tp, "summon_not_enough_money", "a summon a coin short");
        Assert.False(Waiting(s.Mayor), "a teleport was booked without the money");
        Assert.Equal(Payment - 1, money.GetBalance(Account(s.Mayor)));
    }
}
