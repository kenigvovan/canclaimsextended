using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.timers;
using Microsoft.Data.Sqlite;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Copies of the database (X9): /cadmin backup writes a full copy into Config.BackupFolder, and the
/// rotation keeps only the newest BACKUP_KEEP automatic copies, never touching manual ones. The
/// rotation runs after an automatic copy; the scheduled one waits for a BACKUP_TIMES minute, so it
/// is driven through the copy made on start (BackupScheduler.CopyBeforeLoad), which prunes the same
/// way. Mirrors CAN_claims BackupScenarios.
/// </summary>
[Trait("Category", "E2E")]
public class BackupScenarios : CxScenarioBase
{
    /// <summary>
    /// A new empty BACKUP_FOLDER for this scenario; the setting comes back and the folder is
    /// deleted on Dispose.
    /// </summary>
    private string UseOwnBackupFolder(int area)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"canclaimsextended_backup_test_{area}_{Guid.NewGuid():N}");
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

    /// <summary>The number of rows of the city in the CITIES table of a database file.</summary>
    private static int CountCity(string dbPath, string cityGuid)
    {
        using var connection = new SqliteConnection("Data Source=" + dbPath + ";Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = new SqliteCommand("SELECT COUNT(*) FROM CITIES WHERE guid=@g", connection);
        cmd.Parameters.AddWithValue("@g", cityGuid);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // X9: /cadmin backup writes a manual copy into the backup folder, with the city in it
    [AtlasScenario]
    public async Task CadminBackup_Should_WriteAFullCopy_IntoTheBackupFolder()
    {
        // Arrange
        CityScene s = await GivenCity();
        string folder = UseOwnBackupFolder(s.Area);
        AllowAdminCommands(s.Mayor);

        // Act
        CommandResult backup = await s.Mayor.ExecuteCommand($"{CadminCmd} backup");

        // Assert
        Assert.True(backup.Ok, "/cadmin backup: " + backup.Message);
        string[] copies = Directory.GetFiles(folder, "manual_*.db");
        Assert.True(copies.Length == 1, $"expected one manual copy in {folder}, found {copies.Length}");
        Assert.Equal(1, CountCity(copies[0], s.City.Guid));
        Assert.Contains(Path.GetFileName(copies[0]), backup.Message);
    }

    // X9: without BACKUP_FOLDER the copy goes to canclaimsextended_backups next to the database
    [AtlasScenario]
    public async Task CadminBackup_Should_UseTheDefaultFolder_When_BackupFolderIsEmpty()
    {
        // Arrange
        CityScene s = await GivenCity();
        string before = Mod.config.BACKUP_FOLDER;
        AddCleanup(() => Mod.config.BACKUP_FOLDER = before);
        Mod.config.BACKUP_FOLDER = "";
        string folder = Path.Combine(Mod.config.FilesFolder, "canclaimsextended_backups");
        Assert.Equal(folder, Mod.config.BackupFolder);
        var existing = new HashSet<string>(Directory.GetFiles(folder, "manual_*.db"));
        AllowAdminCommands(s.Mayor);

        // Act
        CommandResult backup = await s.Mayor.ExecuteCommand($"{CadminCmd} backup");

        // Assert
        Assert.True(backup.Ok, "/cadmin backup: " + backup.Message);
        string[] added = Directory.GetFiles(folder, "manual_*.db").Where(f => !existing.Contains(f)).ToArray();
        foreach (string f in added) AddCleanup(() => { try { File.Delete(f); } catch (IOException) { } });
        Assert.True(added.Length == 1, $"expected one new manual copy in {folder}, found {added.Length}");
        Assert.Equal(1, CountCity(added[0], s.City.Guid));
    }

    // X9: a player without the admin role gets no copy
    [AtlasScenario]
    public async Task CadminBackup_Should_BeRefused_When_PlayerIsNoAdmin()
    {
        CityScene s = await GivenCity();
        string folder = UseOwnBackupFolder(s.Area);
        string role = s.Mayor.Player.Role.Code;
        bool hadRole = Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Remove(role);
        AddCleanup(() => { if (hadRole) Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Add(role); });

        CommandResult backup = await s.Mayor.ExecuteCommand($"{CadminCmd} backup");

        Assert.False(backup.Ok, "a player without the admin role made a backup");
        Assert.Empty(Directory.GetFiles(folder));
    }

    // X9: past BACKUP_KEEP the oldest automatic copies are deleted; manual copies stay
    [AtlasScenario]
    public async Task Rotation_Should_KeepTheNewestAutoCopies_And_EveryManualOne()
    {
        // Arrange: four old automatic copies and one old manual copy, keep 2
        int area = NewArea();
        ResetConfig();
        string folder = UseOwnBackupFolder(area);
        string[] oldAutos = Enumerable.Range(1, 4).Select(d => Path.Combine(folder, $"auto_2000-01-0{d}_00-00-00.db")).ToArray();
        foreach (string f in oldAutos) File.WriteAllText(f, "old");
        string manual = Path.Combine(folder, "manual_1999-01-01_00-00-00.db");
        File.WriteAllText(manual, "manual");
        string source = Path.Combine(folder, "source.sqlite");
        File.WriteAllText(source, "db");
        int keepBefore = Mod.config.BACKUP_KEEP;
        bool onStartBefore = Mod.config.BACKUP_ON_START;
        AddCleanup(() =>
        {
            Mod.config.BACKUP_KEEP = keepBefore;
            Mod.config.BACKUP_ON_START = onStartBefore;
        });
        Mod.config.BACKUP_KEEP = 2;
        Mod.config.BACKUP_ON_START = true;

        // Act: an automatic copy, which then prunes
        BackupScheduler.CopyBeforeLoad(source);

        // Assert: the new copy and the newest old one are left
        string[] autos = Directory.GetFiles(folder, "auto_*.db").Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.True(autos.Length == 2, "automatic copies left: " + string.Join(", ", autos));
        Assert.Equal("auto_2000-01-04_00-00-00.db", autos[0]);
        Assert.StartsWith("auto_" + DateTime.Now.Year, autos[1]);
        Assert.True(File.Exists(manual), "the rotation deleted a manual copy");
        await World.Ticks(1);
    }

    // X9: BACKUP_ON_START false - no copy on start, nothing pruned
    [AtlasScenario]
    public async Task CopyOnStart_Should_DoNothing_When_BackupOnStartIsOff()
    {
        int area = NewArea();
        ResetConfig();
        string folder = UseOwnBackupFolder(area);
        string old = Path.Combine(folder, "auto_2000-01-01_00-00-00.db");
        File.WriteAllText(old, "old");
        string source = Path.Combine(folder, "source.sqlite");
        File.WriteAllText(source, "db");
        int keepBefore = Mod.config.BACKUP_KEEP;
        bool onStartBefore = Mod.config.BACKUP_ON_START;
        AddCleanup(() =>
        {
            Mod.config.BACKUP_KEEP = keepBefore;
            Mod.config.BACKUP_ON_START = onStartBefore;
        });
        Mod.config.BACKUP_KEEP = 1;
        Mod.config.BACKUP_ON_START = false;

        BackupScheduler.CopyBeforeLoad(source);

        Assert.Equal(new[] { old }, Directory.GetFiles(folder, "auto_*.db"));
        await World.Ticks(1);
    }
}
