using System.Reflection;
using System.Text.Json;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.blocks;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.timers;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// /cadmin, section X of TESTING_canclaimsextended.md, the parts no other class covers:
/// setcfg/getcfg (price settings written to canclaimsextended.json), level list/add/remove/set
/// (written to canclaimsextended_city_levels.json, and what the limits then let a city do),
/// blockrule list/hand and the rule outside a plot of the listed type, the scheduled backup at a
/// BACKUP_TIMES minute, and the city/player/world/plot/astest subcommands that had no scenario.
///
/// Every file a command writes is put back byte for byte on Dispose (the config file and the
/// levels file), and so is the memory behind it.
///
/// Survives a restart: [AtlasScenario(RestartWorld = true)] restarts the host BEFORE the scenario
/// body, so the /cadmin setcfg would have to run in an earlier scenario (xUnit gives no order,
/// ADR 0010), and a class built on CxScenarioBase keeps the players of earlier scenarios online,
/// which makes a restart a setup error. What a boot does with the file is checked instead: the file
/// read back through LoadModConfig (the call Config.LoadConfig makes at boot), and /cadmin reload,
/// which runs Config.LoadConfig itself.
///
/// The scheduled backup: BackupScheduler.Check is private and runs every 30 s off DateTime.Now
/// against the private static lastCheck. Waiting for the next BACKUP_TIMES minute is up to 90 s of
/// wall clock and racy, so the scenario sets lastCheck to one second before a BACKUP_TIMES entry of
/// the current minute and calls Check by reflection on the game thread - exactly the state the
/// timer is in on the first check after that minute has come. The scenario body runs on the game
/// thread, so the timer cannot run between the setup and the call; after the call lastCheck is
/// "now", so the timer does not fire for that entry again.
/// </summary>
[Trait("Category", "E2E")]
public class AdminConfigScenarios : CxScenarioBase
{
    private const string LevelsFile = "canclaimsextended_city_levels.json";
    private const string AnvilKey = "game:anvil-*";

    /// <summary>Every plot type enabled; the host's DISABLED_PLOT_TYPES comes back on Dispose.</summary>
    protected override void ResetConfig()
    {
        base.ResetConfig();
        HashSet<string> before = Mod.config.DISABLED_PLOT_TYPES;
        AddCleanup(() => Mod.config.DISABLED_PLOT_TYPES = before);
        Mod.config.DISABLED_PLOT_TYPES = new HashSet<string>();
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static void AssertDone(CommandResult result, string key, string what)
    {
        Assert.True(result.Ok, $"{what}: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static string ConfigFilePath => Path.Combine(GamePaths.ModConfig, Config.FILE_NAME);

    private static string LevelsFilePath => Mod.config.FilePath(LevelsFile);

    private static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>canclaimsextended.json is written back as it was on Dispose.</summary>
    private void KeepConfigFile()
    {
        string path = ConfigFilePath;
        string? before = ReadOrNull(path);
        AddCleanup(() =>
        {
            if (before != null) File.WriteAllText(path, before);
            else if (File.Exists(path)) File.Delete(path);
        });
    }

    /// <summary>
    /// The city levels - the same objects with the same values under the same keys - and the levels
    /// file come back on Dispose, whatever /cadmin level did to them.
    /// </summary>
    private void KeepLevels()
    {
        string path = LevelsFilePath;
        string? text = ReadOrNull(path);
        SortedDictionary<int, CityLevelInfo> dict = Settings.cityLevelsDict;
        var saved = dict.Select(kv => (Key: kv.Key, Level: kv.Value, Plots: kv.Value.AmountOfPlots, Volume: kv.Value.MaxVolume,
            Summon: kv.Value.SummonPoints, Chunks: kv.Value.MaxChunks, Extra: kv.Value.MaxExtraChunks)).ToList();
        AddCleanup(() =>
        {
            dict.Clear();
            foreach (var l in saved)
            {
                l.Level.AmountOfPlots = l.Plots;
                l.Level.MaxVolume = l.Volume;
                l.Level.SummonPoints = l.Summon;
                l.Level.MaxChunks = l.Chunks;
                l.Level.MaxExtraChunks = l.Extra;
                dict[l.Key] = l.Level;
            }
            Settings.cityLevelsDict = dict;
            if (text != null) File.WriteAllText(path, text);
            else if (File.Exists(path)) File.Delete(path);
        });
    }

    /// <summary>The level key a city of its current size uses, the same lookup as Settings.GetCityLevelInfo.</summary>
    private static int LevelKeyOf(City city)
    {
        int citizens = city.getCityCitizens().Count;
        foreach (int key in Settings.cityLevelsDict.Keys.Reverse())
        {
            if (citizens >= key) return key;
        }
        return Settings.cityLevelsDict.Keys.First();
    }

    /// <summary>One field of one level as the levels file holds it.</summary>
    private static long LevelInFile(int key, string property)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LevelsFilePath));
        Assert.True(doc.RootElement.TryGetProperty(key.ToString(), out JsonElement level), $"level {key} is not in {LevelsFile}");
        return level.GetProperty(property).GetInt64();
    }

    private static bool LevelKeyInFile(int key)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LevelsFilePath));
        return doc.RootElement.TryGetProperty(key.ToString(), out _);
    }

    /// <summary>A citizens threshold no level uses.</summary>
    private static int UnusedLevelKey()
    {
        int key = 777;
        while (Settings.cityLevelsDict.ContainsKey(key)) key++;
        return key;
    }

    /// <summary>An admin of no city, in the scene's area.</summary>
    private async Task<ITestPlayer> GivenAdmin(CityScene s)
    {
        ITestPlayer admin = await GivenStranger(s, "admin");
        AllowAdminCommands(admin);
        return admin;
    }

    /// <summary>The chunk <paramref name="dx"/> chunks east of the area's founding one.</summary>
    private ChunkPos ChunkOf(int area, int dx)
    {
        ChunkPos home = AreaChunk(area);
        return new ChunkPos(home.X + dx, home.Z);
    }

    /// <summary>Puts the player in the middle of that chunk, at the height of the area's origin.</summary>
    private async Task<ChunkPos> GoToChunk(ITestPlayer player, int area, int dx)
    {
        ChunkPos chunk = ChunkOf(area, dx);
        int size = ChunkPos.Size;
        await player.TeleportTo(new BlockPos(chunk.MinBlockX + size / 2, AreaOrigin(area).Y, chunk.MinBlockZ + size / 2));
        return chunk;
    }

    /// <summary>The box moved <paramref name="dx"/> blocks along X.</summary>
    private static BlockBox Shifted(BlockBox b, int dx) => new(b.MinX + dx, b.MinY, b.MinZ, b.MaxX + dx, b.MaxY, b.MaxZ);

    // ---- /cadmin setcfg, getcfg --------------------------------------------------------------

    /// <summary>NEW_CITY_COST comes back in memory, the file as it was, on Dispose.</summary>
    private void KeepNewCityCost()
    {
        KeepConfigFile();
        double before = Mod.config.NEW_CITY_COST;
        // Mod.config at cleanup time: /cadmin reload replaces the object.
        AddCleanup(() => Mod.config.NEW_CITY_COST = before);
    }

    // X: /cadmin setcfg changes the value, says so, and writes it to canclaimsextended.json
    [AtlasScenario]
    public async Task Setcfg_Should_ChangeTheValue_And_StoreItInTheConfigFile()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepNewCityCost();
        Assert.NotEqual(42.5, Mod.config.NEW_CITY_COST);

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg new_city_cost 42.5");

        // Assert
        AssertDone(set, "admin_cfg_set", "/cadmin setcfg new_city_cost 42.5");
        Assert.Equal(new object[] { "new_city_cost", "42.5" }, set.Raw.MessageParams);
        Assert.Equal(42.5, Mod.config.NEW_CITY_COST);
        Config onDisk = Mod.sapi.LoadModConfig<Config>(Config.FILE_NAME);
        Assert.True(onDisk != null && onDisk.NEW_CITY_COST == 42.5,
            $"the config file holds NEW_CITY_COST = {onDisk?.NEW_CITY_COST}, expected 42.5");
    }

    // X: getcfg shows the value just set, with its range
    [AtlasScenario]
    public async Task Getcfg_Should_ShowTheValueSetBySetcfg()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepNewCityCost();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg new_city_cost 42.5");
        Assert.True(set.Ok, "precondition: /cadmin setcfg: " + set.Message);

        CommandResult get = await s.Mayor.ExecuteCommand($"{CadminCmd} getcfg");

        Assert.True(get.Ok, "/cadmin getcfg: " + get.Message);
        Assert.Contains("new_city_cost = 42.5  [0..1000000]", get.Message);
    }

    // X: the key is found whatever its case, and a comma is a decimal mark
    [AtlasScenario]
    public async Task Setcfg_Should_AcceptUpperCaseKey_And_DecimalComma()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepNewCityCost();

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg NEW_CITY_COST 12,5");

        AssertDone(set, "admin_cfg_set", "/cadmin setcfg NEW_CITY_COST 12,5");
        Assert.Equal(new object[] { "new_city_cost", "12.5" }, set.Raw.MessageParams);
        Assert.Equal(12.5, Mod.config.NEW_CITY_COST);
    }

    // X: a per type key goes into the dictionary of the type
    [AtlasScenario]
    public async Task Setcfg_Should_SetThePriceOfAPlotType()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepConfigFile();
        var costs = Mod.config.PLOT_TYPE_SET_COSTS;
        bool had = costs.TryGetValue("farm", out double before);
        AddCleanup(() => { if (had) Mod.config.PLOT_TYPE_SET_COSTS["farm"] = before; else Mod.config.PLOT_TYPE_SET_COSTS.Remove("farm"); });

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg type_set_cost_farm 7");

        AssertDone(set, "admin_cfg_set", "/cadmin setcfg type_set_cost_farm 7");
        Assert.True(Mod.config.PLOT_TYPE_SET_COSTS.TryGetValue("farm", out double now) && now == 7,
            "PLOT_TYPE_SET_COSTS[farm] is not 7");
        Config onDisk = Mod.sapi.LoadModConfig<Config>(Config.FILE_NAME);
        Assert.True(onDisk.PLOT_TYPE_SET_COSTS.TryGetValue("farm", out double stored) && stored == 7,
            "the config file does not hold PLOT_TYPE_SET_COSTS[farm] = 7");
    }

    // X: what was stored is what the config load reads: /cadmin reload (Config.LoadConfig, as at
    // boot) brings the stored value back over a different one in memory
    [AtlasScenario]
    public async Task Setcfg_Should_SurviveAConfigReload()
    {
        // Arrange: set 42.5, then memory drifts away from the file
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepNewCityCost();
        KeepLevels(); // reload rereads the levels too
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg new_city_cost 42.5");
        Assert.True(set.Ok, "precondition: /cadmin setcfg: " + set.Message);
        Mod.config.NEW_CITY_COST = 1;

        // Act
        CommandResult reload = await s.Mayor.ExecuteCommand($"{CadminCmd} reload");

        // Assert
        Assert.True(reload.Ok, "/cadmin reload: " + reload.Message);
        Assert.Equal(42.5, Mod.config.NEW_CITY_COST);
    }

    // X: refusals with their exact lang keys; nothing changes in memory or in the file
    [AtlasTheory]
    [InlineData("no_such_key 5", "admin_cfg_unknown_key", new object[] { "no_such_key" })]
    [InlineData("new_city_cost abc", "admin_cfg_not_number", new object[] { "abc" })]
    [InlineData("new_city_cost -1", "admin_cfg_out_of_range", new object[] { "-1", "0", "1000000" })]
    [InlineData("new_city_cost 1000001", "admin_cfg_out_of_range", new object[] { "1000001", "0", "1000000" })]
    [InlineData("refund_percent 101", "admin_cfg_out_of_range", new object[] { "101", "0", "100" })]
    [InlineData("blocks_per_unit 2.5", "admin_cfg_not_integer", new object[] { "2.5" })]
    public async Task Setcfg_Should_BeRefused_When_TheValueIsWrong(string keyAndValue, string errorKey, object[] errorArgs)
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepConfigFile();
        double cost = Mod.config.NEW_CITY_COST, refund = Mod.config.PLOT_UNCLAIM_REFUND_PERCENT;
        int unit = Mod.config.BLOCKS_PER_COST_UNIT;
        string? fileBefore = ReadOrNull(ConfigFilePath);

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} setcfg {keyAndValue}");

        // Assert
        AssertRefused(set, errorKey, "/cadmin setcfg " + keyAndValue);
        Assert.Equal(errorArgs, set.Raw.MessageParams);
        Assert.Equal(cost, Mod.config.NEW_CITY_COST);
        Assert.Equal(refund, Mod.config.PLOT_UNCLAIM_REFUND_PERCENT);
        Assert.Equal(unit, Mod.config.BLOCKS_PER_COST_UNIT);
        Assert.Equal(fileBefore, ReadOrNull(ConfigFilePath));
    }

    // ---- /cadmin level ------------------------------------------------------------------------

    // X: /cadmin level set changes the field and writes the levels file
    [AtlasScenario]
    public async Task LevelSet_Should_ChangeTheField_And_WriteTheLevelsFile()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        int key = Settings.cityLevelsDict.Keys.First();
        int value = Settings.cityLevelsDict[key].AmountOfPlots == 7 ? 8 : 7;

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} level set {key} plots {value}");

        // Assert
        AssertDone(set, "admin_level_set", $"/cadmin level set {key} plots {value}");
        Assert.Equal(new object[] { key, "plots", (long)value }, set.Raw.MessageParams);
        Assert.Equal(value, Settings.cityLevelsDict[key].AmountOfPlots);
        Assert.Equal((long)value, LevelInFile(key, nameof(CityLevelInfo.AmountOfPlots)));
    }

    // X: the level's plot limit is what /plot create then goes by - down to one, and up again
    [AtlasScenario]
    public async Task LevelSet_Should_ChangeTheCityPlotLimit()
    {
        // Arrange: no bonus, the level allows one plot, the city has it
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        s.City.setBonusPlots(0);
        int key = LevelKeyOf(s.City);
        CommandResult one = await s.Mayor.ExecuteCommand($"{CadminCmd} level set {key} plots 1");
        Assert.True(one.Ok, "precondition: /cadmin level set plots 1: " + one.Message);
        Assert.Equal(1, Settings.GetMaxNumberOfPlotForCity(s.City));
        await GivenPlot(s, 0);
        BlockBox second = PlotBox(s.Area, 1);
        Select(s.Mayor, second);
        await StandIn(s.Mayor, second);

        // Act + Assert: refused at the limit
        CommandResult over = await s.Mayor.ExecuteCommand($"{PlotCmd} create");
        AssertRefused(over, "max_amount_claimed", "a plot over a level limit of 1");
        Assert.Null(PlotIn(second));

        // Act + Assert: the limit raised, the same plot goes through
        CommandResult two = await s.Mayor.ExecuteCommand($"{CadminCmd} level set {key} plots 2");
        Assert.True(two.Ok, "/cadmin level set plots 2: " + two.Message);
        Select(s.Mayor, second);
        CommandResult create = await RunPaid(s.Mayor, $"{PlotCmd} create", () => PlotIn(second) != null, "the second plot to be created");
        Assert.True(PlotIn(second) != null, "the raised limit still refused the plot: " + create.Message);
        Assert.Equal(2, s.City.getCityPlots().Count);
        AssertInvariants();
    }

    // X: a level that does not exist, a value that is no number or out of range: refused, nothing written
    [AtlasTheory]
    [InlineData("missing level")]
    [InlineData("not a number")]
    [InlineData("below the range")]
    [InlineData("above the range")]
    public async Task LevelSet_Should_BeRefused_When_LevelOrValueIsWrong(string how)
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        int key = Settings.cityLevelsDict.Keys.First();
        int missing = UnusedLevelKey();
        int plots = Settings.cityLevelsDict[key].AmountOfPlots;
        string? fileBefore = ReadOrNull(LevelsFilePath);
        (string args, string errorKey, object[] errorArgs) = how switch
        {
            "missing level" => ($"{missing} plots 5", "admin_level_missing", new object[] { missing }),
            "not a number" => ($"{key} plots abc", "admin_cfg_not_integer", new object[] { "abc" }),
            "below the range" => ($"{key} plots 0", "admin_cfg_out_of_range", new object[] { 0L, 1L, 100000L }),
            _ => ($"{key} summon 1001", "admin_cfg_out_of_range", new object[] { 1001L, 0L, 1000L }),
        };

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} level set {args}");

        // Assert
        AssertRefused(set, errorKey, $"/cadmin level set {args} ({how})");
        Assert.Equal(errorArgs, set.Raw.MessageParams);
        Assert.Equal(plots, Settings.cityLevelsDict[key].AmountOfPlots);
        Assert.False(Settings.cityLevelsDict.ContainsKey(missing), "a missing level was created");
        Assert.Equal(fileBefore, ReadOrNull(LevelsFilePath));
    }

    // X: a field outside Settings.LevelFields is stopped by the argument parser
    [AtlasScenario]
    public async Task LevelSet_Should_BeRefused_When_FieldIsUnknown()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        int key = Settings.cityLevelsDict.Keys.First();
        string? fileBefore = ReadOrNull(LevelsFilePath);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} level set {key} cookies 5");

        Assert.False(set.Ok, "/cadmin level set with an unknown field ran: " + set.Message);
        Assert.Equal(fileBefore, ReadOrNull(LevelsFilePath));
    }

    // X: /cadmin level list shows every level with its numbers
    [AtlasScenario]
    public async Task LevelList_Should_ShowEveryLevel()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);

        CommandResult list = await s.Mayor.ExecuteCommand($"{CadminCmd} level list");

        Assert.True(list.Ok, "/cadmin level list: " + list.Message);
        foreach (var it in Settings.cityLevelsDict)
        {
            CityLevelInfo l = it.Value;
            Assert.Contains(Lang.Get("canclaimsextended:admin_level_line", it.Key, l.AmountOfPlots, l.MaxVolume,
                l.SummonPoints, l.MaxExtraChunks, l.MaxChunks), list.Message);
        }
    }

    // X: /cadmin level add copies the level a city of that size has now and writes it; remove takes it away
    [AtlasScenario]
    public async Task LevelAddAndRemove_Should_ChangeTheLevels_And_TheFile()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        int key = UnusedLevelKey();
        CityLevelInfo from = Settings.GetCityLevelInfo(key);

        // Act + Assert: add
        CommandResult add = await s.Mayor.ExecuteCommand($"{CadminCmd} level add {key}");
        AssertDone(add, "admin_level_added", $"/cadmin level add {key}");
        Assert.True(Settings.cityLevelsDict.TryGetValue(key, out CityLevelInfo? added), "the level was not added");
        Assert.NotSame(from, added);
        Assert.Equal(from.AmountOfPlots, added!.AmountOfPlots);
        Assert.Equal(from.MaxChunks, added.MaxChunks);
        Assert.True(LevelKeyInFile(key), "the added level is not in the levels file");

        // Act + Assert: remove
        CommandResult remove = await s.Mayor.ExecuteCommand($"{CadminCmd} level remove {key}");
        AssertDone(remove, "admin_level_removed", $"/cadmin level remove {key}");
        Assert.False(Settings.cityLevelsDict.ContainsKey(key), "the level is still there");
        Assert.False(LevelKeyInFile(key), "the removed level is still in the levels file");
    }

    // X: add refuses an existing threshold and one out of 1..10000; remove refuses a missing one
    [AtlasTheory]
    [InlineData("add existing", "admin_level_exists")]
    [InlineData("add zero", "admin_level_bad_citizens")]
    [InlineData("add too many", "admin_level_bad_citizens")]
    [InlineData("remove missing", "admin_level_missing")]
    public async Task LevelAddOrRemove_Should_BeRefused_When_ThresholdIsWrong(string how, string errorKey)
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        int existing = Settings.cityLevelsDict.Keys.First();
        int count = Settings.cityLevelsDict.Count;
        string? fileBefore = ReadOrNull(LevelsFilePath);
        string args = how switch
        {
            "add existing" => $"add {existing}",
            "add zero" => "add 0",
            "add too many" => $"add {Settings.MaxLevelCitizens + 1}",
            _ => $"remove {UnusedLevelKey()}",
        };

        CommandResult result = await s.Mayor.ExecuteCommand($"{CadminCmd} level {args}");

        AssertRefused(result, errorKey, $"/cadmin level {args}");
        Assert.Equal(count, Settings.cityLevelsDict.Count);
        Assert.Equal(fileBefore, ReadOrNull(LevelsFilePath));
    }

    // X: the last level is never removed (only the memory is swapped for one level; nothing is written)
    [AtlasScenario]
    public async Task LevelRemove_Should_BeRefused_When_ItIsTheLastLevel()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepLevels();
        string? fileBefore = ReadOrNull(LevelsFilePath);
        Settings.cityLevelsDict = new SortedDictionary<int, CityLevelInfo> { [1] = new CityLevelInfo(4, 32768, 0, 2, 0) };

        CommandResult remove = await s.Mayor.ExecuteCommand($"{CadminCmd} level remove 1");

        AssertRefused(remove, "admin_level_last", "/cadmin level remove of the last level");
        Assert.True(Settings.cityLevelsDict.ContainsKey(1), "the last level was removed");
        Assert.Equal(fileBefore, ReadOrNull(LevelsFilePath));
    }

    // ---- /cadmin blockrule ---------------------------------------------------------------------

    /// <summary>
    /// PLOT_TYPE_ONLY_BLOCKS[key] starts absent and comes back as it was on Dispose, and so does the
    /// config file, which /cadmin blockrule writes.
    /// </summary>
    private void KeepBlockRule(string key)
    {
        KeepConfigFile();
        var rules = Mod.config.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
        List<string>? before = rules.TryGetValue(key, out List<string>? types) && types != null ? new List<string>(types) : null;
        AddCleanup(() =>
        {
            var now = Mod.config.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
            if (before == null) now.Remove(key);
            else now[key] = before;
        });
        rules.Remove(key);
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

    private void InSurvival(ITestPlayer player)
    {
        EnumGameMode mode = player.Player.WorldData.CurrentGameMode;
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        AddCleanup(() => player.Player.WorldData.CurrentGameMode = mode);
    }

    private async Task SetType(ITestPlayer mayor, Plot plot, string code, PlotType expected)
    {
        await StandIn(mayor, plot.Shape.Boxes[0]);
        CommandResult set = await RunPaid(mayor, $"{PlotCmd} set type {code}", () => plot.Type == expected, $"the plot to become {code}");
        Assert.True(plot.Type == expected, $"precondition: /plot set type {code}: {set.Message}");
    }

    // X, G5: outside a plot of the listed type means nowhere else - not on city land without a plot,
    // not in the wild
    [AtlasScenario]
    public async Task RuledBlock_Should_BeRefused_OutsideAnyPlot()
    {
        // Arrange: a shop plot, the rule "anvils only on shops"
        CityScene s = await GivenCity("builder");
        ITestPlayer builder = s.Citizens[0];
        InSurvival(builder);
        Plot shop = await GivenPlot(s, 0);
        await SetType(s.Mayor, shop, "shop", PlotType.SHOP);
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);
        CommandResult add = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");
        Assert.True(add.Ok, "precondition: /cadmin blockrule add: " + add.Message);
        ChunkPos chunk = AreaChunk(s.Area);
        // The first column of the chunk is outside every PlotBox (they start one block in).
        var cityLand = new BlockPos(chunk.MinBlockX, AreaOrigin(s.Area).Y, chunk.MinBlockZ);
        Assert.False(Mod.dataStorage.GetSubPlotAt(cityLand, out _), "precondition: no plot on that city land");
        BlockPos wild = AreaOrigin(s.Area).AddCopy(4 * ChunkPos.Size, 0, 0);
        Assert.False(Mod.dataStorage.GetCityAtChunk(ChunkPos.FromBlock(wild.X, wild.Z), out _), "precondition: the wild chunk is free");

        // Act + Assert
        Assert.True(MayPlace(anvil, builder, Middle(shop.Shape.Boxes[0])), "the anvil could not be placed on the shop plot");
        Assert.False(MayPlace(anvil, builder, cityLand), "the anvil was placed on city land without a plot");
        Assert.False(MayPlace(anvil, builder, wild), "the anvil was placed in the wild");
    }

    // X, G5: a key with two types allows both; removing one type keeps the key with the other
    [AtlasScenario]
    public async Task BlockRule_Should_AllowEveryListedType_And_DropOneTypeAtATime()
    {
        // Arrange: a shop, a farm and a plain plot; the rule takes shop, then farm
        CityScene s = await GivenCity("builder");
        ITestPlayer builder = s.Citizens[0];
        InSurvival(builder);
        Plot shop = await GivenPlot(s, 0);
        Plot farm = await GivenPlot(s, 1);
        Plot plain = await GivenPlot(s, 2);
        await SetType(s.Mayor, shop, "shop", PlotType.SHOP);
        await SetType(s.Mayor, farm, "farm", PlotType.FARM);
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        Block anvil = AnyBlockOf(AnvilKey);
        await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");
        CommandResult second = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} farm");
        AssertDone(second, "admin_blockrule_added", "/cadmin blockrule add ... farm");
        Assert.Equal(new[] { "shop", "farm" }, Mod.config.PLOT_TYPE_ONLY_BLOCKS[AnvilKey]);
        BlockPos onShop = Middle(shop.Shape.Boxes[0]), onFarm = Middle(farm.Shape.Boxes[0]), onPlain = Middle(plain.Shape.Boxes[0]);
        Assert.True(MayPlace(anvil, builder, onShop) && MayPlace(anvil, builder, onFarm), "a listed type refused the anvil");
        Assert.False(MayPlace(anvil, builder, onPlain), "the anvil was placed on a plain plot");

        // Act: one type off
        CommandResult remove = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule remove {AnvilKey} shop");

        // Assert: the key stays with farm; the file says so too
        AssertDone(remove, "admin_blockrule_added", "/cadmin blockrule remove ... shop");
        Assert.Equal(new[] { "farm" }, Mod.config.PLOT_TYPE_ONLY_BLOCKS[AnvilKey]);
        Assert.False(MayPlace(anvil, builder, onShop), "the removed type still allows the anvil");
        Assert.True(MayPlace(anvil, builder, onFarm), "the type left lost the anvil");
        Config onDisk = Mod.sapi.LoadModConfig<Config>(Config.FILE_NAME);
        Assert.True(onDisk.PLOT_TYPE_ONLY_BLOCKS.TryGetValue(AnvilKey, out List<string>? stored) && stored.SequenceEqual(new[] { "farm" }),
            "the config file does not hold the rule with farm only");
    }

    // X: /cadmin blockrule list shows the rule with its types and the number of blocks it matches
    [AtlasScenario]
    public async Task BlockRuleList_Should_ShowTheRules()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepBlockRule(AnvilKey);
        await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add {AnvilKey} shop");

        CommandResult list = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule list");

        Assert.True(list.Ok, "/cadmin blockrule list: " + list.Message);
        Assert.Contains(Lang.Get("canclaimsextended:admin_blockrule_line", AnvilKey, "shop",
            BehaviorPlotTypeOnly.CountMatches(Mod.sapi, AnvilKey)), list.Message);
    }

    // X: no rules at all - the list says so (the rules are swapped in memory only; list writes nothing)
    [AtlasScenario]
    public async Task BlockRuleList_Should_SayNone_When_ThereAreNoRules()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        Dictionary<string, List<string>> before = Mod.config.PLOT_TYPE_ONLY_BLOCKS;
        AddCleanup(() => Mod.config.PLOT_TYPE_ONLY_BLOCKS = before);
        Mod.config.PLOT_TYPE_ONLY_BLOCKS = new Dictionary<string, List<string>>();

        CommandResult list = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule list");

        AssertDone(list, "admin_blockrule_none", "/cadmin blockrule list without rules");
    }

    // X: a key that names no block, and removing a key that has no rule, are refused
    [AtlasScenario]
    public async Task BlockRule_Should_BeRefused_When_KeyIsBadOrUnknown()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        KeepBlockRule("be:");
        KeepBlockRule("game:nosuchblock-*");
        string? fileBefore = ReadOrNull(ConfigFilePath);

        CommandResult bad = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule add be: shop");
        CommandResult unknown = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule remove game:nosuchblock-*");

        AssertRefused(bad, "admin_blockrule_bad_key", "/cadmin blockrule add be:");
        AssertRefused(unknown, "admin_blockrule_not_found", "/cadmin blockrule remove of a key without a rule");
        Assert.Equal(new object[] { "game:nosuchblock-*" }, unknown.Raw.MessageParams);
        Assert.False(Mod.config.PLOT_TYPE_ONLY_BLOCKS.ContainsKey("be:"), "the bad key landed in the config");
        Assert.Equal(fileBefore, ReadOrNull(ConfigFilePath));
    }

    // X: /cadmin blockrule hand names the block in hand and the keys that would match it
    [AtlasScenario]
    public async Task BlockRuleHand_Should_NameTheBlockInHand()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        Block anvil = AnyBlockOf(AnvilKey);
        await s.Mayor.GiveItem(anvil.Code.ToString());
        AddCleanup(() => s.Mayor.Player.InventoryManager.ActiveHotbarSlot.Itemstack = null);

        CommandResult hand = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule hand");

        AssertDone(hand, "admin_blockrule_hand", "/cadmin blockrule hand");
        Assert.Contains(anvil.Code.ToString(), hand.Message);
        Assert.Contains(anvil.Code.Domain + ":" + anvil.Code.Path.Split('-')[0] + "-*", hand.Message);
    }

    // X: with nothing in hand
    [AtlasScenario]
    public async Task BlockRuleHand_Should_BeRefused_When_HandIsEmpty()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        s.Mayor.Player.InventoryManager.ActiveHotbarSlot.Itemstack = null;

        CommandResult hand = await s.Mayor.ExecuteCommand($"{CadminCmd} blockrule hand");

        AssertRefused(hand, "admin_blockrule_no_block_in_hand", "/cadmin blockrule hand with an empty hand");
    }

    // ---- scheduled backup ----------------------------------------------------------------------

    /// <summary>A new empty BACKUP_FOLDER; the setting comes back and the folder is deleted on Dispose.</summary>
    private string UseOwnBackupFolder(int area)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"canclaimsextended_sched_backup_{area}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string before = Mod.config.BACKUP_FOLDER;
        Mod.config.BACKUP_FOLDER = folder;
        AddCleanup(() =>
        {
            Mod.config.BACKUP_FOLDER = before;
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        });
        Assert.Equal(folder, Mod.config.BackupFolder);
        return folder;
    }

    /// <summary>BACKUP_TIMES and BACKUP_KEEP for this scenario; the host's come back on Dispose.</summary>
    private void UseBackupSchedule(int keep, params string[] times)
    {
        List<string> timesBefore = Mod.config.BACKUP_TIMES;
        int keepBefore = Mod.config.BACKUP_KEEP;
        AddCleanup(() =>
        {
            Mod.config.BACKUP_TIMES = timesBefore;
            Mod.config.BACKUP_KEEP = keepBefore;
        });
        Mod.config.BACKUP_TIMES = new List<string>(times);
        Mod.config.BACKUP_KEEP = keep;
    }

    /// <summary>
    /// The timer's check, as it runs when the previous one was at <paramref name="lastCheck"/>:
    /// BackupScheduler.lastCheck is set and the private Check is called (see the class summary).
    /// </summary>
    private static void RunScheduledCheck(DateTime lastCheck)
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
        FieldInfo? last = typeof(BackupScheduler).GetField("lastCheck", Private);
        MethodInfo? check = typeof(BackupScheduler).GetMethod("Check", Private, Type.EmptyTypes);
        Assert.True(last != null && last.FieldType == typeof(DateTime), "BackupScheduler.lastCheck (DateTime) is gone");
        Assert.True(check != null, "BackupScheduler.Check() is gone");
        last!.SetValue(null, lastCheck);
        check!.Invoke(null, null);
    }

    private static int CountCity(string dbPath, string cityGuid)
    {
        using var connection = new SqliteConnection("Data Source=" + dbPath + ";Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = new SqliteCommand("SELECT COUNT(*) FROM CITIES WHERE guid=@g", connection);
        cmd.Parameters.AddWithValue("@g", cityGuid);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // X9: at a BACKUP_TIMES minute an auto_ copy of the database is written, and the oldest automatic
    // copies past BACKUP_KEEP go; manual copies stay
    [AtlasScenario]
    public async Task ScheduledBackup_Should_WriteAnAutoCopy_And_Prune()
    {
        // Arrange: three old automatic copies and a manual one, keep 2, a BACKUP_TIMES entry of this minute
        CityScene s = await GivenCity();
        string folder = UseOwnBackupFolder(s.Area);
        foreach (int d in new[] { 1, 2, 3 }) File.WriteAllText(Path.Combine(folder, $"auto_2000-01-0{d}_00-00-00.db"), "old");
        string manual = Path.Combine(folder, "manual_1999-01-01_00-00-00.db");
        File.WriteAllText(manual, "manual");
        DateTime now = DateTime.Now;
        var due = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        UseBackupSchedule(2, due.ToString("HH:mm"));

        // Act: the first check after that minute came
        RunScheduledCheck(due.AddSeconds(-1));

        // Assert
        string[] autos = Directory.GetFiles(folder, "auto_*.db").Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(autos.Length == 2, "automatic copies left: " + string.Join(", ", autos));
        Assert.Equal("auto_2000-01-03_00-00-00.db", autos[0]);
        Assert.StartsWith("auto_" + DateTime.Now.Year, autos[1]);
        Assert.Equal(1, CountCity(Path.Combine(folder, autos[1]), s.City.Guid));
        Assert.True(File.Exists(manual), "the rotation deleted a manual copy");
        await World.Ticks(1);
    }

    // X9: no BACKUP_TIMES entry passed since the last check (one later today, malformed ones) -
    // no copy, nothing pruned
    [AtlasScenario]
    public async Task ScheduledBackup_Should_DoNothing_When_NoTimeHasPassed()
    {
        int area = NewArea();
        ResetConfig();
        string folder = UseOwnBackupFolder(area);
        string old = Path.Combine(folder, "auto_2000-01-01_00-00-00.db");
        File.WriteAllText(old, "old");
        DateTime now = DateTime.Now;
        UseBackupSchedule(1, now.AddHours(2).ToString("HH:mm"), "25:00", "noon");

        RunScheduledCheck(now.AddSeconds(-10));

        Assert.Equal(new[] { old }, Directory.GetFiles(folder));
        await World.Ticks(1);
    }

    // ---- /cadmin city ----------------------------------------------------------------------------

    // X: /cadmin city create on the free chunk under the admin, with a mayor or without one
    [AtlasTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CityCreate_Should_FoundACity_UnderTheAdmin(bool withMayor)
    {
        // Arrange: an admin and a player of no city in a fresh area
        ResetConfig();
        int area = NewArea();
        ITestPlayer admin = await JoinAt("admin" + area, area);
        AllowAdminCommands(admin);
        ITestPlayer future = await JoinAt("future" + area, area);
        string name = CityName(area);
        string command = withMayor ? $"{CadminCmd} city create {name} {Name(future)}" : $"{CadminCmd} city create {name}";

        // Act
        CommandResult create = await admin.ExecuteCommand(command);

        // Assert
        AssertDone(create, "city_created", command);
        Assert.True(Mod.dataStorage.GetCityByName(name, out City city), $"{name} was not founded");
        Assert.True(city.HasChunk(AreaChunk(area)), "the city does not own the chunk under the admin");
        Assert.False(Info(admin).hasCity(), "the admin became a citizen");
        if (withMayor)
        {
            Assert.True(Info(future).Equals(city.getMayor()), "the named player is not the mayor");
            Assert.True(city.Equals(Info(future).City), "the mayor is not a citizen of the city");
        }
        else
        {
            Assert.Null(city.getMayor());
            Assert.False(Info(future).hasCity(), "a player who was not named joined the city");
        }
    }

    // X: refused on a claimed chunk, for a mayor who has a city, for a taken name, for an unknown player
    [AtlasTheory]
    [InlineData("chunk taken", "chunk_already_claimed")]
    [InlineData("mayor has a city", "player_has_city_already")]
    [InlineData("name taken", "city_name_is_already_taken")]
    [InlineData("unknown player", "invalid_player_name")]
    public async Task CityCreate_Should_BeRefused(string how, string errorKey)
    {
        // Arrange: the scene's city, and a fresh area free of it
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        int free = NewArea();
        string name = CityName(free);
        string command;
        switch (how)
        {
            case "chunk taken":
                command = $"{CadminCmd} city create {name}";
                break;
            case "mayor has a city":
                await admin.TeleportTo(AreaOrigin(free));
                command = $"{CadminCmd} city create {name} {Name(s.Mayor)}";
                break;
            case "name taken":
                await admin.TeleportTo(AreaOrigin(free));
                command = $"{CadminCmd} city create {s.City.GetPartName()}";
                break;
            default:
                await admin.TeleportTo(AreaOrigin(free));
                command = $"{CadminCmd} city create {name} nosuchplayer{free}";
                break;
        }
        int cities = Mod.dataStorage.GetCitiesList().Count();

        // Act
        CommandResult create = await admin.ExecuteCommand(command);

        // Assert
        AssertRefused(create, errorKey, $"{command} ({how})");
        Assert.Equal(cities, Mod.dataStorage.GetCitiesList().Count());
        Assert.False(Mod.dataStorage.GetCityAtChunk(AreaChunk(free), out _), "the free chunk was claimed");
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the mayor left his city");
    }

    // X: /cadmin city claim takes the free chunk under the admin, even one that does not touch the city
    [AtlasScenario]
    public async Task CityClaim_Should_AddTheChunkUnderTheAdmin()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        ChunkPos far = await GoToChunk(admin, s.Area, 3);

        CommandResult claim = await admin.ExecuteCommand($"{CadminCmd} city claim {s.City.GetPartName()}");

        AssertDone(claim, "chunk_claimed", "/cadmin city claim");
        Assert.True(s.City.HasChunk(far), "the chunk was not added");
        Assert.Equal(2, s.City.ChunkCount);
        AssertInvariants();
    }

    // X: a claimed chunk, or an unknown city, is refused
    [AtlasScenario]
    public async Task CityClaim_Should_BeRefused_When_ChunkIsTakenOrCityUnknown()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        CityScene other = await GivenCity();
        await admin.TeleportTo(AreaOrigin(other.Area));

        CommandResult taken = await admin.ExecuteCommand($"{CadminCmd} city claim {s.City.GetPartName()}");
        CommandResult unknown = await admin.ExecuteCommand($"{CadminCmd} city claim Nosuchcity{s.Area}");

        AssertRefused(taken, "chunk_already_claimed", "/cadmin city claim of another city's chunk");
        AssertRefused(unknown, "no_such_city", "/cadmin city claim for an unknown city");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.True(other.City.HasChunk(AreaChunk(other.Area)), "the other city lost its chunk");
    }

    // X: /cadmin city unclaim gives up the chunk under the admin
    [AtlasScenario]
    public async Task CityUnclaim_Should_GiveUpTheChunkUnderTheAdmin()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        ChunkPos next = await GoToChunk(admin, s.Area, 1);
        CommandResult claim = await admin.ExecuteCommand($"{CadminCmd} city claim {s.City.GetPartName()}");
        Assert.True(claim.Ok && s.City.HasChunk(next), "precondition: /cadmin city claim: " + claim.Message);

        CommandResult unclaim = await admin.ExecuteCommand($"{CadminCmd} city unclaim {s.City.GetPartName()}");

        AssertDone(unclaim, "chunk_unclaimed", "/cadmin city unclaim");
        Assert.False(s.City.HasChunk(next), "the chunk is still the city's");
        Assert.Equal(1, s.City.ChunkCount);
    }

    // X: never the last chunk, never a chunk with a plot, never a chunk of no or another city
    [AtlasTheory]
    [InlineData("last chunk", "last_city_chunk")]
    [InlineData("plot in it", "chunk_has_plots")]
    [InlineData("not the city's", "not_this_city_chunk")]
    public async Task CityUnclaim_Should_BeRefused(string how, string errorKey)
    {
        // Arrange
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        if (how == "plot in it")
        {
            await GivenPlot(s, 0);
            await GoToChunk(admin, s.Area, 1);
            CommandResult claim = await admin.ExecuteCommand($"{CadminCmd} city claim {s.City.GetPartName()}");
            Assert.True(claim.Ok, "precondition: a second chunk: " + claim.Message);
            await admin.TeleportTo(AreaOrigin(s.Area));
        }
        else if (how == "not the city's")
        {
            await GoToChunk(admin, s.Area, 5);
        }
        int chunks = s.City.ChunkCount;

        // Act
        CommandResult unclaim = await admin.ExecuteCommand($"{CadminCmd} city unclaim {s.City.GetPartName()}");

        // Assert
        AssertRefused(unclaim, errorKey, $"/cadmin city unclaim ({how})");
        Assert.Equal(chunks, s.City.ChunkCount);
        Assert.True(s.City.HasChunk(AreaChunk(s.Area)), "the founding chunk was given up");
        AssertInvariants();
    }

    // X: bonus chunks and bonus plots are set and count into the limits; below 0 is refused
    [AtlasTheory]
    [InlineData("bonuschunks")]
    [InlineData("bonusplots")]
    public async Task CityBonus_Should_SetTheBonus_And_RefuseANegativeOne(string what)
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        string city = s.City.GetPartName();
        int Bonus() => what == "bonuschunks" ? s.City.getBonusChunks() : s.City.getBonusPlots();
        int Max() => what == "bonuschunks" ? Settings.GetMaxChunksForCity(s.City) : Settings.GetMaxNumberOfPlotForCity(s.City);
        int levelPart = Max() - Bonus();

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} city {what} {city} 3");
        CommandResult negative = await s.Mayor.ExecuteCommand($"{CadminCmd} city {what} {city} -1");

        AssertDone(set, what == "bonuschunks" ? "city_bonus_chunks_set" : "city_bonus_plots_set", $"/cadmin city {what} 3");
        Assert.Equal(3, Bonus());
        Assert.Equal(levelPart + 3, Max());
        AssertRefused(negative, "economy_bad_amount", $"/cadmin city {what} -1");
        Assert.Equal(3, Bonus());
    }

    // X: /cadmin city delete dissolves the city; an unknown name is refused
    [AtlasScenario]
    public async Task CityDelete_Should_DissolveTheCity()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);

        CommandResult unknown = await admin.ExecuteCommand($"{CadminCmd} city delete Nosuchcity{s.Area}");
        CommandResult delete = await admin.ExecuteCommand($"{CadminCmd} city delete {s.City.GetPartName()}");

        AssertRefused(unknown, "no_such_city", "/cadmin city delete of an unknown city");
        Assert.True(delete.Ok, "/cadmin city delete: " + delete.Message);
        Assert.DoesNotContain(s.City, Mod.dataStorage.GetCitiesList());
        Assert.False(Info(s.Mayor).hasCity(), "the mayor still has the city");
        Assert.False(Info(citizen).hasCity(), "the citizen still has the city");
        Assert.False(Mod.dataStorage.GetCityAtChunk(AreaChunk(s.Area), out _), "the chunk is still claimed");
        Assert.Null(PlotIn(PlotBox(s.Area, 0)));
    }

    // X: /cadmin city setmayor gives the city to a citizen; a stranger or an unknown name is refused
    [AtlasScenario]
    public async Task CitySetMayor_Should_MakeACitizenMayor_And_RefuseOthers()
    {
        CityScene s = await GivenCity("heir");
        ITestPlayer heir = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        AllowAdminCommands(s.Mayor);
        string city = s.City.GetPartName();

        CommandResult notCitizen = await s.Mayor.ExecuteCommand($"{CadminCmd} city setmayor {city} {Name(stranger)}");
        CommandResult unknown = await s.Mayor.ExecuteCommand($"{CadminCmd} city setmayor {city} nosuchplayer{s.Area}");
        AssertRefused(notCitizen, "player_should_be_citizen", "/cadmin city setmayor of a stranger");
        AssertRefused(unknown, "invalid_player_name", "/cadmin city setmayor of an unknown player");
        Assert.True(Info(s.Mayor).Equals(s.City.getMayor()), "a refused setmayor changed the mayor");

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} city setmayor {city} {Name(heir)}");

        Assert.True(set.Ok, "/cadmin city setmayor: " + set.Message);
        Assert.True(Info(heir).Equals(s.City.getMayor()), "the heir is not the mayor");
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the former mayor left the city");
        AssertInvariants();
    }

    // ---- /cadmin player ----------------------------------------------------------------------------

    // X: /cadmin player info names the player, their id and that they are a mayor
    [AtlasScenario]
    public async Task PlayerInfo_Should_ShowThePlayer()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);

        CommandResult info = await admin.ExecuteCommand($"{CadminCmd} player info {Name(s.Mayor)}");
        CommandResult unknown = await admin.ExecuteCommand($"{CadminCmd} player info nosuchplayer{s.Area}");

        Assert.True(info.Ok, "/cadmin player info: " + info.Message);
        Assert.Contains(Name(s.Mayor), info.Message);
        Assert.Contains(Info(s.Mayor).Guid, info.Message);
        Assert.Contains(Lang.Get("canclaimsextended:admin_is_mayor"), info.Message);
        AssertRefused(unknown, "invalid_player_name", "/cadmin player info of an unknown player");
    }

    // X: /cadmin player kick takes a citizen out of the city
    [AtlasScenario]
    public async Task PlayerKick_Should_RemoveTheCitizen()
    {
        CityScene s = await GivenCity("kicked");
        ITestPlayer kicked = s.Citizens[0];
        ITestPlayer admin = await GivenAdmin(s);

        CommandResult kick = await admin.ExecuteCommand($"{CadminCmd} player kick {Name(kicked)}");

        Assert.True(kick.Ok, "/cadmin player kick: " + kick.Message);
        Assert.False(Info(kicked).hasCity(), "the kicked player still has a city");
        Assert.DoesNotContain(Info(kicked), s.City.getCityCitizens());
        AssertInvariants();
    }

    // X: the mayor, a player of no city and an unknown name are refused
    [AtlasTheory]
    [InlineData("mayor", "admin_kick_mayor")]
    [InlineData("no city", "player_has_no_city")]
    [InlineData("unknown", "invalid_player_name")]
    public async Task PlayerKick_Should_BeRefused(string who, string errorKey)
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        string target = who switch
        {
            "mayor" => Name(s.Mayor),
            "no city" => Name(await GivenStranger(s, "loner")),
            _ => "nosuchplayer" + s.Area,
        };

        CommandResult kick = await admin.ExecuteCommand($"{CadminCmd} player kick {target}");

        AssertRefused(kick, errorKey, $"/cadmin player kick ({who})");
        if (who == "mayor") Assert.Equal(new object[] { Mod.config.COMMAND_CADMIN }, kick.Raw.MessageParams);
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the mayor left the city");
    }

    // ---- /cadmin world ------------------------------------------------------------------------------

    // X: /cadmin world info lists the world flags; world set takes only known flags
    [AtlasScenario]
    public async Task World_Should_ShowInfo_And_RefuseAnUnknownFlag()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);

        CommandResult info = await s.Mayor.ExecuteCommand($"{CadminCmd} world info");
        CommandResult unknown = await s.Mayor.ExecuteCommand($"{CadminCmd} world set nosuchflag on");

        Assert.True(info.Ok, "/cadmin world info: " + info.Message);
        List<string> lines = Mod.dataStorage.World.getStatus();
        Assert.NotEmpty(lines);
        foreach (string line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            Assert.Contains(line, info.Message);
        }
        Assert.False(unknown.Ok, "/cadmin world set of an unknown flag ran: " + unknown.Message);
    }

    // ---- /cadmin astest -----------------------------------------------------------------------------

    // X: the self-test of acting as a mayor passes, for an admin of no city and for the mayor of another one
    [AtlasTheory]
    [InlineData("of no city")]
    [InlineData("mayor elsewhere")]
    public async Task Astest_Should_PassEveryCheck(string admin)
    {
        CityScene s = await GivenCity();
        ITestPlayer caller;
        if (admin == "of no city")
        {
            caller = await GivenAdmin(s);
        }
        else
        {
            CityScene other = await GivenCity();
            caller = other.Mayor;
            AllowAdminCommands(caller);
        }

        CommandResult test = await caller.ExecuteCommand($"{CadminCmd} astest {s.City.GetPartName()}");

        Assert.True(test.Ok, "/cadmin astest: " + test.Message);
        Assert.DoesNotContain("FAIL", test.Message);
        Assert.Contains("OK   off after an error", test.Message);
        Assert.True(Info(s.Mayor).Equals(s.City.getMayor()), "astest changed the mayor");
    }

    // X: astest for an unknown city
    [AtlasScenario]
    public async Task Astest_Should_BeRefused_When_CityIsUnknown()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);

        CommandResult test = await admin.ExecuteCommand($"{CadminCmd} astest Nosuchcity{s.Area}");

        AssertRefused(test, "no_such_city", "/cadmin astest of an unknown city");
    }

    // ---- /cadmin plot (the /plot commands run as the mayor of the plot's city) -------------------------

    // X: /cadmin plot create makes the selection a plot of the city, free of the city's plot limit
    [AtlasScenario]
    public async Task PlotCreate_Should_MakeAPlotOfTheCity_OverItsLimit()
    {
        // Arrange: a city whose limit is 0 plots, an admin of no city with a selection in it
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        s.City.setBonusPlots(-Settings.GetCityLevelInfo(s.City.getCityCitizens().Count).AmountOfPlots);
        Assert.Equal(0, Settings.GetMaxNumberOfPlotForCity(s.City));
        BlockBox box = PlotBox(s.Area, 0);
        Select(admin, box);
        await StandIn(admin, box);

        // Act
        CommandResult create = await admin.ExecuteCommand($"{CadminCmd} plot create {s.City.GetPartName()}");

        // Assert
        Assert.True(create.Ok, "/cadmin plot create: " + create.Message);
        Plot? plot = PlotIn(box);
        Assert.True(plot != null && s.City.Equals(plot.getCity()), "no plot of the city was made");
        Assert.False(Info(admin).hasCity(), "the admin became a citizen");
        AssertInvariants();
    }

    // X: never outside the territory, never for an unknown city
    [AtlasScenario]
    public async Task PlotCreate_Should_BeRefused_OutsideTheTerritory_Or_ForAnUnknownCity()
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        BlockBox wild = Shifted(PlotBox(s.Area, 0), 4 * ChunkPos.Size);
        Select(admin, wild);
        await StandIn(admin, wild);

        CommandResult outside = await admin.ExecuteCommand($"{CadminCmd} plot create {s.City.GetPartName()}");
        CommandResult unknown = await admin.ExecuteCommand($"{CadminCmd} plot create Nosuchcity{s.Area}");

        Assert.False(outside.Ok, "a plot outside the territory was made: " + outside.Message);
        AssertRefused(unknown, "no_such_city", "/cadmin plot create for an unknown city");
        Assert.Null(PlotIn(wild));
        Assert.Empty(s.City.getCityPlots());
    }

    // X: /cadmin plot delete gives the plot under the admin back to city land
    [AtlasScenario]
    public async Task PlotDelete_Should_RemoveThePlotUnderTheAdmin()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);
        await StandIn(admin, plot.Shape.Boxes[0]);

        CommandResult delete = await admin.ExecuteCommand($"{CadminCmd} plot delete");

        Assert.True(delete.Ok, "/cadmin plot delete: " + delete.Message);
        Assert.Null(PlotIn(PlotBox(s.Area, 0)));
        Assert.DoesNotContain(plot, s.City.getCityPlots());
        AssertInvariants();
    }

    // X: the /cadmin plot commands need a plot under the admin - city land or the wild is refused
    [AtlasTheory]
    [InlineData("delete")]
    [InlineData("sell 5")]
    [InlineData("type farm")]
    [InlineData("grow up 1")]
    public async Task CadminPlot_Should_BeRefused_When_NoPlotIsUnderTheAdmin(string sub)
    {
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenAdmin(s);
        ChunkPos chunk = AreaChunk(s.Area);
        await admin.TeleportTo(new BlockPos(chunk.MinBlockX, AreaOrigin(s.Area).Y, chunk.MinBlockZ));
        CommandResult onCityLand = await admin.ExecuteCommand($"{CadminCmd} plot {sub}");
        await GoToChunk(admin, s.Area, 5);

        CommandResult inTheWild = await admin.ExecuteCommand($"{CadminCmd} plot {sub}");

        AssertRefused(onCityLand, "not_a_plot_city_land", $"/cadmin plot {sub} on city land");
        AssertRefused(inTheWild, "plot_not_claimed", $"/cadmin plot {sub} in the wild");
    }

    // X: /cadmin plot sell and nosell put the plot up for sale and take it off
    [AtlasScenario]
    public async Task PlotSellAndNoSell_Should_SetThePrice()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);
        await StandIn(admin, plot.Shape.Boxes[0]);

        CommandResult sell = await admin.ExecuteCommand($"{CadminCmd} plot sell 15");
        Assert.True(sell.Ok, "/cadmin plot sell 15: " + sell.Message);
        Assert.Equal(15, plot.Price);

        CommandResult noSell = await admin.ExecuteCommand($"{CadminCmd} plot nosell");
        Assert.True(noSell.Ok, "/cadmin plot nosell: " + noSell.Message);
        Assert.Equal(-1, plot.Price);
        AssertInvariants();
    }

    // X: /cadmin plot type sets the type; an unknown one is refused
    [AtlasScenario]
    public async Task PlotType_Should_SetTheType_And_RefuseAnUnknownOne()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);
        await StandIn(admin, plot.Shape.Boxes[0]);

        CommandResult unknown = await admin.ExecuteCommand($"{CadminCmd} plot type xyz");
        AssertRefused(unknown, "unknown_plot_type", "/cadmin plot type xyz");
        Assert.Equal(PlotType.DEFAULT, plot.Type);

        CommandResult set = await admin.ExecuteCommand($"{CadminCmd} plot type farm");
        Assert.True(set.Ok, "/cadmin plot type farm: " + set.Message);
        Assert.Equal(PlotType.FARM, plot.Type);
    }

    // X: /cadmin plot fee sets the daily fee of the plot; above MAX_PLOT_FEE is refused
    [AtlasScenario]
    public async Task PlotFee_Should_SetTheFee_And_RefuseOneOverTheMax()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);
        await StandIn(admin, plot.Shape.Boxes[0]);
        int over = (int)Mod.config.MAX_PLOT_FEE + 1;

        CommandResult set = await admin.ExecuteCommand($"{CadminCmd} plot fee 5");
        CommandResult tooHigh = await admin.ExecuteCommand($"{CadminCmd} plot fee {over}");

        AssertDone(set, "plot_fee_set", "/cadmin plot fee 5");
        AssertRefused(tooHigh, "plot_fee_out_of_range", $"/cadmin plot fee {over}");
        Assert.Equal(5, Convert.ToInt32(plot.getCustomTax()));
    }

    // X: /cadmin plot grow and shrink move a face of the box under the admin; 0 blocks is refused
    [AtlasScenario]
    public async Task PlotGrowAndShrink_Should_MoveTheFace()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer admin = await GivenAdmin(s);
        await StandIn(admin, plot.Shape.Boxes[0]);
        int top = plot.Shape.Boxes[0].MaxY;

        CommandResult zero = await admin.ExecuteCommand($"{CadminCmd} plot grow up 0");
        AssertRefused(zero, "plot_resize_bad", "/cadmin plot grow up 0");
        Assert.Equal(top, plot.Shape.Boxes[0].MaxY);

        CommandResult grow = await admin.ExecuteCommand($"{CadminCmd} plot grow up 2");
        Assert.True(grow.Ok, "/cadmin plot grow up 2: " + grow.Message);
        Assert.Equal(top + 2, plot.Shape.Boxes[0].MaxY);

        CommandResult shrink = await admin.ExecuteCommand($"{CadminCmd} plot shrink up 1");
        Assert.True(shrink.Ok, "/cadmin plot shrink up 1: " + shrink.Message);
        Assert.Equal(top + 1, plot.Shape.Boxes[0].MaxY);
        AssertInvariants();
    }
}
