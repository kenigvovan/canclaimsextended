using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.database;
using canclaimsextended.src.geometry;
using Microsoft.Data.Sqlite;

namespace canclaimsextended.scenarios;

/// <summary>
/// Builds the prebuilt databases the fixture classes boot on, from the SQL kept next to them:
///   fixtures/sql/FullWorld.sql  -> fixtures/FullWorld/canclaimsextended.db  (PersistenceScenarios, Y1)
///   fixtures/sql/BrokenRows.sql -> fixtures/BrokenRows/canclaimsextended.db (DbLoadScenarios, Y2)
/// Run by hand, once, and again whenever the SQL or the database schema changes:
///   set CX_GENERATE_FIXTURES=1
///   dotnet test canclaimsextended.scenarios --filter "Category=FixtureGen"
/// Without the variable it does nothing, so an ordinary run never rewrites the fixtures.
///
/// Unlike claims.scenarios' FullWorldFixtureGenerator, which played the world together with
/// commands, the data is plain SQL: hand-broken rows cannot be made by commands at all, and SQL in
/// the repository can be read and reviewed without opening a binary. It still runs inside a booted
/// server, for the game's SQLite and for the one thing SQL cannot know - the schema the mod creates
/// today. Each file is checked against it table by table, column by column.
/// What each file holds is written out below (FullWorld, BrokenRows); the scenarios expect exactly that.
/// </summary>
[Trait("Category", "FixtureGen")]
public class FixtureGenerator : AtlasScenarioBase
{
    public const string EnvVar = "CX_GENERATE_FIXTURES";

    /// <summary>Fixture folders; each is made from fixtures/sql/&lt;name&gt;.sql.</summary>
    private static readonly string[] All = { FullWorld.Folder, BrokenRows.Folder };

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Generate()
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1")
        {
            await World.Ticks(1);
            return;
        }

        string bin = Path.GetDirectoryName(typeof(FixtureGenerator).Assembly.Location)!;
        // bin/<Configuration>/<tfm> -> the project folder, as claims.scenarios does.
        string project = Path.GetFullPath(Path.Combine(bin, "..", "..", ".."));
        Assert.True(File.Exists(Path.Combine(project, "canclaimsextended.scenarios.csproj")),
            $"the project folder was expected at {project} (three levels above the test assembly)");

        // The server booted with no fixture: the mod has just created an empty database with every
        // table and column it uses today.
        DbFile.Flush();
        Dictionary<string, HashSet<string>> live;
        using (SqliteConnection connection = DbFile.OpenReadOnly())
        {
            live = DbFile.Schema(connection);
        }

        var problems = new List<string>();
        foreach (string name in All)
        {
            string sql = File.ReadAllText(Path.Combine(project, "fixtures", "sql", name + ".sql"));
            string target = Path.Combine(project, "fixtures", name, Fixtures.DbFileName);
            Build(target, sql);

            using (SqliteConnection connection = DbFile.OpenReadOnly(target))
            {
                long version = Convert.ToInt64(DbFile.Rows(connection, "PRAGMA user_version")[0]["user_version"]);
                if (version != SQLiteTables.SCHEMA_VERSION)
                    problems.Add($"{name}: user_version {version}, the mod expects {SQLiteTables.SCHEMA_VERSION}");
                problems.AddRange(CompareSchema(name, live, DbFile.Schema(connection)));
            }

            // Next to the tests too, so the next run finds it without a build.
            string binTarget = Path.Combine(Fixtures.BinFolder(name), Fixtures.DbFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(binTarget)!);
            File.Copy(target, binTarget, overwrite: true);
        }

        Assert.True(problems.Count == 0,
            "fixtures were written, but do not match the schema of the running mod - fix fixtures/sql and generate again:\n" +
            string.Join("\n", problems));
    }

    private static void Build(string path, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = new SqliteCommand(sql, connection);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Tables and columns the mod has that the fixture lacks, and what the fixture has that the mod
    /// does not (a typo would otherwise be a column nobody reads).
    /// </summary>
    private static IEnumerable<string> CompareSchema(string name, Dictionary<string, HashSet<string>> live,
        Dictionary<string, HashSet<string>> built)
    {
        foreach (var (table, columns) in live)
        {
            if (!built.TryGetValue(table, out var have))
            {
                yield return $"{name}: table {table} is missing";
                continue;
            }
            foreach (string column in columns.Where(c => !have.Contains(c)))
                yield return $"{name}: column {table}.{column} is missing";
            foreach (string column in have.Where(c => !columns.Contains(c)))
                yield return $"{name}: column {table}.{column} is not in the mod's schema";
        }
        foreach (string table in built.Keys.Where(t => !live.ContainsKey(t)))
            yield return $"{name}: table {table} is not in the mod's schema";
    }
}

/// <summary>What fixtures/FullWorld holds (fixtures/sql/FullWorld.sql). Shared by the generator and the checks.</summary>
public static class FullWorld
{
    public const string Folder = "FullWorld";

    public const string City = "Persistburg";
    public const string CityGuid = "c-full-0001";
    public const string Mayor = "pmayor";
    public const string MayorUid = "u-pmayor";
    public const string Citizen = "pcitizen";
    public const string CitizenUid = "u-pcitizen";
    /// <summary>A criminal of the city, serving in its prison.</summary>
    public const string Outlaw = "poutlaw";
    public const string OutlawUid = "u-poutlaw";
    /// <summary>Invited by the city, not a citizen.</summary>
    public const string Invitee = "pinvitee";
    public const string InviteeUid = "u-pinvitee";

    public const long Created = 1700000000;
    public const string CityPerms = "citizen:use;citizen:build;friend:use;";
    public const bool Open = true;
    public const string InvMsg = "Welcome";
    public const int BonusPlots = 3;
    public const int BonusChunks = 4;
    public const int CityColor = -16744448;
    public const double Debt = 12.5;
    public const int CityFee = 7;
    public const string Emblem = "color_red;cross_white";
    public const string DailyMsg = "Pay your fee";

    public const string Rank = "Elder";
    public static readonly string[] RankPermissions = { "CITY_INVITE", "CITY_SEE_LOG" };

    public const string CitizenPrefix = "Sir";
    public const string CitizenAfterName = "the Bold";

    public static readonly ChunkPos HomeChunk = new(100, 100);
    /// <summary>Bought over the limit, and the land of the plots group.</summary>
    public static readonly ChunkPos ExtraChunk = new(101, 100);
    public static readonly ChunkPos OutpostChunk = new(106, 100);
    public const int Chunks = 3;

    /// <summary>The citizen's temple: two touching boxes, for sale, taxed, own rights, in the group.</summary>
    public const string TempleGuid = "p-full-0001";
    public const string TempleName = "Shrine";
    public const int TempleNumber = 1;
    public const int TemplePrice = 40;
    public const double TempleTax = 5.5;
    public const string TemplePerms = "pvp;citizen:use;citizen:build;friend:use;";
    public static readonly (int X, int Y, int Z) TemplePoint = (1602, 61, 1602);
    public static readonly BlockBox[] TempleBoxes =
    {
        new(1601, 60, 1601, 1605, 70, 1605),
        new(1605, 60, 1601, 1609, 70, 1605),
    };

    public const string PrisonGuid = "p-full-0002";
    public const string PrisonName = "Jail";
    public const int PrisonNumber = 2;
    public const string PrisonPerms = "citizen:use;";
    public static readonly (int X, int Y, int Z)[] PrisonCells = { (1602, 61, 1610), (1604, 61, 1612) };
    public static readonly BlockBox[] PrisonBoxes = { new(1601, 60, 1609, 1607, 66, 1615) };
    public const int CityPlots = 2;

    /// <summary>2100-01-01: far enough that nothing runs out while the scenarios run.</summary>
    public const long Far = 4102444800;
    public const long PrisonReleaseAt = Far;

    public const string GroupGuid = "g-full-0001";
    public const string Group = "Quarter";
    public const string GroupPerms = "citizen:use;citizen:build;";
    public const double GroupFee = 3;
    public const double GroupPendingFee = 7;
    public const long GroupPendingAt = Far;

    public const long InvitationExpire = Far;

    /// <summary>The city log, oldest first: event and its arguments.</summary>
    public static readonly (string Event, string[] Args)[] Log =
    {
        ("CityCreated", new[] { Mayor, City }),
        ("CitizenJoined", new[] { Citizen }),
        ("FeeChanged", new[] { Mayor, "7" }),
    };

    /// <summary>World flags that are on; the other codes of WorldInfo.Codes are off.</summary>
    public static readonly string[] WorldFlagsOn = { "fireew", "pvpfb" };
}

/// <summary>What fixtures/BrokenRows holds (fixtures/sql/BrokenRows.sql): good rows and rows edited by hand.</summary>
public static class BrokenRows
{
    public const string Folder = "BrokenRows";

    public const string City = "Brokenburg";
    public const string CityGuid = "c-bad-0001";
    public const string Mayor = "bmayor";
    public static readonly ChunkPos HomeChunk = new(400, 400);

    public const string GoodPlot = "p-bad-good";
    public const int GoodPlotPrice = 25;
    public static readonly BlockBox GoodPlotBox = new(6401, 60, 6401, 6405, 66, 6405);

    public const string NoBoxPlot = "p-bad-nobox";
    /// <summary>Read first; the other one is the plot that overlaps it.</summary>
    public const string OverlapFirst = "p-bad-ovl-a";
    public const string OverlapSecond = "p-bad-ovl-b";

    /// <summary>A chunk whose city guid "c-missing" names no city.</summary>
    public static readonly ChunkPos OrphanChunk = new(405, 405);
}
