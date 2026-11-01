using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Edge cases of /city summon tp, ported from CAN_claims SummonScenarios (SU2, SU5, SU7, SU10,
/// SU12, SU13, SU14): the price boundary, a failing withdrawal, a second summon while one waits,
/// losing the city or the point while waiting, and points of other cities. The plain flow (set,
/// tp, cooldown, cancel, not enough money) is in AgreeAndSummonScenarios.
/// </summary>
[Trait("Category", "E2E")]
public class SummonEdgeScenarios : CxScenarioBase
{
    private const decimal Payment = 5;
    private const decimal Start = 100;
    private const string PointName = "Square";
    // The teleport timer runs every 250 ms; a few seconds of ticks.
    private const int TeleportTicks = 150;

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        bool allowed = c.SUMMON_ALLOWED, cancelOnMove = c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION;
        double payment = c.SUMMON_PAYMENT;
        int minPlayers = c.SUMMON_MIN_PLAYERS, time = c.SECONDS_SUMMON_TIME, cooldown = c.SECONDS_SUMMON_COOLDOWN;
        HashSet<string> disabled = c.DISABLED_PLOT_TYPES;
        AddCleanup(() =>
        {
            c.SUMMON_ALLOWED = allowed;
            c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = cancelOnMove;
            c.SUMMON_PAYMENT = payment;
            c.SUMMON_MIN_PLAYERS = minPlayers;
            c.SECONDS_SUMMON_TIME = time;
            c.SECONDS_SUMMON_COOLDOWN = cooldown;
            c.DISABLED_PLOT_TYPES = disabled;
        });
        c.SUMMON_ALLOWED = true;
        c.SUMMON_PAYMENT = (double)Payment;
        c.SUMMON_MIN_PLAYERS = 0;
        c.SECONDS_SUMMON_TIME = 1;
        c.SECONDS_SUMMON_COOLDOWN = 600;
        c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = false;
        c.DISABLED_PLOT_TYPES = new HashSet<string>((disabled ?? new HashSet<string>()).Where(t => t != "summon"));
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static string Account(ITestPlayer player) => player.Player.PlayerUID;

    private static string TpCmd(string point) => $"{CityCmd} summon tp {point}";

    private static bool Waiting(ITestPlayer player) => TeleportationHandler.IsWaiting(Account(player));

    private static bool At(ITestPlayer player, Vec3d point) =>
        Math.Abs(player.Position.X - Math.Floor(point.X)) <= 1 && Math.Abs(player.Position.Z - Math.Floor(point.Z)) <= 1;

    /// <summary>A spot of the area well away from the summon plot (box 0), on no plot.</summary>
    private BlockPos Away(CityScene s, int slot = 3) => Middle(PlotBox(s.Area, slot));

    /// <summary>
    /// Raises the summon limit of the city's current level to allow one more summon plot (put
    /// back on Dispose), makes box 0 a summon plot and has the mayor set the point
    /// <see cref="PointName"/> in it, feet and head free. Runs before any fake economy, so the
    /// type change is free.
    /// </summary>
    private async Task<(Plot Plot, Vec3d Point)> GivenSummonPoint(CityScene s)
    {
        CityLevelInfo level = Settings.GetCityLevelInfo(s.City.getCityCitizens().Count);
        int previous = level.SummonPoints;
        AddCleanup(() => level.SummonPoints = previous);
        int summonPlots = s.City.getCityPlots().Count(p => p.Type == PlotType.SUMMON);
        level.SummonPoints = Math.Max(previous, summonPlots + 1);

        Plot plot = await GivenPlot(s);
        await RunPaid(s.Mayor, $"{PlotCmd} set type summon", () => plot.Type == PlotType.SUMMON, "the plot to become a summon plot");
        Assert.Equal(PlotType.SUMMON, plot.Type);

        BlockPos feet = Middle(plot.Shape.Boxes[0]);
        World.SetBlock("game:air", feet);
        World.SetBlock("game:air", feet.UpCopy());
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} summon set {PointName}");
        Assert.True(set.Ok, "precondition: /city summon set: " + set.Message);
        var desc = Assert.IsType<PlotDescSummon>(plot.Desc);
        Assert.NotNull(desc.Point);
        return (plot, desc.Point.Clone());
    }

    /// <summary>A fake economy where the player has <see cref="Start"/>.</summary>
    private FakeMoneyProvider GivenMoney(ITestPlayer player, decimal balance = Start)
    {
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(player), balance);
        return money;
    }

    /// <summary>
    /// Drops a summon still waiting at the end of a scenario through the command, so its refund
    /// goes to the fake economy and not to the host's after Dispose.
    /// </summary>
    private static async Task CancelLeftover(ITestPlayer player)
    {
        if (Waiting(player)) await player.ExecuteCommand($"{CityCmd} summon cancel");
    }

    // ---- SU2: the price boundary ---------------------------------------------------------------

    // SU2: a coin short is refused and takes nothing; exactly the price is enough and takes all of it
    [AtlasScenario]
    public async Task SummonTp_Should_FollowTheMoney_AtThePriceBoundary()
    {
        // Arrange: the point, the mayor away with a coin short of the price; a long wait to look at it
        CityScene s = await GivenCity();
        await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 60;
        FakeMoneyProvider money = GivenMoney(s.Mayor, Payment - 0.01m);

        // Act 1: a coin short
        CommandResult shortTp = await s.Mayor.ExecuteCommand(TpCmd(PointName));

        // Assert 1
        AssertRefused(shortTp, "summon_not_enough_money", "a summon 0.01 short of the price");
        Assert.False(Waiting(s.Mayor), "a teleport was booked 0.01 short of the price");
        Assert.Equal(Payment - 0.01m, money.GetBalance(Account(s.Mayor)));

        // Act 2: exactly the price
        money.SetBalance(Account(s.Mayor), Payment);
        CommandResult exact = await s.Mayor.ExecuteCommand(TpCmd(PointName));

        // Assert 2
        Assert.True(exact.Ok, "a summon with exactly the price: " + exact.Message);
        Assert.True(Waiting(s.Mayor), "no teleport booked with exactly the price");
        Assert.Equal(0m, money.GetBalance(Account(s.Mayor)));

        await CancelLeftover(s.Mayor);
        Assert.Equal(Payment, money.GetBalance(Account(s.Mayor)));
    }

    // ---- SU5: the withdrawal fails ---------------------------------------------------------------

    // SU5: the balance is enough but the economy refuses the withdrawal - nothing is booked or taken
    [AtlasScenario]
    public async Task SummonTp_Should_NotHappen_When_TheWithdrawalFails()
    {
        // Arrange
        CityScene s = await GivenCity();
        Vec3d point = (await GivenSummonPoint(s)).Point;
        await s.Mayor.TeleportTo(Away(s));
        FakeMoneyProvider money = GivenMoney(s.Mayor);
        money.FailNext(FakeMoneyOp.Withdraw, Account(s.Mayor));

        // Act
        CommandResult tp = await s.Mayor.ExecuteCommand(TpCmd(PointName));
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        AssertRefused(tp, "economy_money_transaction_error", "a summon whose withdrawal failed");
        Assert.False(Waiting(s.Mayor), "a teleport was booked without the money taken");
        Assert.False(At(s.Mayor, point), "teleported without the money taken");
        Assert.Equal(Start, money.GetBalance(Account(s.Mayor)));
        Assert.DoesNotContain(money.Log, l => l.StartsWith("Deposit " + Account(s.Mayor)));
    }

    // ---- SU7: a second summon while the first waits ---------------------------------------------

    // SU7: /city summon tp again while waiting replaces the first; the price is held once, not twice
    [AtlasScenario]
    public async Task SecondSummon_Should_NotChargeAgain_While_TheFirstIsWaiting()
    {
        // Arrange: one summon waiting
        CityScene s = await GivenCity();
        await GivenSummonPoint(s);
        await s.Mayor.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 60;
        FakeMoneyProvider money = GivenMoney(s.Mayor);
        CommandResult first = await s.Mayor.ExecuteCommand(TpCmd(PointName));
        Assert.True(first.Ok && Waiting(s.Mayor), "precondition: the first summon is waiting: " + first.Message);
        Assert.Equal(Start - Payment, money.GetBalance(Account(s.Mayor)));

        // Act
        CommandResult second = await s.Mayor.ExecuteCommand(TpCmd(PointName));

        // Assert: still one summon, paid once
        Assert.True(second.Ok, "the second summon: " + second.Message);
        Assert.True(Waiting(s.Mayor), "no summon is waiting after the second one");
        Assert.Equal(Start - Payment, money.GetBalance(Account(s.Mayor)));

        // and cancelling it gives back everything
        await CancelLeftover(s.Mayor);
        Assert.False(Waiting(s.Mayor), "a summon is still waiting after cancel");
        Assert.Equal(Start, money.GetBalance(Account(s.Mayor)));
    }

    // ---- SU10: the player left the city while waiting -------------------------------------------

    // SU10: kicked or gone while the summon waits - no teleport into the city, the money comes back
    [AtlasTheory]
    [InlineData("kick")]
    [InlineData("leave")]
    public async Task SummonTp_Should_NotHappen_When_ThePlayerLeftTheCityMeanwhile(string how)
    {
        // Arrange: a citizen away from the point, summon waiting
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        Vec3d point = (await GivenSummonPoint(s)).Point;
        await citizen.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 2;
        FakeMoneyProvider money = GivenMoney(citizen);
        CommandResult tp = await citizen.ExecuteCommand(TpCmd(PointName));
        Assert.True(tp.Ok && Waiting(citizen), "precondition: the summon is waiting: " + tp.Message);

        // Act
        if (how == "kick") await s.Mayor.ExecuteCommand($"{CityCmd} kick {Name(citizen)}");
        else await citizen.ExecuteCommand($"{CityCmd} leave");
        Assert.False(Info(citizen).hasCity(), $"precondition: the citizen is out of the city ({how})");
        await World.Until(() => !Waiting(citizen), "the summon to run out", TeleportTicks);

        // Assert
        Assert.False(At(citizen, point), $"a player out of the city ({how}) was still teleported into it");
        Assert.Equal(Start, money.GetBalance(Account(citizen)));
        AssertInvariants();
    }

    // ---- SU12: another city's point ------------------------------------------------------------

    // SU12: points are looked up in the caller's own city only - by name and by number
    [AtlasTheory]
    [InlineData("name")]
    [InlineData("number")]
    public async Task SummonTp_Should_NotReach_AnotherCitysPoint(string by)
    {
        // Arrange: our city with a point, their city with none; their mayor can pay
        CityScene theirs = await GivenCity();
        CityScene ours = await GivenCity();
        Vec3d point = (await GivenSummonPoint(ours)).Point;
        Assert.Empty(SummonRules.PointsOf(theirs.City));
        FakeMoneyProvider money = GivenMoney(theirs.Mayor);
        string wanted = by == "name" ? PointName : "1";

        // Act
        CommandResult tp = await theirs.Mayor.ExecuteCommand(TpCmd(wanted));
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        AssertRefused(tp, "summon_no_such_point", $"a foreign mayor's summon to our point by {by}");
        Assert.False(Waiting(theirs.Mayor), "a foreign mayor booked a teleport to our summon point");
        Assert.False(At(theirs.Mayor, point), "a foreign mayor arrived at our summon point");
        Assert.Equal(Start, money.GetBalance(Account(theirs.Mayor)));
    }

    // ---- SU14: the point's plot leaves the city or stops being a summon plot ---------------------

    /// <summary>
    /// The mayor, standing in the plot, deletes it (/plot delete: the only way a plot leaves its
    /// city here) or turns it back into a plain plot; the treasury is filled in case it costs.
    /// </summary>
    private async Task TakeThePointAway(CityScene s, Plot plot, FakeMoneyProvider money, string how)
    {
        money.SetBalance(s.City.MoneyAccountName, 100000);
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        if (how == "delete")
        {
            CommandResult delete = await s.Mayor.ExecuteCommand($"{PlotCmd} delete");
            Assert.False(s.City.getCityPlots().Contains(plot), "precondition: /plot delete took the plot out of the city: " + delete.Message);
        }
        else
        {
            CommandResult type = await RunPaid(s.Mayor, $"{PlotCmd} set type default", () => plot.Type == PlotType.DEFAULT, "the plot to become plain");
            Assert.True(plot.Type == PlotType.DEFAULT, "precondition: /plot set type default: " + type.Message);
        }
    }

    // SU14: once the plot is deleted or no summon plot any more, its point is not listed and cannot be used
    [AtlasTheory]
    [InlineData("delete")]
    [InlineData("default")]
    public async Task SummonPoint_Should_BeGone_When_ItsPlotIsDeletedOrNoSummonPlot(string how)
    {
        // Arrange
        CityScene s = await GivenCity();
        (Plot plot, Vec3d point) = await GivenSummonPoint(s);
        FakeMoneyProvider money = GivenMoney(s.Mayor);

        // Act
        await TakeThePointAway(s, plot, money, how);
        await s.Mayor.TeleportTo(Away(s));
        CommandResult byName = await s.Mayor.ExecuteCommand(TpCmd(PointName));
        CommandResult byNumber = await s.Mayor.ExecuteCommand(TpCmd("1"));
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        Assert.DoesNotContain(SummonRules.PointsOf(s.City), p => p.Plot.Equals(plot));
        AssertRefused(byName, "summon_no_such_point", $"a summon by name to the point after {how}");
        AssertRefused(byNumber, "summon_no_such_point", $"a summon by number to the point after {how}");
        Assert.False(Waiting(s.Mayor), $"a teleport was booked to the point after {how}");
        Assert.False(At(s.Mayor, point), $"teleported to the point after {how}");
        Assert.Equal(Start, money.GetBalance(Account(s.Mayor)));
        AssertInvariants();
    }

    // SU14: the plot is deleted or made plain while a summon to it waits - no teleport, money back
    [AtlasTheory]
    [InlineData("delete")]
    [InlineData("default")]
    public async Task WaitingSummon_Should_BeRefunded_When_ItsPlotIsDeletedOrNoSummonPlot(string how)
    {
        // Arrange: a citizen away from the point, summon waiting
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        (Plot plot, Vec3d point) = await GivenSummonPoint(s);
        await citizen.TeleportTo(Away(s));
        Mod.config.SECONDS_SUMMON_TIME = 2;
        FakeMoneyProvider money = GivenMoney(citizen);
        CommandResult tp = await citizen.ExecuteCommand(TpCmd(PointName));
        Assert.True(tp.Ok && Waiting(citizen), "precondition: the summon is waiting: " + tp.Message);

        // Act
        await TakeThePointAway(s, plot, money, how);
        await World.Until(() => !Waiting(citizen), "the summon to run out", TeleportTicks);

        // Assert
        Assert.False(At(citizen, point), $"teleported to the point after {how}");
        Assert.Equal(Start, money.GetBalance(Account(citizen)));
        AssertInvariants();
    }

    // ---- SU13: after leaving the city -------------------------------------------------------------

    // SU13: a former citizen has no city to summon in; nothing booked, nothing taken
    [AtlasScenario]
    public async Task SummonTp_Should_BeRefused_AfterLeavingTheCity()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        Vec3d point = (await GivenSummonPoint(s)).Point;
        await citizen.TeleportTo(Away(s));
        await citizen.ExecuteCommand($"{CityCmd} leave");
        Assert.False(Info(citizen).hasCity(), "precondition: the citizen left");
        FakeMoneyProvider money = GivenMoney(citizen);

        // Act
        CommandResult tp = await citizen.ExecuteCommand(TpCmd(PointName));
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        AssertRefused(tp, "you_dont_have_city", "a summon by a former citizen");
        Assert.False(Waiting(citizen), "a former citizen booked a teleport into the city");
        Assert.False(At(citizen, point), "a former citizen arrived at the summon point");
        Assert.Equal(Start, money.GetBalance(Account(citizen)));
        AssertInvariants();
    }
}
