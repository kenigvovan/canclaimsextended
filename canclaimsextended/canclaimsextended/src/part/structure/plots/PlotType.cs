using System;

namespace canclaimsextended.src.part.structure
{
    public enum PlotType
    {
        // Stored in the database as a plain int, so new members may only be appended and none
        // removed. The obsolete ones are no longer offered: a stored one reads as a plain plot
        // (PlotTypes.Of). TOURNAMENT, CAMP and EMBASSY belonged to the war, which this mod has not.
        DEFAULT = 0,
        [Obsolete("Not offered; kept so the stored numbers stay.")] TAVERN = 1,
        [Obsolete("War plot type; the mod has no war.")] TOURNAMENT = 2,
        FARM = 3,
        [Obsolete("War plot type; the mod has no war.")] CAMP = 4,
        SUMMON = 5,
        TEMPLE = 6,
        [Obsolete("War plot type; the mod has no war.")] EMBASSY = 7,
        [Obsolete("Not offered; kept so the stored numbers stay.")] MAIN_CITY_PLOT = 8,
        PRISON = 9,
        ORCHARD = 10,
        [Obsolete("Not offered; kept so the stored numbers stay.")] VILLAGE_MAIN = 11,
        SHOP = 12
    }
}
