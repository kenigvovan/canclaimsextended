using System.Collections.Generic;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part.structure
{
    /// <summary>
    /// Server-wide flags that override plots. "Everywhere" forces a thing on all land; "forbidden"
    /// switches it off where plots would allow it (pvp, blasts and hostile mobs: everywhere).
    /// </summary>
    public class WorldInfo
    {
        public bool pvpEverywhere;
        public bool fireEverywhere;
        public bool blastEverywhere;
        // Hostile mobs spawn everywhere, the nomobspawn flag of a plot is ignored.
        public bool mobSpawnEverywhere;

        public bool pvpForbidden;
        public bool fireForbidden;
        public bool blastForbidden;
        // Hostile mobs never spawn, claimed land or not.
        public bool mobSpawnForbidden;

        /// <summary>Command codes, also the keys the flags are stored under.</summary>
        public static readonly string[] Codes = { "pvpew", "fireew", "blastew", "mobspawnew", "pvpfb", "firefb", "blastfb", "mobspawnfb" };

        public bool TryGet(string code, out bool value)
        {
            switch (code)
            {
                case "pvpew": value = pvpEverywhere; return true;
                case "fireew": value = fireEverywhere; return true;
                case "blastew": value = blastEverywhere; return true;
                case "mobspawnew": value = mobSpawnEverywhere; return true;
                case "pvpfb": value = pvpForbidden; return true;
                case "firefb": value = fireForbidden; return true;
                case "blastfb": value = blastForbidden; return true;
                case "mobspawnfb": value = mobSpawnForbidden; return true;
            }
            value = false;
            return false;
        }

        public bool TrySet(string code, bool value)
        {
            switch (code)
            {
                case "pvpew": pvpEverywhere = value; return true;
                case "fireew": fireEverywhere = value; return true;
                case "blastew": blastEverywhere = value; return true;
                case "mobspawnew": mobSpawnEverywhere = value; return true;
                case "pvpfb": pvpForbidden = value; return true;
                case "firefb": fireForbidden = value; return true;
                case "blastfb": blastForbidden = value; return true;
                case "mobspawnfb": mobSpawnForbidden = value; return true;
            }
            return false;
        }

        public List<string> getStatus()
        {
            var status = new List<string>();
            foreach (string code in Codes)
            {
                TryGet(code, out bool value);
                status.Add(Lang.Get("canclaimsextended:world_flag_" + code, Lang.Get(value ? "canclaimsextended:gui-yes" : "canclaimsextended:gui-no")) + "\n");
            }
            return status;
        }

        /// <summary>
        /// Inside the daily pvp window (PVP_TIME_START to PVP_TIME_END, game hours). A window whose
        /// start is past its end wraps around midnight: 19 -> 6 is the night.
        /// </summary>
        public static bool IsPvpTime()
        {
            Config c = canclaimsextended.config;
            if (!c.PVP_DURING_PART_OF_THE_DAY) return false;
            float hour = canclaimsextended.sapi.World.Calendar.HourOfDay;
            float start = c.PVP_TIME_START, end = c.PVP_TIME_END;
            if (start == end) return false;
            if (start < end) return hour >= start && hour < end;
            return hour >= start || hour < end;
        }

        public bool saveToDatabase()
        {
            return canclaimsextended.Db.saveWorldInfo(this);
        }
    }
}
