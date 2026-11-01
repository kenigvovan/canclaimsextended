using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.events;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Getting into prison by being killed, serving the sentence, the way out, cells, and the commands
/// a prisoner may not run. Ported from CAN_claims PrisonScenarios and HourTimerScenarios.
/// Here a sentence is a release time (PlayerInfo.PrisonReleaseAt, epoch seconds) that the
/// 2-second tick of PrisonHandler checks, not a count of hours; only a criminal of the killer's
/// city is jailed (PrisonHandler.TryImprison), wherever the kill is; a prisoner who walks out of
/// the prison plot is put back into a cell, not freed.
/// </summary>
[Trait("Category", "E2E")]
public class PrisonLifecycleScenarios : CxScenarioBase
{
    private const string PointName = "Home";
    // PrisonHandler ticks every 2 s; a sentence of a few seconds plus that, with room to spare.
    private const int PrisonTicks = 300;

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        int hours = c.PRISON_SENTENCE_HOURS, maxCells = c.MAX_CELLS_PER_PRISON;
        HashSet<string> blocked = c.BLOCKED_COMMANDS_PRISON;
        bool allowed = c.SUMMON_ALLOWED, cancelOnMove = c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION;
        int minPlayers = c.SUMMON_MIN_PLAYERS, time = c.SECONDS_SUMMON_TIME;
        AddCleanup(() =>
        {
            c.PRISON_SENTENCE_HOURS = hours;
            c.MAX_CELLS_PER_PRISON = maxCells;
            c.BLOCKED_COMMANDS_PRISON = blocked;
            c.SUMMON_ALLOWED = allowed;
            c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = cancelOnMove;
            c.SUMMON_MIN_PLAYERS = minPlayers;
            c.SECONDS_SUMMON_TIME = time;
        });
        c.PRISON_SENTENCE_HOURS = 2;
        c.MAX_CELLS_PER_PRISON = 4;
        c.BLOCKED_COMMANDS_PRISON = new HashSet<string> { "summon" };
        c.SUMMON_ALLOWED = true;
        c.SUMMON_MIN_PLAYERS = 0;
        // Long enough that a booked summon is still waiting when the scenario looks.
        c.SECONDS_SUMMON_TIME = 60;
        c.PLAYER_MOVEMENT_CANCEL_TELEPORTATION = false;
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Box 0 of the scene made a prison with one cell, where the mayor stands.</summary>
    private async Task<Plot> GivenPrison(CityScene s)
    {
        Plot plot = await GivenPlot(s);
        await RunPaid(s.Mayor, $"{PlotCmd} set type prison", () => plot.Type == PlotType.PRISON, "the plot to become a prison");
        CommandResult cell = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");
        Assert.True(cell.Ok, "precondition: a cell was added: " + cell.Message);
        return plot;
    }

    /// <summary>The mayor lists the player as a criminal of the city.</summary>
    private async Task GivenCriminal(CityScene s, ITestPlayer player)
    {
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(player)}");
        Assert.True(add.Ok, "precondition: /city criminal add: " + add.Message);
        Assert.Contains(player.Player.PlayerUID, s.City.Criminals);
    }

    /// <summary>The death event as the game raises it, without a fight.</summary>
    private static void Kill(ITestPlayer killer, ITestPlayer victim) =>
        OnPlayerDeath.Event_OnPlayerDeath(victim.Player,
            new DamageSource { Source = EnumDamageSource.Player, SourceEntity = killer.Player.Entity });

    private static bool InPlot(Plot plot, ITestPlayer player) =>
        plot.Shape.Contains(player.Position.X, player.Position.Y, player.Position.Z);

    private bool AtWorldSpawn(ITestPlayer player, int within = 3)
    {
        EntityPos spawn = World.Api.World.DefaultSpawnPosition;
        return Math.Abs(player.Position.X - spawn.X) <= within && Math.Abs(player.Position.Z - spawn.Z) <= within;
    }

    private static bool Waiting(ITestPlayer player) => TeleportationHandler.IsWaiting(player.Player.PlayerUID);

    /// <summary>
    /// A prisoner with a city of their own and a summon point there: <paramref name="theirs"/>'s
    /// mayor, a criminal of <paramref name="ours"/>, killed by its mayor and standing on its prison
    /// plot. Returns the prison plot.
    /// </summary>
    private async Task<Plot> GivenPrisonerWithASummonPoint(CityScene ours, CityScene theirs)
    {
        // Their summon point: level 1 (one citizen) may hold one summon plot until Dispose
        Assert.Single(theirs.City.getCityCitizens());
        int previous = Settings.cityLevelsDict[1].SummonPoints;
        AddCleanup(() => Settings.TrySetLevelField(1, "summon", previous, out _, out _));
        AllowAdminCommands(theirs.Mayor);
        CommandResult level = await theirs.Mayor.ExecuteCommand($"{CadminCmd} level set 1 summon 1");
        Assert.True(level.Ok, "precondition: /cadmin level set 1 summon 1: " + level.Message);
        Plot summon = await GivenPlot(theirs);
        await RunPaid(theirs.Mayor, $"{PlotCmd} set type summon", () => summon.Type == PlotType.SUMMON, "the plot to become a summon plot");
        BlockPos feet = Middle(summon.Shape.Boxes[0]);
        World.SetBlock("game:air", feet);
        World.SetBlock("game:air", feet.UpCopy());
        await StandIn(theirs.Mayor, summon.Shape.Boxes[0]);
        CommandResult set = await theirs.Mayor.ExecuteCommand($"{CityCmd} summon set {PointName}");
        Assert.True(set.Ok, "precondition: /city summon set: " + set.Message);

        // Our prison, and their mayor in it
        Plot prison = await GivenPrison(ours);
        await GivenCriminal(ours, theirs.Mayor);
        await StandIn(theirs.Mayor, prison.Shape.Boxes[0]);
        Kill(ours.Mayor, theirs.Mayor);
        Assert.True(Info(theirs.Mayor).isPrisoned(), "precondition: imprisoned");
        Assert.Contains("summon", Mod.config.BLOCKED_COMMANDS_PRISON);
        return prison;
    }

    // ---- getting in ------------------------------------------------------------------------

    // PZ1: a criminal of the city killed by its citizen goes to the city's prison
    [AtlasScenario]
    public async Task Criminal_Should_GoToPrison_When_KilledByACitizen()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot prison = await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        await GivenCriminal(s, stranger);

        // Act
        Kill(s.Mayor, stranger);

        // Assert
        Assert.True(Info(stranger).isPrisoned(), "the criminal killed by a citizen is not a prisoner");
        Assert.Equal(prison.Guid, Info(stranger).PrisonPlotGuid);
        // Not dead in fact: the tick puts the living prisoner into the cell
        await World.Until(() => InPlot(prison, stranger), "the prisoner to be moved into the prison", PrisonTicks);
    }

    // PZ1: a stranger who is no criminal of the killer's city stays free (the new mod's condition)
    [AtlasScenario]
    public async Task Stranger_Should_NotGoToPrison_When_HeIsNoCriminal()
    {
        CityScene s = await GivenCity();
        await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        Assert.DoesNotContain(stranger.Player.PlayerUID, s.City.Criminals);

        Kill(s.Mayor, stranger);

        Assert.False(Info(stranger).isPrisoned(), "a stranger who is no criminal was imprisoned");
    }

    // PZ5: a citizen cannot be listed as a criminal of their own city, so they never go to its prison
    [AtlasScenario]
    public async Task Citizen_Should_NotGoToTheirOwnCitysPrison()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        await GivenPrison(s);
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(citizen)}");

        // Act
        Kill(s.Mayor, citizen);

        // Assert
        Assert.False(add.Ok, "a citizen was listed as a criminal of their own city: " + add.Message);
        Assert.Equal("canclaimsextended:criminal_is_citizen", add.Raw.StatusMessage);
        Assert.False(Info(citizen).isPrisoned(), "a citizen was put in their own city's prison");
        AssertInvariants();
    }

    // ---- serving ---------------------------------------------------------------------------

    // PZ2: the sentence has an end, PRISON_SENTENCE_HOURS from the kill
    [AtlasScenario]
    public async Task Sentence_Should_HaveAnEnd()
    {
        // Arrange
        CityScene s = await GivenCity();
        await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        await GivenCriminal(s, stranger);
        long now = TimeFunctions.GetEpochSeconds();

        // Act
        Kill(s.Mayor, stranger);

        // Assert
        Assert.True(Info(stranger).isPrisoned(), "precondition: imprisoned");
        long left = Info(stranger).PrisonReleaseAt - now;
        Assert.InRange(left, 2 * 3600 - 10, 2 * 3600 + 10);
    }

    // PZ3, H2: kept while time is left, released when it runs out - to the world spawn
    [AtlasScenario]
    public async Task Prisoner_Should_BeReleasedToWorldSpawn_When_TheSentenceRunsOut()
    {
        // Arrange: never area 0, which starts at the world spawn
        NewArea();
        CityScene s = await GivenCity();
        Plot prison = await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        await GivenCriminal(s, stranger);
        Kill(s.Mayor, stranger);
        await World.Until(() => InPlot(prison, stranger), "the prisoner to be moved into the prison", PrisonTicks);
        Assert.False(AtWorldSpawn(stranger, 50), "precondition: the prison is far from the world spawn");
        Info(stranger).PrisonReleaseAt = TimeFunctions.GetEpochSeconds() + 3;

        // Act 1: a tick or so, the time not yet served
        await World.Ticks(20);

        // Assert 1
        Assert.True(Info(stranger).isPrisoned(), "released before the sentence ran out");

        // Act 2
        await World.Until(() => !Info(stranger).isPrisoned(), "the prisoner to be released when the time is served", PrisonTicks);

        // Assert 2
        Assert.Equal("", Info(stranger).PrisonPlotGuid);
        Assert.Equal(0L, Info(stranger).PrisonReleaseAt);
        await World.Until(() => AtWorldSpawn(stranger), "the released prisoner to be at the world spawn", 100);
    }

    // PZ7, O4, A17: the release leaves the spawn point the player had before the prison
    [AtlasScenario]
    public async Task Release_Should_KeepThePlayersOwnSpawnPoint()
    {
        // Arrange: the stranger's own spawn point (a bed, say) in box 3 of the area
        CityScene s = await GivenCity();
        await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        BlockPos home = Middle(PlotBox(s.Area, 3));
        stranger.Player.SetSpawnPosition(new PlayerSpawnPos(home.X, home.Y, home.Z));
        await GivenCriminal(s, stranger);
        Kill(s.Mayor, stranger);
        Assert.True(Info(stranger).isPrisoned(), "precondition: imprisoned");
        Info(stranger).PrisonReleaseAt = TimeFunctions.GetEpochSeconds() + 1;

        // Act
        await World.Until(() => !Info(stranger).isPrisoned(), "the prisoner to be released", PrisonTicks);

        // Assert
        FuzzyEntityPos spawn = stranger.Player.GetSpawnPosition(false);
        Assert.True(Math.Abs(spawn.X - home.X) <= 2 && Math.Abs(spawn.Z - home.Z) <= 2,
            $"the spawn is ({spawn.X:0.#}, {spawn.Z:0.#}), not the player's own ({home.X}, {home.Z})");
    }

    // PZ6 (adapted): walking out of the prison does not free; the prisoner is put back into a cell
    [AtlasScenario]
    public async Task Prisoner_Should_BeBroughtBack_When_HeLeavesThePrison()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot prison = await GivenPrison(s);
        ITestPlayer stranger = await GivenStranger(s);
        await GivenCriminal(s, stranger);
        Kill(s.Mayor, stranger);
        await World.Until(() => InPlot(prison, stranger), "the prisoner to be moved into the prison", PrisonTicks);

        // Act: out into the wild, well off the city's chunk
        await stranger.TeleportTo(AreaOrigin(s.Area).AddCopy(100, 0, 0));
        Assert.False(InPlot(prison, stranger), "precondition: the prisoner is out of the prison");

        // Assert
        await World.Until(() => InPlot(prison, stranger), "the escaped prisoner to be put back into the prison", PrisonTicks);
        Assert.True(Info(stranger).isPrisoned(), "leaving the prison set the prisoner free");
        Assert.Equal(prison.Guid, Info(stranger).PrisonPlotGuid);
    }

    // ---- cells -----------------------------------------------------------------------------

    // PZ8: /city prison addcell stops at MAX_CELLS_PER_PRISON
    [AtlasScenario]
    public async Task Cells_Should_StopAtTheLimit()
    {
        // Arrange: a prison with one cell, room for two
        CityScene s = await GivenCity();
        Plot prison = await GivenPrison(s);
        var desc = Assert.IsType<PlotDescPrison>(prison.Desc);
        Mod.config.MAX_CELLS_PER_PRISON = 2;
        BlockPos middle = Middle(prison.Shape.Boxes[0]);
        await s.Mayor.TeleportTo(middle.AddCopy(1, 0, 0));
        CommandResult second = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");
        Assert.True(second.Ok, "precondition: the second cell: " + second.Message);
        Assert.Equal(2, desc.Cells.Count);

        // Act: a third, on a block of its own
        await s.Mayor.TeleportTo(middle.AddCopy(-1, 0, 0));
        CommandResult third = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");

        // Assert
        Assert.False(third.Ok, "a cell over the limit was added: " + third.Message);
        Assert.Equal("canclaimsextended:prison_cells_max", third.Raw.StatusMessage);
        Assert.Equal(2, desc.Cells.Count);
    }

    // ---- commands in prison ----------------------------------------------------------------

    // SU8: a prisoner standing on the prison plot cannot summon himself home (chat path)
    [AtlasScenario]
    public async Task Prisoner_Should_NotSummonAway_FromThePrisonPlot()
    {
        // Arrange
        CityScene ours = await GivenCity();
        CityScene theirs = await GivenCity();
        Plot prison = await GivenPrisonerWithASummonPoint(ours, theirs);
        Assert.True(InPlot(prison, theirs.Mayor), "precondition: the prisoner stands on the prison plot");

        // Act
        await theirs.Mayor.Say($"{CityCmd} summon tp {PointName}");
        await World.Ticks(5);

        // Assert
        Assert.False(Waiting(theirs.Mayor), "a prisoner booked a summon out of the prison plot");
        Assert.True(Info(theirs.Mayor).isPrisoned(), "the prisoner is free");
    }

    // PZ9: one plot further, on another plot of the city, still no summon
    [AtlasScenario]
    public async Task Prisoner_Should_NotSummonAway_FromTheNextPlotOfTheCity()
    {
        // Arrange: a plain plot of ours in box 1, the prisoner on it
        CityScene ours = await GivenCity();
        CityScene theirs = await GivenCity();
        await GivenPrisonerWithASummonPoint(ours, theirs);
        Plot next = await GivenPlot(ours, 1);
        await StandIn(theirs.Mayor, next.Shape.Boxes[0]);

        // Act: the filter does not look at where the prisoner stands (and the tick puts him back)
        await theirs.Mayor.Say($"{CityCmd} summon tp {PointName}");
        await World.Ticks(5);

        // Assert
        Assert.False(Waiting(theirs.Mayor), "a prisoner one plot from the prison booked a summon home");
        Assert.True(Info(theirs.Mayor).isPrisoned(), "stepping onto the city's own plot freed the prisoner");
    }

    // The chat path goes through the Harmony prefix on ChatCommandApi.Execute (ExecuteCommand skips
    // it). A blocked command must not reach its handler: the filter's refusal comes, the handler's
    // reply does not. Once free, the same line books the summon, so the setup was good.
    [AtlasScenario]
    public async Task PrisonFilter_Should_StopABlockedCommand_BeforeItsHandler()
    {
        // Arrange
        CityScene ours = await GivenCity();
        CityScene theirs = await GivenCity();
        await GivenPrisonerWithASummonPoint(ours, theirs);
        string blockedReply = Lang.Get("canclaimsextended:prison_command_blocked");
        string handlerReply = Lang.Get("canclaimsextended:summon_started", PointName, Mod.config.SECONDS_SUMMON_TIME);
        theirs.Mayor.Client.Clear();

        // Act 1: as a prisoner
        await theirs.Mayor.Say($"{CityCmd} summon tp {PointName}");
        await World.Ticks(5);

        // Assert 1
        Assert.False(Waiting(theirs.Mayor), "a prisoner booked a summon through chat");
        Assert.Contains(theirs.Mayor.Client.ChatLines(), line => line.Contains(blockedReply));
        Assert.DoesNotContain(theirs.Mayor.Client.ChatLines(), line => line.Contains(handlerReply));

        // Act 2: free again
        AllowAdminCommands(ours.Mayor);
        CommandResult release = await ours.Mayor.ExecuteCommand($"{CadminCmd} player release {Name(theirs.Mayor)}");
        Assert.True(release.Ok && !Info(theirs.Mayor).isPrisoned(), "precondition: /cadmin player release: " + release.Message);
        theirs.Mayor.Client.Clear();
        await theirs.Mayor.Say($"{CityCmd} summon tp {PointName}");
        await World.Ticks(5);

        // Assert 2
        Assert.True(Waiting(theirs.Mayor), "a free player could not book the summon: the setup is broken");
        Assert.DoesNotContain(theirs.Mayor.Client.ChatLines(), line => line.Contains(blockedReply));

        // Cleanup: no teleport left waiting for later scenarios
        await theirs.Mayor.ExecuteCommand($"{CityCmd} summon cancel");
    }
}
