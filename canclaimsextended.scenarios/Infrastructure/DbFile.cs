using Microsoft.Data.Sqlite;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios.Infrastructure;

/// <summary>
/// The mod's database file as it is on disk, read through a connection of its own. The mod queues
/// its writes and runs them every half second (SQLiteDatabaseHandler.drainQueue); <see cref="Flush"/>
/// runs the queue now, so what is read afterwards is everything the mod has saved so far.
/// Call from a scenario body: it runs on the game thread, as the mod's own drain does.
/// </summary>
public static class DbFile
{
    /// <summary>Where the mod keeps it: Config.DB_NAME in PATH_TO_DB_AND_JSON_FILES (empty = ModConfig).</summary>
    public static string FilePath => Mod.config.FilePath(Mod.config.DB_NAME);

    /// <summary>Writes every queued save (DatabaseHandler.saveEverything).</summary>
    public static void Flush()
    {
        Assert.True(Mod.Db != null, "the mod has no open database");
        Mod.Db!.saveEverything();
    }

    /// <summary>Read only and unpooled: it never holds the file once the call is done.</summary>
    public static SqliteConnection OpenReadOnly(string? path = null)
    {
        var connection = new SqliteConnection($"Data Source={path ?? FilePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    /// <summary>Every row of the query, column name to value (long, double, string or null).</summary>
    public static List<Dictionary<string, object?>> Rows(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = OpenReadOnly();
        return Rows(connection, sql, parameters);
    }

    public static List<Dictionary<string, object?>> Rows(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = new SqliteCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
        using SqliteDataReader r = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++)
            {
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    public static object? Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = OpenReadOnly();
        using var cmd = new SqliteCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
        object? value = cmd.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public static long Count(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(Scalar(sql, parameters));

    public static long UserVersion() => Convert.ToInt64(Scalar("PRAGMA user_version"));

    public static bool TableExists(string table) =>
        Count("SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@t", ("@t", table)) > 0;

    /// <summary>Table name to its column names, for every table of the file (sqlite_ ones left out).</summary>
    public static Dictionary<string, HashSet<string>> Schema(SqliteConnection connection)
    {
        var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var tables = Rows(connection, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .Select(r => (string)r["name"]!)
            .ToList();
        foreach (string table in tables)
        {
            schema[table] = Rows(connection, $"PRAGMA table_info({table})")
                .Select(r => (string)r["name"]!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return schema;
    }

    // ---- reading values the way SQLite hands them back ---------------------------------------

    public static string Str(Dictionary<string, object?> row, string column) => Convert.ToString(row[column]) ?? "";
    public static long Long(Dictionary<string, object?> row, string column) => row[column] == null ? 0 : Convert.ToInt64(row[column]);
    public static double Dbl(Dictionary<string, object?> row, string column) => row[column] == null ? 0 : Convert.ToDouble(row[column]);
}
