using System.Collections.Generic;
using System.Linq;

namespace canclaimsextended.src.part.structure.plots
{
    /// <summary>
    /// The plot types players can set. A new type is one Register call with its description class;
    /// nothing else has to know about it.
    /// </summary>
    public static class PlotTypes
    {
        static readonly Dictionary<string, PlotInfo> byCode = new();
        static readonly Dictionary<PlotType, PlotInfo> byType = new();

        static PlotTypes()
        {
            Register(new PlotInfo("default", PlotType.DEFAULT, () => new PlotDesc()));
            Register(new PlotInfo("farm", PlotType.FARM, () => new PlotDesc()));
            Register(new PlotInfo("orchard", PlotType.ORCHARD, () => new PlotDesc()));
            Register(new PlotInfo("temple", PlotType.TEMPLE, () => new PlotDescTemple()));
            Register(new PlotInfo("summon", PlotType.SUMMON, () => new PlotDescSummon()));
            Register(new PlotInfo("prison", PlotType.PRISON, () => new PlotDescPrison()));
            Register(new PlotInfo("shop", PlotType.SHOP, () => new PlotDesc()));
        }

        public static void Register(PlotInfo info)
        {
            byCode[info.Code] = info;
            byType[info.Type] = info;
        }

        public static bool TryGet(string code, out PlotInfo info)
        {
            return byCode.TryGetValue(code ?? "", out info);
        }

        /// <summary>The info of a type; the plain type for one that is not registered (an old row).</summary>
        public static PlotInfo Of(PlotType type)
        {
            return byType.TryGetValue(type, out PlotInfo info) ? info : byCode["default"];
        }

        public static IEnumerable<PlotInfo> All => byCode.Values;

        /// <summary>Codes players may pick on this server.</summary>
        public static IEnumerable<string> EnabledCodes => byCode.Keys.Where(code => !IsDisabled(code));

        /// <summary>
        /// Switched off by the host: listed in DISABLED_PLOT_TYPES, or the temple while temple respawn
        /// is off - a temple plot has no other use.
        /// </summary>
        public static bool IsDisabled(string code)
        {
            Config c = canclaimsextended.config;
            if (c.DISABLED_PLOT_TYPES?.Contains(code) == true) return true;
            return code == "temple" && !c.TEMPLE_RESPAWN_ENABLED;
        }

        /// <summary>A description for a plot loaded from the database.</summary>
        public static PlotDesc LoadDesc(PlotType type, string data)
        {
            PlotDesc desc = Of(type).CreateDesc();
            desc.Deserialize(data ?? "");
            return desc;
        }
    }
}
