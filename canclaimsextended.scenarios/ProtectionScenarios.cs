using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part.structure;
using Microsoft.Data.Sqlite;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Who may hurt whom (HarmonyPatches.CanPvpHere, the decision behind the damage patch): the plot's
/// and the city's pvp flags and the wilderness (I1), and the world flags that override them, kept
/// in WORLDFLAGS (I10). Section I of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class ProtectionScenarios : CxScenarioBase
{
    /// <summary>No daily pvp window: it allows fighting everywhere before any flag is read.</summary>
    protected override void ResetConfig()
    {
        base.ResetConfig();
        bool before = Mod.config.PVP_DURING_PART_OF_THE_DAY;
        AddCleanup(() => Mod.config.PVP_DURING_PART_OF_THE_DAY = before);
        Mod.config.PVP_DURING_PART_OF_THE_DAY = false;
    }

    private static bool MayAttack(ITestPlayer attacker, ITestPlayer victim) =>
        HarmonyPatches.CanPvpHere(attacker.Player, victim.Player);

    /// <summary>A citizen standing in a city plot, a stranger next to them; city and plot pvp both off.</summary>
    private async Task<(CityScene Scene, Plot Plot, ITestPlayer Victim, ITestPlayer Attacker)> GivenPeacefulPlot()
    {
        CityScene s = await GivenCity("victim");
        ITestPlayer victim = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        ITestPlayer attacker = await GivenStranger(s, "attacker");
        await s.Mayor.ExecuteCommand($"{CityCmd} set pvp off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp off");
        await StandIn(victim, plot.Shape.Boxes[0]);
        Assert.False(s.City.getPermsHandler().pvpFlag || plot.getPermsHandler().pvpFlag,
            "precondition: pvp is off for the city and the plot");
        Assert.False(Mod.dataStorage.World.pvpEverywhere || Mod.dataStorage.World.pvpForbidden,
            "precondition: no world pvp flag is set");
        return (s, plot, victim, attacker);
    }

    // ---- I1 ----------------------------------------------------------------------------------

    // I1: no fighting on a plot whose pvp is off in a city whose pvp is off
    [AtlasScenario]
    public async Task Pvp_Should_BeDenied_When_PlotAndCityPvpAreOff()
    {
        var (_, _, victim, attacker) = await GivenPeacefulPlot();

        Assert.False(MayAttack(attacker, victim), "a stranger could attack a citizen on a peaceful plot");
    }

    // I1: /plot set pvp on opens the plot to fighting
    [AtlasScenario]
    public async Task Pvp_Should_BeAllowed_When_PlotPvpIsOn()
    {
        var (s, plot, victim, attacker) = await GivenPeacefulPlot();

        CommandResult on = await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp on");

        Assert.True(on.Ok && plot.getPermsHandler().pvpFlag, "precondition: /plot set pvp on: " + on.Message);
        Assert.True(MayAttack(attacker, victim), "the plot's pvp flag did not open it to fighting");
    }

    // I1: the city's flag covers a plot that has its own off - plot OR city
    [AtlasScenario]
    public async Task Pvp_Should_BeAllowed_When_OnlyCityPvpIsOn()
    {
        // Arrange: /city set pvp on reaches the plot too, so the plot's own flag goes off again after
        var (s, plot, victim, attacker) = await GivenPeacefulPlot();
        await s.Mayor.ExecuteCommand($"{CityCmd} set pvp on");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp off");
        Assert.True(s.City.getPermsHandler().pvpFlag, "precondition: the city's pvp is on");
        Assert.False(plot.getPermsHandler().pvpFlag, "precondition: the plot's pvp is off");

        // Act + Assert
        Assert.True(MayAttack(attacker, victim), "the city's pvp flag did not cover its plot");
    }

    // I1: in the wilderness fighting is always allowed
    [AtlasScenario]
    public async Task Pvp_Should_BeAllowed_When_VictimIsInTheWilderness()
    {
        // Arrange: halfway between two areas, far from any city
        var (s, _, victim, attacker) = await GivenPeacefulPlot();
        BlockPos o = AreaOrigin(s.Area);
        BlockPos wild = new(o.X + 160, o.Y, o.Z);
        Assert.False(Mod.dataStorage.GetPlotAt(wild, out _), "precondition: no city owns the spot");

        // Act
        await victim.TeleportTo(wild);

        // Assert
        Assert.True(MayAttack(attacker, victim), "a player in the wilderness could not be attacked");
    }

    // I1: hurting oneself is not pvp
    [AtlasScenario]
    public async Task Pvp_Should_BeAllowed_When_PlayerHurtsHimself()
    {
        var (_, _, victim, _) = await GivenPeacefulPlot();

        Assert.True(MayAttack(victim, victim), "a player could not hurt himself on a peaceful plot");
    }

    // ---- I10 ---------------------------------------------------------------------------------

    /// <summary>The world flags as they are now come back on Dispose, in memory and in the database.</summary>
    private void KeepWorldFlags()
    {
        WorldInfo world = Mod.dataStorage.World;
        var before = WorldInfo.Codes.ToDictionary(code => code, code => world.TryGet(code, out bool v) && v);
        AddCleanup(() =>
        {
            foreach (var flag in before) world.TrySet(flag.Key, flag.Value);
            world.saveToDatabase();
        });
    }

    /// <summary>The flag as WORLDFLAGS holds it, after the write queue has been flushed; null without a row.</summary>
    private static long? StoredWorldFlag(string code)
    {
        Mod.Db.saveEverything();
        string path = Mod.config.FilePath(Mod.config.DB_NAME);
        using var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = new SqliteCommand("SELECT value FROM WORLDFLAGS WHERE flag=@flag", connection);
        cmd.Parameters.AddWithValue("@flag", code);
        return cmd.ExecuteScalar() is long value ? value : null;
    }

    // I10: pvpfb forbids fighting even on a plot with pvp on
    [AtlasScenario]
    public async Task WorldPvpForbidden_Should_DenyPvp_When_PlotAllowsIt()
    {
        // Arrange
        var (s, _, victim, attacker) = await GivenPeacefulPlot();
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp on");
        Assert.True(MayAttack(attacker, victim), "precondition: the plot allows fighting");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set pvpfb on");

        // Assert
        Assert.True(set.Ok, "/cadmin world set pvpfb on: " + set.Message);
        Assert.True(Mod.dataStorage.World.pvpForbidden, "the world info did not take the flag");
        Assert.False(MayAttack(attacker, victim), "pvp went through although the world forbids it");
    }

    // I10: pvpew allows fighting even on a plot and in a city with pvp off
    [AtlasScenario]
    public async Task WorldPvpEverywhere_Should_AllowPvp_When_PlotForbidsIt()
    {
        var (s, _, victim, attacker) = await GivenPeacefulPlot();
        Assert.False(MayAttack(attacker, victim), "precondition: the plot forbids fighting");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set pvpew on");

        Assert.True(set.Ok, "/cadmin world set pvpew on: " + set.Message);
        Assert.True(Mod.dataStorage.World.pvpEverywhere, "the world info did not take the flag");
        Assert.True(MayAttack(attacker, victim), "pvp everywhere did not override the plot");
    }

    // I10: switching the flag off again gives the plot its say back
    [AtlasScenario]
    public async Task WorldPvpFlag_Should_StopOverriding_When_SwitchedOff()
    {
        var (s, _, victim, attacker) = await GivenPeacefulPlot();
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();
        await s.Mayor.ExecuteCommand($"{CadminCmd} world set pvpew on");
        Assert.True(MayAttack(attacker, victim), "precondition: pvp everywhere is on");

        await s.Mayor.ExecuteCommand($"{CadminCmd} world set pvpew off");

        Assert.False(Mod.dataStorage.World.pvpEverywhere, "the world info kept the flag");
        Assert.False(MayAttack(attacker, victim), "the plot's pvp off is not back in force");
    }

    // I10, Y: a world flag is written to WORLDFLAGS, both on and off
    [AtlasTheory]
    [InlineData("pvpew")]
    [InlineData("pvpfb")]
    [InlineData("fireew")]
    [InlineData("firefb")]
    [InlineData("blastew")]
    [InlineData("blastfb")]
    [InlineData("mobspawnew")]
    [InlineData("mobspawnfb")]
    public async Task WorldFlag_Should_BeStored(string code)
    {
        // Arrange
        int area = NewArea();
        ResetConfig();
        ITestPlayer admin = await JoinAt("admin" + area, area);
        AllowAdminCommands(admin);
        KeepWorldFlags();

        // Act + Assert: on
        CommandResult on = await admin.ExecuteCommand($"{CadminCmd} world set {code} on");
        Assert.True(on.Ok, $"/cadmin world set {code} on: " + on.Message);
        Assert.True(Mod.dataStorage.World.TryGet(code, out bool inMemory) && inMemory, $"{code} is not on in the world info");
        Assert.Equal(1L, StoredWorldFlag(code));

        // Act + Assert: off
        CommandResult off = await admin.ExecuteCommand($"{CadminCmd} world set {code} off");
        Assert.True(off.Ok, $"/cadmin world set {code} off: " + off.Message);
        Assert.True(Mod.dataStorage.World.TryGet(code, out inMemory) && !inMemory, $"{code} is still on in the world info");
        Assert.Equal(0L, StoredWorldFlag(code));
    }
}
