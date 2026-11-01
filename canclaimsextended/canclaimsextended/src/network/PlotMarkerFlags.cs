namespace canclaimsextended.src.network
{
    /// <summary>
    /// Bits of ClientPlotData.Flags: the plot's own flags, then what the receiving player may do
    /// there. The rights part differs per player, so every player gets their own copy.
    /// </summary>
    public static class PlotMarkerFlags
    {
        public const int PVP = 1;
        public const int FIRE = 2;
        public const int BLAST = 4;
        public const int NO_MOB_SPAWN = 8;
        public const int CAN_USE = 16;
        public const int CAN_BUILD = 32;
        public const int CAN_ATTACK = 64;
        /// <summary>The plot or land belongs to the receiving player's own city.</summary>
        public const int OWN_CITY = 128;

        public static bool Has(int flags, int bit) => (flags & bit) != 0;
    }
}
