using Vintagestory.API.MathTools;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// Numbers of the client side. Constants: the server config is never loaded on the client.
    /// Zone sizes are shared with the server and live in network.NetworkSetup.
    /// </summary>
    public static class ClientSettings
    {
        // World map: plots are drawn in a height slice around the player.
        public const int MAP_SLICE_DOWN = 24;
        public const int MAP_SLICE_UP = 24;
        // The slice moves in steps, so walking up a hill does not repaint the map every block.
        public const int MAP_LAYER_STEP = 8;
        public const int MAP_CHUNK_PAINTS_PER_TICK = 32;

        // Markers over plots.
        public const int MARKER_RANGE = 48;
        public const int MARKER_MAX_PLOTS = 12;
        public const int MARKER_SQUARE = 26;
        public const int MARKER_GAP = 3;

        // Territory borders: lines on the edges of city chunks this many chunks around the player,
        // from BORDER_DOWN blocks below to BORDER_UP above. Height moves in BORDER_STEP steps,
        // which is also the spacing of the lines.
        public const int BORDER_RADIUS_CHUNKS = 3;
        public const int BORDER_DOWN = 4;
        public const int BORDER_UP = 12;
        public const int BORDER_STEP = 4;
        public const float BORDER_LINE_WIDTH = 2f;
        public static readonly int BORDER_COLOR_OWN = ColorUtil.ColorFromRgba(60, 200, 255, 220);
        public static readonly int BORDER_COLOR_OTHER = ColorUtil.ColorFromRgba(255, 140, 40, 220);

        // Location banner: how often the player's plot is looked up, how long they must stay in a
        // new one before it counts (stairs and edges would flicker otherwise), how long texts stay.
        public const int BANNER_CHECK_MS = 250;
        public const int BANNER_STABLE_MS = 500;
        public const int BANNER_TITLE_MS = 3500;
        public const int BANNER_LINE_MS = 2500;
        public const int BANNER_FADE_IN_MS = 300;
        public const int BANNER_FADE_OUT_MS = 800;
        // The same title is not shown again sooner than this.
        public const int BANNER_REPEAT_MS = 30000;

        // Player window: how long after a command the state is asked for again.
        public const int GUI_REFRESH_DELAY_MS = 400;
        // On the plot tab, how often the state is asked for while the player walks.
        public const int GUI_PLOT_REFRESH_MS = 1000;
    }
}
