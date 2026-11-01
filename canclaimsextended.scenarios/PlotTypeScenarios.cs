using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.blocks;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Plot types: the list (G1), changing the type and who may (G2), what the old type leaves
/// behind (G3), the summon limit of the city level (G4) and blocks allowed only on some types
/// (G5, PLOT_TYPE_ONLY_BLOCKS). Section G of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class PlotTypeScenarios : CxScenarioBase
{
    private static readonly string[] AllCodes = { "default", "farm", "orchard", "temple", "summon", "prison", "shop" };

    /// <summary>Every type enabled; the host's DISABLED_PLOT_TYPES comes back on Dispose.</summary>
    protected override void ResetConfig()
    {
        base.ResetConfig();
        HashSet<string> before = Mod.config.DISABLED_PLOT_TYPES;
        AddCleanup(() => Mod.config.DISABLED_PLOT_TYPES = before);
        Mod.config.DISABLED_PLOT_TYPES = new HashSet<string>();
    }

    /// <summary>The mayor, standing in the plot, sets its type and agrees when it costs.</summary>
    private async Task<CommandResult> SetType(ITestPlayer player, Plot plot, string code, PlotType expected)
    {
        await StandIn(player, plot.Shape.Boxes[0]);
        return await RunPaid(player, $"{PlotCmd} set type {code}", () => plot.Type == expected, $"the plot to become {code}");
    }

    /// <summary>Runs /plot set type that must be refused; agrees too, in case it only made an offer.</summary>
    private async Task<CommandResult> TrySetType(ITestPlayer player, Plot plot, string code)
    {
        await StandIn(player, plot.Shape.Boxes[0]);
        CommandResult result = await player.ExecuteCommand($"{PlotCmd} set type {code}");
        await player.ExecuteCommand("/" + Mod.config.COMMAND_AGREE);
        await World.Ticks(AgreeTimeoutTicks);
        return result;
    }

    // ---- G1 ----------------------------------------------------------------------------------

    // G1: /plot types lists the seven types
    [AtlasScenario]
    public async Task Types_Should_ListEveryEnabledType()
    {
        CityScene s = await GivenCity();

        CommandResult types = await s.Mayor.ExecuteCommand($"{PlotCmd} types");

        Assert.True(types.Ok, "/plot types: " + types.Message);
        Assert.Equal(AllCodes.OrderBy(c => c), PlotTypes.EnabledCodes.OrderBy(c => c));
        foreach (string code in AllCodes)
        {
            Assert.Contains(code, types.Message);
        }
    }

    // G1, G2: a type in DISABLED_PLOT_TYPES is not offered
    [AtlasScenario]
    public async Task Types_Should_LeaveOutDisabledTypes()
    {
        await GivenCity();
        Mod.config.DISABLED_PLOT_TYPES.Add("farm");

        Assert.DoesNotContain("farm", PlotTypes.EnabledCodes);
        Assert.Contains("orchard", PlotTypes.EnabledCodes);
    }

    // ---- G2 ----------------------------------------------------------------------------------

    // G2: the mayor changes the type
    [AtlasScenario]
    public async Task SetType_Should_ChangeTheType_When_MayorSetsIt()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        Assert.Equal(PlotType.DEFAULT, plot.Type);

        CommandResult set = await SetType(s.Mayor, plot, "farm", PlotType.FARM);

        Assert.True(set.Ok, "/plot set type farm: " + set.Message);
        Assert.Equal(PlotType.FARM, plot.Type);
        AssertInvariants();
    }

    // G2: with an economy the city pays PLOT_TYPE_SET_COSTS after /agree
    [AtlasScenario]
    public async Task SetType_Should_ChargeTheCity_When_TypeHasAPrice()
    {
        // Arrange: farm costs 30, the treasury holds 100
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        var costs = Mod.config.PLOT_TYPE_SET_COSTS;
        bool had = costs.TryGetValue("farm", out double before);
        costs["farm"] = 30;
        AddCleanup(() => { if (had) costs["farm"] = before; else costs.Remove("farm"); });
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100);

        // Act
        CommandResult set = await SetType(s.Mayor, plot, "farm", PlotType.FARM);

        // Assert
        Assert.True(set.Ok, "/plot set type farm: " + set.Message);
        Assert.Equal(PlotType.FARM, plot.Type);
        Assert.Equal(70m, money.GetBalance(s.City.MoneyAccountName));
    }

    // G2: an unknown code, or one of a type players cannot set, is refused
    [AtlasTheory]
    [InlineData("xyz")]
    [InlineData("tavern")]
    public async Task SetType_Should_BeRefused_When_CodeIsUnknown(string code)
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult set = await TrySetType(s.Mayor, plot, code);

        Assert.False(set.Ok, $"/plot set type {code}: " + set.Message);
        Assert.Equal(PlotType.DEFAULT, plot.Type);
    }

    // G2: the type the plot already has
    [AtlasScenario]
    public async Task SetType_Should_BeRefused_When_TypeIsTheSame()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await SetType(s.Mayor, plot, "farm", PlotType.FARM);

        CommandResult again = await TrySetType(s.Mayor, plot, "farm");

        Assert.False(again.Ok, "setting the same type again went through: " + again.Message);
        Assert.Equal(PlotType.FARM, plot.Type);
    }

    // G2: a type the host switched off - listed in DISABLED_PLOT_TYPES, or the temple with respawn off
    [AtlasTheory]
    [InlineData("farm", "listed")]
    [InlineData("temple", "respawn off")]
    public async Task SetType_Should_BeRefused_When_TheHostSwitchedItOff(string code, string how)
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        if (how == "listed") Mod.config.DISABLED_PLOT_TYPES.Add(code);
        else Mod.config.TEMPLE_RESPAWN_ENABLED = false;

        CommandResult set = await TrySetType(s.Mayor, plot, code);

        Assert.False(set.Ok, $"/plot set type {code} ({how}): " + set.Message);
        Assert.Equal(PlotType.DEFAULT, plot.Type);
    }

    // G2, F6: an owner who is not the mayor does not set the type - the city pays its upkeep
    [AtlasScenario]
    public async Task SetType_Should_BeRefused_When_OwnerIsNotMayor()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);

        CommandResult set = await TrySetType(owner, plot, "farm");

        Assert.False(set.Ok, "an owner set the type of their plot: " + set.Message);
        Assert.Equal(PlotType.DEFAULT, plot.Type);
    }

    // G2: the mayor of another city cannot change a plot here
    [AtlasScenario]
    public async Task SetType_Should_BeRefused_When_PlotIsOfAnotherCity()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        CityScene other = await GivenCity();

        CommandResult set = await TrySetType(other.Mayor, plot, "farm");

        Assert.False(set.Ok, "a foreign mayor set the type: " + set.Message);
        Assert.Equal(PlotType.DEFAULT, plot.Type);
    }

    // ---- G3 ----------------------------------------------------------------------------------

    // G3, K6: prison -> default frees everyone serving there and removes the cells
    [AtlasScenario]
    public async Task PrisonToDefault_Should_ReleasePrisonersAndCells()
    {
        // Arrange: a prison with one cell and one prisoner
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await SetType(s.Mayor, plot, "prison", PlotType.PRISON);
        CommandResult cell = await s.Mayor.ExecuteCommand($"{CityCmd} prison addcell");
        var prison = Assert.IsType<PlotDescPrison>(plot.Desc);
        Assert.True(cell.Ok && prison.Cells.Count == 1, "precondition: a cell was added: " + cell.Message);
        ITestPlayer prisoner = await GivenStranger(s, "prisoner");
        AllowAdminCommands(s.Mayor);
        CommandResult jail = await s.Mayor.ExecuteCommand($"{CadminCmd} player jail {Name(prisoner)} {s.City.GetPartName()} 30");
        Assert.True(Info(prisoner).isPrisoned(), "precondition: jailed: " + jail.Message);

        // Act
        CommandResult set = await SetType(s.Mayor, plot, "default", PlotType.DEFAULT);

        // Assert
        Assert.True(set.Ok, "/plot set type default: " + set.Message);
        Assert.False(Info(prisoner).isPrisoned(), "the prisoner is still serving in a plot that is no prison");
        Assert.Empty(prison.Cells);
        Assert.IsNotType<PlotDescPrison>(plot.Desc);
        AssertInvariants();
    }

    // G3, J4: temple -> default takes the respawn point away
    [AtlasScenario]
    public async Task TempleToDefault_Should_ClearTheRespawnPoint()
    {
        // Arrange: a temple with its point set (as the temple block would)
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await SetType(s.Mayor, plot, "temple", PlotType.TEMPLE);
        var temple = Assert.IsType<PlotDescTemple>(plot.Desc);
        temple.Point = Middle(plot.Shape.Boxes[0]);

        // Act
        CommandResult set = await SetType(s.Mayor, plot, "default", PlotType.DEFAULT);

        // Assert
        Assert.True(set.Ok, "/plot set type default: " + set.Message);
        Assert.Null(temple.Point);
        Assert.IsNotType<PlotDescTemple>(plot.Desc);
    }

    // ---- G4 ----------------------------------------------------------------------------------

    /// <summary>Sets the summon limit of the city's current level; the old value comes back on Dispose.</summary>
    private void SetSummonLimit(City city, int limit)
    {
        CityLevelInfo level = Settings.GetCityLevelInfo(city.getCityCitizens().Count);
        int previous = level.SummonPoints;
        AddCleanup(() => level.SummonPoints = previous);
        level.SummonPoints = limit;
    }

    // G4: a level without summon points gives no summon plot
    [AtlasScenario]
    public async Task Summon_Should_BeRefused_When_LevelHasNoSummonPoints()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        SetSummonLimit(s.City, 0);

        CommandResult set = await TrySetType(s.Mayor, plot, "summon");

        Assert.False(set.Ok, "a summon plot over a limit of 0: " + set.Message);
        Assert.Equal(PlotType.DEFAULT, plot.Type);
    }

    // G4: with one summon point the first plot passes, the second is refused
    [AtlasScenario]
    public async Task Summon_Should_StopAtTheLevelLimit()
    {
        // Arrange
        CityScene s = await GivenCity();
        Plot first = await GivenPlot(s, 0);
        Plot second = await GivenPlot(s, 1);
        SetSummonLimit(s.City, 1);

        // Act
        CommandResult one = await SetType(s.Mayor, first, "summon", PlotType.SUMMON);
        CommandResult two = await TrySetType(s.Mayor, second, "summon");

        // Assert
        Assert.True(one.Ok, "the first summon plot: " + one.Message);
        Assert.Equal(PlotType.SUMMON, first.Type);
        Assert.False(two.Ok, "a second summon plot over a limit of 1: " + two.Message);
        Assert.Equal(PlotType.DEFAULT, second.Type);
    }

    // G4: summon -> default frees the point for another plot
    [AtlasScenario]
    public async Task Summon_Should_FreeTheLimit_When_PlotStopsBeingSummon()
    {
        CityScene s = await GivenCity();
        Plot first = await GivenPlot(s, 0);
        Plot second = await GivenPlot(s, 1);
        SetSummonLimit(s.City, 1);
        await SetType(s.Mayor, first, "summon", PlotType.SUMMON);
        Assert.Equal(PlotType.SUMMON, first.Type);

        await SetType(s.Mayor, first, "default", PlotType.DEFAULT);
        CommandResult two = await SetType(s.Mayor, second, "summon", PlotType.SUMMON);

        Assert.True(two.Ok, "the limit was not freed: " + two.Message);
        Assert.Equal(PlotType.SUMMON, second.Type);
    }

    // ---- G5 ----------------------------------------------------------------------------------

    private const string AnvilKey = "game:anvil-*";

    /// <summary>
    /// Keeps PLOT_TYPE_ONLY_BLOCKS[key] as it was before the scenario: /cadmin blockrule writes the
    /// config file, so the file is written back too.
    /// </summary>
    private void KeepBlockRule(string key)
    {
        var rules = Mod.config.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
        List<string>? before = rules.TryGetValue(key, out List<string>? types) && types != null ? new List<string>(types) : null;
        AddCleanup(() =>
        {
            var now = Mod.config.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
            if (before == null) now.Remove(key);
            else now[key] = before;
            Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        });
        if (before != null)
        {
            rules.Remove(key);
        }
    }

    private static Block AnyBlockOf(string key)
    {
        Block? block = Mod.sapi.World.Blocks.FirstOrDefault(b => b?.Code != null && BehaviorPlotTypeOnly.Matches(b, key));
        Assert.True(block != null, $"precondition: some block matches {key}");
        return block!;
    }

    /// <summary>What the placement check says for the player putting the block at the position.</summary>
    private static bool MayPlace(Block block, ITestPlayer player, BlockPos pos)
    {
        BehaviorPlotTypeOnly? behavior = block.BlockBehaviors?.OfType<BehaviorPlotTypeOnly>().FirstOrDefault();
        if (behavior == null) return true;
        EnumHandling handling = EnumHandling.PassThrough;
        string failureCode = "";
        return behavior.CanPlaceBlock(Mod.sapi.World, player.Player, new BlockSelection { Position = pos }, ref handling, ref failureCode);
    }

    // G5: /cadmin blockrule add writes the rule and hooks the matching blocks at once
    [AtlasScenario]
    public async Task BlockRuleAdd_Should_StoreTheRule()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);

        CommandResult add = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");

        Assert.True(add.Ok, "/cadmin blockrule add: " + add.Message);
        Assert.True(Mod.config.PLOT_TYPE_ONLY_BLOCKS.TryGetValue(AnvilKey, out List<string>? types), "the rule is not in the config");
        Assert.Equal(new[] { "shop" }, types);
        Assert.Contains(anvil.BlockBehaviors, b => b is BehaviorPlotTypeOnly);
        Assert.Equal(new[] { "shop" }, BehaviorPlotTypeOnly.AllowedTypes(anvil));
    }

    // G5: an unknown plot type is refused and nothing is stored
    [AtlasScenario]
    public async Task BlockRuleAdd_Should_BeRefused_When_TypeIsUnknown()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);

        CommandResult add = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} tavern");

        Assert.False(add.Ok, "a rule with an unknown type was added: " + add.Message);
        Assert.False(Mod.config.PLOT_TYPE_ONLY_BLOCKS.ContainsKey(AnvilKey), "the rule landed in the config");
    }

    // G5: /cadmin blockrule remove takes the whole key away
    [AtlasScenario]
    public async Task BlockRuleRemove_Should_DropTheRule()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);
        await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");
        Assert.True(Mod.config.PLOT_TYPE_ONLY_BLOCKS.ContainsKey(AnvilKey), "precondition: the rule was added");

        CommandResult remove = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule remove {AnvilKey}");

        Assert.True(remove.Ok, "/cadmin blockrule remove: " + remove.Message);
        Assert.False(Mod.config.PLOT_TYPE_ONLY_BLOCKS.ContainsKey(AnvilKey), "the rule is still in the config");
        Assert.Empty(BehaviorPlotTypeOnly.AllowedTypes(anvil));
    }

    // G5: a survival player places the block only on a plot of a listed type, and anywhere again once the rule is gone
    [AtlasScenario]
    public async Task RuledBlock_Should_BePlacedOnlyOnListedType()
    {
        // Arrange: a shop plot and a plain one, the rule "anvils only on shops"
        CityScene s = await GivenCity("builder");
        ITestPlayer builder = s.Citizens[0];
        builder.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        Plot shop = await GivenPlot(s, 0);
        Plot plain = await GivenPlot(s, 1);
        await SetType(s.Mayor, shop, "shop", PlotType.SHOP);
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);
        CommandResult add = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");
        Assert.True(add.Ok, "precondition: /cadmin blockrule add: " + add.Message);
        BlockPos onShop = Middle(shop.Shape.Boxes[0]), onPlain = Middle(plain.Shape.Boxes[0]);

        // Act + Assert
        Assert.True(MayPlace(anvil, builder, onShop), "the anvil could not be placed on the shop plot");
        Assert.False(MayPlace(anvil, builder, onPlain), "the anvil was placed on a plain plot");

        await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule remove {AnvilKey}");
        Assert.True(MayPlace(anvil, builder, onPlain), "the removed rule still stops the anvil");
    }

    // G5: a creative player is not stopped
    [AtlasScenario]
    public async Task RuledBlock_Should_BePlaced_When_PlayerIsInCreative()
    {
        CityScene s = await GivenCity("creator");
        ITestPlayer creator = s.Citizens[0];
        Plot plain = await GivenPlot(s);
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);
        await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");
        EnumGameMode mode = creator.Player.WorldData.CurrentGameMode;
        creator.Player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        AddCleanup(() => creator.Player.WorldData.CurrentGameMode = mode);

        Assert.True(MayPlace(anvil, creator, Middle(plain.Shape.Boxes[0])), "a creative player was stopped by the rule");
    }
}
