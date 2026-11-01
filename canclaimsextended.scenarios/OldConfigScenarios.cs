using System.Text.Json;
using Atlas.XUnit;
using canclaimsextended.src;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// S5: a canclaimsextended.json written while the mod still had alliances. It is already config
/// version 2, so only the removed keys are new here (version 1 is ConfigMigrationScenarios): the
/// keys of the alliance settings are ignored on load and gone after the file is written back, an
/// alliance right in AVAILABLE_CITY_PERMISSIONS is dropped by PermissionSetConverter, and the
/// rest of the file is kept.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/OldAllianceConfig", TargetPath = "ModConfig")]
public class OldConfigScenarios : AtlasScenarioBase
{
    private static readonly string[] RemovedKeys =
    {
        "NEW_ALLIANCE_COST", "COMMAND_ALLIANCE", "COMMAND_ALLIANCE_CHAT", "BOAT_SHARE_WITH_ALLIANCE",
        "ALLIANCE_PREFIX_LENGTH", "MAX_RECEIVED_INVITATIONS_CITY",
    };

    // S5: the server boots on the file without an error about it, and keeps its other values
    [AtlasScenario]
    public async Task OldConfig_Should_Load_When_ItHasAllianceKeys()
    {
        var errors = World.BootDiagnostics
            .Where(e => e.Level >= EnumLogType.Error && e.Message.Contains(Config.FILE_NAME))
            .Select(e => e.Message)
            .ToList();

        Assert.True(errors.Count == 0, "the old config could not be read: " + string.Join(" | ", errors));
        // A value the file sets that is not the default: the file was read, not replaced.
        Assert.True(Mod.config.NEW_CITY_COST == 31, $"NEW_CITY_COST is {Mod.config.NEW_CITY_COST}, the file says 31");
        Assert.True(Mod.config.BOAT_SHARE_WITH_CITY, "BOAT_SHARE_WITH_CITY from the file was lost");
        await World.Ticks(1);
    }

    // S5: the alliance right is dropped from the rankable rights, CITY_INVITE is kept, and the
    // file being version 2 already, nothing is added
    [AtlasScenario]
    public async Task AvailableCityPermissions_Should_DropTheAllianceRight_And_KeepTheRest()
    {
        var available = Mod.config.AVAILABLE_CITY_PERMISSIONS;

        Assert.True(available.Contains(EnumPlayerPermissions.CITY_INVITE), "CITY_INVITE from the file was lost");
        Assert.True(available.Count == 1,
            "AVAILABLE_CITY_PERMISSIONS should hold only CITY_INVITE, holds: " + string.Join(", ", available));
        Assert.DoesNotContain(available, p => p.ToString().StartsWith("ALLIANCE"));
        await World.Ticks(1);
    }

    // S5: the file written back has none of the removed keys and no alliance right
    [AtlasScenario]
    public async Task ConfigFile_Should_LoseTheAllianceKeys_When_WrittenBack()
    {
        string path = Path.Combine(GamePaths.ModConfig, Config.FILE_NAME);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        var keys = root.EnumerateObject().Select(p => p.Name).ToList();
        var names = root.GetProperty("AVAILABLE_CITY_PERMISSIONS").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        foreach (string removed in RemovedKeys)
        {
            Assert.False(keys.Contains(removed), $"the rewritten file still has {removed}");
        }
        Assert.DoesNotContain(keys, k => k.Contains("ALLIANCE"));
        Assert.False(names.Contains("ALLIANCE_DECLARE_CONFLICT"), "the rewritten file still has ALLIANCE_DECLARE_CONFLICT");
        Assert.True(names.Contains("CITY_INVITE"), "the rewritten file lost CITY_INVITE: " + string.Join(", ", names));
        Assert.True(root.GetProperty("NEW_CITY_COST").GetDouble() == 31, "the rewritten file lost NEW_CITY_COST 31");
        await World.Ticks(1);
    }
}
