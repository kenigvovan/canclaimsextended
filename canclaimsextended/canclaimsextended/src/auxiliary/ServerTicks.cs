using System;
using System.Collections.Generic;
using Vintagestory.API.Server;

namespace canclaimsextended.src.auxiliary
{
    /// <summary>
    /// The server tick listeners of the mod, kept so shutdown can stop them all: in single player the
    /// API outlives one world, and a listener left behind would run against the next one.
    /// </summary>
    public static class ServerTicks
    {
        static readonly List<long> ids = new();

        /// <summary>RegisterGameTickListener, remembered for <see cref="StopAll"/>.</summary>
        public static long Every(ICoreServerAPI sapi, Action<float> onTick, int intervalMs)
        {
            long id = sapi.Event.RegisterGameTickListener(onTick, intervalMs);
            ids.Add(id);
            return id;
        }

        public static void StopAll(ICoreServerAPI sapi)
        {
            foreach (long id in ids)
            {
                sapi?.Event.UnregisterGameTickListener(id);
            }
            ids.Clear();
        }
    }
}
