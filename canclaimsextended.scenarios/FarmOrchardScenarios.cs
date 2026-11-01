using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.cropbehaviors;
using canclaimsextended.src.geometry;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Farm and orchard plots (section M of TESTING_canclaimsextended.md), with CROPS_ONLY_ON_FARM_PLOTS
/// and FRUIT_ONLY_ON_ORCHARD_PLOTS on at boot (fixtures/FarmOrchardOn): both are read at start only,
/// the crop rule is attached to the crop blocks at RunGame and the orchard patches are applied in
/// StartServerSide.
///
/// Nothing here refuses a planting: the mod lets a crop or a sapling go in anywhere and then
/// - a crop outside a farm plot does not grow (BehaviorOnlyOnFarmPlot.TryGrowCrop answers
///   PreventDefault) and the planter is told so on planting (crop_wont_grow_outside_farm);
/// - a fruit tree outside an orchard plot blossoms, but its fruit is set back to Empty after every
///   root tick (OrchardRules.StripFruitOutsideOrchard), and the planter is told so on planting
///   (fruit_wont_grow_outside_orchard; in the wilderness only with ORCHARD_WARN_OUTSIDE_CITY).
/// Growth takes game days and a test player cannot plant, so the rule attached to the real crop
/// blocks and the orchard patch methods are asked directly, as the other scenarios ask the fire
/// and damage prefixes. The flags are read again inside, so switching them off at run time is
/// checked here too; the boot with both flags off is <see cref="FarmOrchardOffScenarios"/>.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/FarmOrchardOn", TargetPath = "ModConfig")]
public class FarmOrchardScenarios : CxScenarioBase
{
    private static string CropWarning => Lang.Get("canclaimsextended:crop_wont_grow_outside_farm");
    private static string FruitWarning => Lang.Get("canclaimsextended:fruit_wont_grow_outside_orchard");

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Both rules on, the wilderness warning on; each comes back as it was on Dispose.</summary>
    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        bool crops = c.CROPS_ONLY_ON_FARM_PLOTS, fruit = c.FRUIT_ONLY_ON_ORCHARD_PLOTS, warn = c.ORCHARD_WARN_OUTSIDE_CITY;
        AddCleanup(() =>
        {
            c.CROPS_ONLY_ON_FARM_PLOTS = crops;
            c.FRUIT_ONLY_ON_ORCHARD_PLOTS = fruit;
            c.ORCHARD_WARN_OUTSIDE_CITY = warn;
        });
        c.CROPS_ONLY_ON_FARM_PLOTS = true;
        c.FRUIT_ONLY_ON_ORCHARD_PLOTS = true;
        c.ORCHARD_WARN_OUTSIDE_CITY = true;
    }

    /// <summary>Box <paramref name="slot"/> made a plot and set to the type; the mayor stands in it.</summary>
    private async Task<Plot> GivenTypedPlot(CityScene s, int slot, string code, PlotType type)
    {
        Plot plot = await GivenPlot(s, slot);
        if (plot.Type != type)
        {
            CommandResult set = await RunPaid(s.Mayor, $"{PlotCmd} set type {code}", () => plot.Type == type, $"the plot to become {code}");
            Assert.True(plot.Type == type, $"precondition: /plot set type {code}: {set.Message}");
        }
        return plot;
    }

    /// <summary>The block where farmland or a tree root stands in the plot: one above its floor.</summary>
    private static BlockPos Ground(Plot plot) => Middle(plot.Shape.Boxes[0]).DownCopy();

    /// <summary>A block just west of the city's chunk, at the plots' feet height: free land beside the city.</summary>
    private BlockPos WildernessBeside(CityScene s)
    {
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos pos = new(chunk.MinBlockX - 1, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 4);
        Assert.False(Mod.dataStorage.GetPlotAt(pos, out _), $"precondition: {pos} is free land");
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

    private static bool Got(ITestPlayer player, string line) => player.Client.ChatLines().Any(l => l.Contains(line));

    // ---- crops: the rule attached to the game's crop blocks ------------------------------------

    /// <summary>The farm rule attached to a real crop block at boot.</summary>
    private static BehaviorOnlyOnFarmPlot FarmRule()
    {
        Block? crop = Mod.sapi.World.Blocks.FirstOrDefault(b => b?.CropProps?.Behaviors != null
            && b.CropProps.Behaviors.Any(x => x is BehaviorOnlyOnFarmPlot));
        Assert.True(crop != null, "precondition: a crop block carries BehaviorOnlyOnFarmPlot (CROPS_ONLY_ON_FARM_PLOTS on at boot)");
        return crop!.CropProps.Behaviors.OfType<BehaviorOnlyOnFarmPlot>().First();
    }

    /// <summary>
    /// A farmland block entity as the crop's growth sees it: only its position is read by the rule.
    /// A proxy, so the scenario does not depend on the game's farmland block codes.
    /// </summary>
    public class FarmlandStub : DispatchProxy
    {
        public BlockPos? At { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Pos") return At;
            if (targetMethod?.Name == "get_UpPos") return At?.UpCopy();
            Type? returns = targetMethod?.ReturnType;
            return returns == null || returns == typeof(void) || !returns.IsValueType ? null : Activator.CreateInstance(returns);
        }
    }

    /// <summary>What the rule tells the crop's growth for farmland at the position.</summary>
    private static EnumHandling Grow(BlockPos farmland)
    {
        IFarmlandBlockEntity stub = DispatchProxy.Create<IFarmlandBlockEntity, FarmlandStub>();
        ((FarmlandStub)(object)stub).At = farmland.Copy();
        EnumHandling handling = EnumHandling.PassThrough;
        bool result = FarmRule().TryGrowCrop(Mod.sapi, stub, 0, 2, ref handling);
        Assert.False(result, "the farm rule answered true; it only ever stops growth through the handling");
        return handling;
    }

    private static void Plant(ITestPlayer planter, BlockPos farmland) =>
        FarmRule().OnPlanted(Mod.sapi, null!, planter.Entity, new BlockSelection { Position = farmland.Copy() });

    // M1: CROPS_ONLY_ON_FARM_PLOTS on at boot gives every crop block the rule, once
    [AtlasScenario]
    public async Task FarmRule_Should_BeAttachedToEveryCrop_When_FlagIsOnAtBoot()
    {
        var crops = Mod.sapi.World.Blocks.Where(b => b?.CropProps != null).ToList();

        Assert.True(crops.Count > 0, "precondition: the game has crop blocks");
        var missing = crops.Where(b => (b.CropProps.Behaviors ?? Array.Empty<CropBehavior>()).Count(x => x is BehaviorOnlyOnFarmPlot) != 1)
            .Select(b => b.Code.ToString())
            .ToList();
        Assert.True(missing.Count == 0, "crops without exactly one farm rule: " + string.Join(", ", missing.Take(10)));
        await World.Ticks(1);
    }

    // M1: on a farm plot the crop grows - the rule leaves vanilla alone
    [AtlasScenario]
    public async Task Crop_Should_Grow_When_FarmlandIsOnAFarmPlot()
    {
        CityScene s = await GivenCity();
        Plot farm = await GivenTypedPlot(s, 0, "farm", PlotType.FARM);

        EnumHandling handling = Grow(Ground(farm));

        Assert.Equal(EnumHandling.PassThrough, handling);
    }

    // M1: the farmland under the plot's floor counts too - the crop above it is on the farm
    [AtlasScenario]
    public async Task Crop_Should_Grow_When_OnlyTheCropAboveTheFarmlandIsOnAFarmPlot()
    {
        // Arrange: farmland one block below the box, so on city land; the crop above it is the box's floor
        CityScene s = await GivenCity();
        Plot farm = await GivenTypedPlot(s, 0, "farm", PlotType.FARM);
        BlockBox box = farm.Shape.Boxes[0];
        BlockPos farmland = new(box.MinX + 3, box.MinY - 1, box.MinZ + 3);
        Assert.False(farm.Shape.Contains(farmland.X, farmland.Y, farmland.Z), "precondition: the farmland is below the plot");
        Assert.True(farm.Shape.Contains(farmland.X, farmland.Y + 1, farmland.Z), "precondition: the crop above it is in the plot");

        // Act
        EnumHandling handling = Grow(farmland);

        // Assert
        Assert.Equal(EnumHandling.PassThrough, handling);
    }

    // M1: on a plot of another type the crop does not grow
    [AtlasScenario]
    public async Task Crop_Should_NotGrow_When_OnAPlotOfAnotherType()
    {
        CityScene s = await GivenCity();
        await GivenTypedPlot(s, 0, "farm", PlotType.FARM);
        Plot plain = await GivenPlot(s, 1);
        Assert.Equal(PlotType.DEFAULT, plain.Type);

        EnumHandling handling = Grow(Ground(plain));

        Assert.Equal(EnumHandling.PreventDefault, handling);
    }

    // M1: on city land, outside any plot, the crop does not grow
    [AtlasScenario]
    public async Task Crop_Should_NotGrow_When_OnCityLand()
    {
        CityScene s = await GivenCity();
        await GivenTypedPlot(s, 0, "farm", PlotType.FARM);

        EnumHandling handling = Grow(CityLand(s).DownCopy());

        Assert.Equal(EnumHandling.PreventDefault, handling);
    }

    // M1: the code has no exception for free land - in the wilderness the crop does not grow either
    [AtlasScenario]
    public async Task Crop_Should_NotGrow_When_InTheWilderness()
    {
        CityScene s = await GivenCity();

        EnumHandling handling = Grow(WildernessBeside(s));

        Assert.Equal(EnumHandling.PreventDefault, handling);
    }

    // M1, M5: the rule reads the flag on every call, so switched off at run time it stops nothing
    [AtlasScenario]
    public async Task Crop_Should_Grow_OutsideAFarm_When_FlagIsSwitchedOffAtRunTime()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);
        Assert.Equal(EnumHandling.PreventDefault, Grow(Ground(plain)));

        // Act
        Mod.config.CROPS_ONLY_ON_FARM_PLOTS = false;

        // Assert
        Assert.Equal(EnumHandling.PassThrough, Grow(Ground(plain)));
        Assert.Equal(EnumHandling.PassThrough, Grow(WildernessBeside(s)));
    }

    // M1: planting outside a farm plot tells the planter the crop will not grow
    [AtlasScenario]
    public async Task Planting_Should_Warn_When_OutsideAFarmPlot()
    {
        CityScene s = await GivenCity("farmer");
        Plot plain = await GivenPlot(s);
        ITestPlayer farmer = s.Citizens[0];
        farmer.Client.Clear();

        Plant(farmer, Ground(plain));

        Assert.True(Got(farmer, CropWarning), "no crop_wont_grow_outside_farm on planting outside a farm: " + string.Join(" | ", farmer.Client.ChatLines()));
    }

    // M1: planting on a farm plot says nothing
    [AtlasScenario]
    public async Task Planting_Should_NotWarn_When_OnAFarmPlot()
    {
        // Arrange
        CityScene s = await GivenCity("farmer");
        Plot farm = await GivenTypedPlot(s, 0, "farm", PlotType.FARM);
        Plot plain = await GivenPlot(s, 1);
        ITestPlayer farmer = s.Citizens[0];
        farmer.Client.Clear();

        // Act
        Plant(farmer, Ground(farm));

        // Assert, then the positive control: the same planter is warned off the farm
        Assert.False(Got(farmer, CropWarning), "planting on a farm plot was warned about");
        Plant(farmer, Ground(plain));
        Assert.True(Got(farmer, CropWarning), "precondition: the planter's chat is observed");
    }

    // M1, M5: with the flag off nobody is warned
    [AtlasScenario]
    public async Task Planting_Should_NotWarn_When_FlagIsOff()
    {
        CityScene s = await GivenCity("farmer");
        Plot plain = await GivenPlot(s);
        ITestPlayer farmer = s.Citizens[0];
        Mod.config.CROPS_ONLY_ON_FARM_PLOTS = false;
        farmer.Client.Clear();

        Plant(farmer, Ground(plain));

        Assert.False(Got(farmer, CropWarning), "planting was warned about with CROPS_ONLY_ON_FARM_PLOTS off");
    }

    // ---- orchard: the planting warning ---------------------------------------------------------

    /// <summary>What the TryPlaceBlock postfix does for a sapling the player placed at the position.</summary>
    private static void PlaceSapling(ITestPlayer planter, BlockPos at, bool placed = true) =>
        OrchardPatches.Postfix_FruitTreeTryPlaceBlock(placed, Mod.sapi.World, planter.Player, new BlockSelection { Position = at.Copy() });

    // M2, M5: FRUIT_ONLY_ON_ORCHARD_PLOTS on at boot patches the root tick and the sapling placement
    [AtlasScenario]
    public async Task OrchardPatches_Should_BeApplied_When_FlagIsOnAtBoot()
    {
        MethodBase? rootTick = typeof(FruitTreeRootBH).GetMethod("onRootTick", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodBase? place = typeof(BlockFruitTreeBranch).GetMethod("TryPlaceBlock",
            new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(ItemStack), typeof(BlockSelection), typeof(string).MakeByRefType() });

        Assert.True(FarmOrchardProbe.PatchedByMod(rootTick, "FruitTreeRootBH.onRootTick"), "the root tick is not patched");
        Assert.True(FarmOrchardProbe.PatchedByMod(place, "BlockFruitTreeBranch.TryPlaceBlock"), "the sapling placement is not patched");
        await World.Ticks(1);
    }

    // M2: a sapling on a plot of another type - the planter is told it will only blossom
    [AtlasScenario]
    public async Task SaplingPlacement_Should_Warn_When_OnAPlotOfAnotherType()
    {
        CityScene s = await GivenCity("gardener");
        Plot plain = await GivenPlot(s);
        ITestPlayer gardener = s.Citizens[0];
        gardener.Client.Clear();

        PlaceSapling(gardener, Ground(plain));

        Assert.True(Got(gardener, FruitWarning), "no fruit_wont_grow_outside_orchard on a default plot: " + string.Join(" | ", gardener.Client.ChatLines()));
    }

    // M2: city land is not an orchard either
    [AtlasScenario]
    public async Task SaplingPlacement_Should_Warn_When_OnCityLand()
    {
        CityScene s = await GivenCity("gardener");
        ITestPlayer gardener = s.Citizens[0];
        gardener.Client.Clear();

        PlaceSapling(gardener, CityLand(s));

        Assert.True(Got(gardener, FruitWarning), "no fruit_wont_grow_outside_orchard on city land");
    }

    // M2: on an orchard plot nothing is said
    [AtlasScenario]
    public async Task SaplingPlacement_Should_NotWarn_When_OnAnOrchardPlot()
    {
        // Arrange
        CityScene s = await GivenCity("gardener");
        Plot orchard = await GivenTypedPlot(s, 0, "orchard", PlotType.ORCHARD);
        Plot plain = await GivenPlot(s, 1);
        ITestPlayer gardener = s.Citizens[0];
        gardener.Client.Clear();

        // Act
        PlaceSapling(gardener, Ground(orchard));

        // Assert, then the positive control on the plain plot
        Assert.False(Got(gardener, FruitWarning), "a sapling on an orchard plot was warned about");
        PlaceSapling(gardener, Ground(plain));
        Assert.True(Got(gardener, FruitWarning), "precondition: the gardener's chat is observed");
    }

    // M3: in the wilderness the warning follows ORCHARD_WARN_OUTSIDE_CITY - on
    [AtlasScenario]
    public async Task SaplingPlacement_Should_Warn_InTheWilderness_When_WarnOutsideCityIsOn()
    {
        CityScene s = await GivenCity();
        Assert.True(Mod.config.ORCHARD_WARN_OUTSIDE_CITY, "precondition: ORCHARD_WARN_OUTSIDE_CITY is on");
        s.Mayor.Client.Clear();

        PlaceSapling(s.Mayor, WildernessBeside(s));

        Assert.True(Got(s.Mayor, FruitWarning), "no warning for a sapling in the wilderness");
    }

    // M3: off - no warning in the wilderness, but still one on a plot that is not an orchard
    [AtlasScenario]
    public async Task SaplingPlacement_Should_NotWarnInTheWilderness_ButStillOnAPlot_When_WarnOutsideCityIsOff()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);
        Mod.config.ORCHARD_WARN_OUTSIDE_CITY = false;
        s.Mayor.Client.Clear();

        // Act + Assert: the wilderness
        PlaceSapling(s.Mayor, WildernessBeside(s));
        Assert.False(Got(s.Mayor, FruitWarning), "a sapling in the wilderness was warned about with ORCHARD_WARN_OUTSIDE_CITY off");

        // Act + Assert: the plot
        PlaceSapling(s.Mayor, Ground(plain));
        Assert.True(Got(s.Mayor, FruitWarning), "ORCHARD_WARN_OUTSIDE_CITY off also took the warning on a plot away");
    }

    // M2: a placement the game refused is not warned about
    [AtlasScenario]
    public async Task SaplingPlacement_Should_NotWarn_When_ThePlacementFailed()
    {
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);
        s.Mayor.Client.Clear();

        PlaceSapling(s.Mayor, Ground(plain), placed: false);

        Assert.False(Got(s.Mayor, FruitWarning), "a failed placement was warned about");
    }

    // M5: with the flag off the patch, should it be there, says nothing
    [AtlasScenario]
    public async Task SaplingPlacement_Should_NotWarn_When_FlagIsOff()
    {
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);
        Mod.config.FRUIT_ONLY_ON_ORCHARD_PLOTS = false;
        s.Mayor.Client.Clear();

        PlaceSapling(s.Mayor, Ground(plain));

        Assert.False(Got(s.Mayor, FruitWarning), "a sapling was warned about with FRUIT_ONLY_ON_ORCHARD_PLOTS off");
    }

    // ---- orchard: the fruit after the root tick ------------------------------------------------

    /// <summary>A block entity standing for the tree's root; the rule reads only its position.</summary>
    private sealed class RootEntity : BlockEntity
    {
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

    /// <summary>
    /// The root behavior of a fruit tree at the position with one fruit type in the given state,
    /// as the vanilla root tick leaves it. Built without its constructor: the rule reads only the
    /// block entity's position and propsByType, and growing a real tree takes a game year.
    /// </summary>
    private static (FruitTreeRootBH Root, FruitTreeProperties Props) TreeAt(BlockPos pos, EnumFruitTreeState state)
    {
        var be = new RootEntity { Pos = pos.Copy() };
        var root = (FruitTreeRootBH)RuntimeHelpers.GetUninitializedObject(typeof(FruitTreeRootBH));
        SetMember(root, "Blockentity", be);

        var props = (FruitTreeProperties)RuntimeHelpers.GetUninitializedObject(typeof(FruitTreeProperties));
        props.State = state;
        props.workingState = state;

        FieldInfo? field = typeof(FruitTreeRootBH).GetField("propsByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(field != null, "precondition: FruitTreeRootBH has the field propsByType");
        var map = (IDictionary)Activator.CreateInstance(field!.FieldType)!;
        map["cherry"] = props;
        field.SetValue(root, map);
        return (root, props);
    }

    /// <summary>What the root tick postfix leaves of the tree's state.</summary>
    private static EnumFruitTreeState AfterRootTick(BlockPos pos, EnumFruitTreeState state)
    {
        var (root, props) = TreeAt(pos, state);
        OrchardPatches.Postfix_FruitTreeRootTick(root);
        return props.State;
    }

    // M2: fruit on an orchard plot is kept
    [AtlasTheory]
    [InlineData(EnumFruitTreeState.Fruiting)]
    [InlineData(EnumFruitTreeState.Ripe)]
    public async Task Fruit_Should_BeKept_When_TreeIsOnAnOrchardPlot(EnumFruitTreeState state)
    {
        CityScene s = await GivenCity();
        Plot orchard = await GivenTypedPlot(s, 0, "orchard", PlotType.ORCHARD);

        EnumFruitTreeState after = AfterRootTick(Ground(orchard), state);

        Assert.Equal(state, after);
    }

    // M2: fruit on a plot of another type goes back to Empty
    [AtlasTheory]
    [InlineData(EnumFruitTreeState.Fruiting)]
    [InlineData(EnumFruitTreeState.Ripe)]
    public async Task Fruit_Should_BeStripped_When_TreeIsOnAPlotOfAnotherType(EnumFruitTreeState state)
    {
        CityScene s = await GivenCity();
        await GivenTypedPlot(s, 0, "orchard", PlotType.ORCHARD);
        Plot plain = await GivenPlot(s, 1);

        EnumFruitTreeState after = AfterRootTick(Ground(plain), state);

        Assert.Equal(EnumFruitTreeState.Empty, after);
    }

    // M2: the wilderness is no orchard - the fruit is stripped there too (ORCHARD_WARN_OUTSIDE_CITY
    // only decides about the warning)
    [AtlasScenario]
    public async Task Fruit_Should_BeStripped_When_TreeIsInTheWilderness()
    {
        CityScene s = await GivenCity();
        Mod.config.ORCHARD_WARN_OUTSIDE_CITY = false;

        EnumFruitTreeState after = AfterRootTick(WildernessBeside(s), EnumFruitTreeState.Ripe);

        Assert.Equal(EnumFruitTreeState.Empty, after);
    }

    // M2: outside an orchard the tree still blossoms
    [AtlasScenario]
    public async Task Blossom_Should_BeLeftAlone_When_TreeIsOutsideAnOrchard()
    {
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);

        EnumFruitTreeState after = AfterRootTick(Ground(plain), EnumFruitTreeState.Flowering);

        Assert.Equal(EnumFruitTreeState.Flowering, after);
    }

    // M5: with the flag off the fruit stays wherever the tree is
    [AtlasScenario]
    public async Task Fruit_Should_BeKept_When_FlagIsOff()
    {
        CityScene s = await GivenCity();
        Plot plain = await GivenPlot(s);
        Mod.config.FRUIT_ONLY_ON_ORCHARD_PLOTS = false;

        EnumFruitTreeState after = AfterRootTick(Ground(plain), EnumFruitTreeState.Ripe);

        Assert.Equal(EnumFruitTreeState.Ripe, after);
    }

    // M4: the type is read on every tick, so on a plot made an orchard the fruit is no longer stripped.
    // (Fruit stripped before the change is not brought back: the tree stays Empty until its next
    // season, so the plan's "fruit appears without waiting" does not hold: a bug candidate for M4.)
    [AtlasScenario]
    public async Task Fruit_Should_NoLongerBeStripped_When_ThePlotBecomesAnOrchard()
    {
        // Arrange: a plain plot strips the fruit
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        Assert.Equal(EnumFruitTreeState.Empty, AfterRootTick(Ground(plot), EnumFruitTreeState.Ripe));

        // Act
        CommandResult set = await RunPaid(s.Mayor, $"{PlotCmd} set type orchard", () => plot.Type == PlotType.ORCHARD, "the plot to become an orchard");

        // Assert
        Assert.True(plot.Type == PlotType.ORCHARD, "precondition: /plot set type orchard: " + set.Message);
        Assert.Equal(EnumFruitTreeState.Ripe, AfterRootTick(Ground(plot), EnumFruitTreeState.Ripe));
    }
}

/// <summary>
/// M5: the default config (both flags off) - the crop rule is not attached and the orchard patches
/// are not applied, so planting and fruit are vanilla. A server of its own: the flags are read at boot.
/// </summary>
[Trait("Category", "E2E")]
public class FarmOrchardOffScenarios : CxScenarioBase
{
    // M5: no crop block carries the farm rule
    [AtlasScenario]
    public async Task FarmRule_Should_NotBeAttached_When_FlagIsOffAtBoot()
    {
        Assert.False(Mod.config.CROPS_ONLY_ON_FARM_PLOTS, "precondition: CROPS_ONLY_ON_FARM_PLOTS is off by default");
        var crops = Mod.sapi.World.Blocks.Where(b => b?.CropProps != null).ToList();

        Assert.True(crops.Count > 0, "precondition: the game has crop blocks");
        var with = crops.Where(b => b.CropProps.Behaviors?.Any(x => x is BehaviorOnlyOnFarmPlot) == true)
            .Select(b => b.Code.ToString())
            .ToList();
        Assert.True(with.Count == 0, "crops with the farm rule although the flag is off: " + string.Join(", ", with.Take(10)));
        await World.Ticks(1);
    }

    // M5: neither orchard target is patched
    [AtlasScenario]
    public async Task OrchardPatches_Should_NotBeApplied_When_FlagIsOffAtBoot()
    {
        Assert.False(Mod.config.FRUIT_ONLY_ON_ORCHARD_PLOTS, "precondition: FRUIT_ONLY_ON_ORCHARD_PLOTS is off by default");
        MethodBase? rootTick = typeof(FruitTreeRootBH).GetMethod("onRootTick", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodBase? place = typeof(BlockFruitTreeBranch).GetMethod("TryPlaceBlock",
            new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(ItemStack), typeof(BlockSelection), typeof(string).MakeByRefType() });
        // Positive control: a patch the mod always applies is seen by the probe.
        MethodBase? barrel = typeof(BlockEntityBarrel).GetMethod("OnReceivedClientPacket");

        Assert.True(FarmOrchardProbe.PatchedByMod(barrel, "BlockEntityBarrel.OnReceivedClientPacket"), "precondition: the probe sees the mod's patches");
        Assert.False(FarmOrchardProbe.PatchedByMod(rootTick, "FruitTreeRootBH.onRootTick"), "the root tick is patched with the flag off");
        Assert.False(FarmOrchardProbe.PatchedByMod(place, "BlockFruitTreeBranch.TryPlaceBlock"), "the sapling placement is patched with the flag off");
        await World.Ticks(1);
    }
}

/// <summary>Whether the mod's Harmony instance patched a method. Harmony is reached by reflection: the test project does not reference it.</summary>
file static class FarmOrchardProbe
{
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
