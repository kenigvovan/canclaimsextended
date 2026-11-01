using System.Text.Json;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// H6, S3: a canclaimsextended_permissions.json written by an older build. RightsHandler reads its
/// sets through PermissionSetConverter: the old name CITY_CLAIM_PLOT (and CITY_UNCLAIM_PLOT) is
/// read as both of its halves, the ALLIANCE_* rights of the removed alliances are dropped with a
/// warning, the host's file is kept as .bak and the translated one is written in its place. Groups
/// already in the file are left as the host set them.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/OldPermissions", TargetPath = "ModConfig")]
public class PermissionsMigrationScenarios : CxScenarioBase
{
    private const string Fixture = "OldPermissions";

    private static string ServerFile => Mod.config.FilePath(Mod.config.PERMS_FILE_NAME);

    private static string SeededFile =>
        Path.Combine(Path.GetDirectoryName(typeof(PermissionsMigrationScenarios).Assembly.Location)!,
            "fixtures", Fixture, "canclaimsextended_permissions.json");

    private static Dictionary<string, List<string>> ReadGroups(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().ToDictionary(
            g => g.Name,
            g => g.Value.EnumerateArray().Select(e => e.ToString()).ToList());
    }

    // H6: the old names reach a mayor as their two halves
    [AtlasScenario]
    public async Task OldRightNames_Should_GiveTheMayorBothHalves()
    {
        // Arrange, Act: the mayor's rights come from the MAYOR group of the old file
        CityScene s = await GivenCity();
        PlayerPermissions perms = Info(s.Mayor).PlayerPermissionsHandler;

        // Assert
        Assert.True(perms.HasPermission(EnumPlayerPermissions.CITY_CLAIM_CHUNK), "CITY_CLAIM_PLOT was not read as CITY_CLAIM_CHUNK");
        Assert.True(perms.HasPermission(EnumPlayerPermissions.CITY_CREATE_PLOT), "CITY_CLAIM_PLOT was not read as CITY_CREATE_PLOT");
        Assert.True(perms.HasPermission(EnumPlayerPermissions.CITY_UNCLAIM_CHUNK), "CITY_UNCLAIM_PLOT was not read as CITY_UNCLAIM_CHUNK");
        Assert.True(perms.HasPermission(EnumPlayerPermissions.CITY_DELETE_PLOT), "CITY_UNCLAIM_PLOT was not read as CITY_DELETE_PLOT");
        Assert.True(perms.HasPermission(EnumPlayerPermissions.CITY_INVITE), "CITY_INVITE of the file was lost");
        // Left as the host set it: a right the old MAYOR group did not have is not added.
        Assert.False(perms.HasPermission(EnumPlayerPermissions.CITY_WITHDRAW_MONEY),
            "the MAYOR group of the file got a right the host had not given it");
        Assert.DoesNotContain(perms.GetPermissions(), p => p.ToString().StartsWith("ALLIANCE"));
        AssertInvariants();
    }

    // S3: the dropped alliance rights are named in a warning
    [AtlasScenario]
    public async Task AllianceRights_Should_BeDroppedWithAWarning()
    {
        var warnings = World.BootDiagnostics
            .Where(e => e.Level == EnumLogType.Warning && e.Message.Contains("unknown rights dropped"))
            .Select(e => e.Message)
            .ToList();

        string all = string.Join(" | ", warnings);
        Assert.True(warnings.Count > 0, "no warning about the dropped rights");
        Assert.Contains("ALLIANCE_DECLARE_CONFLICT", all);
        Assert.Contains("ALLIANCE_ACCEPT_CONFLICT", all);
        Assert.Contains("ALLIANCE_WITHDRAW_MONEY", all);
        Assert.DoesNotContain("CITY_CLAIM_PLOT", all);
        await World.Ticks(1);
    }

    // H6, S3: the file is rewritten with today's names, the host's version kept as .bak
    [AtlasScenario]
    public async Task PermissionsFile_Should_BeRewrittenWithNewNames_And_BackedUp()
    {
        var groups = ReadGroups(ServerFile);
        var names = groups.Values.SelectMany(v => v).ToList();

        // Assert: the old names are gone, their halves are there
        Assert.DoesNotContain("CITY_CLAIM_PLOT", names);
        Assert.DoesNotContain("CITY_UNCLAIM_PLOT", names);
        Assert.DoesNotContain(names, n => n.StartsWith("ALLIANCE"));
        Assert.True(groups.TryGetValue("MAYOR", out var mayor), "the rewritten file lost the MAYOR group");
        foreach (string expected in new[] { "CITY_CLAIM_CHUNK", "CITY_CREATE_PLOT", "CITY_UNCLAIM_CHUNK", "CITY_DELETE_PLOT", "CITY_INVITE", "CITY_KICK" })
        {
            Assert.True(mayor!.Contains(expected), $"MAYOR in the rewritten file lacks {expected}: {string.Join(", ", mayor)}");
        }
        Assert.True(groups.TryGetValue("DEFAULT", out var everyone) && everyone.Contains("CITY_INFO"),
            "the DEFAULT group of the file was lost");
        // The host's own version is kept next to it.
        string backup = ServerFile + ".bak";
        Assert.True(File.Exists(backup), "the old file was not kept as .bak");
        Assert.Equal(File.ReadAllText(SeededFile), File.ReadAllText(backup));
        await World.Ticks(1);
    }
}
