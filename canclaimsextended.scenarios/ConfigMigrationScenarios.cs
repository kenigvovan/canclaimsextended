using System.Text.Json;
using Atlas.XUnit;
using canclaimsextended.src;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Z2: a canclaimsextended.json written by config version 1. On boot the old right name
/// CITY_CLAIM_PLOT is read as both of its halves (PermissionSetConverter), the rights rankable
/// since version 2 are added (Config.Validate), and the file is written back as version 2 - while
/// a version 1 right the admin had left out of the list stays out.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/ConfigV1", TargetPath = "ModConfig")]
public class ConfigMigrationScenarios : AtlasScenarioBase
{
    // Z2: the list is migrated in memory
    [AtlasScenario]
    public async Task AvailableCityPermissions_Should_BeMigrated_When_ConfigIsVersion1()
    {
        var available = Mod.config.AVAILABLE_CITY_PERMISSIONS;

        Assert.False(World.BootDiagnostics.Any(e => e.Level >= EnumLogType.Error && e.Message.Contains(Config.FILE_NAME)),
            "the seeded config could not be read");
        Assert.True(Mod.config.CONFIG_VERSION == 2, $"CONFIG_VERSION is {Mod.config.CONFIG_VERSION}, expected 2");
        // Kept from the file
        Assert.True(available.Contains(EnumPlayerPermissions.CITY_INVITE), "CITY_INVITE from the file was lost");
        // The old name, both halves
        Assert.True(available.Contains(EnumPlayerPermissions.CITY_CLAIM_CHUNK), "CITY_CLAIM_PLOT was not read as CITY_CLAIM_CHUNK");
        Assert.True(available.Contains(EnumPlayerPermissions.CITY_CREATE_PLOT), "CITY_CLAIM_PLOT was not read as CITY_CREATE_PLOT");
        // Added by the version 2 migration
        Assert.True(available.Contains(EnumPlayerPermissions.CITY_SEE_LOG), "the migration did not add CITY_SEE_LOG");
        Assert.True(available.Contains(EnumPlayerPermissions.CITY_BUY_OUTPOST), "the migration did not add CITY_BUY_OUTPOST");
        // A version 1 right the admin left out is not brought back
        Assert.False(available.Contains(EnumPlayerPermissions.CITY_KICK), "CITY_KICK, left out by the admin, came back");
        await World.Ticks(1);
    }

    // Z2: the migrated config is written back, so the next start does not migrate it again
    [AtlasScenario]
    public async Task ConfigFile_Should_BeWrittenAsVersion2_When_ItWasVersion1()
    {
        string path = Path.Combine(GamePaths.ModConfig, Config.FILE_NAME);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        int version = root.GetProperty("CONFIG_VERSION").GetInt32();
        var names = root.GetProperty("AVAILABLE_CITY_PERMISSIONS").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.True(version == 2, $"the file still says CONFIG_VERSION {version}");
        Assert.True(names.Contains("CITY_SEE_LOG"), "the file lacks CITY_SEE_LOG: " + string.Join(", ", names));
        Assert.True(names.Contains("CITY_CREATE_PLOT"), "the file lacks CITY_CREATE_PLOT: " + string.Join(", ", names));
        Assert.False(names.Contains("CITY_CLAIM_PLOT"), "the file still has the old name CITY_CLAIM_PLOT");
        Assert.False(names.Contains("CITY_KICK"), "the file got CITY_KICK back");
        await World.Ticks(1);
    }
}
