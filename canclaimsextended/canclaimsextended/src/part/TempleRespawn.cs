using System.Collections.Generic;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Temples as respawn points of their city, and the temple a citizen picked. The pick is kept in
    /// the player entity's WatchedAttributes as "cityGuid|plotGuid": vanilla saves those, and the city
    /// guid keeps a pick from following the player into another city.
    /// </summary>
    public static class TempleRespawn
    {
        const string PICK_KEY = "canclaimsextendedrespawntemple";

        /// <summary>Temple plots of the city that hold a temple block.</summary>
        public static IEnumerable<(Plot Plot, BlockPos Point)> Points(City city)
        {
            foreach (Plot plot in city.getCityPlots())
            {
                if (plot.Type == PlotType.TEMPLE && plot.Desc is PlotDescTemple temple && temple.Point != null)
                {
                    yield return (plot, temple.Point);
                }
            }
        }

        public static bool TryReadPick(IPlayer player, City city, out string plotGuid)
        {
            plotGuid = null;
            string raw = player?.Entity?.WatchedAttributes?.GetString(PICK_KEY, null);
            if (string.IsNullOrEmpty(raw) || city == null) return false;
            string[] parts = raw.Split('|');
            if (parts.Length != 2 || parts[0] != city.Guid) return false;
            plotGuid = parts[1];
            return true;
        }

        public static void WritePick(IPlayer player, City city, Plot plot)
        {
            var attributes = player?.Entity?.WatchedAttributes;
            if (attributes == null) return;
            attributes.SetString(PICK_KEY, city.Guid + "|" + plot.Guid);
            attributes.MarkPathDirty(PICK_KEY);
        }

        public static void ClearPick(IPlayer player)
        {
            var attributes = player?.Entity?.WatchedAttributes;
            if (attributes == null) return;
            attributes.RemoveAttribute(PICK_KEY);
            attributes.MarkPathDirty(PICK_KEY);
        }

        /// <summary>
        /// Where the citizen comes back: the picked temple while it still stands, otherwise the one
        /// nearest to where they died. False when the city has no temple or the player no city.
        /// </summary>
        public static bool TryFindRespawnPoint(IPlayer player, out BlockPos point, out bool pickLost)
        {
            point = null;
            pickLost = false;
            if (!canclaimsextended.config.TEMPLE_RESPAWN_ENABLED) return false;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo) || !playerInfo.hasCity()) return false;
            City city = playerInfo.City;

            if (canclaimsextended.config.TEMPLE_RESPAWN_CHOICE_ENABLED && TryReadPick(player, city, out string pickedGuid))
            {
                foreach (var (plot, p) in Points(city))
                {
                    if (plot.Guid == pickedGuid)
                    {
                        point = p;
                        return true;
                    }
                }
                ClearPick(player);
                pickLost = true;
            }

            Vec3d died = player.Entity?.Pos?.XYZ;
            double best = double.MaxValue;
            foreach (var (_, p) in Points(city))
            {
                double dist = died == null ? 0 : p.DistanceSqTo(died.X, died.Y, died.Z);
                if (dist < best)
                {
                    best = dist;
                    point = p;
                }
            }
            return point != null;
        }
    }
}
