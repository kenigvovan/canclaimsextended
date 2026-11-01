using System;

namespace canclaimsextended.src.auxiliary
{
    public static class TimeFunctions
    {
        public static long GetEpochSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        /// <summary>Length of a mod day in real seconds, never under a minute.</summary>
        public static long DayLength => canclaimsextended.config.MOD_DAY_DURATION_IN_SECONDS > 0
            ? Math.Max(60, canclaimsextended.config.MOD_DAY_DURATION_IN_SECONDS)
            : 86400;

        /// <summary>Start of the mod day containing now; days are counted from the epoch shifted by HOUR_NEW_DAY_START.</summary>
        public static long GetCurrentDayStart(long now)
        {
            long day = DayLength;
            long sinceAnchor = now - canclaimsextended.config.HOUR_NEW_DAY_START;
            long cycles = sinceAnchor >= 0 ? sinceAnchor / day : (sinceAnchor - day + 1) / day;
            return canclaimsextended.config.HOUR_NEW_DAY_START + cycles * day;
        }
        public static string GetDateFromEpochSeconds(long date)
        {
            return DateTimeOffset.FromUnixTimeSeconds(date).DateTime.ToString("dd/MM/yyyy");
        }
    }
}
