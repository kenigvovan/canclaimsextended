using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>
    /// A prison: cells are points inside the plot where jailed criminals of the city respawn.
    /// Kept in plotdesc as "x,y,z;x,y,z".
    /// </summary>
    public class PlotDescPrison : PlotDesc
    {
        public List<BlockPos> Cells { get; } = new List<BlockPos>();

        public override string Serialize()
        {
            return string.Join(";", Cells.Select(c => c.X + "," + c.Y + "," + c.Z));
        }

        public override void Deserialize(string data)
        {
            Cells.Clear();
            foreach (string cell in data.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = cell.Split(',');
                if (parts.Length == 3 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) && int.TryParse(parts[2], out int z))
                {
                    Cells.Add(new BlockPos(x, y, z));
                }
            }
        }

        /// <summary>Everyone serving here walks free; the cells go with the type.</summary>
        public override void OnDeactivated(Plot plot)
        {
            PrisonHandler.ReleaseAllIn(plot);
            Cells.Clear();
        }
    }
}
