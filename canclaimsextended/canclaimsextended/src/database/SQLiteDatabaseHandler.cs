using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace canclaimsextended.src.database
{
    /// <summary>
    /// Writes are queued as ready statements with their values taken at the moment of the save,
    /// then run in one transaction every half second on the main thread. Loading is in
    /// SQLiteDatabaseHandler.Load.cs.
    /// </summary>
    public partial class SQLiteDatabaseHandler : DatabaseHandler
    {
        private readonly SqliteConnection connection;
        private readonly ConcurrentQueue<Action<SqliteTransaction>> queue = new();
        private readonly long drainListenerId;
        // Rights stored by name: numbers would shift if the enum is ever reordered. Names renamed
        // since are translated on read, see PermissionSetConverter.
        private static readonly rights.PermissionSetConverter rankPermsConverter = new();
        private static readonly JsonSerializerSettings rankJson = new() { Converters = { rankPermsConverter, new StringEnumConverter() } };

        const string DeleteInvitationSql =
            "DELETE FROM INVITATIONS WHERE sendertype=@stype AND senderguid=@sguid AND receivertype=@rtype AND receiverguid=@rguid";

        public SQLiteDatabaseHandler()
        {
            string path = canclaimsextended.config.FilePath(canclaimsextended.config.DB_NAME);
            canclaimsextended.sapi.Logger.Debug("[canclaimsextended] db path is " + path);

            connection = new SqliteConnection("Data Source=" + path);
            connection.Open();
            CheckSchemaVersion(path);
            foreach (string sql in new[] { SQLiteTables.cityTable, SQLiteTables.playerTable, SQLiteTables.plotTable, SQLiteTables.plotBoxesTable,
                SQLiteTables.worldFlagsTable, SQLiteTables.invitationsTable, SQLiteTables.cityChunksTable })
            {
                Execute(sql);
            }
            // Columns added after the table first shipped; CREATE IF NOT EXISTS does not add them.
            EnsureColumn("CITIES", "fee", "INTEGER DEFAULT 0");
            EnsureColumn("CITIES", "emblem", "TEXT DEFAULT ''");
            EnsureColumn("CITIES", "dailymsg", "TEXT DEFAULT ''");
            EnsureColumn("PLAYERS", "prefix", "TEXT DEFAULT ''");
            EnsureColumn("PLAYERS", "aftername", "TEXT DEFAULT ''");
            EnsureColumn("PLOTS", "number", "INTEGER DEFAULT 0");
            EnsureColumn("CITYCHUNKS", "groupguid", "TEXT DEFAULT ''");
            Execute(SQLiteTables.cityLogTable);
            Execute(SQLiteTables.prisonersTable);
            Execute(SQLiteTables.criminalsTable);
            CreatePlotsGroupTables();
            DropAllianceLeftovers();
            Execute("PRAGMA user_version = " + SQLiteTables.SCHEMA_VERSION);

            drainListenerId = canclaimsextended.sapi.Event.RegisterGameTickListener(_ => drainQueue(), 500);
        }

        /// <summary>
        /// A file of another format is refused instead of being written into: there is no migration
        /// from the column-per-chunk layout of claims.
        /// </summary>
        private void CheckSchemaVersion(string path)
        {
            long version = (long)new SqliteCommand("PRAGMA user_version", connection).ExecuteScalar();
            long tables = (long)new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table'", connection).ExecuteScalar();
            if (tables > 0 && version != SQLiteTables.SCHEMA_VERSION)
            {
                connection.Close();
                throw new InvalidOperationException($"[canclaimsextended] {path} has format {version}, expected {SQLiteTables.SCHEMA_VERSION}. " +
                    "Old worlds are not supported: move the file away to start with an empty database.");
            }
        }

        /// <summary>Adds the column when the table lacks it. Safe on every start.</summary>
        private void EnsureColumn(string table, string column, string declaration)
        {
            using (var cmd = new SqliteCommand($"PRAGMA table_info({table})", connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
                }
            }
            Execute($"ALTER TABLE {table} ADD COLUMN {column} {declaration}");
        }

        /// <summary>
        /// Alliances were removed: their table, invitations to or from them or to a city, and their
        /// log entries go. Does nothing once done. CITIES.alliance stays in old files, never read.
        /// </summary>
        private void DropAllianceLeftovers()
        {
            Execute("DROP TABLE IF EXISTS ALLIANCES");
            Execute("DELETE FROM INVITATIONS WHERE sendertype='alliance' OR receivertype='alliance' OR receivertype='city'");
            Execute("DELETE FROM CITYLOG WHERE event IN ('AllianceJoined', 'AllianceLeft')");
        }

        private void Execute(string sql)
        {
            using var cmd = new SqliteCommand(sql, connection);
            cmd.ExecuteNonQuery();
        }

        private void Enqueue(string sql, params (string Name, object Value)[] values)
        {
            queue.Enqueue(tx =>
            {
                using var cmd = new SqliteCommand(sql, connection, tx);
                foreach (var v in values)
                {
                    cmd.Parameters.AddWithValue(v.Name, v.Value ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            });
        }

        private void drainQueue()
        {
            if (queue.IsEmpty) return;
            SqliteTransaction tx;
            try
            {
                tx = connection.BeginTransaction();
            }
            catch (Exception e)
            {
                // Runs inside the tick: a throw here would stop the server. The queue waits for the next try.
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] database transaction could not start: {0}", e);
                return;
            }
            using (tx)
            {
                while (queue.TryDequeue(out var action))
                {
                    // One failed write is logged and the rest still go through.
                    try
                    {
                        action(tx);
                    }
                    catch (Exception e)
                    {
                        canclaimsextended.sapi.Logger.Error("[canclaimsextended] database write failed: {0}", e);
                    }
                }
                try
                {
                    tx.Commit();
                }
                catch (Exception e)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] database commit failed: {0}", e);
                }
            }
        }

        public override bool makeBackup(string path)
        {
            try
            {
                drainQueue();
                // No pooling: a pooled connection would keep the file open and old copies undeletable.
                using var dest = new SqliteConnection("Data Source=" + path + ";Pooling=False");
                dest.Open();
                connection.BackupDatabase(dest);
                return true;
            }
            catch (Exception e)
            {
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] backup to {0} failed: {1}", path, e);
                return false;
            }
        }

        public override void saveEverything()
        {
            drainQueue();
        }

        public override void close()
        {
            canclaimsextended.sapi.Event.UnregisterGameTickListener(drainListenerId);
            drainQueue();
            connection.Close();
            connection.Dispose();
        }

        /*==============================================================================================*/
        /*=====================================SAVE=====================================================*/
        /*==============================================================================================*/
        public override bool savePlayerInfo(PlayerInfo player, bool update = true)
        {
            Enqueue("INSERT OR REPLACE INTO PLAYERS(uid, name, timestampfirstjoined, timestamplastonline, city, friends, perms, citytitles) " +
                    "VALUES(@uid, @name, @first, @last, @city, @friends, @perms, @titles)",
                ("@uid", player.Guid),
                ("@name", player.GetPartName()),
                ("@first", player.TimeStampFirstJoined),
                ("@last", player.TimeStampLasOnline),
                ("@city", player.City?.Guid ?? ""),
                ("@friends", string.Join(";", player.Friends.Select(f => f.Guid))),
                ("@perms", player.PermsHandler.ToString()),
                ("@titles", string.Join(";", player.getCityTitles())));
            // After the replace above, which resets columns it does not list.
            Enqueue("UPDATE PLAYERS SET prefix=@prefix, aftername=@after WHERE uid=@uid",
                ("@uid", player.Guid), ("@prefix", player.Prefix ?? ""), ("@after", player.AfterName ?? ""));
            return true;
        }
        public override bool deletePlayerInfo(PlayerInfo player)
        {
            Enqueue("DELETE FROM PLAYERS WHERE uid=@uid", ("@uid", player.Guid));
            return true;
        }

        public override bool saveCity(City city, bool update = true)
        {
            Enqueue("INSERT OR REPLACE INTO CITIES(guid, name, mayor, timestampcreated, perms, opencity, invmsg, bonusplots, istechnical, citycolor, debtbalance, ranks, fee, emblem, bonuschunks) " +
                    "VALUES(@guid, @name, @mayor, @created, @perms, @open, @invmsg, @bonus, @technical, @color, @debt, @ranks, @fee, @emblem, @bonuschunks)",
                ("@bonuschunks", city.getBonusChunks()),
                ("@emblem", city.Emblem ?? ""),
                ("@fee", city.fee),
                ("@guid", city.Guid),
                ("@name", city.GetPartName()),
                ("@mayor", city.getMayor()?.Guid ?? ""),
                ("@created", city.TimeStampCreated),
                ("@perms", city.getPermsHandler().ToString()),
                ("@open", city.openCity ? 1 : 0),
                ("@invmsg", city.invMsg ?? ""),
                ("@bonus", city.getBonusPlots()),
                ("@technical", city.isTechnicalCity() ? 1 : 0),
                ("@color", city.cityColor),
                ("@debt", city.DebtBalance),
                ("@ranks", JsonConvert.SerializeObject(city.CustomCityRanks, rankJson)));
            // After the replace above, which resets columns it does not list.
            Enqueue("UPDATE CITIES SET dailymsg=@msg WHERE guid=@guid",
                ("@guid", city.Guid), ("@msg", city.DailyMsg ?? ""));
            return true;
        }
        public override bool deleteCity(City city)
        {
            Enqueue("DELETE FROM CITIES WHERE guid=@guid", ("@guid", city.Guid));
            Enqueue("DELETE FROM CITYLOG WHERE cityguid=@guid", ("@guid", city.Guid));
            Enqueue("DELETE FROM CITYCHUNKS WHERE cityguid=@guid", ("@guid", city.Guid));
            return true;
        }

        public override bool saveChunk(City city, CityChunk chunk)
        {
            Enqueue("INSERT OR REPLACE INTO CITYCHUNKS(cityguid, cx, cz, timestampclaimed, extrabought, outpost, groupguid) " +
                    "VALUES(@city, @cx, @cz, @claimed, @extra, @outpost, @group)",
                ("@city", city.Guid), ("@cx", chunk.Pos.X), ("@cz", chunk.Pos.Z), ("@claimed", chunk.TimeStampClaimed),
                ("@extra", chunk.ExtraBought ? 1 : 0), ("@outpost", chunk.IsOutpost ? 1 : 0), ("@group", chunk.Group?.Guid ?? ""));
            return true;
        }

        public override bool deleteChunk(ChunkPos chunk)
        {
            Enqueue("DELETE FROM CITYCHUNKS WHERE cx=@cx AND cz=@cz", ("@cx", chunk.X), ("@cz", chunk.Z));
            return true;
        }

        /// <summary>The boxes are rewritten as a whole, so a reshape needs no special case.</summary>
        public override bool savePlot(Plot plot, bool update = true)
        {
            Enqueue("INSERT OR REPLACE INTO PLOTS(guid, name, city, owner, type, price, customtax, perms, timestampclaimed, plotdesc, number) " +
                    "VALUES(@guid, @name, @city, @owner, @type, @price, @tax, @perms, @claimed, @desc, @number)",
                ("@number", plot.Number),
                ("@desc", plot.Desc?.Serialize() ?? ""),
                ("@guid", plot.Guid),
                ("@name", plot.GetPartName()),
                ("@city", plot.getCity()?.Guid ?? ""),
                ("@owner", plot.getPlotOwner()?.Guid ?? ""),
                ("@type", (int)plot.Type),
                ("@price", plot.Price),
                ("@tax", plot.getCustomTax()),
                ("@perms", plot.getPermsHandler().ToString()),
                ("@claimed", plot.TimeStampClaimed));
            Enqueue("DELETE FROM PLOTBOXES WHERE plotguid=@guid", ("@guid", plot.Guid));
            IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                BlockBox b = boxes[i];
                Enqueue("INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) " +
                        "VALUES(@guid, @idx, @minx, @miny, @minz, @maxx, @maxy, @maxz)",
                    ("@guid", plot.Guid), ("@idx", i),
                    ("@minx", b.MinX), ("@miny", b.MinY), ("@minz", b.MinZ),
                    ("@maxx", b.MaxX), ("@maxy", b.MaxY), ("@maxz", b.MaxZ));
            }
            return true;
        }
        public override bool deletePlot(Plot plot)
        {
            Enqueue("DELETE FROM PLOTBOXES WHERE plotguid=@guid", ("@guid", plot.Guid));
            Enqueue("DELETE FROM PLOTS WHERE guid=@guid", ("@guid", plot.Guid));
            return true;
        }

        public override bool saveWorldInfo(WorldInfo world)
        {
            foreach (string code in WorldInfo.Codes)
            {
                world.TryGet(code, out bool value);
                Enqueue("INSERT OR REPLACE INTO WORLDFLAGS(flag, value) VALUES(@flag, @value)",
                    ("@flag", code), ("@value", value ? 1 : 0));
            }
            return true;
        }

        public override bool saveInvitation(delayed.invitations.Invitation invitation)
        {
            Enqueue("INSERT OR REPLACE INTO INVITATIONS(sendertype, senderguid, receivertype, receiverguid, expire) " +
                    "VALUES(@stype, @sguid, @rtype, @rguid, @expire)",
                ("@stype", delayed.invitations.InvitationParty.TypeOf(invitation.getSender())),
                ("@sguid", delayed.invitations.InvitationParty.GuidOf(invitation.getSender())),
                ("@rtype", delayed.invitations.InvitationParty.TypeOf(invitation.getReceiver())),
                ("@rguid", delayed.invitations.InvitationParty.GuidOf(invitation.getReceiver())),
                ("@expire", invitation.getTimeStamp()));
            return true;
        }

        public override bool deleteInvitation(delayed.invitations.Invitation invitation)
        {
            Enqueue(DeleteInvitationSql,
                ("@stype", delayed.invitations.InvitationParty.TypeOf(invitation.getSender())),
                ("@sguid", delayed.invitations.InvitationParty.GuidOf(invitation.getSender())),
                ("@rtype", delayed.invitations.InvitationParty.TypeOf(invitation.getReceiver())),
                ("@rguid", delayed.invitations.InvitationParty.GuidOf(invitation.getReceiver())));
            return true;
        }

        public override bool savePrisoner(PlayerInfo player)
        {
            if (player.PrisonPlotGuid.Length == 0)
            {
                Enqueue("DELETE FROM PRISONERS WHERE uid=@uid", ("@uid", player.Guid));
                return true;
            }
            Enqueue("INSERT OR REPLACE INTO PRISONERS(uid, plotguid, releaseat) VALUES(@uid, @plot, @release)",
                ("@uid", player.Guid), ("@plot", player.PrisonPlotGuid), ("@release", player.PrisonReleaseAt));
            return true;
        }

        public override bool saveCriminal(City city, string uid)
        {
            Enqueue("INSERT OR REPLACE INTO CRIMINALS(cityguid, uid) VALUES(@city, @uid)", ("@city", city.Guid), ("@uid", uid));
            return true;
        }

        public override bool deleteCriminal(City city, string uid)
        {
            Enqueue("DELETE FROM CRIMINALS WHERE cityguid=@city AND uid=@uid", ("@city", city.Guid), ("@uid", uid));
            return true;
        }

        public override bool deleteCriminalsOf(City city)
        {
            Enqueue("DELETE FROM CRIMINALS WHERE cityguid=@city", ("@city", city.Guid));
            return true;
        }

        const char LOG_ARG_SEPARATOR = '\u001f';

        public override bool saveCityLogEntry(City city, part.citylog.CityLogEntry entry)
        {
            Enqueue("INSERT INTO CITYLOG(cityguid, ts, event, args) VALUES(@city, @ts, @event, @args)",
                ("@city", city.Guid), ("@ts", entry.Timestamp), ("@event", entry.EventType.ToString()),
                ("@args", string.Join(LOG_ARG_SEPARATOR, entry.Args)));
            return true;
        }
    }
}
