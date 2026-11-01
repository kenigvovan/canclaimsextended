using System.Collections.Generic;
using System.Linq;

namespace canclaimsextended.src.part.structure
{
    /// <summary>
    /// Coat of arms of a city, stored as one string: layers separated by ';', bottom first, e.g.
    /// "color_red;cross_white;lion_left_yellow". A layer is a texture name under
    /// assets/canclaimsextended/textures/emblem/pattern. Layers are whitelisted: the string comes
    /// from players and ends up in an asset path.
    /// Named ...Handler because City carries an "Emblem" property that would shadow the type.
    /// </summary>
    public static class EmblemHandler
    {
        public const char LAYER_DELIMITER = ';';
        public const char COLOR_DELIMITER = '_';
        public const string DOMAIN = "canclaimsextended";
        /// <summary>Path inside DOMAIN, without the domain: used as AssetLocation(DOMAIN, path).</summary>
        public const string TEXTURE_PATH_PREFIX = "textures/emblem/pattern/";

        /// <summary>The pattern that fills the whole cloth; it is the bottom layer and only that.</summary>
        public const string BACKGROUND_PATTERN = "color";

        public static readonly IReadOnlyList<string> DEFAULT_PATTERNS = new List<string>
        {
            // backgrounds and divisions
            "color", "half_horizontal", "half_vertical", "diagonal_left", "diagonal_right",
            "gradient", "checker4", "small_stripes",
            // ordinaries
            "border", "curly_border", "cross", "straight_cross", "falx_left", "falx_right",
            "stripe_center", "stripe_middle", "triangle_bottom", "triangle_top", "rhombus",
            "circle", "circle_large",
            // charges
            "lion_left", "lion_right", "elk", "skull", "heart", "crescent_up",
            "fleurdelis", "flower", "club", "spade", "globe"
        };

        public static readonly IReadOnlyList<string> DEFAULT_COLORS = new List<string>
        {
            "black", "blue", "brown", "gray", "green", "orange", "pink", "purple", "red", "white", "yellow"
        };

        // The client has no config: the server sends its rules with the emblems (EmblemsPacket).
        static List<string> clientPatterns;
        static List<string> clientColors;
        static int clientMaxLayers;

        public static void SetClientRules(List<string> patterns, List<string> colors, int maxLayers)
        {
            clientPatterns = patterns;
            clientColors = colors;
            clientMaxLayers = maxLayers;
        }

        /// <summary>A non-empty config list replaces the built-in one rather than narrowing it.</summary>
        public static IReadOnlyList<string> AvailablePatterns()
        {
            var configured = canclaimsextended.config?.EMBLEM_AVAILABLE_PATTERNS;
            if (configured != null && configured.Count > 0) return configured.ToList();
            if (canclaimsextended.config == null && clientPatterns != null && clientPatterns.Count > 0) return clientPatterns;
            return DEFAULT_PATTERNS;
        }

        public static IReadOnlyList<string> AvailableColors()
        {
            var configured = canclaimsextended.config?.EMBLEM_AVAILABLE_COLORS;
            if (configured != null && configured.Count > 0) return configured.ToList();
            if (canclaimsextended.config == null && clientColors != null && clientColors.Count > 0) return clientColors;
            return DEFAULT_COLORS;
        }

        public static int MaxLayers()
        {
            int max = canclaimsextended.config?.EMBLEM_MAX_LAYERS ?? (clientMaxLayers > 0 ? clientMaxLayers : 6);
            return max < 1 ? 1 : max;
        }

        /// <summary>The background alone at the bottom, everything else above it.</summary>
        public static IReadOnlyList<string> AvailablePatternsForLayer(int layerIndex)
        {
            var all = AvailablePatterns();
            if (layerIndex <= 0)
            {
                return all.Contains(BACKGROUND_PATTERN) ? new List<string> { BACKGROUND_PATTERN } : new List<string>();
            }
            return all.Where(p => p != BACKGROUND_PATTERN).ToList();
        }

        public static bool IsBackgroundLayer(string layer)
            => TrySplitLayer(layer, out string pattern, out _) && pattern == BACKGROUND_PATTERN;

        /// <summary>The color follows the LAST underscore: patterns contain underscores too ("lion_left_red").</summary>
        public static bool TrySplitLayer(string layer, out string pattern, out string color)
        {
            pattern = null;
            color = null;
            if (string.IsNullOrEmpty(layer)) return false;
            int idx = layer.LastIndexOf(COLOR_DELIMITER);
            if (idx <= 0 || idx == layer.Length - 1) return false;
            pattern = layer.Substring(0, idx);
            color = layer.Substring(idx + 1);
            return true;
        }

        public static string MakeLayer(string pattern, string color) => pattern + COLOR_DELIMITER + color;

        public static bool IsValidLayer(string layer)
        {
            if (!TrySplitLayer(layer, out string pattern, out string color)) return false;
            return AvailablePatterns().Contains(pattern) && AvailableColors().Contains(color);
        }

        /// <summary>Asset path of a layer that passed <see cref="IsValidLayer"/>.</summary>
        public static string TexturePath(string layer) => TEXTURE_PATH_PREFIX + layer + ".png";

        /// <summary>Known layers of a stored or received emblem, cut to the limit. Never null.</summary>
        public static List<string> Parse(string raw)
        {
            Split(raw, false, out List<string> layers, out _);
            return layers;
        }

        /// <summary>For typed input: reports the first bad layer instead of dropping it.</summary>
        public static bool TryParse(string raw, out List<string> layers, out string error)
            => Split(raw, true, out layers, out error);

        /// <summary>
        /// Strict reports the first problem and yields nothing; lenient keeps what it can, so a stored
        /// emblem still draws after the whitelist changed.
        /// </summary>
        private static bool Split(string raw, bool strict, out List<string> layers, out string error)
        {
            layers = new List<string>();
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;

            foreach (string part in raw.Split(LAYER_DELIMITER))
            {
                string layer = part.Trim();
                if (layer.Length == 0) continue;

                if (!IsValidLayer(layer))
                {
                    if (!strict) continue;
                    return Fail("canclaimsextended:emblem_bad_layer", layer, out layers, out error);
                }

                bool isBackground = IsBackgroundLayer(layer);
                bool wantsBackground = layers.Count == 0;
                if (isBackground != wantsBackground)
                {
                    if (!strict)
                    {
                        layers.Add(layer);
                    }
                    else
                    {
                        return Fail(wantsBackground ? "canclaimsextended:emblem_need_background" : "canclaimsextended:emblem_background_only_first",
                            layer, out layers, out error);
                    }
                }
                else
                {
                    layers.Add(layer);
                }

                if (!strict && layers.Count >= MaxLayers()) break;
            }

            if (strict && layers.Count > MaxLayers())
            {
                return Fail("canclaimsextended:emblem_too_many_layers", MaxLayers().ToString(), out layers, out error);
            }
            return true;
        }

        /// <summary>error is a lang key; its argument goes into ErrorArg.</summary>
        private static bool Fail(string langKey, string arg, out List<string> layers, out string error)
        {
            layers = new List<string>();
            error = langKey;
            ErrorArg = arg;
            return false;
        }

        /// <summary>Argument of the last TryParse error (the bad layer or the limit).</summary>
        public static string ErrorArg { get; private set; } = "";

        /// <summary>A stack not starting with a solid fill is dropped; fills above the bottom are removed.</summary>
        public static List<string> Sanitize(List<string> layers)
        {
            if (layers == null || layers.Count == 0) return new List<string>();
            if (!IsBackgroundLayer(layers[0])) return new List<string>();

            var result = new List<string> { layers[0] };
            result.AddRange(layers.Skip(1).Where(layer => !IsBackgroundLayer(layer)));
            return result;
        }

        public static string Join(IEnumerable<string> layers) =>
            layers == null ? "" : string.Join(LAYER_DELIMITER.ToString(), layers);

        /// <summary>A string safe to store: unknown layers dropped, limit and background rule enforced.</summary>
        public static string Normalize(string raw) => Join(Sanitize(Parse(raw)));
    }
}
