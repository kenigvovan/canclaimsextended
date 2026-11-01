namespace canclaimsextended.src.database
{
    public class SQLiteTables
    {
        /// <summary>Format of this database. A file with another non-zero value is refused.</summary>
        public const int SCHEMA_VERSION = 101;

        public static string cityTable =
            "CREATE TABLE IF NOT EXISTS CITIES(" +
            "guid TEXT PRIMARY KEY NOT NULL," +
            "name TEXT," +
            "mayor TEXT DEFAULT ''," +
            "timestampcreated INTEGER DEFAULT 0," +
            "perms TEXT DEFAULT ''," +
            "opencity INTEGER DEFAULT 0," +
            "invmsg TEXT DEFAULT ''," +
            "bonusplots INTEGER DEFAULT 0," +
            "bonuschunks INTEGER DEFAULT 0," +
            "istechnical INTEGER DEFAULT 0," +
            "citycolor INTEGER DEFAULT 0," +
            "debtbalance REAL DEFAULT 0," +
            // Daily fee each citizen but the mayor pays to the treasury.
            "fee INTEGER DEFAULT 0," +
            // CustomCityRanks as JSON.
            "ranks TEXT DEFAULT ''" +
            ");";

        public static string playerTable =
            "CREATE TABLE IF NOT EXISTS PLAYERS(" +
            "uid TEXT PRIMARY KEY NOT NULL," +
            "name TEXT," +
            "timestampfirstjoined INTEGER DEFAULT 0," +
            "timestamplastonline INTEGER DEFAULT 0," +
            "city TEXT DEFAULT ''," +
            "friends TEXT DEFAULT ''," +
            "perms TEXT DEFAULT ''," +
            // Names of the city ranks held, ';'-separated.
            "citytitles TEXT DEFAULT ''" +
            ");";

        // Plot attributes. Where the plot is lives in PLOTBOXES.
        public static string plotTable =
            "CREATE TABLE IF NOT EXISTS PLOTS(" +
            "guid TEXT PRIMARY KEY NOT NULL," +
            "name TEXT DEFAULT ''," +
            "city TEXT DEFAULT ''," +
            "owner TEXT DEFAULT ''," +
            "type INTEGER DEFAULT 0," +
            "price INTEGER DEFAULT -1," +
            "customtax REAL DEFAULT 0," +
            "perms TEXT DEFAULT ''," +
            "timestampclaimed INTEGER DEFAULT 0," +
            // Data of the plot type, see PlotDesc.Serialize.
            "plotdesc TEXT DEFAULT ''," +
            // Plot.Number: #n within the city.
            "number INTEGER DEFAULT 0" +
            ");";

        // One row per world flag, keyed by its command code (WorldInfo.Codes).
        public static string worldFlagsTable =
            "CREATE TABLE IF NOT EXISTS WORLDFLAGS(" +
            "flag TEXT PRIMARY KEY NOT NULL," +
            "value INTEGER DEFAULT 0" +
            ");";

        // Pending invitations; a side is a type ("city", "player") and a guid.
        public static string invitationsTable =
            "CREATE TABLE IF NOT EXISTS INVITATIONS(" +
            "sendertype TEXT NOT NULL," +
            "senderguid TEXT NOT NULL," +
            "receivertype TEXT NOT NULL," +
            "receiverguid TEXT NOT NULL," +
            "expire INTEGER DEFAULT 0," +
            "PRIMARY KEY(sendertype, senderguid, receivertype, receiverguid)" +
            ");";

        // Players serving a sentence: in which prison plot and until when. plotguid "#release" =
        // the sentence ended while offline, teleport out on the next login.
        public static string prisonersTable =
            "CREATE TABLE IF NOT EXISTS PRISONERS(" +
            "uid TEXT PRIMARY KEY NOT NULL," +
            "plotguid TEXT NOT NULL," +
            "releaseat INTEGER DEFAULT 0" +
            ");";

        // Players a city wants jailed.
        public static string criminalsTable =
            "CREATE TABLE IF NOT EXISTS CRIMINALS(" +
            "cityguid TEXT NOT NULL," +
            "uid TEXT NOT NULL," +
            "PRIMARY KEY(cityguid, uid)" +
            ");";

        // City history, one row per entry; args are '\u001f'-separated. Trimmed to CITY_LOG_MAX_ENTRIES on load.
        public static string cityLogTable =
            "CREATE TABLE IF NOT EXISTS CITYLOG(" +
            "cityguid TEXT NOT NULL," +
            "ts INTEGER NOT NULL," +
            "event TEXT NOT NULL," +
            "args TEXT DEFAULT ''" +
            ");";

        // City territory, one row per chunk (ChunkPos, CLAIM_CHUNK_SIZE blocks a side). A chunk has
        // one city at most, so the position is the key.
        public static string cityChunksTable =
            "CREATE TABLE IF NOT EXISTS CITYCHUNKS(" +
            "cityguid TEXT NOT NULL," +
            "cx INTEGER NOT NULL," +
            "cz INTEGER NOT NULL," +
            "timestampclaimed INTEGER DEFAULT 0," +
            "extrabought INTEGER DEFAULT 0," +
            "outpost INTEGER DEFAULT 0," +
            // Guid of the plots group the chunk belongs to, empty when none.
            "groupguid TEXT DEFAULT ''," +
            "PRIMARY KEY(cx, cz)" +
            ");";

        // One row per box of a plot; max is exclusive, as in BlockBox.
        public static string plotBoxesTable =
            "CREATE TABLE IF NOT EXISTS PLOTBOXES(" +
            "plotguid TEXT NOT NULL," +
            "idx INTEGER NOT NULL," +
            "minx INTEGER, miny INTEGER, minz INTEGER," +
            "maxx INTEGER, maxy INTEGER, maxz INTEGER," +
            "PRIMARY KEY(plotguid, idx)" +
            ");";
    }
}
