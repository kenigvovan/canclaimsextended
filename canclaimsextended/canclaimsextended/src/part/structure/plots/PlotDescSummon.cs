using System.Globalization;
using System.Linq;
using canclaimsextended.src.auxiliary;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>
    /// A summon plot: citizens can teleport to its point. The point is set by /city summon set from
    /// inside the plot; until then the plot summons nobody.
    /// </summary>
    public class PlotDescSummon : PlotDesc
    {
        /// <summary>Feet position of an arriving player; null until set.</summary>
        public Vec3d Point { get; set; }
        public string Name { get; set; } = "";

        public override string Serialize()
        {
            if (Point == null) return "";
            return string.Join(";", Name,
                Point.X.ToString(CultureInfo.InvariantCulture),
                Point.Y.ToString(CultureInfo.InvariantCulture),
                Point.Z.ToString(CultureInfo.InvariantCulture));
        }

        public override void Deserialize(string data)
        {
            string[] parts = data.Split(';');
            if (parts.Length == 4
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            {
                Name = parts[0];
                Point = new Vec3d(x, y, z);
            }
        }

        /// <summary>How many summon plots a city may have is set by its level.</summary>
        public override ClaimCheck Validate(Plot plot)
        {
            City city = plot.getCity();
            if (city == null) return ClaimCheck.Success;
            int max = Settings.GetCityLevelInfo(city.getCityCitizens().Count).SummonPoints;
            int count = city.getCityPlots().Count(p => p.Type == PlotType.SUMMON && !p.Equals(plot));
            return count >= max ? ClaimCheck.Fail("canclaimsextended:too_many_summon_plots", max) : ClaimCheck.Success;
        }

        public override void OnDeactivated(Plot plot)
        {
            Point = null;
            Name = "";
        }

        /// <summary>
        /// Whether a player can stand at this point: feet and head blocks inside the plot and free of
        /// anything solid.
        /// </summary>
        public static bool IsStandable(IBlockAccessor blockAccessor, Plot plot, Vec3d point)
        {
            BlockPos feet = new BlockPos((int)System.Math.Floor(point.X), (int)System.Math.Floor(point.Y), (int)System.Math.Floor(point.Z));
            BlockPos head = feet.UpCopy();
            if (!plot.Shape.Contains(feet.X, feet.Y, feet.Z) || !plot.Shape.Contains(head.X, head.Y, head.Z)) return false;
            return IsFree(blockAccessor, feet) && IsFree(blockAccessor, head);
        }

        static bool IsFree(IBlockAccessor blockAccessor, BlockPos pos)
        {
            Cuboidf[] boxes = blockAccessor.GetBlock(pos).GetCollisionBoxes(blockAccessor, pos);
            return boxes == null || boxes.Length == 0;
        }
    }
}
