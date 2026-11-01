using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Where a citizen comes back after death (J2-J6). A test player cannot be made to press
/// "respawn", so the scenario does what TemplePatches.Prefix_OnPlayerRespawn does for a dead
/// player - it marks the player as the one being respawned - and asks the engine's own
/// GetSpawnPosition, which the mod's prefix answers; the finalizer clears the mark.
/// Temple points are seeded as the temple block would set them: a temple plot and its
/// PlotDescTemple.Point (the block itself: TempleScenarios).
/// </summary>
[Trait("Category", "E2E")]
public class TempleRespawnScenarios : CxScenarioBase
{
    private static readonly FieldInfo? RespawningUid =
        typeof(TemplePatches).GetField("respawningUid", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>The spawn position the engine would use if the player respawned now.</summary>
    private static FuzzyEntityPos RespawnPosition(ITestPlayer player)
    {
        Assert.True(RespawningUid != null, "precondition: TemplePatches keeps the uid of the player being respawned");
        RespawningUid!.SetValue(null, player.Player.PlayerUID);
        try
        {
            return player.Player.GetSpawnPosition(false);
        }
        finally
        {
            TemplePatches.Finalizer_OnPlayerRespawn();
        }
    }

    /// <summary>The spawn position outside a respawn: what vanilla answers, the mod's prefix stays out.</summary>
    private static FuzzyEntityPos VanillaPosition(ITestPlayer player) => player.Player.GetSpawnPosition(false);

    /// <summary>The position TemplePatches gives for a temple point: beside the block, not in it.</summary>
    private static bool IsAt(FuzzyEntityPos pos, BlockPos point) =>
        Math.Abs(pos.X - (point.X + 0.5)) < 0.01
        && Math.Abs(pos.Y - (point.Y + 1)) < 0.01
        && Math.Abs(pos.Z - (point.Z + 1.5)) < 0.01;

    private static string Show(FuzzyEntityPos pos) => $"({pos.X:0.##}, {pos.Y:0.##}, {pos.Z:0.##})";

    /// <summary>Plot <paramref name="slot"/> made a temple, its point in the middle of the box.</summary>
    private async Task<(Plot Plot, BlockPos Point)> GivenTemple(CityScene s, int slot)
    {
        Plot plot = await GivenPlot(s, slot);
        CommandResult result = await RunPaid(s.Mayor, $"{PlotCmd} set type temple", () => plot.Type == PlotType.TEMPLE, "the plot to become a temple");
        var desc = Assert.IsType<PlotDescTemple>(plot.Desc);
        BlockPos point = Middle(plot.Shape.Boxes[0]);
        desc.Point = point;
        Assert.True(plot.Type == PlotType.TEMPLE, "precondition: /plot set type temple: " + result.Message);
        return (plot, point);
    }

    // ---- J2 ------------------------------------------------------------------------------------

    // J2: a citizen comes back at the temple of their city
    [AtlasScenario]
    public async Task Citizen_Should_RespawnAtTheTemple_When_CityHasOne()
    {
        // Arrange: the citizen in another box of the area
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (_, point) = await GivenTemple(s, 0);
        await StandIn(citizen, PlotBox(s.Area, 3));

        // Act
        FuzzyEntityPos spawn = RespawnPosition(citizen);

        // Assert
        Assert.True(IsAt(spawn, point), $"the citizen respawns at {Show(spawn)}, not at the temple {point}");
        Assert.True(spawn.Radius == 0, "the respawn point is spread by a radius: " + spawn.Radius);
    }

    // J2: outside a respawn the prefix leaves GetSpawnPosition to vanilla (it is used elsewhere too)
    [AtlasScenario]
    public async Task SpawnPosition_Should_StayVanilla_When_PlayerIsNotRespawning()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (_, point) = await GivenTemple(s, 0);

        // Prefix_OnPlayerRespawn marks only a dead player; this one is alive
        TemplePatches.Prefix_OnPlayerRespawn(citizen.Player);
        FuzzyEntityPos spawn;
        try
        {
            spawn = citizen.Player.GetSpawnPosition(false);
        }
        finally
        {
            TemplePatches.Finalizer_OnPlayerRespawn();
        }

        Assert.False(IsAt(spawn, point), "a living player's spawn position was taken over by the temple");
    }

    // J2: a player of no city respawns as vanilla, even next to a temple of a city
    [AtlasScenario]
    public async Task Stranger_Should_RespawnVanilla_When_HeHasNoCity()
    {
        CityScene s = await GivenCity();
        var (_, point) = await GivenTemple(s, 0);
        ITestPlayer stranger = await GivenStranger(s);
        FuzzyEntityPos vanilla = VanillaPosition(stranger);

        FuzzyEntityPos spawn = RespawnPosition(stranger);

        Assert.False(IsAt(spawn, point), "a player of no city respawned at a city's temple");
        Assert.True(spawn.X == vanilla.X && spawn.Y == vanilla.Y && spawn.Z == vanilla.Z,
            $"the respawn {Show(spawn)} is not the vanilla spawn {Show(vanilla)}");
    }

    // ---- J3 ------------------------------------------------------------------------------------

    // J3: without a pick the temple nearest to where the citizen died wins
    [AtlasScenario]
    public async Task Citizen_Should_RespawnAtTheNearestTemple_When_NothingIsPicked()
    {
        // Arrange: temples in opposite corners of the chunk
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (_, near0) = await GivenTemple(s, 0);
        var (_, near3) = await GivenTemple(s, 3);
        TempleRespawn.ClearPick(citizen.Player);

        // Act
        await StandIn(citizen, PlotBox(s.Area, 3));
        FuzzyEntityPos byThree = RespawnPosition(citizen);
        await StandIn(citizen, PlotBox(s.Area, 0));
        FuzzyEntityPos byZero = RespawnPosition(citizen);

        // Assert
        Assert.True(IsAt(byThree, near3), $"dying by temple 3 the citizen respawns at {Show(byThree)}, not at {near3}");
        Assert.True(IsAt(byZero, near0), $"dying by temple 0 the citizen respawns at {Show(byZero)}, not at {near0}");
    }

    // J3: the picked temple wins over the nearest one
    [AtlasScenario]
    public async Task Citizen_Should_RespawnAtThePickedTemple_When_AnotherIsNearer()
    {
        // Arrange: picked temple 0, the citizen dies by temple 3
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (picked, pickedPoint) = await GivenTemple(s, 0);
        await GivenTemple(s, 3);
        TempleRespawn.WritePick(citizen.Player, s.City, picked);
        await StandIn(citizen, PlotBox(s.Area, 3));

        // Act
        FuzzyEntityPos spawn = RespawnPosition(citizen);

        // Assert
        Assert.True(IsAt(spawn, pickedPoint), $"the citizen respawns at {Show(spawn)}, not at the picked temple {pickedPoint}");
    }

    // J6: TEMPLE_RESPAWN_CHOICE_ENABLED false - the pick is ignored, the nearest temple wins
    [AtlasScenario]
    public async Task Pick_Should_BeIgnored_When_ChoiceIsDisabled()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (picked, _) = await GivenTemple(s, 0);
        var (_, nearPoint) = await GivenTemple(s, 3);
        TempleRespawn.WritePick(citizen.Player, s.City, picked);
        await StandIn(citizen, PlotBox(s.Area, 3));
        bool before = Mod.config.TEMPLE_RESPAWN_CHOICE_ENABLED;
        AddCleanup(() => Mod.config.TEMPLE_RESPAWN_CHOICE_ENABLED = before);
        Mod.config.TEMPLE_RESPAWN_CHOICE_ENABLED = false;

        FuzzyEntityPos spawn = RespawnPosition(citizen);

        Assert.True(IsAt(spawn, nearPoint), $"with the choice off the citizen respawns at {Show(spawn)}, not at the nearest temple {nearPoint}");
    }

    // ---- J4 ------------------------------------------------------------------------------------

    // J4: the picked temple stopped being one - the nearest remaining temple, and the pick is dropped
    [AtlasScenario]
    public async Task Citizen_Should_RespawnAtTheNearestRemaining_When_PickedTempleIsGone()
    {
        // Arrange: picked temple 0, which the mayor turns back into a plain plot
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (picked, _) = await GivenTemple(s, 0);
        var (_, remaining) = await GivenTemple(s, 3);
        TempleRespawn.WritePick(citizen.Player, s.City, picked);
        await StandIn(s.Mayor, picked.Shape.Boxes[0]);
        CommandResult plain = await RunPaid(s.Mayor, $"{PlotCmd} set type default", () => picked.Type != PlotType.TEMPLE, "the temple to become a plain plot");
        Assert.True(picked.Type != PlotType.TEMPLE, "precondition: /plot set type default: " + plain.Message);
        await StandIn(citizen, PlotBox(s.Area, 0));

        // Act
        FuzzyEntityPos spawn = RespawnPosition(citizen);

        // Assert
        Assert.True(IsAt(spawn, remaining), $"the citizen respawns at {Show(spawn)}, not at the remaining temple {remaining}");
        Assert.False(TempleRespawn.TryReadPick(citizen.Player, s.City, out _), "the pick of a temple that is gone was kept");
    }

    // J4: no temple in the city - vanilla
    [AtlasScenario]
    public async Task Citizen_Should_RespawnVanilla_When_CityHasNoTemple()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        await GivenPlot(s);
        Assert.Empty(TempleRespawn.Points(s.City));
        FuzzyEntityPos vanilla = VanillaPosition(citizen);

        FuzzyEntityPos spawn = RespawnPosition(citizen);

        Assert.True(spawn.X == vanilla.X && spawn.Y == vanilla.Y && spawn.Z == vanilla.Z,
            $"without a temple the respawn {Show(spawn)} is not the vanilla spawn {Show(vanilla)}");
    }

    // J4, J7: a temple plot whose block is gone (no point) is no respawn point
    [AtlasScenario]
    public async Task Citizen_Should_RespawnVanilla_When_TempleHasNoPoint()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (plot, point) = await GivenTemple(s, 0);
        ((PlotDescTemple)plot.Desc).Point = null;

        FuzzyEntityPos spawn = RespawnPosition(citizen);

        Assert.False(IsAt(spawn, point), "a temple plot without its block still took the respawn");
    }

    // ---- J5 ------------------------------------------------------------------------------------

    // J5: who left the city respawns as vanilla, the old pick does not count
    [AtlasScenario]
    public async Task Citizen_Should_RespawnVanilla_When_HeLeftTheCity()
    {
        // Arrange
        CityScene s = await GivenCity("leaver");
        ITestPlayer leaver = s.Citizens[0];
        var (picked, point) = await GivenTemple(s, 0);
        TempleRespawn.WritePick(leaver.Player, s.City, picked);

        // Act
        CommandResult leave = await leaver.ExecuteCommand($"{CityCmd} leave");

        // Assert
        Assert.True(leave.Ok && !Info(leaver).hasCity(), "precondition: /city leave: " + leave.Message);
        FuzzyEntityPos spawn = RespawnPosition(leaver);
        Assert.False(IsAt(spawn, point), "a player who left the city respawned at its temple");
    }

    // ---- prison --------------------------------------------------------------------------------

    // J2, K: a jailed citizen comes back in a cell of the prison, not at the temple he picked
    [AtlasScenario]
    public async Task Prisoner_Should_RespawnInTheCell_When_HeHasATemplePicked()
    {
        // Arrange: a temple in slot 0, a prison with one cell in slot 1, the citizen jailed there
        CityScene s = await GivenCity("prisoner");
        ITestPlayer prisoner = s.Citizens[0];
        var (temple, point) = await GivenTemple(s, 0);
        TempleRespawn.WritePick(prisoner.Player, s.City, temple);
        Plot prison = await GivenPlot(s, 1);
        await RunPaid(s.Mayor, $"{PlotCmd} set type prison", () => prison.Type == PlotType.PRISON, "the plot to become a prison");
        CommandResult cell = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");
        Assert.True(cell.Ok, "precondition: a cell was added: " + cell.Message);
        AllowAdminCommands(s.Mayor);
        CommandResult jail = await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(prisoner)} {s.City.GetPartName()} 30");
        Assert.True(jail.Ok && Info(prisoner).isPrisoned(), "precondition: /cadmin player jail: " + jail.Message);

        // Act
        FuzzyEntityPos spawn = RespawnPosition(prisoner);

        // Assert
        Assert.False(IsAt(spawn, point), "a prisoner escaped to the temple");
        Assert.True(prison.Shape.Contains(spawn.X, spawn.Y, spawn.Z), $"the prisoner respawns at {Show(spawn)}, outside the prison");
        Assert.True(spawn.Radius == 0, "the respawn point is spread by a radius: " + spawn.Radius);

        // Cleanup: free him, so the sentence does not run on into later scenarios
        await s.Mayor.ExecuteCommand($"{CadminCmd} player release {Name(prisoner)}");
    }

    // ---- J6 ------------------------------------------------------------------------------------

    // J6: TEMPLE_RESPAWN_ENABLED false - vanilla, temples or not
    [AtlasScenario]
    public async Task Citizen_Should_RespawnVanilla_When_TempleRespawnIsDisabled()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        var (_, point) = await GivenTemple(s, 0);
        bool before = Mod.config.TEMPLE_RESPAWN_ENABLED;
        AddCleanup(() => Mod.config.TEMPLE_RESPAWN_ENABLED = before);
        Mod.config.TEMPLE_RESPAWN_ENABLED = false;
        FuzzyEntityPos vanilla = VanillaPosition(citizen);

        // Act
        FuzzyEntityPos spawn = RespawnPosition(citizen);

        // Assert
        Assert.False(IsAt(spawn, point), "the citizen respawned at the temple with temple respawn off");
        Assert.True(spawn.X == vanilla.X && spawn.Y == vanilla.Y && spawn.Z == vanilla.Z,
            $"with temple respawn off the respawn {Show(spawn)} is not the vanilla spawn {Show(vanilla)}");
    }
}
