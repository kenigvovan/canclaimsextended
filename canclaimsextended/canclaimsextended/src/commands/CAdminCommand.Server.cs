using System.Collections.Generic;
using System.Text;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/cadmin reload, backup, setcfg and getcfg.</summary>
    public partial class CAdminCommand
    {
        public static TextCommandResult Backup(TextCommandCallingArgs args)
        {
            return timers.BackupScheduler.MakeManual(out string fileName)
                ? SuccessWithParams("canclaimsextended:admin_backup_done", fileName, canclaimsextended.config.BackupFolder)
                : TextCommandResult.Error("canclaimsextended:admin_backup_failed");
        }

        /// <summary>
        /// Rereads canclaimsextended.json, the city levels and the permissions file. The command
        /// names stay as registered: commands cannot be renamed without a restart.
        /// </summary>
        public static TextCommandResult Reload(TextCommandCallingArgs args)
        {
            Config old = canclaimsextended.config;
            Config.LoadConfig(canclaimsextended.sapi);
            Config fresh = canclaimsextended.config;
            // All seven, as registered: a name taken by another mod got an "x" in front at start.
            fresh.COMMAND_CITY = old.COMMAND_CITY;
            fresh.COMMAND_PLOT = old.COMMAND_PLOT;
            fresh.COMMAND_CITIZEN = old.COMMAND_CITIZEN;
            fresh.COMMAND_AGREE = old.COMMAND_AGREE;
            fresh.COMMAND_CADMIN = old.COMMAND_CADMIN;
            fresh.COMMAND_CITY_CHAT = old.COMMAND_CITY_CHAT;
            fresh.COMMAND_GENERAL_CHAT = old.COMMAND_GENERAL_CHAT;
            // Patches and crop behaviours are set up at start, so these keep their start values too;
            // the admin is told which of them changed in the file and wait for a restart.
            var restartOnly = new List<string>();
            KeepStartValue(nameof(Config.FIRE_PROTECTION_PATCH), ref fresh.FIRE_PROTECTION_PATCH, old.FIRE_PROTECTION_PATCH, restartOnly);
            KeepStartValue(nameof(Config.BLAST_PROTECTION_PATCH), ref fresh.BLAST_PROTECTION_PATCH, old.BLAST_PROTECTION_PATCH, restartOnly);
            KeepStartValue(nameof(Config.WATER_FLOW_CITY_PLOTS_PATCH), ref fresh.WATER_FLOW_CITY_PLOTS_PATCH, old.WATER_FLOW_CITY_PLOTS_PATCH, restartOnly);
            KeepStartValue(nameof(Config.FALLING_BLOCKS_PROTECTION_PATCH), ref fresh.FALLING_BLOCKS_PROTECTION_PATCH, old.FALLING_BLOCKS_PROTECTION_PATCH, restartOnly);
            KeepStartValue(nameof(Config.CROPS_ONLY_ON_FARM_PLOTS), ref fresh.CROPS_ONLY_ON_FARM_PLOTS, old.CROPS_ONLY_ON_FARM_PLOTS, restartOnly);
            KeepStartValue(nameof(Config.FRUIT_ONLY_ON_ORCHARD_PLOTS), ref fresh.FRUIT_ONLY_ON_ORCHARD_PLOTS, old.FRUIT_ONLY_ON_ORCHARD_PLOTS, restartOnly);

            Settings.Load();
            AlwaysAccessBlocks.Resolve(canclaimsextended.sapi);
            RightsHandler.readOrCreateRightPerms();
            foreach (IPlayer online in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                if (canclaimsextended.dataStorage.GetPlayerByUid(online.PlayerUID, out PlayerInfo info))
                {
                    RightsHandler.reapplyRights(info);
                }
            }
            canclaimsextended.dataStorage.ResetCacheForAll();
            // Keys added to PLOT_TYPE_ONLY_BLOCKS by hand work without a restart.
            blocks.BehaviorPlotTypeOnly.ClearMatchCounts();
            blocks.BehaviorPlotTypeOnly.AttachToBlocks(canclaimsextended.sapi);
            return restartOnly.Count == 0
                ? TextCommandResult.Success("canclaimsextended:admin_reloaded")
                : SuccessWithParams("canclaimsextended:admin_reloaded_needs_restart", string.Join(", ", restartOnly));
        }

        static void KeepStartValue(string name, ref bool fresh, bool start, List<string> changed)
        {
            if (fresh != start) changed.Add(name);
            fresh = start;
        }

        /// <summary>
        /// /cadmin day: runs the daily cycle now (fees, group fees, plot fees, upkeep, debt) and
        /// settles due group fee raises. The regular day still comes at its own time.
        /// </summary>
        public static TextCommandResult Day(TextCommandCallingArgs args)
        {
            timers.DayTimer.Run();
            PlotsGroupHandler.SettleDueRaises();
            return TextCommandResult.Success("canclaimsextended:admin_day_done");
        }

        /// <summary>/cadmin setcfg &lt;key&gt; &lt;value&gt;: one price setting, written to the config file at once.</summary>
        public static TextCommandResult SetCfg(TextCommandCallingArgs args)
        {
            string key = (string)args[0];
            if (!economy.PriceSettings.TrySet(key, (string)args[1], out string errorKey, out object[] errorArgs))
            {
                return ErrorWithParams(errorKey, errorArgs);
            }
            canclaimsextended.sapi.StoreModConfig(canclaimsextended.config, Config.FILE_NAME);
            economy.PriceSettings.TryFind(key, out var entry);
            return SuccessWithParams("canclaimsextended:admin_cfg_set", entry.Key, economy.PriceSettings.Format(entry.Get()));
        }

        /// <summary>/cadmin getcfg: every price setting with its value and range.</summary>
        public static TextCommandResult GetCfg(TextCommandCallingArgs args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var entry in economy.PriceSettings.All)
            {
                sb.Append(entry.Key).Append(" = ").Append(economy.PriceSettings.Format(entry.Get()))
                  .Append("  [").Append(economy.PriceSettings.Format(entry.Min)).Append("..")
                  .Append(economy.PriceSettings.Format(entry.Max)).Append("]\n");
            }
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
