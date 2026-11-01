using canclaimsextended.src;
using canclaimsextended.src.database;
using Microsoft.Data.Sqlite;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    /// <summary>
    /// A real SQLiteDatabaseHandler on a temp file, no server: Mod.sapi is a mock whose logger
    /// counts calls, Mod.dataStorage a fresh DataStorage. The handler is opened by the test with
    /// <see cref="Open"/>, so a test can lay out an older file first. The drain timer is never
    /// fired; <see cref="Dispose"/> closes the handler, which writes what is queued.
    /// The handler's connection is private: tests read the file through their own connection.
    /// </summary>
    public abstract class DatabaseTestBase : IDisposable
    {
        protected readonly string dir = Path.Combine(Path.GetTempPath(), "canclaimsextended-db-tests", Guid.NewGuid().ToString("N"));
        private readonly ICoreServerAPI previousSapi = Mod.sapi;
        private readonly Config previousConfig = Mod.config;
        private readonly DataStorage previousStorage = Mod.dataStorage;
        protected readonly Mock<ILogger> logger = new();
        private SQLiteDatabaseHandler? db;

        protected DatabaseTestBase()
        {
            Directory.CreateDirectory(dir);
            var sapi = new Mock<ICoreServerAPI> { DefaultValue = DefaultValue.Mock };
            sapi.Setup(a => a.Logger).Returns(logger.Object);
            Mod.sapi = sapi.Object;
            Mod.config = new Config { PATH_TO_DB_AND_JSON_FILES = dir, DB_NAME = "test.db" };
            Mod.dataStorage = new DataStorage();
        }

        protected string DbPath => Path.Combine(dir, "test.db");

        /// <summary>Opens the handler on the test file, as the server does at start.</summary>
        protected SQLiteDatabaseHandler Open()
        {
            db = new SQLiteDatabaseHandler();
            return db;
        }

        /// <summary>A separate connection to the test file; not pooled, so the file can be deleted.</summary>
        protected SqliteConnection Connect()
        {
            var connection = new SqliteConnection("Data Source=" + DbPath + ";Pooling=False");
            connection.Open();
            return connection;
        }

        protected void Exec(params string[] statements)
        {
            using SqliteConnection connection = Connect();
            foreach (string sql in statements)
            {
                using var cmd = new SqliteCommand(sql, connection);
                cmd.ExecuteNonQuery();
            }
        }

        protected object? Scalar(string sql)
        {
            using SqliteConnection connection = Connect();
            using var cmd = new SqliteCommand(sql, connection);
            return cmd.ExecuteScalar();
        }

        protected long Count(string sql) => Convert.ToInt64(Scalar(sql));

        protected bool TableExists(string table) =>
            Count($"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}'") > 0;

        protected List<string> Columns(string table)
        {
            using SqliteConnection connection = Connect();
            using var cmd = new SqliteCommand($"PRAGMA table_info({table})", connection);
            using SqliteDataReader r = cmd.ExecuteReader();
            var columns = new List<string>();
            while (r.Read()) columns.Add(r.GetString(1));
            return columns;
        }

        protected List<string[]> Rows(string sql)
        {
            using SqliteConnection connection = Connect();
            using var cmd = new SqliteCommand(sql, connection);
            using SqliteDataReader r = cmd.ExecuteReader();
            var rows = new List<string[]>();
            while (r.Read())
            {
                var row = new string[r.FieldCount];
                for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i))!;
                rows.Add(row);
            }
            return rows;
        }

        protected int Logged(string level) => logger.Invocations.Count(i => i.Method.Name == level);

        protected int LoggedErrors() => Logged(nameof(ILogger.Error));

        public void Dispose()
        {
            try { db?.close(); } catch (Exception) { /* a test that broke the file must not fail the cleanup */ }
            SqliteConnection.ClearAllPools();
            Mod.sapi = previousSapi;
            Mod.config = previousConfig;
            Mod.dataStorage = previousStorage;
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
