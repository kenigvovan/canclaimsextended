using Atlas.XUnit;
using canclaimsextended.src;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// A4: a canclaimsextended.json the server cannot parse. It must not be replaced with the defaults
/// on boot (Config.LoadConfig does not store in its catch): an admin's edit with one typo in it
/// would be silently thrown away. The server runs on the defaults until the file is fixed.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/BrokenConfig", TargetPath = "ModConfig")]
public class ConfigLoadScenarios : AtlasScenarioBase
{
    // A4: the file stays as the admin left it, the error is logged
    [AtlasScenario]
    public async Task BrokenConfigFile_Should_StayUntouched_And_BeLogged_When_ItCannotBeParsed()
    {
        // Not AppContext.BaseDirectory: Atlas points it at the game install once the server boots.
        string testBin = Path.GetDirectoryName(typeof(ConfigLoadScenarios).Assembly.Location)!;
        string seeded = File.ReadAllText(Path.Combine(testBin, "fixtures", "BrokenConfig", Config.FILE_NAME));
        string onServer = File.ReadAllText(Path.Combine(GamePaths.ModConfig, Config.FILE_NAME));

        Assert.Equal(seeded, onServer);
        Assert.Contains(World.BootDiagnostics, e =>
            e.Level == EnumLogType.Error && e.Message.Contains(Config.FILE_NAME + " could not be read"));
        await World.Ticks(1);
    }

    // A4: the server still runs, on the defaults (validated), not on half of the broken file
    [AtlasScenario]
    public async Task Server_Should_RunOnDefaults_When_ConfigCannotBeParsed()
    {
        var defaults = new Config();

        Assert.NotNull(Mod.config);
        Assert.NotNull(Mod.dataStorage);
        // The broken file says 999; the default is used.
        Assert.True(Mod.config.NEW_CITY_COST == defaults.NEW_CITY_COST,
            $"NEW_CITY_COST is {Mod.config.NEW_CITY_COST}, expected the default {defaults.NEW_CITY_COST}");
        Assert.True(Mod.config.CITY_MAX_DEBT == defaults.CITY_MAX_DEBT,
            $"CITY_MAX_DEBT is {Mod.config.CITY_MAX_DEBT}, expected the default {defaults.CITY_MAX_DEBT}");
        // Validate ran on the defaults too.
        Assert.True(Mod.config.CONFIG_VERSION == 2, $"CONFIG_VERSION is {Mod.config.CONFIG_VERSION}, expected 2");
        await World.Ticks(1);
    }
}
