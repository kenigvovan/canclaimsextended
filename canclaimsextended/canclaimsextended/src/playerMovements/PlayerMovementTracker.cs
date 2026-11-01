using System.Collections.Generic;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.playerMovements
{
    /// <summary>
    /// Tells a player in chat when they walk into or out of a city. Moving between plots of one
    /// city says nothing.
    /// </summary>
    public static class PlayerMovementTracker
    {
        // Player uid -> guid of the city they were last seen in, "" for free land.
        static readonly Dictionary<string, string> lastCity = new();

        public static void Init(ICoreServerAPI sapi)
        {
            lastCity.Clear();
            if (!canclaimsextended.config.MOVEMENT_MESSAGES) return;
            auxiliary.ServerTicks.Every(sapi, _ => CheckAll(sapi), canclaimsextended.config.MOVEMENT_CHECK_MS);
            sapi.Event.PlayerDisconnect += player => lastCity.Remove(player.PlayerUID);
        }

        static void CheckAll(ICoreServerAPI sapi)
        {
            if (canclaimsextended.dataStorage == null) return;
            foreach (IPlayer it in sapi.World.AllOnlinePlayers)
            {
                if (it is not IServerPlayer player || player.ConnectionState != EnumClientState.Playing || player.Entity == null)
                {
                    continue;
                }
                City city = null;
                if (canclaimsextended.dataStorage.GetPlotAt(player.Entity.Pos.XYZ, out Plot plot))
                {
                    city = plot.getCity();
                }
                string guid = city?.Guid ?? "";

                // First sight after login: remember the place quietly.
                if (!lastCity.TryGetValue(player.PlayerUID, out string previous))
                {
                    lastCity[player.PlayerUID] = guid;
                    continue;
                }
                if (previous == guid) continue;
                lastCity[player.PlayerUID] = guid;

                if (city != null)
                {
                    bool pvp = rules.PvpRules.IsPvpOn(plot);
                    MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:entered_city",
                        city.getPartNameReplaceUnder(), Lang.Get(pvp ? "canclaimsextended:pvp_on" : "canclaimsextended:pvp_off")));
                }
                else if (canclaimsextended.dataStorage.GetCityByGUID(previous, out City left))
                {
                    MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:left_city", left.getPartNameReplaceUnder()));
                }
            }
        }
    }
}
