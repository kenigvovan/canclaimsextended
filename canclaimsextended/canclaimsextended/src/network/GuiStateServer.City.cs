using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.cooldowns;
using canclaimsextended.src.delayed.teleportation;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;

namespace canclaimsextended.src.network
{
    /// <summary>The city tabs: city, ranks, identity, summon, prison, plots groups.</summary>
    public static partial class GuiStateServer
    {
        static void FillCity(GuiStatePacket state, City city, PlayerInfo playerInfo, Config cfg)
        {
            state.CityName = city.GetPartName();
            state.IsMayor = city.isMayor(playerInfo);
            state.MayorName = city.getMayor()?.GetPartName() ?? "";
            state.Citizens = city.getCityCitizens().Select(c => c.GetPartName()).ToList();
            state.CreatedAt = city.TimeStampCreated;
            state.OpenCity = city.openCity;
            state.InvMsg = city.invMsg;
            state.ChunksCount = city.RegularChunksCount;
            state.MaxChunks = Settings.GetMaxChunksForCity(city);
            state.ExtraChunksCount = city.ExtraChunksCount;
            state.MaxExtraChunks = Settings.GetMaxExtraChunksForCity(city);
            state.PlotsCount = city.getCityPlots().Count;
            state.MaxPlots = Settings.GetMaxNumberOfPlotForCity(city);
            state.Volume = city.GetTotalVolume();
            state.MaxVolume = Settings.GetMaxVolumeForCity(city);
            state.Upkeep = Money((decimal)city.GetDayPaymentAmount());
            state.Debt = Money((decimal)city.DebtBalance);
            if (state.HasEconomy)
            {
                state.CityBalance = Money(canclaimsextended.economyProvider.GetBalance(city.MoneyAccountName));
            }
            state.InvitationsSent = city.GetSentInvitations().Select(i => i.getReceiver().getNameReceiver()).ToList();

            PermsHandler perms = city.getPermsHandler();
            state.CityPerms = PermsRow(perms);
            state.CityPvp = perms.pvpFlag;
            state.CityFire = perms.fireFlag;
            state.CityBlast = perms.blastFlag;
            state.CityNoMobSpawn = perms.noMobSpawnFlag;

            FillCityRanks(state, city, cfg);
            FillCityIdentity(state, city, playerInfo, cfg);
            FillCitySummon(state, city, playerInfo, cfg);
            FillCityJustice(state, city, playerInfo, cfg);
        }

        static void FillCityRanks(GuiStatePacket state, City city, Config cfg)
        {
            foreach (var rank in city.CustomCityRanks.Values)
            {
                state.Ranks.Add(new GuiRankEntry
                {
                    Name = rank.Name,
                    Permissions = rank.Permissions.Select(p => p.ToString()).ToList(),
                    Holders = rank.CitizensNames.ToList()
                });
            }
            state.RankablePermissions = cfg.AVAILABLE_CITY_PERMISSIONS.Select(p => p.ToString()).ToList();
        }

        /// <summary>Fee, emblem, daily message and the event log.</summary>
        static void FillCityIdentity(GuiStatePacket state, City city, PlayerInfo playerInfo, Config cfg)
        {
            state.CityFee = city.fee;
            state.MaxCityFee = cfg.MAX_CITY_FEE;
            state.CityEmblem = city.Emblem ?? "";
            state.CanSetEmblem = ManageRules.IsMayorOr(playerInfo, city,
                EnumPlayerPermissions.CITY_SET_EMBLEM, EnumPlayerPermissions.CITY_SET_ALL);
            state.CityDailyMsg = city.DailyMsg ?? "";
            // Newest first, as the log page shows them; only to who may read /city log.
            if (!ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_SEE_LOG)) return;
            for (int i = city.EventLog.Count - 1; i >= 0; i--)
            {
                var entry = city.EventLog[i];
                state.CityLog.Add(new GuiLogEntry
                {
                    Timestamp = entry.Timestamp,
                    Event = entry.EventType.ToString(),
                    Args = new List<string>(entry.Args)
                });
            }
        }

        static void FillCitySummon(GuiStatePacket state, City city, PlayerInfo playerInfo, Config cfg)
        {
            // Set points first, in the order /city summon tp numbers them; then plots still without one.
            var summonPlots = city.getCityPlots().Where(p => p.Type == PlotType.SUMMON).ToList();
            foreach (var point in SummonRules.PointsOf(city))
            {
                state.SummonPoints.Add(new GuiSummonEntry
                {
                    Name = point.Desc.Name,
                    X = (int)point.Desc.Point.X,
                    Y = (int)point.Desc.Point.Y,
                    Z = (int)point.Desc.Point.Z,
                    IsSet = true,
                    PlotName = point.Plot.GetPartName()
                });
            }
            foreach (Plot plot in summonPlots.Where(p => p.Desc is not PlotDescSummon { Point: not null }))
            {
                BlockBox b = plot.Shape.Bounds;
                state.SummonPoints.Add(new GuiSummonEntry
                {
                    X = (b.MinX + b.MaxX) / 2,
                    Y = b.MinY,
                    Z = (b.MinZ + b.MaxZ) / 2,
                    PlotName = plot.GetPartName()
                });
            }
            state.SummonPayment = Money((decimal)cfg.SUMMON_PAYMENT);
            state.SummonAllowed = cfg.SUMMON_ALLOWED;
            state.SummonMaxPlots = Settings.GetCityLevelInfo(city.getCityCitizens().Count).SummonPoints;
            state.SummonDelaySeconds = cfg.SECONDS_SUMMON_TIME;
            state.SummonMinPlayers = cfg.SUMMON_MIN_PLAYERS;
            long cooldownEnd = CooldownHandler.GetEnd(playerInfo.Guid, CooldownType.SUMMON);
            state.SummonCooldownLeft = cooldownEnd == 0 ? 0 : System.Math.Max(0, cooldownEnd - TimeFunctions.GetEpochSeconds());
            state.SummonWaiting = TeleportationHandler.IsWaiting(playerInfo.Guid);
        }

        /// <summary>Criminals and prisons of the city.</summary>
        static void FillCityJustice(GuiStatePacket state, City city, PlayerInfo playerInfo, Config cfg)
        {
            // The same rights as /city criminal list.
            if (ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CRIMINAL_ALL, EnumPlayerPermissions.CITY_ADD_CRIMINAL,
                EnumPlayerPermissions.CITY_REMOVE_CRIMINAL, EnumPlayerPermissions.CITY_PRISON_LIST, EnumPlayerPermissions.CITY_PRISON_ALL))
            {
                state.Criminals = city.Criminals
                    .Select(uid => canclaimsextended.dataStorage.GetPlayerByUid(uid, out PlayerInfo p) ? p.GetPartName() : uid)
                    .ToList();
            }
            foreach (var prison in PrisonHandler.Prisons(city))
            {
                state.Prisons.Add(new GuiPrisonEntry
                {
                    Name = prison.Plot.GetPartName(),
                    Cells = prison.Desc.Cells.Count,
                    MaxCells = cfg.MAX_CELLS_PER_PRISON
                });
            }
        }

        /// <summary>Groups of the player's city, the group of the plot under them, and group invitations.</summary>
        static void FillPlotsGroups(GuiStatePacket state, City city, Plot plot, PlayerInfo playerInfo)
        {
            state.PlotGroupName = plot?.Group?.GetPartName() ?? "";
            state.PlotsGroupInvitations = playerInfo.getReceivedInvitations()
                .Where(i => i.getSender() is PlotsGroup)
                .Select(i => i.getSender().getNameSender())
                .ToList();
            if (city == null) return;
            long now = TimeFunctions.GetEpochSeconds();
            foreach (PlotsGroup group in city.PlotsGroups)
            {
                PermsHandler perms = group.Perms;
                state.PlotsGroups.Add(new GuiPlotsGroupEntry
                {
                    Name = group.GetPartName(),
                    Members = group.Members.Select(m => m.GetPartName()).ToList(),
                    Plots = group.Plots.Count(),
                    Chunks = group.Chunks.Count(),
                    PlotNumbers = group.Plots.Select(p => p.Number).OrderBy(n => n).ToList(),
                    ChunkCoords = group.Chunks.OrderBy(c => c.Pos.X).ThenBy(c => c.Pos.Z).Select(c => c.Pos.X + ", " + c.Pos.Z).ToList(),
                    Fee = Money((decimal)group.Fee),
                    HasPendingFee = group.HasPendingFee,
                    PendingFee = group.HasPendingFee ? Money((decimal)group.PendingFee) : "",
                    PendingHoursLeft = group.HasPendingFee ? (System.Math.Max(0, group.PendingFeeAt - now) + 3599) / 3600 : 0,
                    IAccepted = group.PendingFeeAccepted.Contains(playerInfo.Guid),
                    IsMember = group.Members.Contains(playerInfo),
                    Perms = (bool[])perms.CitizenPerms.Clone(),
                    Pvp = perms.pvpFlag,
                    Fire = perms.fireFlag,
                    Blast = perms.blastFlag,
                    NoMobSpawn = perms.noMobSpawnFlag
                });
            }
        }
    }
}
