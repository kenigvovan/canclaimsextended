using System;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.auxiliary
{
    /// <summary>
    /// A shade of the city color for one plot, so neighbouring plots of a city tell apart. Used by
    /// the server highlight and the client map: both know the plot guid and get the same shade.
    /// </summary>
    public static class PlotTint
    {
        /// <param name="color">Red in the lowest byte, as City.cityColor is stored.</param>
        /// <returns>Same byte order, with the given alpha.</returns>
        public static int Tint(int color, string guid, int alpha)
        {
            uint hash = Fnv(guid ?? "");
            int[] hsv = ColorUtil.RgbToHsvInts(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF);
            // Value moves too: a grey city has no hue to shift.
            int dh = ((int)(hash % 5) - 2) * 6;
            int dv = ((int)(hash / 5 % 3) - 1) * 20;
            int h = ((hsv[0] + dh) % 256 + 256) % 256;
            int v = Math.Clamp(hsv[2] + dv, 0, 255);
            return ColorUtil.HsvToRgba(h, hsv[1], v, alpha);
        }

        // Not string.GetHashCode: that one differs between processes, and so between server and client.
        static uint Fnv(string s)
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }
}
