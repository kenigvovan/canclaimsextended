using System;
using System.Collections.Generic;
using canclaimsextended.src.auxiliary;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part.citylog
{
    /// <summary>Writes an entry into a city's log: kept in memory up to CITY_LOG_MAX_ENTRIES, and stored.</summary>
    public static class CityLog
    {
        public static void Add(City city, EnumCityLogEvent eventType, params string[] args)
        {
            if (city == null) return;
            var entry = new CityLogEntry(TimeFunctions.GetEpochSeconds(), eventType, args);
            city.AddLogEntry(entry);
            canclaimsextended.Db?.saveCityLogEntry(city, entry);
        }

        /// <summary>"yyyy-MM-dd HH:mm  text" in the local language; lang key citylog-&lt;event&gt;.</summary>
        public static string Format(long timestamp, string eventName, IList<string> args)
        {
            return When(timestamp) + "  " + Text(eventName, args);
        }

        public static string When(long timestamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }

        public static string Text(string eventName, IList<string> args)
        {
            object[] langArgs = new object[args?.Count ?? 0];
            for (int i = 0; i < langArgs.Length; i++) langArgs[i] = args[i];
            return Lang.Get("canclaimsextended:citylog-" + eventName.ToLowerInvariant(), langArgs);
        }
    }
}
