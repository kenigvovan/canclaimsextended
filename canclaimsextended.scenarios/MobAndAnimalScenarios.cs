using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.events;
using canclaimsextended.src.harmony;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Animals and mobs on a plot. I3: the animals of PROTECTED_MOB_TYPES are hurt on a plot only by
/// players with the attack right - the damage prefix (HarmonyPatches.Prefix_On_ReceiveDamage) is
/// asked with a real spawned animal and a damage source of a test player, since a test player
/// cannot swing a weapon. I9, I10: the spawn decision (OnTryEntitySpawn.Event_OnTrySpawnEntity),
/// asked with the game's own entity types, as the spawner would ask it.
/// Section I of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class MobAndAnimalScenarios : CxScenarioBase
{
    private const string Chicken = "game:chicken-hen";
    private const string Hare = "game:hare-european-adult-male";
    private const string Drifter = "game:drifter-normal";

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>The protected list as the defaults have it, back as it was on Dispose.</summary>
    protected override void ResetConfig()
    {
        base.ResetConfig();
        var before = new HashSet<string>(Mod.config.PROTECTED_MOB_TYPES);
        AddCleanup(() => Mod.config.PROTECTED_MOB_TYPES = before);
        Mod.config.PROTECTED_MOB_TYPES = new HashSet<string> { "pig-*", "sheep-*", "chicken-*" };
    }

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

    /// <summary>Spawns the animal at the position. It stays: each scenario has its own area.</summary>
    private Entity SpawnAnimal(string code, BlockPos pos) => World.SpawnEntity(code, pos);

    /// <summary>
    /// What the damage prefix answers for a melee hit of the player on the entity: true lets the
    /// hit go on to vanilla, false refuses it.
    /// </summary>
    private static bool MayHurt(ITestPlayer attacker, Entity victim)
    {
        var source = new DamageSource
        {
            Source = EnumDamageSource.Player,
            Type = EnumDamageType.BluntAttack,
            SourceEntity = attacker.Entity,
        };
        bool result = true;
        bool goesOn = HarmonyPatches.Prefix_On_ReceiveDamage(victim, source, ref result);
        return goesOn && result;
    }

    /// <summary>What the spawner is told for an entity of this type at the position.</summary>
    private static bool MaySpawn(string code, BlockPos at)
    {
        EntityProperties properties = Mod.sapi.World.GetEntityType(new AssetLocation(code));
        Assert.True(properties != null, $"precondition: {code} is a known entity type");
        return OnTryEntitySpawn.Event_OnTrySpawnEntity(Mod.sapi.World.BlockAccessor, ref properties,at.ToVec3d().Add(0.5, 0, 0.5), 0);
    }

    private static string? SpawnGroup(string code) =>
        Mod.sapi.World.GetEntityType(new AssetLocation(code))?.Server?.SpawnConditions?.Runtime?.Group;

    /// <summary>A plot where strangers may not attack animals, a stranger, and a chicken in the plot.</summary>
    private async Task<(CityScene Scene, Plot Plot, ITestPlayer Stranger, Entity Animal)> GivenAnimalOnPlot(string code = Chicken)
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger attack off");
        ITestPlayer stranger = await GivenStranger(s, "hunter");
        await StandIn(stranger, plot.Shape.Boxes[0]);
        Entity animal = SpawnAnimal(code, Middle(plot.Shape.Boxes[0]));
        Assert.True(Mod.dataStorage.GetPlotAt(animal.Pos.XYZ, out Plot here) && plot.Equals(here),
            "precondition: the animal stands in the plot");
        return (s, plot, stranger, animal);
    }

    // ---- I3: protected animals -----------------------------------------------------------------

    // I3: the default list is what the plan names
    [AtlasTheory]
    [InlineData(Chicken)]
    [InlineData("game:pig-wild-male")]
    [InlineData("game:sheep-bighorn-male")]
    public async Task ProtectedMobTypes_Should_CoverChickenPigAndSheep(string code)
    {
        ResetConfig();

        bool isProtected = HarmonyPatches.IsProtectedMob(new AssetLocation(code));

        Assert.True(isProtected, $"{code} is not protected by PROTECTED_MOB_TYPES");
        await World.Ticks(1);
    }

    // I3: a stranger without ATTACK cannot hurt a chicken on the plot
    [AtlasScenario]
    public async Task ProtectedAnimal_Should_NotBeHurt_When_StrangerHasNoAttackRight()
    {
        var (_, _, stranger, chicken) = await GivenAnimalOnPlot();

        Assert.False(MayHurt(stranger, chicken), "a stranger without ATTACK hurt a chicken on the plot");
    }

    // I3: /plot set perm stranger attack on lets the stranger hunt there
    [AtlasScenario]
    public async Task ProtectedAnimal_Should_BeHurt_When_StrangerHasAttackRight()
    {
        // Arrange
        var (s, plot, stranger, chicken) = await GivenAnimalOnPlot();
        Assert.False(MayHurt(stranger, chicken), "precondition: the chicken is protected");

        // Act
        CommandResult on = await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger attack on");

        // Assert
        Assert.True(on.Ok, "precondition: /plot set perm stranger attack on: " + on.Message);
        Assert.True(MayHurt(stranger, chicken), "the attack right did not let the stranger hurt the chicken");
    }

    // I3: the mayor may hurt the chicken whatever the rows say
    [AtlasScenario]
    public async Task ProtectedAnimal_Should_BeHurt_When_TheMayorAttacks()
    {
        var (s, _, _, chicken) = await GivenAnimalOnPlot();

        Assert.True(MayHurt(s.Mayor, chicken), "the mayor could not hurt a chicken on a plot of his city");
    }

    // I3: an animal not in PROTECTED_MOB_TYPES is not protected on the plot
    [AtlasScenario]
    public async Task UnprotectedAnimal_Should_BeHurt_When_StrangerHasNoAttackRight()
    {
        var (_, _, stranger, hare) = await GivenAnimalOnPlot(Hare);
        Assert.False(HarmonyPatches.IsProtectedMob(hare.Code), "precondition: the hare is not protected");

        Assert.True(MayHurt(stranger, hare), "an unprotected animal could not be hurt");
    }

    // I3: in the wilderness every animal may be hunted
    [AtlasScenario]
    public async Task ProtectedAnimal_Should_BeHurt_When_InTheWilderness()
    {
        // Arrange: halfway between two areas, far from any city
        var (s, _, stranger, _) = await GivenAnimalOnPlot();
        BlockPos o = AreaOrigin(s.Area);
        BlockPos wild = new(o.X + 160, o.Y, o.Z);
        Assert.False(Mod.dataStorage.GetPlotAt(wild, out _), "precondition: no city owns the spot");
        await stranger.TeleportTo(wild);
        Entity chicken = SpawnAnimal(Chicken, wild);

        // Act + Assert
        Assert.True(MayHurt(stranger, chicken), "a chicken in the wilderness could not be hurt");
    }

    // ---- I9: nomobspawn ------------------------------------------------------------------------

    /// <summary>A plot with nomobspawn on; the mayor stands in it.</summary>
    private async Task<(CityScene Scene, Plot Plot)> GivenNoMobPlot()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        CommandResult on = await s.Mayor.ExecuteCommand($"{PlotCmd} set nomobspawn on");
        Assert.True(on.Ok && plot.getPermsHandler().noMobSpawnFlag, "precondition: /plot set nomobspawn on: " + on.Message);
        WorldInfo w = Mod.dataStorage.World;
        Assert.False(w.mobSpawnEverywhere || w.mobSpawnForbidden, "precondition: no world mob spawn flag is set");
        Assert.Equal("hostile", SpawnGroup(Drifter));
        return (s, plot);
    }

    // I9: a hostile mob does not spawn on a nomobspawn plot
    [AtlasScenario]
    public async Task HostileSpawn_Should_BeRefused_When_PlotHasNoMobSpawn()
    {
        var (_, plot) = await GivenNoMobPlot();

        bool spawns = MaySpawn(Drifter, Middle(plot.Shape.Boxes[0]));

        Assert.False(spawns, "a drifter spawned on a nomobspawn plot");
    }

    // I9: a peaceful animal still spawns there
    [AtlasScenario]
    public async Task PeacefulSpawn_Should_GoThrough_When_PlotHasNoMobSpawn()
    {
        var (_, plot) = await GivenNoMobPlot();
        Assert.NotEqual("hostile", SpawnGroup(Chicken));

        bool spawns = MaySpawn(Chicken, Middle(plot.Shape.Boxes[0]));

        Assert.True(spawns, "a chicken was refused on a nomobspawn plot");
    }

    // I9: switching the flag off lets hostile mobs spawn again
    [AtlasScenario]
    public async Task HostileSpawn_Should_GoThrough_When_NoMobSpawnIsOff()
    {
        var (s, plot) = await GivenNoMobPlot();

        await s.Mayor.ExecuteCommand($"{PlotCmd} set nomobspawn off");

        Assert.False(plot.getPermsHandler().noMobSpawnFlag, "precondition: nomobspawn is off");
        Assert.True(MaySpawn(Drifter, Middle(plot.Shape.Boxes[0])), "a drifter was refused on a plot without nomobspawn");
    }

    // I9: the flag of one plot does not reach the city land around it
    [AtlasScenario]
    public async Task HostileSpawn_Should_GoThrough_When_OnCityLandBesideTheNoMobPlot()
    {
        var (s, _) = await GivenNoMobPlot();
        var chunk = AreaChunk(s.Area);
        BlockPos land = new(chunk.MinBlockX + 8, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 8);
        Assert.True(Mod.dataStorage.GetPlotAt(land, out Plot here) && here.IsCityLand, "precondition: the block is city land");
        Assert.False(s.City.getPermsHandler().noMobSpawnFlag, "precondition: the city's nomobspawn is off");

        Assert.True(MaySpawn(Drifter, land), "a drifter was refused on city land without nomobspawn");
    }

    // ---- I10: world mob spawn flags ------------------------------------------------------------

    // I10: mobspawnew lets hostile mobs spawn on a nomobspawn plot
    [AtlasScenario]
    public async Task HostileSpawn_Should_GoThrough_When_WorldMobSpawnEverywhereIsOn()
    {
        // Arrange
        var (s, plot) = await GivenNoMobPlot();
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set mobspawnew on");

        // Assert
        Assert.True(set.Ok && Mod.dataStorage.World.mobSpawnEverywhere, "precondition: /cadmin world set mobspawnew on: " + set.Message);
        Assert.True(MaySpawn(Drifter, Middle(plot.Shape.Boxes[0])), "mob spawn everywhere did not override the plot");
    }

    // I10: mobspawnfb stops hostile mobs everywhere, the wilderness included; peaceful ones still spawn
    [AtlasScenario]
    public async Task HostileSpawn_Should_BeRefused_When_WorldForbidsMobSpawn()
    {
        // Arrange: a spot in the wilderness, where nothing else would stop a drifter
        var (s, _) = await GivenNoMobPlot();
        BlockPos o = AreaOrigin(s.Area);
        BlockPos wild = new(o.X + 160, o.Y, o.Z);
        Assert.True(MaySpawn(Drifter, wild), "precondition: a drifter spawns in the wilderness");
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} world set mobspawnfb on");

        // Assert
        Assert.True(set.Ok && Mod.dataStorage.World.mobSpawnForbidden, "precondition: /cadmin world set mobspawnfb on: " + set.Message);
        Assert.False(MaySpawn(Drifter, wild), "a drifter spawned although the world forbids hostile mobs");
        Assert.True(MaySpawn(Chicken, wild), "the world flag refused a peaceful animal");
    }

    // I10: with both world flags on, "everywhere" wins (it is read first)
    [AtlasScenario]
    public async Task HostileSpawn_Should_GoThrough_When_BothWorldFlagsAreOn()
    {
        var (s, plot) = await GivenNoMobPlot();
        AllowAdminCommands(s.Mayor);
        KeepWorldFlags();

        await s.Mayor.ExecuteCommand($"{CadminCmd} world set mobspawnfb on");
        await s.Mayor.ExecuteCommand($"{CadminCmd} world set mobspawnew on");

        Assert.True(Mod.dataStorage.World.mobSpawnEverywhere && Mod.dataStorage.World.mobSpawnForbidden, "precondition: both flags are on");
        Assert.True(MaySpawn(Drifter, Middle(plot.Shape.Boxes[0])), "forbidden won over everywhere");
    }
}
