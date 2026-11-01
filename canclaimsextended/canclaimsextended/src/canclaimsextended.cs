using canclaimsextended.src.auxiliary;
using canclaimsextended.src.database;
using canclaimsextended.src.events;
using canclaimsextended.src.harmony;
using canclaimsextended.src.items;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using canclaimsextended.src.selection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src
{
    public class canclaimsextended : ModSystem
    {
        public static canclaimsextended modInstance;
        public static ICoreServerAPI sapi;
        public static Config config;
        public static DataStorage dataStorage;
        public static SelectionHandler selections;
        // Replaced by the economy bridge when an economy mod is installed.
        public static economy.IMoneyProvider economyProvider = new economy.NoopMoneyProvider();
        static DatabaseHandler databaseHandler;
        static Harmony harmonyInstance;

        /// <summary>The database; null before LoadDatabase and after shutdown.</summary>
        public static DatabaseHandler Db => databaseHandler;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("ItemClaimWand", typeof(ItemClaimWand));
            // Not "CANTempleBlock": that one belongs to claims.
            api.RegisterBlockClass("CXTempleBlock", typeof(blocks.TempleBlock));
        }

        // The client gets the plots around the player and draws them on the world map.
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            client.ClientSetup.Start(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            modInstance = this;
            sapi = api;
            Config.LoadConfig(api);
            harmonyInstance = new Harmony(ApplyPatches.HarmonyID);
            ApplyPatches.ApplyServerPatches(harmonyInstance);

            dataStorage = new DataStorage();
            // Here and not later: the channel must exist before the first client connects.
            network.NetworkSetup.WireServer(api);
            delayed.teleportation.TeleportationHandler.Init(api);
            // Mod assemblies are loaded by now, so block classes of other mods can be found.
            part.AlwaysAccessBlocks.Resolve(api);
            part.BoatShare.Init(api);
            selections = new SelectionHandler();
            RightsHandler.readOrCreateRightPerms();
            ServerEvents.AddEvents(api);

            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, ShutDownServer);
        }

        /// <summary>Opens and loads the database. False when it could not be read.</summary>
        public static bool LoadDatabase()
        {
            timers.BackupScheduler.CopyBeforeLoad(config.FilePath(config.DB_NAME));
            // A file of an older format throws here on purpose: the server must not start on it.
            databaseHandler = new SQLiteDatabaseHandler();
            return databaseHandler.loadEverything();
        }

        public static void ShutDownServer()
        {
            ServerTicks.StopAll(sapi);
            harmonyInstance?.UnpatchAll(ApplyPatches.HarmonyID);
            harmonyInstance = null;
            databaseHandler?.saveEverything();
            databaseHandler?.close();
            databaseHandler = null;
            dataStorage = null;
            selections = null;
            config = null;
            modInstance = null;
            Settings.Clear();
            // Static, so in single player they would survive into the next world.
            agreement.AgreementHandler.clearAll();
            messages.ChatModes.Clear();
            delayed.teleportation.TeleportationHandler.Clear();
            delayed.cooldowns.CooldownHandler.Clear();
            part.AlwaysAccessBlocks.Clear();
            blocks.BehaviorPlotTypeOnly.ClearMatchCounts();
        }
    }
}
