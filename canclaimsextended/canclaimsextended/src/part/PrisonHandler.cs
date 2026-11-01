using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.messages;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Jail: a criminal of a city killed by one of its citizens respawns in a cell of the city's prison
    /// and stays inside the prison plot until the sentence ends or a ransom is paid. Offline players
    /// are handled when they log in, so nothing has to walk every known player.
    /// </summary>
    public static class PrisonHandler
    {
        /// <summary>PrisonPlotGuid of a player freed while offline: teleported out on the next login.</summary>
        public const string RELEASE_ON_LOGIN = "#release";
        static readonly Random rand = new Random();

        public static void Init(ICoreServerAPI sapi)
        {
            auxiliary.ServerTicks.Every(sapi, _ => Tick(), 2000);
        }

        /// <summary>Prison plots of the city that have at least one cell.</summary>
        public static IEnumerable<(Plot Plot, PlotDescPrison Desc)> Prisons(City city)
        {
            foreach (Plot plot in city.getCityPlots())
            {
                if (plot.Type == PlotType.PRISON && plot.Desc is PlotDescPrison desc && desc.Cells.Count > 0)
                {
                    yield return (plot, desc);
                }
            }
        }

        /// <summary>Jails the victim when the killer's city lists them as a criminal and has a prison.</summary>
        public static bool TryImprison(PlayerInfo victim, PlayerInfo killer)
        {
            if (victim == null || killer == null || victim.isPrisoned() || killer.Equals(victim) || !killer.hasCity()) return false;
            City city = killer.City;
            if (!city.Criminals.Contains(victim.Guid)) return false;
            int hours = Math.Max(1, canclaimsextended.config.PRISON_SENTENCE_HOURS);
            return Jail(victim, city, hours * 3600L, false);
        }

        /// <summary>
        /// Puts the player into a random prison of the city for the given time; false when the city
        /// has no prison with cells. teleportIn moves an online player into a cell at once (the
        /// killed one respawns there by itself).
        /// </summary>
        public static bool Jail(PlayerInfo player, City city, long seconds, bool teleportIn)
        {
            var prisons = Prisons(city).ToList();
            if (prisons.Count == 0) return false;
            var prison = prisons[rand.Next(prisons.Count)];
            player.PrisonPlotGuid = prison.Plot.Guid;
            player.PrisonReleaseAt = TimeFunctions.GetEpochSeconds() + seconds;
            canclaimsextended.Db.savePrisoner(player);
            // Whole hours in the messages, as the sentence is set in hours; at least 1.
            long hours = Math.Max(1, (seconds + 3599) / 3600);
            MessageHandler.sendMsgToPlayerInfo(player, Lang.Get("canclaimsextended:you_were_jailed", city.getPartNameReplaceUnder(), hours));
            MessageHandler.sendMsgInCity(city, Lang.Get("canclaimsextended:criminal_jailed", player.GetPartName(), hours));
            IServerPlayer online = Online(player);
            if (teleportIn && online != null)
            {
                BlockPos cell = prison.Desc.Cells[rand.Next(prison.Desc.Cells.Count)];
                online.Entity.TeleportToDouble(cell.X + 0.5, cell.Y, cell.Z + 0.5);
            }
            return true;
        }

        /// <summary>
        /// The prison the player is held in, if it is still a prison with cells. On false the plot
        /// is still given when it exists: the ransom goes to its city even if the cells are gone.
        /// </summary>
        public static bool TryGetPrison(PlayerInfo player, out Plot plot, out PlotDescPrison desc)
        {
            desc = null;
            if (!player.isPrisoned() || !canclaimsextended.dataStorage.GetPlotByGuid(player.PrisonPlotGuid, out plot))
            {
                plot = null;
                return false;
            }
            if (plot.Type != PlotType.PRISON || plot.Desc is not PlotDescPrison d || d.Cells.Count == 0) return false;
            desc = d;
            return true;
        }

        /// <summary>Where a jailed player respawns: a random cell of their prison.</summary>
        public static bool TryGetCell(IPlayer player, out BlockPos cell)
        {
            cell = null;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo info)) return false;
            if (!TryGetPrison(info, out _, out PlotDescPrison desc)) return false;
            cell = desc.Cells[rand.Next(desc.Cells.Count)];
            return true;
        }

        /// <summary>Ends the sentence. Offline players are moved out on their next login.</summary>
        public static void Release(PlayerInfo player, bool teleportOut)
        {
            IServerPlayer online = Online(player);
            if (online != null)
            {
                if (teleportOut) TeleportToSpawn(online);
                player.PrisonPlotGuid = "";
                MessageHandler.sendMsgToPlayer(online, Lang.Get("canclaimsextended:you_were_released"));
            }
            else
            {
                player.PrisonPlotGuid = teleportOut ? RELEASE_ON_LOGIN : "";
            }
            player.PrisonReleaseAt = 0;
            canclaimsextended.Db.savePrisoner(player);
        }

        /// <summary>The prison plot stops being one: everyone online in it walks free now, the rest on login.</summary>
        public static void ReleaseAllIn(Plot plot)
        {
            foreach (IPlayer p in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                if (canclaimsextended.dataStorage.GetPlayerByUid(p.PlayerUID, out PlayerInfo info) && info.PrisonPlotGuid == plot.Guid)
                {
                    Release(info, true);
                }
            }
        }

        /// <summary>Price of a ransom by who the prisoner is.</summary>
        public static double RansomPrice(PlayerInfo player)
        {
            Config c = canclaimsextended.config;
            if (!player.hasCity()) return c.RANSOM_FOR_NO_CITIZEN;
            return player.City.isMayor(player) ? c.RANSOM_FOR_MAYOR : c.RANSOM_FOR_CITIZEN;
        }

        /// <summary>Releases, moves out or brings back one online player. Called on login and every tick.</summary>
        public static void CheckPlayer(IServerPlayer player, PlayerInfo info)
        {
            if (player?.Entity == null || info == null || info.PrisonPlotGuid.Length == 0) return;
            if (info.PrisonPlotGuid == RELEASE_ON_LOGIN)
            {
                TeleportToSpawn(player);
                info.PrisonPlotGuid = "";
                canclaimsextended.Db.savePrisoner(info);
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:you_were_released"));
                return;
            }
            // The prison is gone or the time is served.
            if (!TryGetPrison(info, out Plot plot, out PlotDescPrison desc) || TimeFunctions.GetEpochSeconds() >= info.PrisonReleaseAt)
            {
                Release(info, true);
                return;
            }
            if (!player.Entity.Alive) return;
            var pos = player.Entity.Pos;
            if (!plot.Shape.Contains(pos.X, pos.Y, pos.Z))
            {
                BlockPos cell = desc.Cells[rand.Next(desc.Cells.Count)];
                player.Entity.TeleportToDouble(cell.X + 0.5, cell.Y, cell.Z + 0.5);
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:prison_no_escape"));
            }
        }

        static void Tick()
        {
            if (canclaimsextended.dataStorage == null) return;
            foreach (IPlayer p in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                if (p is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing
                    && canclaimsextended.dataStorage.GetPlayerByUid(sp.PlayerUID, out PlayerInfo info))
                {
                    CheckPlayer(sp, info);
                }
            }
        }

        static IServerPlayer Online(PlayerInfo player)
        {
            return canclaimsextended.sapi.World.PlayerByUid(player.Guid) is IServerPlayer sp
                && sp.ConnectionState == EnumClientState.Playing && sp.Entity != null ? sp : null;
        }

        static void TeleportToSpawn(IServerPlayer player)
        {
            var spawn = canclaimsextended.sapi.World.DefaultSpawnPosition;
            player.Entity.TeleportToDouble(spawn.X, spawn.Y, spawn.Z);
        }
    }
}
