using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Microsoft.Data.Sqlite;

namespace canclaimsextended.src.database
{
    /// <summary>
    /// Plots groups and the group of each plot. Kept out of PLOTS and CITIES: those rows are written
    /// with fixed column lists, so any column added there would be reset on every save.
    /// </summary>
    public partial class SQLiteDatabaseHandler
    {
        const string plotExtrasTable = "CREATE TABLE IF NOT EXISTS PLOTEXTRAS (plotguid TEXT PRIMARY KEY, groupguid TEXT DEFAULT '')";
        const string plotsGroupsTable = "CREATE TABLE IF NOT EXISTS PLOTSGROUPS (guid TEXT PRIMARY KEY, city TEXT, name TEXT, perms TEXT, " +
            "fee REAL DEFAULT 0, pendingfee REAL DEFAULT -1, pendingat INTEGER DEFAULT 0, accepted TEXT DEFAULT '')";
        const string plotsGroupMembersTable = "CREATE TABLE IF NOT EXISTS PLOTSGROUPMEMBERS (groupguid TEXT, uid TEXT, PRIMARY KEY (groupguid, uid))";

        private void CreatePlotsGroupTables()
        {
            Execute(plotExtrasTable);
            Execute(plotsGroupsTable);
            Execute(plotsGroupMembersTable);
        }

        public override bool savePlotExtra(Plot plot)
        {
            if (plot.Group == null)
            {
                return deletePlotExtra(plot);
            }
            Enqueue("INSERT OR REPLACE INTO PLOTEXTRAS (plotguid, groupguid) VALUES (@guid, @group)",
                ("@guid", plot.Guid), ("@group", plot.Group.Guid));
            return true;
        }

        public override bool deletePlotExtra(Plot plot)
        {
            Enqueue("DELETE FROM PLOTEXTRAS WHERE plotguid=@guid", ("@guid", plot.Guid));
            return true;
        }

        public override bool savePlotsGroup(PlotsGroup group)
        {
            Enqueue("INSERT OR REPLACE INTO PLOTSGROUPS (guid, city, name, perms, fee, pendingfee, pendingat, accepted) " +
                "VALUES (@guid, @city, @name, @perms, @fee, @pending, @at, @accepted)",
                ("@guid", group.Guid), ("@city", group.City?.Guid ?? ""), ("@name", group.GetPartName()), ("@perms", group.Perms.ToString()),
                ("@fee", group.Fee), ("@pending", group.PendingFee), ("@at", group.PendingFeeAt),
                ("@accepted", string.Join(";", group.PendingFeeAccepted)));
            // Members are rewritten whole: the set is small and changes one player at a time.
            Enqueue("DELETE FROM PLOTSGROUPMEMBERS WHERE groupguid=@guid", ("@guid", group.Guid));
            foreach (PlayerInfo member in group.Members)
            {
                Enqueue("INSERT OR REPLACE INTO PLOTSGROUPMEMBERS (groupguid, uid) VALUES (@guid, @uid)", ("@guid", group.Guid), ("@uid", member.Guid));
            }
            return true;
        }

        public override bool deletePlotsGroup(PlotsGroup group)
        {
            Enqueue("DELETE FROM PLOTSGROUPMEMBERS WHERE groupguid=@guid", ("@guid", group.Guid));
            Enqueue("DELETE FROM PLOTSGROUPS WHERE guid=@guid", ("@guid", group.Guid));
            Enqueue("UPDATE PLOTEXTRAS SET groupguid='' WHERE groupguid=@guid", ("@guid", group.Guid));
            Enqueue("UPDATE CITYCHUNKS SET groupguid='' WHERE groupguid=@guid", ("@guid", group.Guid));
            return true;
        }

        /// <summary>Groups first, then their members, then the plot flags that may point at a group.</summary>
        private void loadPlotsGroups()
        {
            var groups = new Dictionary<string, PlotsGroup>();
            ReadRows("SELECT guid, city, name, perms, fee, pendingfee, pendingat, accepted FROM PLOTSGROUPS", r =>
            {
                if (!canclaimsextended.dataStorage.GetCityByGUID(Str(r, 1), out City city))
                {
                    return;
                }
                var group = new PlotsGroup(Str(r, 2), Str(r, 0)) { City = city };
                group.Perms.setPerms(Str(r, 3));
                group.Fee = Dbl(r, 4);
                group.PendingFee = r.IsDBNull(5) ? -1 : r.GetDouble(5);
                group.PendingFeeAt = Long(r, 6);
                foreach (string uid in Str(r, 7).Split(';').Where(s => s.Length > 0))
                {
                    group.PendingFeeAccepted.Add(uid);
                }
                city.PlotsGroups.Add(group);
                groups[group.Guid] = group;
            });
            ReadRows("SELECT groupguid, uid FROM PLOTSGROUPMEMBERS", r =>
            {
                if (groups.TryGetValue(Str(r, 0), out PlotsGroup group)
                    && canclaimsextended.dataStorage.GetPlayerByUid(Str(r, 1), out PlayerInfo player)
                    && group.City.isCitizen(player))
                {
                    group.Members.Add(player);
                }
            });
            ReadRows("SELECT plotguid, groupguid FROM PLOTEXTRAS", r =>
            {
                if (!canclaimsextended.dataStorage.GetPlotByGuid(Str(r, 0), out Plot plot))
                {
                    return;
                }
                // A group of another city is ignored: the plot could have changed hands.
                if (groups.TryGetValue(Str(r, 1), out PlotsGroup group) && group.City.Equals(plot.getCity()))
                {
                    plot.Group = group;
                }
            });
            ReadRows("SELECT cx, cz, groupguid FROM CITYCHUNKS WHERE groupguid != ''", r =>
            {
                if (canclaimsextended.dataStorage.GetChunk(new geometry.ChunkPos(Int(r, 0), Int(r, 1)), out CityChunk chunk)
                    && groups.TryGetValue(Str(r, 2), out PlotsGroup group) && group.City.Equals(chunk.City))
                {
                    chunk.Group = group;
                }
            });
            foreach (PlotsGroup group in groups.Values)
            {
                PlotsGroupHandler.RefreshLand(group);
            }
        }
    }
}
