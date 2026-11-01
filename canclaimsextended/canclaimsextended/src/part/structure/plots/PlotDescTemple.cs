using Vintagestory.API.MathTools;

namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>
    /// A temple: the respawn point of its city's citizens, at the temple block placed on it. No block,
    /// no point - a temple plot without one respawns nobody.
    /// </summary>
    public class PlotDescTemple : PlotDesc
    {
        public BlockPos Point { get; set; }

        public override string Serialize()
        {
            return Point == null ? "" : Point.X + "," + Point.Y + "," + Point.Z;
        }

        public override void Deserialize(string data)
        {
            string[] parts = data.Split(',');
            if (parts.Length == 3 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) && int.TryParse(parts[2], out int z))
            {
                Point = new BlockPos(x, y, z);
            }
        }

        public override void OnDeactivated(Plot plot)
        {
            Point = null;
        }
    }
}
