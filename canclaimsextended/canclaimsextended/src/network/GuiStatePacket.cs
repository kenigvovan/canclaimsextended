using System.Collections.Generic;
using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Everything the player window shows, built on the server for one player. The window only
    /// reads it; its buttons run the usual chat commands, so every rule stays in the commands.
    /// With ImplicitFields.AllPublic protobuf-net numbers the fields by name, not by place, so a new
    /// field may go anywhere; client and server only work with the same build of the mod.
    /// The list types of the fields are in their own Gui*Entry files.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiStatePacket
    {
        // Command roots as registered (may carry an "x" prefix next to claims).
        public string CommandCity = "city";
        public string CommandPlot = "plot";
        public string CommandCitizen = "citizen";
        public string CommandAgree = "agree";

        // Player
        public string PlayerName = "";
        /// <summary>EnumPlayerPermissions names the player holds; buttons are shown by them.</summary>
        public List<string> Permissions = new();
        public List<string> Friends = new();
        /// <summary>Names of cities inviting this player.</summary>
        public List<string> InvitationsReceived = new();
        public string Balance = "";
        public bool HasEconomy;

        // City (empty CityName = no city)
        public string CityName = "";
        public bool IsMayor;
        public string MayorName = "";
        public List<string> Citizens = new();
        public long CreatedAt;
        public bool OpenCity;
        public string InvMsg = "";
        public int PlotsCount;
        public int MaxPlots;
        public long Volume;
        public long MaxVolume;
        public string CityBalance = "";
        public string Upkeep = "";
        public string Debt = "";
        /// <summary>Player names the city invited.</summary>
        public List<string> InvitationsSent = new();
        /// <summary>Rights of the city: group (stranger, citizen, friend) x type (use, build, attack), row by row.</summary>
        // Empty by default: protobuf-net appends a received array to the existing one instead of
        // replacing it, so a pre-filled default would hide what the server sent.
        public bool[] CityPerms = new bool[0];
        public bool CityPvp, CityFire, CityBlast, CityNoMobSpawn;

        // Plot the player stands in (empty PlotGuid = none)
        public string PlotGuid = "";
        public string PlotName = "";
        public string PlotCity = "";
        public string PlotOwner = "";
        public int PlotPrice = -1;
        /// <summary>Daily fee the plot's owner pays the city; 0 for none.</summary>
        public int PlotFee;
        public int MaxPlotFee;
        /// <summary>The player may change the plot fee, as /plot set fee allows.</summary>
        public bool CanSetPlotFee;
        public int PlotSizeX, PlotSizeY, PlotSizeZ;
        public long PlotVolume;
        public int PlotBoxes;
        public bool[] PlotPerms = new bool[0];
        public bool PlotPvp, PlotFire, PlotBlast, PlotNoMobSpawn;
        /// <summary>The player may change this plot's rights, flags and name.</summary>
        public bool CanManagePlot;

        // Ranks of the city
        public List<GuiRankEntry> Ranks = new();
        /// <summary>Permissions a rank may be given, for the rank editor.</summary>
        public List<string> RankablePermissions = new();

        // Plot type of the plot the player stands in, and the types that may be set
        public string PlotType = "";
        public bool CanSetPlotType;
        public List<GuiPlotTypeEntry> PlotTypes = new();

        // Citizen fee
        public int CityFee;
        public double MaxCityFee;

        // Summon points of the city
        public List<GuiSummonEntry> SummonPoints = new();
        public string SummonPayment = "";

        // Prison: the city's criminals and prisons, and the player's own sentence
        public List<string> Criminals = new();
        public List<GuiPrisonEntry> Prisons = new();
        public bool IsPrisoned;
        public long PrisonHoursLeft;
        public string PrisonRansom = "";

        // Admin
        public bool IsAdmin;
        public string CommandCadmin = "cadmin";
        public List<GuiWorldFlagEntry> WorldFlags = new();

        // Selection of the player (land tab). Lists default to empty: protobuf-net appends to them.
        public bool SelHasCorner1, SelHasCorner2;
        public int SelSizeX, SelSizeY, SelSizeZ;
        public long SelVolume;
        /// <summary>Price of making it a plot; empty without an economy or a city.</summary>
        public string SelPrice = "";
        public bool SelCanClaim;
        public bool SelCanExtend;
        /// <summary>The caller is an admin and /cadmin plot extend would take the selection.</summary>
        public bool SelCanAdminExtend;
        /// <summary>Lang key of why /cadmin plot extend would refuse the selection; empty when it would not, or not in a plot.</summary>
        public string SelAdminExtendProblem = "";
        public List<string> SelAdminExtendProblemArgs = new();
        /// <summary>Lang key of why it cannot be claimed; the client translates it.</summary>
        public string SelProblem = "";
        public List<string> SelProblemArgs = new();
        public bool SelAlwaysShow;
        /// <summary>Boxes of the plot the player stands in.</summary>
        public List<GuiBoxEntry> PlotParts = new();
        public bool CanClaimForCity;
        /// <summary>The plot under the player belongs to their city and they may give it up.</summary>
        public bool CanUnclaim;

        // Coat of arms of the player's city; the editor's rules come with EmblemsPacket.
        public string CityEmblem = "";
        public bool CanSetEmblem;

        // City log, newest first; message of the day
        public List<GuiLogEntry> CityLog = new();
        public string CityDailyMsg = "";

        // Plots groups of the city; the group of the plot under the player; groups inviting the player
        public List<GuiPlotsGroupEntry> PlotsGroups = new();
        public string PlotGroupName = "";
        public List<string> PlotsGroupInvitations = new();

        // Price settings, for the prices tab of everyone and the admin editor; plot types players may pick
        public List<GuiPriceSettingEntry> PriceSettings = new();
        public List<string> EnabledPlotTypes = new();

        // City levels, for the admin editor only
        public List<GuiCityLevelEntry> CityLevels = new();

        // Block rules (PLOT_TYPE_ONLY_BLOCKS), for the admin editor only; the block in the admin's
        // hand with the keys that would match it and the types it is already held to.
        public List<GuiBlockRuleEntry> BlockRules = new();

        // Every city, for the admin tab only.
        public List<GuiAdminCityEntry> AdminCities = new();
        /// <summary>The city an admin's window is built for as its mayor (/cadmin as); empty = their own.</summary>
        public string ActingCity = "";
        public string HandBlockCode = "";
        public List<string> HandBlockKeys = new();
        public List<string> HandBlockAllowed = new();

        // Summon tab. SummonPoints lists every summon plot, also those without a point yet.
        public bool SummonAllowed;
        public int SummonMaxPlots;
        public int SummonDelaySeconds;
        public long SummonCooldownLeft;
        public bool SummonWaiting;
        public int SummonMinPlayers;

        // Territory of the player's city: chunks under the limit, and bought over it.
        public int ChunksCount;
        public int MaxChunks;
        public int ExtraChunksCount;
        public int MaxExtraChunks;

        // The chunk the player stands in (land tab). HereCity empty = nobody's.
        public int HereChunkX, HereChunkZ;
        public int ChunkSize;
        public string HereCity = "";
        public bool HereIsOwnCity;
        public bool HereIsOutpost, HereIsExtra;
        /// <summary>What may be done with the chunk now, by the same checks the commands use.</summary>
        public bool ChunkCanClaim, ChunkCanClaimExtra, ChunkCanOutpost, ChunkCanUnclaim, ChunkCanFound;
        /// <summary>Lang key and arguments of why /city claim would fail; empty when it would pass.</summary>
        public string ChunkProblem = "";
        public List<string> ChunkProblemArgs = new();
        /// <summary>One-time prices, empty without an economy.</summary>
        public string ChunkPrice = "";
        public string OutpostPrice = "";
        public string ExtraChunkPrice = "";

        /// <summary>#n of the plot the player stands in, 0 for none.</summary>
        public int PlotNumber;
        /// <summary>Plots group of the chunk the player stands in, empty when none.</summary>
        public string HereChunkGroup = "";

        /// <summary>Plots of the player's city they may reshape, for extend and cut from afar; empty otherwise.</summary>
        public List<GuiPlotEntry> CityPlots = new();
    }
}
