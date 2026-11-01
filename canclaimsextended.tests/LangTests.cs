using System.Text.RegularExpressions;
using canclaimsextended.src;
using Newtonsoft.Json.Linq;

namespace canclaimsextended.tests
{
    // The lang files against each other and against the code. Cases: TESTING_canclaimsextended.md
    // Z1, Z2, S1, X5, Q (rank window labels). No statics.
    public class LangTests
    {
        private static readonly Lazy<JObject> en = new(() => Load("en.json"));
        private static readonly Lazy<JObject> ru = new(() => Load("ru.json"));

        private static JObject Load(string name) => JObject.Parse(File.ReadAllText(ModPaths.LangFile(name)));

        private static HashSet<string> Keys(JObject lang) => lang.Properties().Select(p => p.Name).ToHashSet();

        public static TheoryData<string> Languages() => new() { "en.json", "ru.json" };

        private static JObject Lang(string name) => name == "en.json" ? en.Value : ru.Value;

        [Theory]
        [MemberData(nameof(Languages))]
        public void LangFile_Should_Parse_When_Read(string name)
        {
            Exception? error = Record.Exception(() => Load(name));

            Assert.Null(error);
            Assert.NotEmpty(Keys(Lang(name)));
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void LangFile_Should_HaveOnlyStringValues(string name)
        {
            string[] notText = Lang(name).Properties().Where(p => p.Value.Type != JTokenType.String).Select(p => p.Name).ToArray();

            Assert.True(notText.Length == 0, name + ": not a string: " + string.Join(", ", notText));
        }

        // Every line is translated, and nothing exists in one language only.
        [Fact]
        public void LangFiles_Should_HaveTheSameKeys()
        {
            HashSet<string> enKeys = Keys(en.Value), ruKeys = Keys(ru.Value);

            string[] onlyEn = enKeys.Except(ruKeys).OrderBy(k => k).ToArray();
            string[] onlyRu = ruKeys.Except(enKeys).OrderBy(k => k).ToArray();

            Assert.True(onlyEn.Length == 0, "missing in ru.json: " + string.Join(", ", onlyEn));
            Assert.True(onlyRu.Length == 0, "missing in en.json: " + string.Join(", ", onlyRu));
        }

        // Z1, Z2: every right a rank may carry has a label in the Ranks window.
        [Theory]
        [MemberData(nameof(Languages))]
        public void PermLabels_Should_ExistForEveryRankableRight(string name)
        {
            HashSet<string> keys = Keys(Lang(name));

            string[] missing = new Config().AVAILABLE_CITY_PERMISSIONS
                .Select(p => "perm-" + p)
                .Where(k => !keys.Contains(k))
                .OrderBy(k => k)
                .ToArray();

            Assert.True(missing.Length == 0, name + ": missing " + string.Join(", ", missing));
        }

        // S1, S3: no lines of the alliances are left.
        [Theory]
        [MemberData(nameof(Languages))]
        public void LangFile_Should_HaveNoAllianceKeys(string name)
        {
            string[] alliance = Keys(Lang(name)).Where(k => k.Contains("alliance", StringComparison.OrdinalIgnoreCase)).ToArray();

            Assert.True(alliance.Length == 0, name + ": " + string.Join(", ", alliance));
        }

        // S1, X5: the text names only /city and /plot, so it takes exactly two arguments.
        [Theory]
        [MemberData(nameof(Languages))]
        public void AdminAsOnlyCityPlot_Should_HaveExactlyArguments0And1(string name)
        {
            string text = (string)Lang(name)["admin_as_only_city_plot"]!;

            string[] args = Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(a => a).ToArray();

            Assert.Equal(new[] { "0", "1" }, args);
        }

        /// <summary>
        /// "canclaimsextended:xxx" literals in the sources that name a lang line. Skipped: literals
        /// followed by '+' (a key built by concatenation), interpolated ones, asset paths (with '/'),
        /// asset codes in AssetLocation, and the icon names of GuiIcons.cs.
        /// </summary>
        private static IEnumerable<(string File, int Line, string Key)> SourceLangKeys()
        {
            var literal = new Regex("(?<![$@])\"canclaimsextended:([^\"]*)\"(\\s*\\+)?");
            foreach (string file in Directory.EnumerateFiles(ModPaths.SourceDir, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "GuiIcons.cs") continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in literal.Matches(lines[i]))
                    {
                        string key = m.Groups[1].Value;
                        if (m.Groups[2].Success) continue;            // concatenated
                        if (key.Length == 0 || key.Contains('/') || key.Contains('{')) continue;
                        if (lines[i].Contains("AssetLocation")) continue;
                        yield return (Path.GetRelativePath(ModPaths.SourceDir, file), i + 1, key);
                    }
                }
            }
        }

        [Fact]
        public void SourceScan_Should_FindLangLiterals()
        {
            Assert.True(SourceLangKeys().Count() > 100, "the scan found almost nothing; is ModProjectDir right? " + ModPaths.SourceDir);
        }

        // Every line the code asks for exists, so no raw key reaches a player.
        [Fact]
        public void LangKeys_Should_ExistInEnJson_When_UsedInTheSources()
        {
            HashSet<string> keys = Keys(en.Value);

            string[] missing = SourceLangKeys()
                .Where(k => !keys.Contains(k.Key))
                .Select(k => $"{k.Key} ({k.File}:{k.Line})")
                .Distinct()
                .ToArray();

            Assert.True(missing.Length == 0, "missing in en.json: " + string.Join(", ", missing));
        }
    }
}
