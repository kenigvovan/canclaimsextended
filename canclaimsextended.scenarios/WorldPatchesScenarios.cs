using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.events;
using canclaimsextended.src.geometry;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The protection patches of section I that act on the world rather than on a player's hit:
/// - I6 liquids (WATER_FLOW_CITY_PLOTS_PATCH): HarmonyPatches.Postfix_CanSpreadIntoBlock and
///   Postfix_FindDownwardPaths, and once through the patched vanilla CanSpreadIntoBlock itself;
/// - I7 falling blocks (FALLING_BLOCKS_PROTECTION_PATCH): Prefix_CreateFallingBlock and
///   Prefix_FallingUpdateBlock;
/// - I8 barrels: Prefix_BarrelReceivedClientPacket (sealing needs USE), and the block use event
///   that guards every container (OnBlockAction.Event_OnBlockUse);
/// - I4 kilns and piles: Prefix_On_FirePlaced lets USE light a pit kiln or a ground storage pile,
///   where a loose fire needs BUILD (the loose fire itself is BlastAndFireScenarios).
/// A test player cannot pour a bucket, dig under sand or press "Seal", so the patch methods are
/// asked directly, as BlastAndFireScenarios does. Types of VSEssentials (the liquid behavior, the
/// falling block entity) are reached by reflection: the test project references only VSSurvivalMod.
/// The boot with both world patches off is <see cref="WorldPatchesOffScenarios"/>.
/// </summary>
[Trait("Category", "E2E")]
public class WorldPatchesScenarios : CxScenarioBase
{
    private const string AirCode = "game:air";
    private const string FireCode = "game:fire";

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>A block just west of the city's chunk, at the plots' feet height: free land beside the city.</summary>
    private BlockPos WildernessBeside(CityScene s, int dx = -1)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos pos = new(chunk.MinBlockX + dx, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 4);
        Assert.False(Mod.dataStorage.GetPlotAt(pos, out _), $"precondition: {pos} is free land");
        return pos;
    }

    /// <summary>The first block of the city's chunk on that row: city land at its west border.</summary>
    private BlockPos BorderLand(CityScene s)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos pos = new(chunk.MinBlockX, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 4);
        Assert.True(Mod.dataStorage.GetPlotAt(pos, out Plot here) && here.IsCityLand, $"precondition: {pos} is city land");
        return pos;
    }

    /// <summary>A block of the city's chunk between the plot boxes: city land, in no plot.</summary>
    private BlockPos CityLand(CityScene s)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos pos = new(chunk.MinBlockX + 8, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 4);
        Assert.True(Mod.dataStorage.GetPlotAt(pos, out Plot here) && here.IsCityLand, $"precondition: {pos} is city land");
        return pos;
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

    private static object? GetMember(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (Type? t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo? field = t.GetField(name, flags);
            if (field != null) return field.GetValue(target);
            PropertyInfo? property = t.GetProperty(name, flags);
            if (property != null && property.CanRead) return property.GetValue(target);
        }
        Assert.Fail($"precondition: {target.GetType().Name} has no member {name}");
        return null;
    }

    /// <summary>The player is in survival until Dispose: creative skips the rights.</summary>
    private void InSurvival(ITestPlayer player)
    {
        var data = player.Player.WorldData;
        EnumGameMode before = data.CurrentGameMode;
        data.CurrentGameMode = EnumGameMode.Survival;
        AddCleanup(() => data.CurrentGameMode = before);
    }

    // ---- I6: liquids ---------------------------------------------------------------------------

    /// <summary>What the postfix makes of a step the vanilla liquid allowed, from one block into the next.</summary>
    private static bool MayFlow(BlockPos from, BlockPos to)
    {
        bool result = true;
        HarmonyPatches.Postfix_CanSpreadIntoBlock(from, to, ref result);
        return result;
    }

    // I6, I11: the flag is on by default and both liquid targets carry the mod's patch
    [AtlasScenario]
    public async Task LiquidPatches_Should_BeApplied_ByDefault()
    {
        Assert.True(Mod.config.WATER_FLOW_CITY_PLOTS_PATCH, "precondition: WATER_FLOW_CITY_PLOTS_PATCH is on by default");
        Type liquid = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorFiniteSpreadingLiquid");

        MethodBase? spread = liquid.GetMethod("CanSpreadIntoBlock",
            new[] { typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) });
        MethodBase? paths = liquid.GetMethod("FindDownwardPaths");

        Assert.True(WorldPatchProbe.PatchedByMod(spread, "CanSpreadIntoBlock"), "CanSpreadIntoBlock is not patched");
        Assert.True(WorldPatchProbe.PatchedByMod(paths, "FindDownwardPaths"), "FindDownwardPaths is not patched");
        await World.Ticks(1);
    }

    // I6: a liquid from the wilderness does not flow into a plot
    [AtlasScenario]
    public async Task Liquid_Should_NotFlow_IntoAPlot_FromTheWilderness()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        BlockPos from = WildernessBeside(s);
        BlockPos into = new(plot.Shape.Boxes[0].MinX, from.Y, from.Z);
        Assert.True(plot.Shape.Contains(into.X, into.Y, into.Z), "precondition: the target is in the plot");

        Assert.False(MayFlow(from, into), "a liquid from the wilderness flowed into a plot");
    }

    // I6: nor onto the city land at the border - the whole territory is closed, not only plots
    [AtlasScenario]
    public async Task Liquid_Should_NotFlow_IntoCityLand_FromTheWilderness()
    {
        CityScene s = await GivenCity();

        bool flows = MayFlow(WildernessBeside(s), BorderLand(s));

        Assert.False(flows, "a liquid from the wilderness flowed onto the city land");
    }

    // I6: inside one city a liquid flows as usual - land to plot, plot to plot, plot to land
    [AtlasScenario]
    public async Task Liquid_Should_Flow_WithinOneCity()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot a = await GivenPlot(s, 0);
        Plot b = await GivenPlot(s, 1);
        BlockPos land = CityLand(s);
        BlockPos inA = Middle(a.Shape.Boxes[0]);
        BlockPos inB = Middle(b.Shape.Boxes[0]);

        // Act + Assert
        Assert.True(MayFlow(land, inA), "a liquid could not flow from the city land into its plot");
        Assert.True(MayFlow(inA, inB), "a liquid could not flow from one plot of the city into another");
        Assert.True(MayFlow(inB, land), "a liquid could not flow from a plot onto the city land");
    }

    // I6: out of the city and in the wilderness nothing is held back
    [AtlasScenario]
    public async Task Liquid_Should_Flow_OutOfTheCity_And_InTheWilderness()
    {
        CityScene s = await GivenCity();
        BlockPos border = BorderLand(s);
        BlockPos wild = WildernessBeside(s);

        Assert.True(MayFlow(border, wild), "a liquid could not flow out of the city into the wilderness");
        Assert.True(MayFlow(wild, WildernessBeside(s, -2)), "a liquid was held back in the wilderness");
    }

    // I6: one city's water does not flow into another city
    [AtlasScenario]
    public async Task Liquid_Should_NotFlow_FromOneCityIntoAnother()
    {
        CityScene first = await GivenCity();
        CityScene second = await GivenCity();
        Assert.False(first.City.Equals(second.City), "precondition: two cities");

        bool flows = MayFlow(CityLand(first), CityLand(second));

        Assert.False(flows, "a liquid flowed from one city into another");
    }

    // I6: a step vanilla already refused stays refused - the patch only takes away
    [AtlasScenario]
    public async Task Liquid_Should_StayRefused_When_VanillaRefused()
    {
        CityScene s = await GivenCity();
        bool result = false;

        HarmonyPatches.Postfix_CanSpreadIntoBlock(CityLand(s), BorderLand(s), ref result);

        Assert.False(result, "the patch let a liquid through that vanilla had stopped");
    }

    // I6: of the downward paths of a liquid in the wilderness, the one into the city is dropped and the others kept
    [AtlasScenario]
    public async Task DownwardPaths_Should_LoseOnlyThePathIntoTheCity()
    {
        // Arrange: a List<PosAndDist> built from the postfix's own parameter type
        CityScene s = await GivenCity();
        BlockPos from = WildernessBeside(s);
        BlockPos intoCity = BorderLand(s).DownCopy();
        BlockPos stayOut = WildernessBeside(s, -2).DownCopy();
        MethodInfo postfix = typeof(HarmonyPatches).GetMethod("Postfix_FindDownwardPaths")!;
        Type listType = postfix.GetParameters()[1].ParameterType;
        Type pathType = listType.GetGenericArguments()[0];
        var paths = (IList)Activator.CreateInstance(listType)!;
        foreach (BlockPos target in new[] { intoCity, stayOut })
        {
            object path = Activator.CreateInstance(pathType)!;
            SetMember(path, "pos", target);
            paths.Add(path);
        }

        // Act
        postfix.Invoke(null, new object[] { from, paths });

        // Assert
        var left = paths.Cast<object>().Select(p => (BlockPos)GetMember(p, "pos")!).ToList();
        Assert.True(left.Count == 1 && left[0].Equals(stayOut),
            "paths left: " + string.Join(", ", left) + $"; expected only {stayOut}");
    }

    /// <summary>The patched vanilla CanSpreadIntoBlock of still water, for a step into a block made air.</summary>
    internal static bool VanillaWaterMaySpread(IWorldSession world, BlockPos from, BlockPos to, BlockFacing facing)
    {
        Block? water = Mod.sapi.World.GetBlock(new AssetLocation("game:water-still-7"));
        Assert.True(water != null && water.BlockId != 0, "precondition: game:water-still-7 is a known block");
        Type liquid = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorFiniteSpreadingLiquid");
        object? behavior = water!.BlockBehaviors?.FirstOrDefault(b => liquid.IsInstanceOfType(b));
        Assert.True(behavior != null, "precondition: water spreads as a finite liquid");
        MethodInfo? spread = liquid.GetMethod("CanSpreadIntoBlock",
            new[] { typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) });
        Assert.True(spread != null, "precondition: CanSpreadIntoBlock exists in this game version");

        world.SetBlock(AirCode, to);
        Block neighbour = Mod.sapi.World.BlockAccessor.GetBlock(to);
        return (bool)spread!.Invoke(behavior, new object[] { water, neighbour, from, to, facing, Mod.sapi.World })!;
    }

    // I6: the real vanilla check, patched: water beside the city spreads west into free air but not east into the city
    [AtlasScenario]
    public async Task VanillaWaterSpread_Should_StopAtTheCityBorder()
    {
        // Arrange
        CityScene s = await GivenCity();
        BlockPos from = WildernessBeside(s);
        World.SetBlock(AirCode, from);

        // Act
        bool intoWilderness = VanillaWaterMaySpread(World, from, WildernessBeside(s, -2), BlockFacing.WEST);
        bool intoCity = VanillaWaterMaySpread(World, from, BorderLand(s), BlockFacing.EAST);

        // Assert
        Assert.True(intoWilderness, "precondition: vanilla lets water spread into free air");
        Assert.False(intoCity, "water from the wilderness spread into the city");
    }

    // ---- I7: falling blocks --------------------------------------------------------------------

    private static Type FallingEntityType() =>
        typeof(HarmonyPatches).GetMethod("Prefix_FallingUpdateBlock")!.GetParameters()[0].ParameterType;

    /// <summary>A falling block entity built without its constructor: the landing prefix reads only Api and initialPos.</summary>
    private static object FallingBlockFrom(BlockPos start)
    {
        object entity = RuntimeHelpers.GetUninitializedObject(FallingEntityType());
        SetMember(entity, "Api", Mod.sapi);
        SetMember(entity, "initialPos", start.Copy());
        return entity;
    }

    /// <summary>The landing prefix: true lets the block land, false makes it drop as items.</summary>
    private static bool MayLand(object fallingEntity, BlockPos at) =>
        (bool)typeof(HarmonyPatches).GetMethod("Prefix_FallingUpdateBlock")!.Invoke(null, new object[] { fallingEntity, false, at.Copy() })!;

    // I7, I11: the flag is on by default and both falling targets carry the mod's patch
    [AtlasScenario]
    public async Task FallingPatches_Should_BeApplied_ByDefault()
    {
        Assert.True(Mod.config.FALLING_BLOCKS_PROTECTION_PATCH, "precondition: FALLING_BLOCKS_PROTECTION_PATCH is on by default");
        Type unstable = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorUnstableFalling");

        MethodBase? create = unstable.GetMethod("createFallingBlock");
        MethodBase? land = FallingEntityType().GetMethod("UpdateBlock", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.True(WorldPatchProbe.PatchedByMod(create, "createFallingBlock"), "createFallingBlock is not patched");
        Assert.True(WorldPatchProbe.PatchedByMod(land, "EntityBlockFalling.UpdateBlock"), "EntityBlockFalling.UpdateBlock is not patched");
        await World.Ticks(1);
    }

    // I7: loose blocks of the wilderness fall as usual
    [AtlasScenario]
    public async Task LooseBlock_Should_StartFalling_When_InTheWilderness()
    {
        CityScene s = await GivenCity();

        Assert.True(HarmonyPatches.Prefix_CreateFallingBlock(WildernessBeside(s)), "sand in the wilderness was held up");
    }

    // I7: sand on the floor of a plot falls onto the city land below it - one city
    [AtlasScenario]
    public async Task LooseBlock_Should_StartFalling_When_TheBlockBelowIsOfTheSameCity()
    {
        // Arrange: the lowest block of the plot; under it is city land of the same city
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        BlockBox box = plot.Shape.Boxes[0];
        BlockPos floor = new(box.MinX + 3, box.MinY, box.MinZ + 3);
        Assert.True(Mod.dataStorage.GetPlotAt(floor.DownCopy(), out Plot below) && below.IsCityLand,
            "precondition: under the plot is city land");

        // Act
        bool falls = HarmonyPatches.Prefix_CreateFallingBlock(floor);

        // Assert
        Assert.True(falls, "sand of a plot did not fall onto the land of its own city");
    }

    // I7: a block falling within one city lands
    [AtlasScenario]
    public async Task FallingBlock_Should_Land_When_ItFallsWithinOneCity()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        bool lands = MayLand(FallingBlockFrom(CityLand(s)), Middle(plot.Shape.Boxes[0]));

        Assert.True(lands, "a block falling from the city land into its plot did not land");
    }

    // I7: a block falling in the wilderness lands, wherever it came from
    [AtlasScenario]
    public async Task FallingBlock_Should_Land_When_ItLandsInTheWilderness()
    {
        CityScene s = await GivenCity();

        bool lands = MayLand(FallingBlockFrom(BorderLand(s)), WildernessBeside(s));

        Assert.True(lands, "a block landing in the wilderness was turned into items");
    }

    // I7: a block falling into the city from the wilderness does not land - it drops as items
    [AtlasScenario]
    public async Task FallingBlock_Should_DropAsItems_When_ItFallsIntoTheCityFromOutside()
    {
        // Arrange: a real falling block entity, spawned in the wilderness beside the plot
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        BlockPos start = WildernessBeside(s).Up(3);
        BlockPos into = new(plot.Shape.Boxes[0].MinX, Middle(plot.Shape.Boxes[0]).Y, start.Z);
        Assert.True(plot.Shape.Contains(into.X, into.Y, into.Z), "precondition: the landing spot is in the plot");

        Type unstable = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorUnstableFalling");
        Block? sand = Mod.sapi.World.Blocks.FirstOrDefault(b => b?.BlockBehaviors?.Any(x => unstable.IsInstanceOfType(x)) == true);
        Assert.True(sand != null, "precondition: the game has a falling block");
        ConstructorInfo? ctor = FallingEntityType().GetConstructor(new[]
            { typeof(Block), typeof(BlockEntity), typeof(BlockPos), typeof(AssetLocation), typeof(float), typeof(bool), typeof(float) });
        Assert.True(ctor != null, "precondition: EntityBlockFalling(Block, BlockEntity, BlockPos, AssetLocation, float, bool, float) exists");
        var falling = (Entity)ctor!.Invoke(new object?[] { sand, null, start.Copy(), new AssetLocation("game:effect/rockslide"), 0f, false, 0f });
        Mod.sapi.World.SpawnEntity(falling);
        await World.Until(() => falling.Api != null, "the falling block to be spawned", 20);

        // Act
        bool lands = MayLand(falling, into);

        // Assert
        Assert.False(lands, "a block falling in from the wilderness landed in the plot");
        Vec3d spot = into.ToVec3d().Add(0.5, 0.5, 0.5);
        await World.Until(() => Mod.sapi.World.GetEntitiesAround(spot, 1, 1, e => e.GetType().Name == "EntityItem").Length > 0,
            "the refused block to drop as items in the plot", 20);
    }

    // ---- I8: barrels and containers ------------------------------------------------------------

    /// <summary>A barrel at the position, built without its constructor: the prefix reads only its position.</summary>
    private static BlockEntityBarrel BarrelAt(BlockPos pos)
    {
        var barrel = (BlockEntityBarrel)RuntimeHelpers.GetUninitializedObject(typeof(BlockEntityBarrel));
        barrel.Pos = pos.Copy();
        return barrel;
    }

    private static bool MaySeal(ITestPlayer player, BlockPos at) =>
        HarmonyPatches.Prefix_BarrelReceivedClientPacket(BarrelAt(at), player.Player);

    /// <summary>A plot whose stranger row has no USE, a survival stranger standing in it.</summary>
    private async Task<(CityScene Scene, Plot Plot, ITestPlayer Stranger)> GivenStrangerWithoutUse()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        CommandResult off = await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger use off");
        Assert.True(off.Ok && !plot.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM),
            "precondition: /plot set perm stranger use off: " + off.Message);
        ITestPlayer stranger = await GivenStranger(s, "cooper");
        InSurvival(stranger);
        await StandIn(stranger, plot.Shape.Boxes[0]);
        return (s, plot, stranger);
    }

    // I8: the seal packet is always guarded, whatever the flags
    [AtlasScenario]
    public async Task BarrelPatch_Should_BeApplied()
    {
        MethodBase? packet = typeof(BlockEntityBarrel).GetMethod("OnReceivedClientPacket");

        Assert.True(WorldPatchProbe.PatchedByMod(packet, "BlockEntityBarrel.OnReceivedClientPacket"), "the barrel's packet is not patched");
        await World.Ticks(1);
    }

    // I8: a stranger without USE cannot seal a barrel on the plot
    [AtlasScenario]
    public async Task BarrelSeal_Should_BeRefused_When_StrangerHasNoUse()
    {
        var (_, plot, stranger) = await GivenStrangerWithoutUse();

        Assert.False(MaySeal(stranger, Middle(plot.Shape.Boxes[0])), "a stranger without USE sealed a barrel on the plot");
    }

    // I8: /plot set perm stranger use on lets him seal it
    [AtlasScenario]
    public async Task BarrelSeal_Should_BeAllowed_When_StrangerGetsUse()
    {
        // Arrange
        var (s, plot, stranger) = await GivenStrangerWithoutUse();
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.False(MaySeal(stranger, spot), "precondition: the stranger may not seal");

        // Act
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        CommandResult on = await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger use on");

        // Assert
        Assert.True(on.Ok, "precondition: /plot set perm stranger use on: " + on.Message);
        Assert.True(MaySeal(stranger, spot), "the USE right did not let the stranger seal the barrel");
    }

    // I8: creative skips the check, as it does for every right
    [AtlasScenario]
    public async Task BarrelSeal_Should_BeAllowed_When_PlayerIsInCreative()
    {
        var (_, plot, stranger) = await GivenStrangerWithoutUse();
        stranger.Player.WorldData.CurrentGameMode = EnumGameMode.Creative;

        Assert.True(MaySeal(stranger, Middle(plot.Shape.Boxes[0])), "a creative player could not seal a barrel");
    }

    // I8: in the wilderness anybody seals
    [AtlasScenario]
    public async Task BarrelSeal_Should_BeAllowed_When_BarrelIsInTheWilderness()
    {
        var (s, _, stranger) = await GivenStrangerWithoutUse();

        Assert.True(MaySeal(stranger, WildernessBeside(s)), "a barrel in the wilderness could not be sealed");
    }

    // I8, H1: opening a container (any block use) on the plot follows the USE row too
    [AtlasScenario]
    public async Task ContainerUse_Should_FollowTheUseRight()
    {
        // Arrange
        var (s, plot, stranger) = await GivenStrangerWithoutUse();
        var chestSpot = new BlockSelection { Position = Middle(plot.Shape.Boxes[0]) };
        Assert.False(OnBlockAction.Event_OnBlockUse(stranger.Player, chestSpot), "a stranger without USE opened a container on the plot");

        // Act
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger use on");

        // Assert
        Assert.True(OnBlockAction.Event_OnBlockUse(stranger.Player, chestSpot), "the USE right did not let the stranger open a container");
    }

    // I8: in creative a container is opened without the right
    [AtlasScenario]
    public async Task ContainerUse_Should_BeAllowed_When_PlayerIsInCreative()
    {
        var (_, plot, stranger) = await GivenStrangerWithoutUse();
        stranger.Player.WorldData.CurrentGameMode = EnumGameMode.Creative;

        bool opens = OnBlockAction.Event_OnBlockUse(stranger.Player, new BlockSelection { Position = Middle(plot.Shape.Boxes[0]) });

        Assert.True(opens, "a creative player could not open a container");
    }

    // ---- I4: kilns and piles need only USE -----------------------------------------------------

    /// <summary>A block entity of the type at the position, built without its constructor: the fire prefix reads only its type and position.</summary>
    private static BlockEntity BurnsItself<T>(BlockPos pos) where T : BlockEntity
    {
        var be = (BlockEntity)RuntimeHelpers.GetUninitializedObject(typeof(T));
        be.Pos = pos.Copy();
        return be;
    }

    /// <summary>The fire-placed decision for a fire lit in the kiln or pile by <paramref name="starter"/> (null: nobody).</summary>
    private static bool MayLight(BlockEntity kilnOrPile, ITestPlayer? starter)
    {
        var burning = (BEBehaviorBurning)RuntimeHelpers.GetUninitializedObject(typeof(BEBehaviorBurning));
        SetMember(burning, "Api", Mod.sapi);
        SetMember(burning, "Blockentity", kilnOrPile);
        BlockPos at = kilnOrPile.Pos;
        return HarmonyPatches.Prefix_On_FirePlaced(at, at.DownCopy(), starter?.Player.PlayerUID, burning);
    }

    /// <summary>The same decision for a loose fire block set into the world; the fire is put out after.</summary>
    private bool MayLightLooseFire(BlockPos firePos, ITestPlayer starter)
    {
        World.SetBlock(FireCode, firePos);
        BEBehaviorBurning? burning = World.BlockEntityAt<BlockEntity>(firePos)?.GetBehavior<BEBehaviorBurning>();
        Assert.True(burning != null, $"precondition: the fire block at {firePos} has a BEBehaviorBurning");
        bool allowed = HarmonyPatches.Prefix_On_FirePlaced(firePos, firePos.DownCopy(), starter.Player.PlayerUID, burning!);
        World.SetBlock(AirCode, firePos);
        return allowed;
    }

    /// <summary>A plot without the fire flag whose citizen row may use but not build; the mayor stands in it.</summary>
    private async Task<(CityScene Scene, Plot Plot)> GivenPlotCitizensMayOnlyUse()
    {
        CityScene s = await GivenCity("potter");
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{CityCmd} set fire off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set fire off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build off");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen use on");
        var perms = plot.getPermsHandler();
        Assert.False(perms.fireFlag, "precondition: fire is off on the plot");
        Assert.True(perms.getPerm(PermGroup.CITIZEN, PermType.USE_PERM) && !perms.getPerm(PermGroup.CITIZEN, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: citizens may use but not build on the plot");
        WorldInfo w = Mod.dataStorage.World;
        Assert.False(w.fireEverywhere || w.fireForbidden, "precondition: no world fire flag is set");
        return (s, plot);
    }

    // I4: a citizen with USE but without BUILD lights a pit kiln - and not a loose fire on the same plot
    [AtlasScenario]
    public async Task PitKiln_Should_BeLit_When_CitizenHasUseButNoBuild()
    {
        // Arrange
        var (s, plot) = await GivenPlotCitizensMayOnlyUse();
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        bool kiln = MayLight(BurnsItself<BlockEntityPitKiln>(spot), s.Citizens[0]);
        bool loose = MayLightLooseFire(spot.EastCopy(), s.Citizens[0]);

        // Assert
        Assert.True(kiln, "a citizen with USE could not light a pit kiln");
        Assert.False(loose, "a citizen without BUILD lit a loose fire - the kiln case proves nothing");
    }

    // I4: the same for a ground storage pile
    [AtlasScenario]
    public async Task GroundStoragePile_Should_BeLit_When_CitizenHasUseButNoBuild()
    {
        var (s, plot) = await GivenPlotCitizensMayOnlyUse();

        bool lit = MayLight(BurnsItself<BlockEntityGroundStorage>(Middle(plot.Shape.Boxes[0])), s.Citizens[0]);

        Assert.True(lit, "a citizen with USE could not light a pile");
    }

    // I4: without USE the kiln stays cold
    [AtlasScenario]
    public async Task PitKiln_Should_NotBeLit_When_CitizenHasNoUse()
    {
        var (s, plot) = await GivenPlotCitizensMayOnlyUse();
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen use off");
        Assert.False(plot.getPermsHandler().getPerm(PermGroup.CITIZEN, PermType.USE_PERM), "precondition: citizens may not use");

        bool lit = MayLight(BurnsItself<BlockEntityPitKiln>(Middle(plot.Shape.Boxes[0])), s.Citizens[0]);

        Assert.False(lit, "a citizen without USE lit a pit kiln on a plot without the fire flag");
    }

    // I4: a stranger needs the stranger row's USE
    [AtlasScenario]
    public async Task PitKiln_Should_FollowTheStrangerUseRow()
    {
        // Arrange
        var (s, plot) = await GivenPlotCitizensMayOnlyUse();
        ITestPlayer stranger = await GivenStranger(s, "potter2");
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger use off");
        BlockEntity kiln = BurnsItself<BlockEntityPitKiln>(Middle(plot.Shape.Boxes[0]));
        Assert.False(MayLight(kiln, stranger), "a stranger without USE lit a pit kiln");

        // Act
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger use on");

        // Assert
        Assert.True(plot.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM), "precondition: strangers may use");
        Assert.True(MayLight(kiln, stranger), "the stranger's USE did not let him light the kiln");
    }

    // I4: a kiln fire nobody lit is refused on a plot without the fire flag
    [AtlasScenario]
    public async Task PitKiln_Should_NotBeLit_When_NobodyLitIt()
    {
        var (_, plot) = await GivenPlotCitizensMayOnlyUse();

        bool lit = MayLight(BurnsItself<BlockEntityPitKiln>(Middle(plot.Shape.Boxes[0])), null);

        Assert.False(lit, "a kiln fire of nobody burned on a plot without the fire flag");
    }
}

/// <summary>
/// I11: WATER_FLOW_CITY_PLOTS_PATCH and FALLING_BLOCKS_PROTECTION_PATCH off at boot
/// (fixtures/WorldPatchesOff). Their targets are left alone, the vanilla liquid flows into a city
/// again, and the patches without a flag (barrel) or with their own (fire, blast) are still there.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/WorldPatchesOff", TargetPath = "ModConfig")]
public class WorldPatchesOffScenarios : CxScenarioBase
{
    // I11: no liquid or falling target is patched; the others are
    [AtlasScenario]
    public async Task WorldPatches_Should_NotBeApplied_When_TheirFlagsAreOffAtBoot()
    {
        // Arrange
        Assert.False(Mod.config.WATER_FLOW_CITY_PLOTS_PATCH, "precondition: the fixture switched WATER_FLOW_CITY_PLOTS_PATCH off");
        Assert.False(Mod.config.FALLING_BLOCKS_PROTECTION_PATCH, "precondition: the fixture switched FALLING_BLOCKS_PROTECTION_PATCH off");
        Type liquid = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorFiniteSpreadingLiquid");
        Type unstable = WorldPatchProbe.GameType("Vintagestory.GameContent.BlockBehaviorUnstableFalling");
        Type falling = typeof(HarmonyPatches).GetMethod("Prefix_FallingUpdateBlock")!.GetParameters()[0].ParameterType;

        // Act
        bool spread = WorldPatchProbe.PatchedByMod(liquid.GetMethod("CanSpreadIntoBlock",
            new[] { typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) }), "CanSpreadIntoBlock");
        bool paths = WorldPatchProbe.PatchedByMod(liquid.GetMethod("FindDownwardPaths"), "FindDownwardPaths");
        bool create = WorldPatchProbe.PatchedByMod(unstable.GetMethod("createFallingBlock"), "createFallingBlock");
        bool land = WorldPatchProbe.PatchedByMod(falling.GetMethod("UpdateBlock", BindingFlags.NonPublic | BindingFlags.Instance), "EntityBlockFalling.UpdateBlock");
        bool barrel = WorldPatchProbe.PatchedByMod(typeof(BlockEntityBarrel).GetMethod("OnReceivedClientPacket"), "BlockEntityBarrel.OnReceivedClientPacket");
        bool bomb = WorldPatchProbe.PatchedByMod(typeof(BlockEntityBomb).GetMethod("HasPermissionToUse"), "BlockEntityBomb.HasPermissionToUse");

        // Assert
        Assert.False(spread, "CanSpreadIntoBlock is patched with WATER_FLOW_CITY_PLOTS_PATCH off");
        Assert.False(paths, "FindDownwardPaths is patched with WATER_FLOW_CITY_PLOTS_PATCH off");
        Assert.False(create, "createFallingBlock is patched with FALLING_BLOCKS_PROTECTION_PATCH off");
        Assert.False(land, "EntityBlockFalling.UpdateBlock is patched with FALLING_BLOCKS_PROTECTION_PATCH off");
        Assert.True(barrel, "the barrel patch went away with the world patches");
        Assert.True(bomb, "the blast patch went away with the world patches");
        await World.Ticks(1);
    }

    // I6, I11: with the patch off water from the wilderness spreads into the city as in vanilla
    [AtlasScenario]
    public async Task VanillaWaterSpread_Should_GoIntoTheCity_When_PatchIsOff()
    {
        // Arrange
        CityScene s = await GivenCity();
        ChunkPos chunk = AreaChunk(s.Area);
        int y = Middle(PlotBox(s.Area)).Y;
        BlockPos from = new(chunk.MinBlockX - 1, y, chunk.MinBlockZ + 4);
        BlockPos border = new(chunk.MinBlockX, y, chunk.MinBlockZ + 4);
        Assert.False(Mod.dataStorage.GetPlotAt(from, out _), "precondition: the water is in the wilderness");
        Assert.True(Mod.dataStorage.GetPlotAt(border, out Plot land) && land.IsCityLand, "precondition: the target is city land");
        World.SetBlock("game:air", from);

        // Act
        bool intoCity = WorldPatchesScenarios.VanillaWaterMaySpread(World, from, border, BlockFacing.EAST);

        // Assert
        Assert.True(intoCity, "water was stopped at the city border with WATER_FLOW_CITY_PLOTS_PATCH off");
    }
}

/// <summary>Game types by name and whether the mod's Harmony instance patched a method; Harmony is reached by reflection.</summary>
file static class WorldPatchProbe
{
    public static Type GameType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, throwOnError: false);
            if (type != null) return type;
        }
        Assert.Fail($"precondition: {fullName} is a loaded game type");
        return null!;
    }

    public static bool PatchedByMod(MethodBase? target, string what)
    {
        Assert.True(target != null, $"precondition: {what} exists in this game version");
        string? wanted = typeof(ApplyPatches).Assembly.GetReferencedAssemblies().FirstOrDefault(a => a.Name == "0Harmony")?.FullName;
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "0Harmony").ToList();
        Assembly? harmonyAssembly = loaded.FirstOrDefault(a => a.FullName == wanted) ?? loaded.FirstOrDefault();
        Assert.True(harmonyAssembly != null, "precondition: Harmony is loaded");
        Type harmony = harmonyAssembly!.GetType("HarmonyLib.Harmony", throwOnError: true)!;
        MethodInfo getPatchInfo = harmony.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static)!;
        object? info = getPatchInfo.Invoke(null, new object[] { target! });
        if (info == null) return false;
        var owners = (IEnumerable<string>)info.GetType().GetProperty("Owners")!.GetValue(info)!;
        return owners.Contains(ApplyPatches.HarmonyID);
    }
}
