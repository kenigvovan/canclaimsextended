using System;
using System.IO;
using System.Linq;
using Vintagestory.API.Server;

namespace canclaimsextended.src.timers
{
    /// <summary>
    /// Copies of the database: at the BACKUP_TIMES of the server clock, when the server starts, and
    /// by /cadmin backup. Automatic copies beyond BACKUP_KEEP are deleted, oldest first.
    /// </summary>
    public static class BackupScheduler
    {
        const string AutoPrefix = "auto_";
        const string ManualPrefix = "manual_";
        const int CHECK_MS = 30000;

        static DateTime lastCheck;

        public static void Start(ICoreServerAPI sapi)
        {
            foreach (string time in canclaimsextended.config.BACKUP_TIMES.Where(t => !TryParseTime(t, out _)))
            {
                sapi.Logger.Error("[canclaimsextended] BACKUP_TIMES: \"{0}\" is not HH:mm, skipped.", time);
            }
            lastCheck = DateTime.Now;
            auxiliary.ServerTicks.Every(sapi, _ => Check(), CHECK_MS);
        }

        /// <summary>
        /// Copies the database file before it is opened, so a load that goes wrong can be undone.
        /// The file is closed at this point, a plain copy is enough.
        /// </summary>
        public static void CopyBeforeLoad(string dbPath)
        {
            if (!canclaimsextended.config.BACKUP_ON_START || !File.Exists(dbPath)) return;
            try
            {
                File.Copy(dbPath, NewPath(AutoPrefix), true);
                Prune();
            }
            catch (Exception e)
            {
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] backup on start failed: {0}", e);
            }
        }

        /// <summary>/cadmin backup: never deleted by the rotation.</summary>
        public static bool MakeManual(out string fileName)
        {
            string path = NewPath(ManualPrefix);
            fileName = Path.GetFileName(path);
            return canclaimsextended.Db?.makeBackup(path) ?? false;
        }

        static void Check()
        {
            DateTime now = DateTime.Now;
            DateTime from = lastCheck;
            lastCheck = now;
            foreach (string time in canclaimsextended.config.BACKUP_TIMES)
            {
                if (!TryParseTime(time, out TimeSpan at)) continue;
                // Due when it passed since the last check; yesterday's covers a check across midnight.
                DateTime today = now.Date + at;
                DateTime yesterday = today.AddDays(-1);
                if ((today > from && today <= now) || (yesterday > from && yesterday <= now))
                {
                    string path = NewPath(AutoPrefix);
                    if (canclaimsextended.Db?.makeBackup(path) == true)
                    {
                        canclaimsextended.sapi.Logger.Notification("[canclaimsextended] backup written: {0}", path);
                        Prune();
                    }
                    return;
                }
            }
        }

        static string NewPath(string prefix)
        {
            // Sortable by name, so the rotation can go by it.
            return Path.Combine(canclaimsextended.config.BackupFolder, prefix + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".db");
        }

        static void Prune()
        {
            var autos = Directory.GetFiles(canclaimsextended.config.BackupFolder, AutoPrefix + "*.db")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(canclaimsextended.config.BACKUP_KEEP);
            foreach (string old in autos)
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception e)
                {
                    canclaimsextended.sapi.Logger.Warning("[canclaimsextended] old backup {0} could not be deleted: {1}", old, e.Message);
                }
            }
        }

        static bool TryParseTime(string text, out TimeSpan time)
        {
            time = default;
            string[] parts = (text ?? "").Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out int hour) || hour < 0 || hour > 23
                || !int.TryParse(parts[1], out int minute) || minute < 0 || minute > 59)
            {
                return false;
            }
            time = new TimeSpan(hour, minute, 0);
            return true;
        }
    }
}
