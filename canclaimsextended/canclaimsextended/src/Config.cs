using System;
using System.Collections.Generic;
using System.IO;
using canclaimsextended.src.rights;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src
{
    /// <summary>
    /// canclaimsextended.json. Collections are marked Replace: by default Newtonsoft adds the file's
    /// items to the defaults, so an item could never be removed from a list.
    /// </summary>
    public class Config
    {
        public const string FILE_NAME = "canclaimsextended.json";

        //Format of this file; Validate brings an older one up to date. Not for hand editing.
        public int CONFIG_VERSION = 0;
        const int CURRENT_CONFIG_VERSION = 2;

        //FILES
        //Folder for the database and the json files next to it; empty = the game's ModConfig folder.
        public string PATH_TO_DB_AND_JSON_FILES = "";
        public string DB_NAME = "canclaimsextended.db";
        public string PERMS_FILE_NAME = "canclaimsextended_permissions.json";

        //COMMANDS. Root names; when another mod (claims) already has one, "x" is put in front of it.
        public string COMMAND_CITY = "city";
        public string COMMAND_PLOT = "plot";
        public string COMMAND_CITIZEN = "citizen";
        public string COMMAND_AGREE = "agree";
        public string COMMAND_CADMIN = "cadmin";
        public string COMMAND_CITY_CHAT = "cc";
        public string COMMAND_GENERAL_CHAT = "gc";

        //ACCESS
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> ROLE_CODES_WITH_ADMIN_RIGHTS = new HashSet<string> { "admin" };

        //CITY TERRITORY. A city holds columns of CLAIM_CHUNK_SIZE x CLAIM_CHUNK_SIZE blocks over the
        //whole world height; 16 or 32. Changing it needs an empty database.
        public int CLAIM_CHUNK_SIZE = 16;
        //Free chunks that must stay between the territories of two cities.
        public int MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = 3;

        //PLOT GEOMETRY, in blocks. Plots are parts of the territory handed out inside it; a box is
        //taken exactly as selected.
        //Smallest side of a box on any axis.
        public int PLOT_MIN_DIM = 4;
        public long PLOT_MAX_VOLUME = 64 * 64 * 64;
        public int PLOT_MAX_HEIGHT = 128;
        //Most boxes one plot may be made of (/plot extend).
        public int PLOT_MAX_BOXES = 8;
        //Whether a box added by /plot extend has to share a face with the plot.
        public bool PLOT_PARTS_MUST_TOUCH = false;

        //SELECTION AND HIGHLIGHT
        //Longest side of a selection, so a typo in grow cannot select half the world.
        public int SELECTION_MAX_DIM = 256;
        //How far around a player holding the claim wand other plots are shown.
        public int SELECTION_VIEW_RADIUS = 64;
        //Colors as alpha, red, green, blue.
        public int[] SELECTION_COLOR_OK = new int[] { 80, 40, 220, 40 };
        public int[] SELECTION_COLOR_BAD = new int[] { 80, 230, 40, 40 };
        public int[] SELECTION_COLOR_CORNER = new int[] { 80, 255, 220, 0 };
        //Plots are drawn in a shade of their city's color; of these two only the alpha is used,
        //and the whole color for a plot without a city.
        public int[] PLOT_COLOR_OWN_CITY = new int[] { 50, 40, 120, 255 };
        public int[] PLOT_COLOR_OTHER_CITY = new int[] { 50, 200, 60, 200 };
        //The plot the player stands in, every part of it, so the parts of one plot read together.
        public int[] PLOT_COLOR_CURRENT = new int[] { 110, 90, 220, 255 };
        //Edges of city territory are drawn by the client, see client/ClientSettings.

        //PROTECTION PATCHES
        public bool FIRE_PROTECTION_PATCH = true;
        public bool BLAST_PROTECTION_PATCH = true;
        public bool WATER_FLOW_CITY_PLOTS_PATCH = true;
        //Sand and gravel: a plot's loose blocks fall only within its city, blocks falling in from outside land as items.
        public bool FALLING_BLOCKS_PROTECTION_PATCH = true;
        //Animals that only players with the attack right may hurt on a plot. "pig-*" matches by path, "game:pig-*" by full code.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> PROTECTED_MOB_TYPES = new HashSet<string> { "pig-*", "sheep-*", "chicken-*" };

        //ECONOMY. Prices count a plot in units of BLOCKS_PER_COST_UNIT blocks, rounded up.
        public int BLOCKS_PER_COST_UNIT = 1024;
        public double PLOT_CLAIM_PRICE_PER_UNIT = 1.25;
        public double NEW_CITY_COST = 25;
        //One chunk of territory: bought once, then kept every day.
        public double CHUNK_CLAIM_PRICE = 10;
        public double CHUNK_DAILY_UPKEEP = 0.5;
        public double CITY_BASE_CARE = 2;
        public double CITY_MAX_DEBT = 1000;
        //Daily upkeep of one unit; every plot type uses it for now.
        public double DEFAULT_PLOT_COST = 0.25;
        //Flat daily charge per plot with pvp off / with nomobspawn on.
        public double PLOT_NO_PVP_FLAG_COST = 0;
        public double PLOT_NO_MOBSPAWN_FLAG_COST = 0;
        //Its own prefix, so a city account never collides with one of claims in the same economy.
        public string CITY_ACCOUNT_STRING_PREFIX = "#cxcity_";

        //Name of the city every player joins on their very first login; empty = none.
        public string NEW_PLAYERS_DEFAULT_CITY = "";

        //DAY. Real seconds; HOUR_NEW_DAY_START shifts the day grid from the Unix epoch.
        public int HOUR_NEW_DAY_START = 43200;
        public long MOD_DAY_DURATION_IN_SECONDS = 86400;
        public int DAY_MAX_MISSED_CYCLES_TO_PROCESS = 1;

        //CONFIRMATIONS
        public int AGREEMENT_TIMEOUT_SECONDS = 120;

        //CITIZENS
        public int INVITATION_TIMEOUT_HOURS = 2;
        public int MAX_SENT_INVITATIONS_CITY = 20;
        public int MAX_RECEIVED_INVITATIONS_PLAYER = 10;
        public int MAX_LENGTH_CITY_NAME = 40;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> BLOCKED_NAMES = new HashSet<string>();

        //ALWAYS ACCESS. Block classes of other mods anyone may use on a plot ("modid:ClassName").
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> ALWAYS_ACCESS_BLOCKS = new HashSet<string> { "canmailbox:CANBlockGenericTypedContainer",
            "canmarket:BlockCANMarket", "canmarket:BlockCANMarketSingle", "canmarket:BlockCANStall",
            "vinconomy:BlockVLiquidContainer", "vinconomy:BlockVDualLiquidContainer", "vinconomy:BlockVContainer",
            "vinconomy:BlockVPurchaseContainer", "vinconomy:BlockVGacha", "vinconomy:BlockVClothingDisplay" };

        //CITY IDENTITY
        public double CITY_NAME_CHANGE_COST = 20;
        public int CITY_LOG_MAX_ENTRIES = 100;
        //Titles set by the mayor, shown around the name in chat.
        public int MAX_CITIZEN_TITLE_LENGTH = 16;
        public string PREFIX_COLOR_PLAYER = "#00FFFF";
        public string POSTFIX_COLOR_PLAYER = "#1F920E";

        //PERSONAL PLOTS. Daily fee an owner pays the city, and the share of the claim price returned
        //when land is given up (only for land older than the minimum age).
        public double MAX_PLOT_FEE = 50;
        public double PLOT_UNCLAIM_REFUND_PERCENT = 0;
        public long PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS = 300;

        //OUTPOSTS AND EXTRA CHUNKS. Distances in chunks from the nearest chunk of the city. Both
        //costs are added to CHUNK_CLAIM_PRICE.
        public double OUTPOST_CHUNK_COST = 150;
        public int MIN_OUTPOST_DISTANCE_CHUNKS = 4;
        public int MAX_OUTPOST_DISTANCE_CHUNKS = 125;
        public double EXTRA_CHUNK_COST = 30;

        //PLOTS GROUPS. A raised fee waits this long for members to agree.
        public double MAX_PLOTSGROUP_FEE = 50;
        public int PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 24;
        public int MAX_PLOTS_GROUP_PER_CITY = 5;

        //EMBLEM. Empty lists offer EmblemHandler's defaults; a non-empty list replaces them.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> EMBLEM_AVAILABLE_PATTERNS = new HashSet<string>();
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> EMBLEM_AVAILABLE_COLORS = new HashSet<string>();
        //Layers per emblem.
        public int EMBLEM_MAX_LAYERS = 6;

        //BOATS. A fellow citizen of a boat's owner counts as its owner.
        public bool BOAT_SHARE_WITH_CITY = true;

        //PLOT TYPES. Codes: default, farm, orchard, temple, summon, prison, shop.
        //Daily upkeep of one unit of volume by type; a missing type costs DEFAULT_PLOT_COST.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, double> PLOT_TYPE_UNIT_COSTS = new Dictionary<string, double>
        {
            { "default", 0.25 }, { "farm", 0.4 }, { "orchard", 0.4 }, { "temple", 0.5 },
            { "summon", 0.5 }, { "prison", 0.5 }, { "shop", 0.4 }
        };
        //One-time price of setting a plot to a type, paid by the city.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, double> PLOT_TYPE_SET_COSTS = new Dictionary<string, double>
        {
            { "default", 0 }, { "farm", 6 }, { "orchard", 6 }, { "temple", 5 },
            { "summon", 7 }, { "prison", 3 }, { "shop", 6 }
        };
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> DISABLED_PLOT_TYPES = new HashSet<string>();
        //Blocks that may be placed only on plots of the listed types (creative is not stopped). Key: a
        //block code with * wildcards, or "be:<block entity behaviour>" for every block with it, e.g.
        //{ "be:Vinconomy.Register": ["shop"] } for the vinconomy registers. Easiest to set with the
        //cadmin blockrule command or the admin "Block rules" tab of the window.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, List<string>> PLOT_TYPE_ONLY_BLOCKS = new Dictionary<string, List<string>>();
        public bool CROPS_ONLY_ON_FARM_PLOTS = false;
        public bool FRUIT_ONLY_ON_ORCHARD_PLOTS = false;
        public bool ORCHARD_WARN_OUTSIDE_CITY = true;

        //RESPAWN. Off = temples respawn nobody and the temple type is gone.
        public bool TEMPLE_RESPAWN_ENABLED = true;
        public bool TEMPLE_RESPAWN_CHOICE_ENABLED = true;

        //SUMMON. Citizens teleport to a point on a summon plot of their city.
        public bool SUMMON_ALLOWED = true;
        public double SUMMON_PAYMENT = 5;
        public int SECONDS_SUMMON_TIME = 10;
        public int SECONDS_SUMMON_COOLDOWN = 10;
        //Players needed near the point for it to work; 0 = no condition.
        public int SUMMON_MIN_PLAYERS = 0;
        public int SUMMON_HOR_RANGE = 10;
        public int SUMMON_VER_RANGE = 10;
        public bool PLAYER_MOVEMENT_CANCEL_TELEPORTATION = true;

        //PRISON
        public int PRISON_SENTENCE_HOURS = 3;
        public int MAX_CELLS_PER_PRISON = 4;
        //A command name or the first word of its arguments: "summon" blocks /summon and /city summon.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> BLOCKED_COMMANDS_PRISON = new HashSet<string> { "summon" };
        public double RANSOM_FOR_NO_CITIZEN = 2;
        public double RANSOM_FOR_CITIZEN = 10;
        public double RANSOM_FOR_MAYOR = 20;

        //PVP BY TIME. Inside the window (game hours) players may fight everywhere, as in claims.
        public bool PVP_DURING_PART_OF_THE_DAY = false;
        public float PVP_TIME_START = 19;
        public float PVP_TIME_END = 6;

        //CITY FEE, taken from every citizen but the mayor each day (real economy only).
        public double MAX_CITY_FEE = 50;
        public bool DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE = true;

        //CHAT. Tags in front of names in the general chat.
        public bool SHOW_CITY_NAME_IN_CHAT = true;
        public string CITY_COLOR_NAME = "#755985";

        //RANKS. Rights a city rank may carry (/city rank addperm). Written by name; old names and
        //numbers are still read.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        [JsonConverter(typeof(PermissionSetConverter))]
        public HashSet<EnumPlayerPermissions> AVAILABLE_CITY_PERMISSIONS = WithRankableSinceVersion2(new HashSet<EnumPlayerPermissions>
        {
            EnumPlayerPermissions.CITY_CLAIM_CHUNK, EnumPlayerPermissions.CITY_UNCLAIM_CHUNK,
            EnumPlayerPermissions.CITY_CREATE_PLOT, EnumPlayerPermissions.CITY_DELETE_PLOT,
            EnumPlayerPermissions.CITY_INVITE, EnumPlayerPermissions.CITY_UNINVITE,
            EnumPlayerPermissions.CITY_KICK, EnumPlayerPermissions.SHOW_INVITES_SENT,
            EnumPlayerPermissions.CITY_SET_OPEN_STATE, EnumPlayerPermissions.CITY_SET_INV_MSG,
            EnumPlayerPermissions.CITY_SET_PVP, EnumPlayerPermissions.CITY_SET_FIRE,
            EnumPlayerPermissions.CITY_SET_BLAST, EnumPlayerPermissions.CITY_SET_MOBSPAWN,
            EnumPlayerPermissions.CITY_SET_PLOT_ACCESS_PERMISSIONS, EnumPlayerPermissions.PLOT_SET_ALL_CITY_PLOTS,
            EnumPlayerPermissions.CITY_WITHDRAW_MONEY, EnumPlayerPermissions.CITY_SEE_CITY_RANKS,
            EnumPlayerPermissions.CITY_SET_RANK, EnumPlayerPermissions.CITY_REMOVE_RANK,
            EnumPlayerPermissions.CITY_SET_GLOBAL_FEE, EnumPlayerPermissions.CITY_SET_SUMMON,
            EnumPlayerPermissions.CITY_ADD_CRIMINAL, EnumPlayerPermissions.CITY_REMOVE_CRIMINAL,
            EnumPlayerPermissions.CITY_PRISON_LIST, EnumPlayerPermissions.CITY_SET_EMBLEM,
            EnumPlayerPermissions.CITY_SET_NAME, EnumPlayerPermissions.CITY_SET_PLOTS_COLOR,
            EnumPlayerPermissions.CITY_SET_DAILY_MSG, EnumPlayerPermissions.CITY_SET_OTHERS_PREFIX,
            EnumPlayerPermissions.PLOT_SET_FEE
        });

        static HashSet<EnumPlayerPermissions> WithRankableSinceVersion2(HashSet<EnumPlayerPermissions> set)
        {
            set.UnionWith(RankableSinceVersion2);
            return set;
        }

        /// <summary>The rights config version 2 made rankable; also added to files written before it.</summary>
        static readonly EnumPlayerPermissions[] RankableSinceVersion2 =
        {
            EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK, EnumPlayerPermissions.CITY_BUY_OUTPOST,
            EnumPlayerPermissions.CITY_SET_ALL,
            EnumPlayerPermissions.CITY_CREATE_CITY_RANK, EnumPlayerPermissions.CITY_DELETE_CITY_RANK,
            EnumPlayerPermissions.CITY_ADD_PERMISSION_TO_RANK, EnumPlayerPermissions.CITY_REMOVE_PERMISSION_FROM_RANK,
            EnumPlayerPermissions.CITY_CRIMINAL_ALL, EnumPlayerPermissions.CITY_PRISON_ALL,
            EnumPlayerPermissions.CITY_PRISON_ADD_CELL, EnumPlayerPermissions.CITY_PRISON_REMOVE_CELL,
            EnumPlayerPermissions.CITY_PLOTSGROUP_CREATE, EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE,
            EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLOT, EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE_PLOT,
            EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLAYER, EnumPlayerPermissions.CITY_PLOTSGROUP_KICK_PLAYER,
            EnumPlayerPermissions.CITY_PLOTSGROUP_SET, EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FEE,
            EnumPlayerPermissions.CITY_PLOTSGROUP_SET_PVP, EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FIRE,
            EnumPlayerPermissions.CITY_PLOTSGROUP_SET_BLAST, EnumPlayerPermissions.CITY_PLOTSGROUP_SET_MOBSPAWN,
            EnumPlayerPermissions.CITY_SEE_LOG
        };

        //MOVEMENT. A chat line when a player walks into or out of a city. Off by default: the client
        //shows a banner on screen instead (client.LocationBanner).
        public bool MOVEMENT_MESSAGES = false;
        public int MOVEMENT_CHECK_MS = 500;

        //BACKUPS. Copies of the database at these server clock times ("HH:mm") and when the server
        //starts. Only the newest BACKUP_KEEP automatic copies stay; /cadmin backup copies are never
        //deleted. Empty BACKUP_FOLDER = "canclaimsextended_backups" next to the database.
        public List<string> BACKUP_TIMES = new List<string> { "06:00", "18:00" };
        public bool BACKUP_ON_START = true;
        public int BACKUP_KEEP = 14;
        public string BACKUP_FOLDER = "";

        /// <summary>Folder of the database and the json files, created when missing.</summary>
        [JsonIgnore]
        public string FilesFolder
        {
            get
            {
                string folder = PATH_TO_DB_AND_JSON_FILES.Length == 0 ? GamePaths.ModConfig : PATH_TO_DB_AND_JSON_FILES;
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>Folder of the database copies, created when missing.</summary>
        [JsonIgnore]
        public string BackupFolder
        {
            get
            {
                string folder = BACKUP_FOLDER.Length == 0 ? Path.Combine(FilesFolder, "canclaimsextended_backups") : BACKUP_FOLDER;
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>Full path of a file in <see cref="FilesFolder"/>.</summary>
        public string FilePath(string fileName) => Path.Combine(FilesFolder, fileName);

        public static void LoadConfig(ICoreAPI api)
        {
            try
            {
                canclaimsextended.config = api.LoadModConfig<Config>(FILE_NAME) ?? new Config();
                Validate(api);
                api.StoreModConfig(canclaimsextended.config, FILE_NAME);
            }
            catch (Exception e)
            {
                // Not written back: storing defaults here would wipe a file that only has a typo in it.
                api.Logger.Error("[canclaimsextended] " + FILE_NAME + " could not be read, defaults are used until it is fixed: " + e.Message);
                canclaimsextended.config ??= new Config();
                Validate(api);
            }
        }

        /// <summary>Brings values that would break the mod back into range; a json null becomes the default.</summary>
        private static void Validate(ICoreAPI api)
        {
            Config c = canclaimsextended.config;
            Config defaults = new Config();

            c.PATH_TO_DB_AND_JSON_FILES ??= "";
            if (string.IsNullOrWhiteSpace(c.DB_NAME)) c.DB_NAME = defaults.DB_NAME;
            if (string.IsNullOrWhiteSpace(c.PERMS_FILE_NAME)) c.PERMS_FILE_NAME = defaults.PERMS_FILE_NAME;
            if (string.IsNullOrWhiteSpace(c.COMMAND_CITY)) c.COMMAND_CITY = defaults.COMMAND_CITY;
            if (string.IsNullOrWhiteSpace(c.COMMAND_PLOT)) c.COMMAND_PLOT = defaults.COMMAND_PLOT;
            if (string.IsNullOrWhiteSpace(c.COMMAND_CITIZEN)) c.COMMAND_CITIZEN = defaults.COMMAND_CITIZEN;
            if (string.IsNullOrWhiteSpace(c.COMMAND_AGREE)) c.COMMAND_AGREE = defaults.COMMAND_AGREE;
            if (string.IsNullOrWhiteSpace(c.COMMAND_CADMIN)) c.COMMAND_CADMIN = defaults.COMMAND_CADMIN;
            if (string.IsNullOrWhiteSpace(c.COMMAND_CITY_CHAT)) c.COMMAND_CITY_CHAT = defaults.COMMAND_CITY_CHAT;
            if (string.IsNullOrWhiteSpace(c.COMMAND_GENERAL_CHAT)) c.COMMAND_GENERAL_CHAT = defaults.COMMAND_GENERAL_CHAT;
            c.PLOT_TYPE_UNIT_COSTS ??= defaults.PLOT_TYPE_UNIT_COSTS;
            c.PLOT_TYPE_SET_COSTS ??= defaults.PLOT_TYPE_SET_COSTS;
            // The files replace these lists whole, so a type added in a later version (shop) would
            // be free in an older config; its default prices are filled in, the host's stay.
            foreach (var price in defaults.PLOT_TYPE_UNIT_COSTS) c.PLOT_TYPE_UNIT_COSTS.TryAdd(price.Key, price.Value);
            foreach (var price in defaults.PLOT_TYPE_SET_COSTS) c.PLOT_TYPE_SET_COSTS.TryAdd(price.Key, price.Value);
            c.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
            c.NEW_PLAYERS_DEFAULT_CITY ??= "";
            c.DISABLED_PLOT_TYPES ??= defaults.DISABLED_PLOT_TYPES;
            c.BLOCKED_COMMANDS_PRISON ??= defaults.BLOCKED_COMMANDS_PRISON;
            c.ALWAYS_ACCESS_BLOCKS ??= defaults.ALWAYS_ACCESS_BLOCKS;
            c.EMBLEM_AVAILABLE_PATTERNS ??= defaults.EMBLEM_AVAILABLE_PATTERNS;
            c.EMBLEM_AVAILABLE_COLORS ??= defaults.EMBLEM_AVAILABLE_COLORS;
            if (c.EMBLEM_MAX_LAYERS < 1) c.EMBLEM_MAX_LAYERS = 1;
            if (c.CITY_LOG_MAX_ENTRIES < 1) c.CITY_LOG_MAX_ENTRIES = 1;
            if (c.MIN_OUTPOST_DISTANCE_CHUNKS < 1) c.MIN_OUTPOST_DISTANCE_CHUNKS = 1;
            if (c.MAX_OUTPOST_DISTANCE_CHUNKS < c.MIN_OUTPOST_DISTANCE_CHUNKS) c.MAX_OUTPOST_DISTANCE_CHUNKS = c.MIN_OUTPOST_DISTANCE_CHUNKS;
            if (c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY < 0) c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = 0;
            if (c.CLAIM_CHUNK_SIZE != 16 && c.CLAIM_CHUNK_SIZE != 32)
            {
                api.Logger.Error("[canclaimsextended] CLAIM_CHUNK_SIZE must be 16 or 32, using 16.");
                c.CLAIM_CHUNK_SIZE = 16;
            }
            if (c.PLOTSGROUP_FEE_RAISE_DELAY_HOURS < 0) c.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 0;
            // Above 100% a claim and unclaim round would pay out more than was paid in.
            c.PLOT_UNCLAIM_REFUND_PERCENT = Math.Clamp(c.PLOT_UNCLAIM_REFUND_PERCENT, 0, 100);
            if (c.PRISON_SENTENCE_HOURS < 1) c.PRISON_SENTENCE_HOURS = 1;
            if (c.MAX_CELLS_PER_PRISON < 1) c.MAX_CELLS_PER_PRISON = 1;
            c.CITY_ACCOUNT_STRING_PREFIX ??= defaults.CITY_ACCOUNT_STRING_PREFIX;

            c.ROLE_CODES_WITH_ADMIN_RIGHTS ??= new HashSet<string>();
            c.PROTECTED_MOB_TYPES ??= new HashSet<string>();
            c.BLOCKED_NAMES ??= new HashSet<string>();
            c.AVAILABLE_CITY_PERMISSIONS ??= new HashSet<EnumPlayerPermissions>();
            if (c.CONFIG_VERSION < 1)
            {
                // Claiming land was split into chunks and plots: whoever could hand out the old
                // right (same number as the chunk one now) may hand out the plot half as well.
                if (c.AVAILABLE_CITY_PERMISSIONS.Contains(EnumPlayerPermissions.CITY_CLAIM_CHUNK))
                    c.AVAILABLE_CITY_PERMISSIONS.Add(EnumPlayerPermissions.CITY_CREATE_PLOT);
                if (c.AVAILABLE_CITY_PERMISSIONS.Contains(EnumPlayerPermissions.CITY_UNCLAIM_CHUNK))
                    c.AVAILABLE_CITY_PERMISSIONS.Add(EnumPlayerPermissions.CITY_DELETE_PLOT);
            }
            if (c.CONFIG_VERSION < 2)
            {
                // Ranks only pass on rights their editor holds, so a longer list gives nobody more
                // than the mayor already decided to give.
                c.AVAILABLE_CITY_PERMISSIONS.UnionWith(RankableSinceVersion2);
            }
            c.CONFIG_VERSION = CURRENT_CONFIG_VERSION;

            if (c.PLOT_MIN_DIM < 1) c.PLOT_MIN_DIM = 1;
            if (c.PLOT_MAX_HEIGHT < c.PLOT_MIN_DIM) c.PLOT_MAX_HEIGHT = c.PLOT_MIN_DIM;
            if (c.PLOT_MAX_VOLUME < 1) c.PLOT_MAX_VOLUME = 1;
            if (c.PLOT_MAX_BOXES < 1) c.PLOT_MAX_BOXES = 1;
            if (c.SELECTION_MAX_DIM < c.PLOT_MIN_DIM) c.SELECTION_MAX_DIM = c.PLOT_MIN_DIM;
            if (c.SELECTION_VIEW_RADIUS < 0) c.SELECTION_VIEW_RADIUS = 0;
            // Prices divide by it.
            if (c.BLOCKS_PER_COST_UNIT < 1) c.BLOCKS_PER_COST_UNIT = 1;
            if (c.DAY_MAX_MISSED_CYCLES_TO_PROCESS < 0) c.DAY_MAX_MISSED_CYCLES_TO_PROCESS = 0;
            if (c.AGREEMENT_TIMEOUT_SECONDS < 1) c.AGREEMENT_TIMEOUT_SECONDS = 1;
            if (c.INVITATION_TIMEOUT_HOURS < 1) c.INVITATION_TIMEOUT_HOURS = 1;
            if (c.MAX_LENGTH_CITY_NAME < 1) c.MAX_LENGTH_CITY_NAME = 1;
            // A tick listener with 0 ms would run every tick.
            if (c.MOVEMENT_CHECK_MS < 50) c.MOVEMENT_CHECK_MS = 50;
            c.BACKUP_TIMES ??= new List<string>();
            c.BACKUP_FOLDER ??= "";
            if (c.BACKUP_KEEP < 1) c.BACKUP_KEEP = 1;

            c.SELECTION_COLOR_OK = ColorOrDefault(api, "SELECTION_COLOR_OK", c.SELECTION_COLOR_OK, defaults.SELECTION_COLOR_OK);
            c.SELECTION_COLOR_BAD = ColorOrDefault(api, "SELECTION_COLOR_BAD", c.SELECTION_COLOR_BAD, defaults.SELECTION_COLOR_BAD);
            c.SELECTION_COLOR_CORNER = ColorOrDefault(api, "SELECTION_COLOR_CORNER", c.SELECTION_COLOR_CORNER, defaults.SELECTION_COLOR_CORNER);
            c.PLOT_COLOR_OWN_CITY = ColorOrDefault(api, "PLOT_COLOR_OWN_CITY", c.PLOT_COLOR_OWN_CITY, defaults.PLOT_COLOR_OWN_CITY);
            c.PLOT_COLOR_OTHER_CITY = ColorOrDefault(api, "PLOT_COLOR_OTHER_CITY", c.PLOT_COLOR_OTHER_CITY, defaults.PLOT_COLOR_OTHER_CITY);
            c.PLOT_COLOR_CURRENT = ColorOrDefault(api, "PLOT_COLOR_CURRENT", c.PLOT_COLOR_CURRENT, defaults.PLOT_COLOR_CURRENT);
        }

        private static int[] ColorOrDefault(ICoreAPI api, string name, int[] value, int[] fallback)
        {
            if (value != null && value.Length == 4) return value;
            api.Logger.Error("[canclaimsextended] {0} needs 4 values (alpha, red, green, blue), using the default.", name);
            return fallback;
        }
    }
}
