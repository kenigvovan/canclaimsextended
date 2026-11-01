using System.Collections.Generic;
using canclaimsextended.src.auxiliary;

namespace canclaimsextended.src.delayed.cooldowns
{
    /// <summary>Per player cooldowns, in memory: a restart lifts them.</summary>
    public static class CooldownHandler
    {
        // (player uid, type) -> unix seconds when the cooldown ends.
        static readonly Dictionary<(string, CooldownType), long> cooldowns = new();

        /// <summary>When the cooldown ends, or 0 when there is none.</summary>
        public static long GetEnd(string playerUid, CooldownType type)
        {
            if (!cooldowns.TryGetValue((playerUid, type), out long end)) return 0;
            if (end > TimeFunctions.GetEpochSeconds()) return end;
            cooldowns.Remove((playerUid, type));
            return 0;
        }

        public static void Set(string playerUid, CooldownType type, long seconds)
        {
            if (seconds <= 0) return;
            cooldowns[(playerUid, type)] = TimeFunctions.GetEpochSeconds() + seconds;
        }

        public static void Clear()
        {
            cooldowns.Clear();
        }
    }
}
