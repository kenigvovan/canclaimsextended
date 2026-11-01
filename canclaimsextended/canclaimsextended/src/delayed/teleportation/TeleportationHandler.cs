using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.cooldowns;
using canclaimsextended.src.messages;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.delayed.teleportation
{
    /// <summary>
    /// Teleports with a delay: one per player, cancelled by moving (PLAYER_MOVEMENT_CANCEL_TELEPORTATION),
    /// by leaving the server, or when the target stops being allowed.
    /// </summary>
    public static class TeleportationHandler
    {
        // Moving further than this from where it was asked for cancels it.
        const double CANCEL_DISTANCE = 0.6;

        static readonly Dictionary<string, TeleportationInfo> waiting = new();

        public static void Init(ICoreServerAPI sapi)
        {
            waiting.Clear();
            auxiliary.ServerTicks.Every(sapi, _ => Tick(sapi), 250);
        }

        /// <summary>Queues it; one already waiting for this player is cancelled and refunded.</summary>
        public static void Add(TeleportationInfo info)
        {
            Cancel(info.PlayerUid);
            waiting[info.PlayerUid] = info;
        }

        public static bool Cancel(string playerUid)
        {
            if (!waiting.Remove(playerUid, out TeleportationInfo info)) return false;
            info.Refund();
            return true;
        }

        public static bool IsWaiting(string playerUid) => waiting.ContainsKey(playerUid);

        public static void Clear()
        {
            waiting.Clear();
        }

        static void Tick(ICoreServerAPI sapi)
        {
            if (waiting.Count == 0) return;
            long now = TimeFunctions.GetEpochSeconds();
            foreach (TeleportationInfo info in waiting.Values.ToList())
            {
                IServerPlayer player = sapi.World.PlayerByUid(info.PlayerUid) as IServerPlayer;
                if (player?.Entity == null || player.ConnectionState != EnumClientState.Playing)
                {
                    Cancel(info.PlayerUid);
                    continue;
                }
                if (canclaimsextended.config.PLAYER_MOVEMENT_CANCEL_TELEPORTATION
                    && player.Entity.Pos.XYZ.DistanceTo(info.Start) > CANCEL_DISTANCE)
                {
                    Cancel(info.PlayerUid);
                    MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:teleport_cancelled_moved"));
                    continue;
                }
                if (info.DueAt > now) continue;

                waiting.Remove(info.PlayerUid);
                if (!info.IsStillAllowed())
                {
                    info.Refund();
                    MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:teleport_cancelled_no_longer_allowed"));
                    continue;
                }
                player.Entity.TeleportToDouble(info.Target.X, info.Target.Y, info.Target.Z);
                CooldownHandler.Set(info.PlayerUid, info.Cooldown, info.CooldownSeconds);
            }
        }
    }
}
