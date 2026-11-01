using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace canclaimsextended.src.database
{
    /// <summary>Reading the whole database into canclaimsextended.dataStorage at server start.</summary>
    public partial class SQLiteDatabaseHandler
    {
        // Mayors are linked after the players exist.
        private readonly Dictionary<City, string> pendingMayors = new();
        // Cities whose ranks used old right names; saved again once everything of them is loaded,
        // as saveCity writes the whole row.
        private readonly List<City> ranksToRewrite = new();

        public override bool loadEverything()
        {
            bool ok = true;
            ok &= TryLoad("cities", loadCities);
            ok &= TryLoad("players", loadPlayers);
            // After cities; before plots, which are checked against the territory.
            ok &= TryLoad("chunks", loadChunks);
            ok &= TryLoad("plots", loadPlots);
            // After cities and players: fills fields of both.
            ok &= TryLoad("city and player extras", loadCityAndPlayerExtras);
            ok &= TryLoad("world flags", loadWorldInfo);
            // After cities, players and plots: rows point at all three.
            ok &= TryLoad("prisons", loadPrisons);
            // After cities, players and plots: groups point at all three.
            ok &= TryLoad("plots groups", loadPlotsGroups);
            // Last: both sides of an invitation must exist by now.
            ok &= TryLoad("invitations", loadInvitations);
            foreach (City city in ranksToRewrite)
            {
                saveCity(city);
            }
            if (ranksToRewrite.Count > 0)
            {
                canclaimsextended.sapi.Logger.Notification("[canclaimsextended] ranks of {0} cities used old right names and were rewritten", ranksToRewrite.Count);
            }
            ranksToRewrite.Clear();
            return ok;
        }

        private static bool TryLoad(string name, Action load)
        {
            try
            {
                load();
                return true;
            }
            catch (Exception e)
            {
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] loading {0} failed: {1}", name, e);
                return false;
            }
        }

        /// <summary>Runs the query and hands each row to the callback.</summary>
        private void ReadRows(string sql, Action<SqliteDataReader> row)
        {
            using var cmd = new SqliteCommand(sql, connection);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                row(r);
            }
        }

        // A NULL in a column reads as the default instead of throwing for the whole table.
        private static string Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        private static int Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
        private static long Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);
        private static double Dbl(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);

        private void loadCities()
        {
            ReadRows("SELECT guid, name, mayor, timestampcreated, perms, opencity, invmsg, bonusplots, istechnical, citycolor, debtbalance, ranks, fee, emblem, bonuschunks FROM CITIES", r =>
            {
                City city = new City(Str(r, 1), Str(r, 0), Long(r, 8) != 0)
                {
                    TimeStampCreated = Long(r, 3),
                    openCity = Long(r, 5) != 0,
                    invMsg = Str(r, 6),
                    cityColor = Int(r, 9),
                    DebtBalance = Dbl(r, 10),
                    fee = Int(r, 12),
                    // Normalized on load too: the whitelist may have changed since it was stored.
                    Emblem = EmblemHandler.Normalize(Str(r, 13))
                };
                city.getPermsHandler().setPerms(Str(r, 4));
                city.setBonusPlots(Int(r, 7));
                city.setBonusChunks(Int(r, 14));
                loadRanks(city, Str(r, 11));
                if (!canclaimsextended.dataStorage.AddCity(city))
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] city {0} ({1}) clashes with another by guid or name, skipped", city.GetPartName(), city.Guid);
                    return;
                }
                pendingMayors[city] = Str(r, 2);
            });
        }

        private void loadRanks(City city, string ranks)
        {
            if (ranks.Length == 0) return;
            try
            {
                rankPermsConverter.Reset();
                city.CustomCityRanks = JsonConvert.DeserializeObject<Dictionary<string, CustomCityRank>>(ranks, rankJson)
                    ?? new Dictionary<string, CustomCityRank>();
                if (rankPermsConverter.Changed)
                {
                    if (rankPermsConverter.Unknown.Count > 0)
                    {
                        canclaimsextended.sapi.Logger.Warning("[canclaimsextended] ranks of city {0}: unknown rights dropped: {1}",
                            city.GetPartName(), string.Join(", ", rankPermsConverter.Unknown));
                    }
                    // Written back with today's names once the city is in the storage.
                    ranksToRewrite.Add(city);
                }
            }
            catch (JsonException e)
            {
                // The city still loads, only without its ranks.
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] ranks of city {0} could not be read: {1}", city.GetPartName(), e.Message);
            }
        }

        private void loadChunks()
        {
            ReadRows("SELECT cityguid, cx, cz, timestampclaimed, extrabought, outpost FROM CITYCHUNKS", r =>
            {
                if (!canclaimsextended.dataStorage.GetCityByGUID(Str(r, 0), out City city))
                {
                    canclaimsextended.sapi.Logger.Warning("[canclaimsextended] chunk {0},{1} belongs to no known city, skipped", Int(r, 1), Int(r, 2));
                    return;
                }
                CityChunk chunk = new CityChunk(new ChunkPos(Int(r, 1), Int(r, 2)))
                {
                    TimeStampClaimed = Long(r, 3),
                    ExtraBought = Int(r, 4) != 0,
                    IsOutpost = Int(r, 5) != 0
                };
                canclaimsextended.dataStorage.AddChunk(city, chunk);
            });
        }

        private void loadPlayers()
        {
            var friends = new Dictionary<PlayerInfo, string>();
            ReadRows("SELECT uid, name, timestampfirstjoined, timestamplastonline, city, friends, perms, citytitles FROM PLAYERS", r =>
            {
                PlayerInfo player = new PlayerInfo(Str(r, 1), Str(r, 0))
                {
                    TimeStampFirstJoined = Long(r, 2),
                    TimeStampLasOnline = Long(r, 3)
                };
                player.PermsHandler.setPerms(Str(r, 6));
                if (!canclaimsextended.dataStorage.AddPlayer(player))
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] player {0} loaded twice, skipped", player.Guid);
                    return;
                }
                if (canclaimsextended.dataStorage.GetCityByGUID(Str(r, 4), out City city))
                {
                    player.setCity(city);
                    city.getCityCitizens().Add(player);
                    // Only ranks the city still has.
                    foreach (string title in Str(r, 7).Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (city.HasCityRank(title))
                        {
                            player.addCityTitle(title);
                        }
                    }
                }
                friends[player] = Str(r, 5);
            });
            foreach (var it in friends)
            {
                foreach (string uid in it.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (canclaimsextended.dataStorage.GetPlayerByUid(uid, out PlayerInfo friend))
                    {
                        it.Key.addComrade(friend);
                    }
                }
            }
            foreach (var it in pendingMayors)
            {
                if (canclaimsextended.dataStorage.GetPlayerByUid(it.Value, out PlayerInfo mayor))
                {
                    it.Key.setMayor(mayor);
                }
            }
            pendingMayors.Clear();
        }

        private void loadPlots()
        {
            var boxesByPlot = new Dictionary<string, SortedList<long, BlockBox>>();
            ReadRows("SELECT plotguid, idx, minx, miny, minz, maxx, maxy, maxz FROM PLOTBOXES", r =>
            {
                string guid = Str(r, 0);
                if (!boxesByPlot.TryGetValue(guid, out var list))
                {
                    list = new SortedList<long, BlockBox>();
                    boxesByPlot[guid] = list;
                }
                try
                {
                    list[Long(r, 1)] = new BlockBox(Int(r, 2), Int(r, 3), Int(r, 4), Int(r, 5), Int(r, 6), Int(r, 7));
                }
                catch (ArgumentException e)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] box of plot {0} is broken, skipped: {1}", guid, e.Message);
                }
            });

            ReadRows("SELECT guid, name, city, owner, type, price, customtax, perms, timestampclaimed, plotdesc, number FROM PLOTS", r =>
            {
                string guid = Str(r, 0);
                if (!boxesByPlot.TryGetValue(guid, out var boxes) || boxes.Count == 0)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] plot {0} has no boxes, skipped", guid);
                    return;
                }
                PlotShape shape;
                try
                {
                    shape = new PlotShape(boxes.Values);
                }
                catch (ArgumentException e)
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] plot {0} has a broken shape, skipped: {1}", guid, e.Message);
                    return;
                }
                Plot plot = new Plot(guid, shape)
                {
                    Type = (PlotType)Int(r, 4),
                    Price = Int(r, 5),
                    TimeStampClaimed = Long(r, 8),
                    Number = Int(r, 10)
                };
                plot.SetPartName(Str(r, 1));
                plot.setCustomTax(Dbl(r, 6));
                plot.getPermsHandler().setPerms(Str(r, 7));
                plot.Desc = PlotTypes.LoadDesc(plot.Type, Str(r, 9));

                // Overlap is never written by the commands; a file edited by hand may still have it.
                if (canclaimsextended.dataStorage.QueryPlots(shape.Bounds).Any(other => other.Shape.Intersects(shape)))
                {
                    canclaimsextended.sapi.Logger.Warning("[canclaimsextended] plot {0} overlaps another plot", guid);
                }
                // Into the index first: a duplicate must not reach the lists of its city or owner.
                if (!canclaimsextended.dataStorage.AddPlot(plot))
                {
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] plot {0} loaded twice, skipped", guid);
                    return;
                }
                if (canclaimsextended.dataStorage.GetCityByGUID(Str(r, 2), out City city))
                {
                    plot.setCity(city);
                    city.getCityPlots().Add(plot);
                    // Kept and still protected; the mayor can cut it back with /plot cut.
                    if (!part.ClaimRules.InsideTerritory(city, shape))
                    {
                        canclaimsextended.sapi.Logger.Warning("[canclaimsextended] plot {0} of city {1} reaches outside its territory", guid, city.GetPartName());
                    }
                }
                if (canclaimsextended.dataStorage.GetPlayerByUid(Str(r, 3), out PlayerInfo owner))
                {
                    plot.setPlotOwner(owner);
                    owner.PlayerPlots.Add(plot);
                }
            });
            // Plots stored before numbers existed get one now, in the order they were claimed.
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList())
            {
                foreach (Plot plot in city.getCityPlots().Where(p => p.Number <= 0).OrderBy(p => p.TimeStampClaimed).ToList())
                {
                    plot.Number = city.NextPlotNumber();
                    savePlot(plot);
                }
            }
        }

        /// <summary>
        /// Columns written by their own UPDATE after the city and player rows, plus the city logs.
        /// A log keeps its newest CITY_LOG_MAX_ENTRIES lines; older rows are dropped from the file.
        /// </summary>
        private void loadCityAndPlayerExtras()
        {
            ReadRows("SELECT guid, dailymsg FROM CITIES", r =>
            {
                if (canclaimsextended.dataStorage.GetCityByGUID(Str(r, 0), out City city))
                {
                    city.DailyMsg = Str(r, 1);
                }
            });
            ReadRows("SELECT uid, prefix, aftername FROM PLAYERS", r =>
            {
                if (canclaimsextended.dataStorage.GetPlayerByUid(Str(r, 0), out PlayerInfo player))
                {
                    player.Prefix = Str(r, 1);
                    player.AfterName = Str(r, 2);
                }
            });
            var trimmed = new HashSet<City>();
            ReadRows("SELECT cityguid, ts, event, args FROM CITYLOG ORDER BY ts", r =>
            {
                if (!canclaimsextended.dataStorage.GetCityByGUID(Str(r, 0), out City city)) return;
                // An event name this build does not know is skipped, not fatal.
                if (!Enum.TryParse(Str(r, 2), out part.citylog.EnumCityLogEvent type)) return;
                string args = Str(r, 3);
                int before = city.EventLog.Count;
                city.AddLogEntry(new part.citylog.CityLogEntry(Long(r, 1), type,
                    args.Length == 0 ? new string[0] : args.Split(LOG_ARG_SEPARATOR)));
                if (city.EventLog.Count == before) trimmed.Add(city);
            });
            foreach (City city in trimmed)
            {
                if (city.EventLog.Count == 0) continue;
                Enqueue("DELETE FROM CITYLOG WHERE cityguid=@city AND ts < @oldest",
                    ("@city", city.Guid), ("@oldest", city.EventLog[0].Timestamp));
            }
        }

        private void loadWorldInfo()
        {
            ReadRows("SELECT flag, value FROM WORLDFLAGS", r =>
                canclaimsextended.dataStorage.World.TrySet(Str(r, 0), Long(r, 1) != 0));
        }

        private void loadPrisons()
        {
            ReadRows("SELECT cityguid, uid FROM CRIMINALS", r =>
            {
                if (canclaimsextended.dataStorage.GetCityByGUID(Str(r, 0), out City city))
                {
                    city.Criminals.Add(Str(r, 1));
                }
            });
            ReadRows("SELECT uid, plotguid, releaseat FROM PRISONERS", r =>
            {
                if (canclaimsextended.dataStorage.GetPlayerByUid(Str(r, 0), out PlayerInfo player))
                {
                    player.PrisonPlotGuid = Str(r, 1);
                    player.PrisonReleaseAt = Long(r, 2);
                }
            });
        }

        /// <summary>
        /// Rebuilt through InvitationKinds. A row that ran out while the server was down, or whose
        /// side no longer exists, is dropped. A row of a type or kind this build does not know is
        /// kept as it is, for the build that does.
        /// </summary>
        private void loadInvitations()
        {
            long now = auxiliary.TimeFunctions.GetEpochSeconds();
            var dropped = new List<(string, string, string, string)>();
            ReadRows("SELECT sendertype, senderguid, receivertype, receiverguid, expire FROM INVITATIONS", r =>
            {
                var key = (Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3));
                long expire = Long(r, 4);
                if (!delayed.invitations.InvitationParty.CanResolve(key.Item1) || !delayed.invitations.InvitationParty.CanResolve(key.Item3))
                {
                    return;
                }
                if (expire < now
                    || delayed.invitations.InvitationParty.Resolve(key.Item1, key.Item2) is not part.interfaces.ISender sender
                    || delayed.invitations.InvitationParty.Resolve(key.Item3, key.Item4) is not part.interfaces.IReceiver receiver)
                {
                    dropped.Add(key);
                    return;
                }
                if (delayed.invitations.InvitationKinds.TryCreate(sender, receiver, expire, out var invitation))
                {
                    delayed.invitations.InvitationHandler.Restore(invitation);
                }
            });
            foreach (var key in dropped)
            {
                Enqueue(DeleteInvitationSql,
                    ("@stype", key.Item1), ("@sguid", key.Item2), ("@rtype", key.Item3), ("@rguid", key.Item4));
            }
        }
    }
}
