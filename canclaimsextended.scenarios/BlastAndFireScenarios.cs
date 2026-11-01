using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Fire (I4) and bombs (I5, Z12). A test player cannot strike a firestarter or light a fuse, so
/// the patches' decisions are called directly, as CAN_claims does:
/// - fire: a real fire block is set into the world, and HarmonyPatches.Prefix_On_FirePlaced /
///   Prefix_On_TrySpreadFireAllDirs are asked with its BEBehaviorBurning;
/// - bombs: a BlockEntityBomb of the test's own is asked HasPermissionToUse(); the server runs in
///   this process, so the mod's harmony prefix answers.
/// Section I of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class BlastAndFireScenarios : CxScenarioBase
{
    private const string FireCode = "game:fire";
    private const string AirCode = "game:air";

    // ---- helpers -------------------------------------------------------------------------------

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

    private static void AssertNoWorldFireOrBlastFlag()
    {
        WorldInfo w = Mod.dataStorage.World;
        Assert.False(w.fireEverywhere || w.fireForbidden || w.blastEverywhere || w.blastForbidden,
            "precondition: no world fire or blast flag is set");
    }

    /// <summary>Sets a field or a writable property, public or not, for engine state a test cannot reach otherwise.</summary>
    private static void SetMember(object target, string name, object? value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo? field = t.GetField(name, flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            PropertyInfo? property = t.GetProperty(name, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }
        }
        Assert.Fail($"precondition: {target.GetType().Name} has no writable member {name}");
    }

    /// <summary>A fire block set at the position, and the burning behavior of its block entity.</summary>
    private BEBehaviorBurning PlaceFire(BlockPos pos)
    {
        World.SetBlock(FireCode, pos);
        BlockEntity? be = World.BlockEntityAt<BlockEntity>(pos);
        BEBehaviorBurning? burning = be?.GetBehavior<BEBehaviorBurning>();
        Assert.True(burning != null, $"precondition: the fire block at {pos} has a BEBehaviorBurning");
        return burning!;
    }

    private bool IsFireAt(BlockPos pos) => World.BlockAt(pos).Code?.Path == "fire";

    /// <summary>
    /// The fire-placed decision for a fire block at <paramref name="firePos"/> burning the block
    /// below it, lit by <paramref name="starter"/> (null: no player). The fire is put out afterwards.
    /// </summary>
    private bool MayLightFire(BlockPos firePos, ITestPlayer? starter, out bool fireRemoved)
    {
        BEBehaviorBurning burning = PlaceFire(firePos);
        bool allowed = HarmonyPatches.Prefix_On_FirePlaced(firePos, firePos.DownCopy(), starter?.Player.PlayerUID, burning);
        fireRemoved = !IsFireAt(firePos);
        World.SetBlock(AirCode, firePos);
        return allowed;
    }

    /// <summary>A bomb at the position, as if lit by the player (null: by nobody), with the given blast radius.</summary>
    private static BlockEntityBomb Bomb(BlockPos pos, ITestPlayer? igniter, float radius = 0)
    {
        var bomb = new BlockEntityBomb { Pos = pos.Copy() };
        SetMember(bomb, "ignitedByPlayerUid", igniter?.Player.PlayerUID);
        if (radius > 0) SetMember(bomb, "BlastRadius", radius);
        return bomb;
    }

    /// <summary>A city plot where neither fire nor blasts are allowed, the mayor standing in it.</summary>
    private async Task<(CityScene Scene, Plot Plot)> GivenClosedPlot(params string[] citizens)
    {
        CityScene s = await GivenCity(citizens);
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{CityCmd} set fire off");
        await s.Mayor.ExecuteCommand($"{CityCmd} set blast off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set blast off");
        Assert.False(plot.getPermsHandler().fireFlag || plot.getPermsHandler().blastFlag,
            "precondition: fire and blast are off on the plot");
        Assert.False(s.City.getPermsHandler().fireFlag || s.City.getPermsHandler().blastFlag,
            "precondition: fire and blast are off in the city");
        AssertNoWorldFireOrBlastFlag();
        return (s, plot);
    }

    /// <summary>A block just west of the city's chunk, at the plots' feet height: free land beside the city.</summary>
    private BlockPos WildernessBeside(CityScene s, int dx = -1)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos pos = new(chunk.MinBlockX + dx, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 4);
        Assert.False(Mod.dataStorage.GetPlotAt(pos, out _), $"precondition: {pos} is free land");
        return pos;
    }

    // ---- I4: fire placed -----------------------------------------------------------------------

    // I4: a stranger's fire on a plot without the fire flag is refused, and the fire block removed
    [AtlasScenario]
    public async Task FirePlaced_Should_BeRefused_When_StrangerLightsAPlotWithoutFireFlag()
    {
        // Arrange
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "arsonist");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        bool allowed = MayLightFire(spot, stranger, out bool removed);

        // Assert
        Assert.False(allowed, "a stranger lit a fire on a plot without the fire flag");
        Assert.True(removed, "the refused fire block was left burning");
    }

    // I4: /plot set fire on lets anybody's fire burn there
    [AtlasScenario]
    public async Task FirePlaced_Should_BeAllowed_When_PlotFireFlagIsOn()
    {
        // Arrange
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "arsonist");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        CommandResult on = await s.Mayor.ExecuteCommand($"{PlotCmd} set fire on");
        Assert.True(on.Ok && plot.getPermsHandler().fireFlag, "precondition: /plot set fire on: " + on.Message);

        // Act
        bool allowed = MayLightFire(spot, stranger, out bool removed);

        // Assert
        Assert.True(allowed, "the plot's fire flag did not let the fire burn");
        Assert.False(removed, "an allowed fire block was removed");
    }

    // I4: a citizen with BUILD on the plot may light a fire there without the fire flag
    [AtlasScenario]
    public async Task FirePlaced_Should_BeAllowed_When_CitizenHasBuildOnThePlot()
    {
        // Arrange: the citizen row may build, the fire flag is off
        var (s, plot) = await GivenClosedPlot("smith");
        ITestPlayer citizen = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build on");
        Assert.True(plot.getPermsHandler().getPerm(PermGroup.CITIZEN, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: citizens may build on the plot");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        bool allowed = MayLightFire(spot, citizen, out _);

        // Assert
        Assert.True(allowed, "a citizen with BUILD could not light a fire on the plot");
    }

    // I4: the same citizen without BUILD is refused - the right, not the citizenship, decides
    [AtlasScenario]
    public async Task FirePlaced_Should_BeRefused_When_CitizenHasNoBuildOnThePlot()
    {
        var (s, plot) = await GivenClosedPlot("smith");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build off");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        bool allowed = MayLightFire(spot, s.Citizens[0], out _);

        Assert.False(allowed, "a citizen without BUILD lit a fire on a plot without the fire flag");
    }

    // I4: a fire nobody lit (lightning, lava) is refused on a plot without the fire flag
    [AtlasScenario]
    public async Task FirePlaced_Should_BeRefused_When_NoPlayerLitIt()
    {
        var (_, plot) = await GivenClosedPlot();
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        bool allowed = MayLightFire(spot, null, out _);

        Assert.False(allowed, "a fire of nobody burned on a plot without the fire flag");
    }

    // I4, I10: fireew lets a stranger's fire burn on a plot without the fire flag
    [AtlasScenario]
    public async Task FirePlaced_Should_BeAllowed_When_WorldFireEverywhereIsOn()
    {
        // Arrange
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "arsonist");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set fireew on");
        Assert.True(set.Ok && Mod.dataStorage.World.fireEverywhere, "precondition: /cadmin world set fireew on: " + set.Message);

        // Act
        bool allowed = MayLightFire(Middle(plot.Shape.Boxes[0]), stranger, out _);

        // Assert
        Assert.True(allowed, "fire everywhere did not override the plot");
    }

    // I4, I10: firefb takes the plot's fire flag away
    [AtlasScenario]
    public async Task FirePlaced_Should_BeRefused_When_WorldForbidsFireOnAPlotWithFireFlag()
    {
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "arsonist");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire on");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set firefb on");
        Assert.True(set.Ok && Mod.dataStorage.World.fireForbidden, "precondition: /cadmin world set firefb on: " + set.Message);

        bool allowed = MayLightFire(Middle(plot.Shape.Boxes[0]), stranger, out _);

        Assert.False(allowed, "a stranger's fire burned although the world forbids fire on claimed land");
    }

    // ---- I4: fire spreading --------------------------------------------------------------------

    /// <summary>A burning fire just outside the city, aimed at a block inside the plot.</summary>
    private (BEBehaviorBurning Fire, BlockPos FirePos, BlockPos Target) GivenWildFireBesidePlot(CityScene s, Plot plot)
    {
        BlockPos firePos = WildernessBeside(s);
        BEBehaviorBurning fire = PlaceFire(firePos);
        // What OnFirePlaced would have set: the prefix reads the block under the fire.
        SetMember(fire, "FirePos", firePos.Copy());
        SetMember(fire, "FuelPos", firePos.DownCopy());
        BlockPos target = new(AreaChunk(s.Area).MinBlockX + 1, firePos.Y, firePos.Z);
        Assert.True(plot.Shape.Contains(target.X, target.Y, target.Z), $"precondition: the target {target} is in the plot");
        return (fire, firePos, target);
    }

    // I4: fire from the wilderness does not spread into a plot without the fire flag
    [AtlasScenario]
    public async Task FireSpread_Should_StopAtThePlot_When_FireComesFromTheWilderness()
    {
        // Arrange
        var (s, plot) = await GivenClosedPlot();
        var (fire, firePos, target) = GivenWildFireBesidePlot(s, plot);

        // Act
        bool spreads = HarmonyPatches.Prefix_On_TrySpreadFireAllDirs(target, fire);
        World.SetBlock(AirCode, firePos);

        // Assert
        Assert.False(spreads, "fire from the wilderness spread into a plot without the fire flag");
    }

    // I4: with the fire flag on the plot the same fire spreads in
    [AtlasScenario]
    public async Task FireSpread_Should_GoIntoThePlot_When_PlotFireFlagIsOn()
    {
        var (s, plot) = await GivenClosedPlot();
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire on");
        Assert.True(plot.getPermsHandler().fireFlag, "precondition: the plot's fire flag is on");
        var (fire, firePos, target) = GivenWildFireBesidePlot(s, plot);

        bool spreads = HarmonyPatches.Prefix_On_TrySpreadFireAllDirs(target, fire);
        World.SetBlock(AirCode, firePos);

        Assert.True(spreads, "fire did not spread into a plot whose fire flag is on");
    }

    // I4: in the wilderness the mod does not hold fire back
    [AtlasScenario]
    public async Task FireSpread_Should_GoOn_When_TargetIsInTheWilderness()
    {
        var (s, plot) = await GivenClosedPlot();
        var (fire, firePos, _) = GivenWildFireBesidePlot(s, plot);
        BlockPos wildTarget = WildernessBeside(s, -3);

        bool spreads = HarmonyPatches.Prefix_On_TrySpreadFireAllDirs(wildTarget, fire);
        World.SetBlock(AirCode, firePos);

        Assert.True(spreads, "fire was held back in the wilderness");
    }

    // ---- I5: bombs -----------------------------------------------------------------------------

    // I5, CAN F4: no plot in reach - nothing to protect
    [AtlasScenario]
    public async Task Bomb_Should_GoOff_When_NoPlotIsInReach()
    {
        // Arrange: halfway between two areas, far from any city
        int area = NewArea();
        ResetConfig();
        AssertNoWorldFireOrBlastFlag();
        BlockPos o = AreaOrigin(area);
        BlockPos wild = new(o.X + 160, o.Y, o.Z);
        Assert.False(Mod.dataStorage.GetPlotAt(wild, out _), "precondition: no city owns the spot");

        // Act
        bool mayGoOff = Bomb(wild, null, 4).HasPermissionToUse();

        // Assert
        Assert.True(mayGoOff, "a bomb could not go off in the wilderness");
        await World.Ticks(1);
    }

    // I5, Z12: a new city and its new plot do not allow blasts
    [AtlasScenario]
    public async Task NewCityAndPlot_Should_HaveBlastOff_ByDefault()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer stranger = await GivenStranger(s, "bomber");
        AssertNoWorldFireOrBlastFlag();

        // Act
        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), stranger).HasPermissionToUse();

        // Assert
        Assert.False(s.City.getPermsHandler().blastFlag, "a new city allows blasts (Z12)");
        Assert.False(plot.getPermsHandler().blastFlag, "a new plot allows blasts (Z12)");
        Assert.False(mayGoOff, "a stranger's bomb went off on a new plot");
    }

    // I5: a stranger's bomb is refused on a plot without the blast flag
    [AtlasScenario]
    public async Task Bomb_Should_BeRefused_When_StrangerIgnitesOnAPlotWithoutBlast()
    {
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "bomber");

        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), stranger).HasPermissionToUse();

        Assert.False(mayGoOff, "a stranger's bomb went off on a plot without the blast flag");
    }

    // I5: with blasts allowed on the plot and on the city land it shares the chunk with, the bomb goes off
    [AtlasScenario]
    public async Task Bomb_Should_GoOff_When_BlastIsOnForPlotAndCity()
    {
        // Arrange: /city set blast on reaches the city land and the plot nobody owns
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "bomber");
        CommandResult on = await s.Mayor.ExecuteCommand($"{CityCmd} set blast on");
        Assert.True(on.Ok, "precondition: /city set blast on: " + on.Message);
        Assert.True(plot.getPermsHandler().blastFlag && s.City.getPermsHandler().blastFlag,
            "precondition: blast is on for the plot and the city");

        // Act
        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), stranger).HasPermissionToUse();

        // Assert
        Assert.True(mayGoOff, "the blast flag did not let the bomb go off");
    }

    // I5: the reach of a bomb also covers the city land of its chunk, so the plot's flag alone is not
    // enough while the city forbids blasts (QueryPlotsAndLand adds the land of every chunk in reach)
    [AtlasScenario]
    public async Task Bomb_Should_BeRefused_When_OnlyThePlotAllowsBlastsAndCityLandIsInReach()
    {
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "bomber");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set blast on");
        Assert.True(plot.getPermsHandler().blastFlag && !s.City.getPermsHandler().blastFlag,
            "precondition: blast is on for the plot only");

        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), stranger).HasPermissionToUse();

        Assert.False(mayGoOff, "a bomb went off although the city land in its reach forbids blasts");
    }

    // I5: an igniter with BUILD (on the plot and on the city land) may blast without the flag
    [AtlasScenario]
    public async Task Bomb_Should_GoOff_When_IgniterHasBuild()
    {
        // Arrange: /city set perm reaches the city land and the plot nobody owns
        var (s, plot) = await GivenClosedPlot("miner");
        ITestPlayer miner = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{CityCmd} set perm citizen build on");
        Assert.True(plot.getPermsHandler().getPerm(PermGroup.CITIZEN, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: citizens may build on the plot");

        // Act
        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), miner).HasPermissionToUse();

        // Assert
        Assert.True(mayGoOff, "a citizen with BUILD could not blast on the plot");
    }

    // I5: a bomb lit by nobody is refused on a plot without blast
    [AtlasScenario]
    public async Task Bomb_Should_BeRefused_When_NobodyIgnitedIt()
    {
        var (_, plot) = await GivenClosedPlot();

        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), null).HasPermissionToUse();

        Assert.False(mayGoOff, "a bomb of nobody went off on a plot without the blast flag");
    }

    // I5: a bomb in the wilderness next to the border is refused when its radius reaches the city
    [AtlasScenario]
    public async Task Bomb_Should_BeRefused_When_ItsRadiusReachesTheCityFromOutside()
    {
        // Arrange: two blocks west of the city's chunk, radius 4
        var (s, _) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "bomber");
        BlockPos outside = WildernessBeside(s, -2);

        // Act
        bool nearBorder = Bomb(outside, stranger, 4).HasPermissionToUse();
        bool farAway = Bomb(WildernessBeside(s, -12), stranger, 4).HasPermissionToUse();

        // Assert
        Assert.False(nearBorder, "a bomb beside the city went off although its blast reaches the city");
        Assert.True(farAway, "a bomb whose blast does not reach the city could not go off");
    }

    // I5, I10: blastew lets a stranger's bomb go off on a plot without blast
    [AtlasScenario]
    public async Task Bomb_Should_GoOff_When_WorldBlastEverywhereIsOn()
    {
        var (s, plot) = await GivenClosedPlot();
        ITestPlayer stranger = await GivenStranger(s, "bomber");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set blastew on");
        Assert.True(set.Ok && Mod.dataStorage.World.blastEverywhere, "precondition: /cadmin world set blastew on: " + set.Message);

        bool mayGoOff = Bomb(Middle(plot.Shape.Boxes[0]), stranger).HasPermissionToUse();

        Assert.True(mayGoOff, "blast everywhere did not override the plot");
    }

    // I5, I10: blastfb forbids bombs everywhere, the wilderness included
    [AtlasScenario]
    public async Task Bomb_Should_BeRefused_When_WorldForbidsBlasts()
    {
        var (s, _) = await GivenClosedPlot();
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set blastfb on");
        Assert.True(set.Ok && Mod.dataStorage.World.blastForbidden, "precondition: /cadmin world set blastfb on: " + set.Message);
        BlockPos o = AreaOrigin(s.Area);
        BlockPos wild = new(o.X + 160, o.Y, o.Z);

        bool mayGoOff = Bomb(wild, s.Mayor, 4).HasPermissionToUse();

        Assert.False(mayGoOff, "a bomb went off although the world forbids blasts");
    }
}
