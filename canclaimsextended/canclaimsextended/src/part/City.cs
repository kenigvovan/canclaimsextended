using System;
using System.Collections.Generic;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.interfaces;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.part
{
    public class City : Part, IGetStatus, ISender
    {
        HashSet<PlayerInfo> cityCitizens = new HashSet<PlayerInfo>();
        List<Invitation> listSentInvitations = new List<Invitation>();
        PermsHandler permsHandler = new PermsHandler();
        PlayerInfo mayor;
        List<Plot> cityPlots = new List<Plot>();
        public long TimeStampCreated { get; set; }
        public string invMsg { get; set; } = "";
        public bool openCity { get; set; } = false;
        bool isTechnical = false;
        int bonusPlots = 0;
        public int cityColor { get; set; } = -992222222;
        /// <summary>Coat of arms: ';'-separated texture layers, see EmblemHandler. Empty = none yet.</summary>
        public string Emblem { get; set; } = "";
        // Unpaid upkeep, in whole coins. Paid off first from the next payments.
        public double DebtBalance { get; set; }
        /// <summary>What each citizen but the mayor pays the treasury every day; 0 = nothing.</summary>
        public int fee { get; set; } = 0;
        public string MoneyAccountName => canclaimsextended.config.CITY_ACCOUNT_STRING_PREFIX + Guid;
        public double GetDayPaymentAmount() => economy.PlotPricing.CityDailyPayment(this);
        /// <summary>UIDs of players the city wants jailed: killed by a citizen, they respawn in its prison.</summary>
        public HashSet<string> Criminals { get; } = new HashSet<string>();
        /// <summary>Groups of players with their own rights on some plots of the city.</summary>
        public List<PlotsGroup> PlotsGroups { get; } = new List<PlotsGroup>();
        // Territory. Changed only through DataStorage.AddChunk / RemoveChunk, which keep the
        // world-wide chunk index in step.
        readonly Dictionary<ChunkPos, CityChunk> chunks = new Dictionary<ChunkPos, CityChunk>();
        int bonusChunks = 0;
        /// <summary>
        /// The city's land outside its plots, one object for the city's lifetime. Its rights and
        /// flags are the city's own (shared PermsHandler).
        /// </summary>
        public Plot Land { get; }

        public IReadOnlyCollection<CityChunk> Chunks => chunks.Values;
        public int ChunkCount => chunks.Count;
        public bool HasChunk(ChunkPos pos) => chunks.ContainsKey(pos);
        public bool TryGetChunk(ChunkPos pos, out CityChunk chunk) => chunks.TryGetValue(pos, out chunk);
        internal void AddChunkEntry(CityChunk chunk) => chunks[chunk.Pos] = chunk;
        internal bool RemoveChunkEntry(ChunkPos pos) => chunks.Remove(pos);

        /// <summary>Chunks bought over the chunk limit; see CityChunk.ExtraBought.</summary>
        public int ExtraChunksCount
        {
            get
            {
                int count = 0;
                foreach (CityChunk chunk in chunks.Values)
                {
                    if (chunk.ExtraBought) count++;
                }
                return count;
            }
        }
        /// <summary>Chunks that count against the level's chunk limit.</summary>
        public int RegularChunksCount => chunks.Count - ExtraChunksCount;
        // Ranks the mayor made: name -> the rights it grants and who holds it.
        public Dictionary<string, CustomCityRank> CustomCityRanks { get; set; } = new();
        /// <summary>Shown to citizens when they log in; empty = none.</summary>
        public string DailyMsg { get; set; } = "";
        /// <summary>History, oldest first, at most CITY_LOG_MAX_ENTRIES. Written through CityLog.Add.</summary>
        public List<citylog.CityLogEntry> EventLog { get; } = new();

        public void AddLogEntry(citylog.CityLogEntry entry)
        {
            EventLog.Add(entry);
            int max = Math.Max(1, canclaimsextended.config.CITY_LOG_MAX_ENTRIES);
            if (EventLog.Count > max)
            {
                EventLog.RemoveRange(0, EventLog.Count - max);
            }
        }

        public bool HasCityRank(string rankName)
        {
            return CustomCityRanks.ContainsKey(rankName);
        }
        public bool AddNewCityRank(string rankName, CustomCityRank rank)
        {
            return CustomCityRanks.TryAdd(rankName, rank);
        }
        public bool RemoveCityRank(string rankName)
        {
            return CustomCityRanks.Remove(rankName);
        }
        public bool GrantPlayerRank(string rankName, PlayerInfo playerInfo)
        {
            return CustomCityRanks.TryGetValue(rankName, out var rank) && rank.CitizensNames.Add(playerInfo.GetPartName());
        }
        public bool RevokePlayerRank(string rankName, PlayerInfo playerInfo)
        {
            return CustomCityRanks.TryGetValue(rankName, out var rank) && rank.CitizensNames.Remove(playerInfo.GetPartName());
        }

        public City(string valName, string guid, bool isTechnical = false) : base(valName, guid)
        {
            this.isTechnical = isTechnical;
            Land = new Plot(this, permsHandler);
        }

        public static event Action<City> CityCreated;
        public static event Action<City> CityDestroyed;
        public static void FireCityCreated(City city) => CityCreated?.Invoke(city);
        public static void FireCityDestroyed(City city) => CityDestroyed?.Invoke(city);

        public int getBonusPlots()
        {
            return bonusPlots;
        }
        public void setBonusPlots(int val)
        {
            this.bonusPlots = val;
        }
        public int getBonusChunks()
        {
            return bonusChunks;
        }
        public void setBonusChunks(int val)
        {
            this.bonusChunks = val;
        }
        public bool isTechnicalCity()
        {
            return isTechnical;
        }
        public void setIsTechnicalCity(bool val)
        {
            this.isTechnical = val;
        }
        public bool isCitizen(PlayerInfo playerInfo)
        {
            return cityCitizens.Contains(playerInfo);
        }
        /// <summary>The mayor, or an admin acting for the city in the command running now (AdminActing).</summary>
        public bool isMayor(PlayerInfo playerInfo)
        {
            return (mayor != null && mayor.Equals(playerInfo)) || AdminActing.IsActingFor(playerInfo, this);
        }
        public List<IServerPlayer> getOnlineCitizens()
        {
            List<IServerPlayer> outList = new List<IServerPlayer>();
            foreach (var it in cityCitizens)
            {
                if (canclaimsextended.sapi.World.PlayerByUid(it.Guid) is IServerPlayer player
                    && player.ConnectionState == EnumClientState.Playing)
                {
                    outList.Add(player);
                }
            }
            return outList;
        }
        public List<Plot> getCityPlots()
        {
            return cityPlots;
        }
        /// <summary>One above the highest number the city's plots hold, so a number is never handed out twice at once.</summary>
        public int NextPlotNumber()
        {
            int max = 0;
            foreach (Plot plot in cityPlots)
            {
                if (plot.Number > max) max = plot.Number;
            }
            return max + 1;
        }
        public long GetTotalVolume()
        {
            long sum = 0;
            foreach (Plot plot in cityPlots)
            {
                sum += plot.Volume;
            }
            return sum;
        }
        public HashSet<PlayerInfo> getCityCitizens()
        {
            return cityCitizens;
        }
        public PlayerInfo getMayor()
        {
            return mayor;
        }
        public void setMayor(PlayerInfo player)
        {
            this.mayor = player;
        }
        public PermsHandler getPermsHandler()
        {
            return permsHandler;
        }
        public override bool saveToDatabase(bool update = true)
        {
            return canclaimsextended.Db.saveCity(this, update);
        }

        // ISender: invitations this city sent to players.
        public List<Invitation> GetSentInvitations() => listSentInvitations;
        public void deleteSentInvitation(Invitation invitation) => listSentInvitations.Remove(invitation);
        public void addSentInvitation(Invitation invitation) => listSentInvitations.Add(invitation);
        public int getMaxSentInvitations() => canclaimsextended.config.MAX_SENT_INVITATIONS_CITY;
        public string getNameSender() => GetPartName();

        public List<string> getStatus(PlayerInfo forPlayer = null)
        {
            List<string> outStrings = new List<string>
            {
                "[" + getPartNameReplaceUnder() + "]" + (openCity ? Lang.Get("canclaimsextended:open_city") : "") + "\n"
            };
            if (invMsg.Length != 0)
            {
                outStrings.Add(Lang.Get("canclaimsextended:invite_msg_city", invMsg) + "\n");
            }
            if (mayor != null)
            {
                outStrings.Add(Lang.Get("canclaimsextended:mayor") + mayor.getPartNameReplaceUnder() + "\n");
            }
            outStrings.Add(Lang.Get("canclaimsextended:city_chunks_status",
                RegularChunksCount, Settings.GetMaxChunksForCity(this), ExtraChunksCount) + "\n");
            outStrings.Add(Lang.Get("canclaimsextended:city_claimed_amount_status",
                cityPlots.Count, Settings.GetMaxNumberOfPlotForCity(this),
                GetTotalVolume(), Settings.GetMaxVolumeForCity(this)) + "\n");
            if (canclaimsextended.economyProvider.SupportsPlayerWallet && forPlayer != null && isCitizen(forPlayer))
            {
                outStrings.Add(Lang.Get("canclaimsextended:city_balance_status", canclaimsextended.economyProvider.GetBalance(MoneyAccountName)) + "\n");
            }
            outStrings.Add(Lang.Get("canclaimsextended:city_upkeep_status", Math.Floor(GetDayPaymentAmount())) + "\n");
            if (DebtBalance > 0)
            {
                outStrings.Add(Lang.Get("canclaimsextended:city_debt_status", DebtBalance, canclaimsextended.config.CITY_MAX_DEBT) + "\n");
            }
            if (fee > 0)
            {
                outStrings.Add(Lang.Get("canclaimsextended:city_fee_status", fee) + "\n");
            }
            outStrings.Add(Lang.Get("canclaimsextended:created") + TimeFunctions.GetDateFromEpochSeconds(TimeStampCreated) + "\n");
            outStrings.Add(Lang.Get("canclaimsextended:citizens") + StringFunctions.MakeStringPlayersName(cityCitizens, ", ") + "\n");
            outStrings.Add(permsHandler.getStringForChat() + "\n");
            return outStrings;
        }

        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            return obj is City other && Guid.Equals(other.Guid);
        }
    }
}
