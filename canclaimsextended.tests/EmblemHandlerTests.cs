using canclaimsextended.src;
using canclaimsextended.src.part.structure;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    // City coats of arms as typed by players and as stored. Cases: TESTING_canclaimsextended.md
    // O1, O2, O3, O4. The whitelists and the layer limit come from the static config; the defaults
    // are used here (empty lists, EMBLEM_MAX_LAYERS 6).
    [Collection("ModStatics")]
    public class EmblemHandlerTests : IDisposable
    {
        private readonly Config previousConfig = Mod.config;

        public EmblemHandlerTests()
        {
            Mod.config = new Config();
        }

        public void Dispose()
        {
            Mod.config = previousConfig;
        }

        // ---- TrySplitLayer -------------------------------------------------------------------

        // O1: patterns have underscores themselves, the color follows the last one.
        [Theory]
        [InlineData("lion_left_red", "lion_left", "red")]
        [InlineData("color_red", "color", "red")]
        [InlineData("a_b_c_d", "a_b_c", "d")]
        public void TrySplitLayer_Should_SplitAtTheLastUnderscore(string layer, string pattern, string color)
        {
            Assert.True(EmblemHandler.TrySplitLayer(layer, out string p, out string c));
            Assert.Equal(pattern, p);
            Assert.Equal(color, c);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("red")]
        [InlineData("_red")]
        [InlineData("cross_")]
        public void TrySplitLayer_Should_Fail_When_PatternOrColorIsMissing(string? layer)
        {
            Assert.False(EmblemHandler.TrySplitLayer(layer!, out _, out _));
        }

        // ---- TryParse (typed input, strict) --------------------------------------------------

        // O1
        [Fact]
        public void TryParse_Should_AcceptTheLayers_When_BackgroundComesFirst()
        {
            bool ok = EmblemHandler.TryParse("color_red; cross_white ;lion_left_yellow", out List<string> layers, out string error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(new[] { "color_red", "cross_white", "lion_left_yellow" }, layers);
        }

        // O3: no argument removes the emblem.
        [Fact]
        public void TryParse_Should_GiveNoLayers_When_InputIsEmpty()
        {
            Assert.True(EmblemHandler.TryParse("", out List<string> layers, out string error));
            Assert.Empty(layers);
            Assert.Null(error);
        }

        // O2: emblem_bad_layer with the layer.
        [Theory]
        [InlineData("color_red;nosuch_white", "nosuch_white")]
        [InlineData("color_red;cross_neon", "cross_neon")]
        [InlineData("color_red;cross", "cross")]
        public void TryParse_Should_ReportTheBadLayer_When_LayerIsUnknown(string raw, string bad)
        {
            bool ok = EmblemHandler.TryParse(raw, out List<string> layers, out string error);

            Assert.False(ok);
            Assert.Empty(layers);
            Assert.Equal("canclaimsextended:emblem_bad_layer", error);
            Assert.Equal(bad, EmblemHandler.ErrorArg);
        }

        // O2: emblem_need_background.
        [Fact]
        public void TryParse_Should_AskForABackground_When_FirstLayerIsNotColor()
        {
            bool ok = EmblemHandler.TryParse("cross_white;color_red", out _, out string error);

            Assert.False(ok);
            Assert.Equal("canclaimsextended:emblem_need_background", error);
            Assert.Equal("cross_white", EmblemHandler.ErrorArg);
        }

        // O2: emblem_background_only_first.
        [Fact]
        public void TryParse_Should_Refuse_When_BackgroundIsNotFirst()
        {
            bool ok = EmblemHandler.TryParse("color_red;cross_white;color_blue", out _, out string error);

            Assert.False(ok);
            Assert.Equal("canclaimsextended:emblem_background_only_first", error);
            Assert.Equal("color_blue", EmblemHandler.ErrorArg);
        }

        // O2: emblem_too_many_layers with the limit.
        [Fact]
        public void TryParse_Should_Refuse_When_ThereAreMoreLayersThanTheLimit()
        {
            string raw = "color_red;cross_white;circle_blue;heart_red;skull_black;elk_green;globe_yellow";

            bool ok = EmblemHandler.TryParse(raw, out List<string> layers, out string error);

            Assert.False(ok);
            Assert.Empty(layers);
            Assert.Equal("canclaimsextended:emblem_too_many_layers", error);
            Assert.Equal("6", EmblemHandler.ErrorArg);
        }

        [Fact]
        public void TryParse_Should_Accept_When_LayersAreExactlyTheLimit()
        {
            string raw = "color_red;cross_white;circle_blue;heart_red;skull_black;elk_green";

            Assert.True(EmblemHandler.TryParse(raw, out List<string> layers, out _));
            Assert.Equal(6, layers.Count);
        }

        // O4: a non-empty config list replaces the built-in one.
        [Fact]
        public void TryParse_Should_RefuseADefaultPattern_When_ConfigListsItsOwn()
        {
            Mod.config.EMBLEM_AVAILABLE_PATTERNS = new HashSet<string> { "color", "star" };

            Assert.True(EmblemHandler.TryParse("color_red;star_white", out _, out _));
            Assert.False(EmblemHandler.TryParse("color_red;cross_white", out _, out string error));
            Assert.Equal("canclaimsextended:emblem_bad_layer", error);
        }

        // ---- Parse (stored emblems, lenient) -------------------------------------------------

        // A stored emblem still draws after the whitelist changed: unknown layers are skipped.
        [Fact]
        public void Parse_Should_SkipUnknownLayers_When_Stored()
        {
            Assert.Equal(new[] { "color_red", "cross_white" }, EmblemHandler.Parse("color_red;nosuch_white;cross_white"));
        }

        [Fact]
        public void Parse_Should_CutToTheLimit_When_StoredHasTooMany()
        {
            string raw = "color_red;cross_white;circle_blue;heart_red;skull_black;elk_green;globe_yellow;club_red";

            Assert.Equal(6, EmblemHandler.Parse(raw).Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(";;")]
        public void Parse_Should_GiveNoLayers_When_NothingIsStored(string? raw)
        {
            Assert.Empty(EmblemHandler.Parse(raw!));
        }

        // Lenient keeps misplaced backgrounds; Sanitize decides about them.
        [Fact]
        public void Parse_Should_KeepMisplacedBackgrounds_When_Stored()
        {
            Assert.Equal(new[] { "cross_white", "color_red" }, EmblemHandler.Parse("cross_white;color_red"));
        }

        // ---- Sanitize / Normalize ------------------------------------------------------------

        [Fact]
        public void Sanitize_Should_DropTheStack_When_ItDoesNotStartWithABackground()
        {
            Assert.Empty(EmblemHandler.Sanitize(new List<string> { "cross_white", "color_red" }));
        }

        [Fact]
        public void Sanitize_Should_RemoveBackgroundsAboveTheBottom()
        {
            List<string> result = EmblemHandler.Sanitize(new List<string> { "color_red", "cross_white", "color_blue", "heart_red" });

            Assert.Equal(new[] { "color_red", "cross_white", "heart_red" }, result);
        }

        [Fact]
        public void Sanitize_Should_GiveAnEmptyList_When_InputIsNullOrEmpty()
        {
            Assert.Empty(EmblemHandler.Sanitize(null!));
            Assert.Empty(EmblemHandler.Sanitize(new List<string>()));
        }

        // Y1: what is stored is already clean, so a load keeps it as it was.
        [Theory]
        [InlineData("color_red;cross_white", "color_red;cross_white")]
        [InlineData("color_red;nosuch_x;cross_white;color_blue", "color_red;cross_white")]
        [InlineData("cross_white;color_red", "")]
        public void Normalize_Should_GiveAStorableString(string raw, string expected)
        {
            Assert.Equal(expected, EmblemHandler.Normalize(raw));
        }
    }
}
