using Newtonsoft.Json.Linq;

namespace canclaimsextended.tests
{
    // Grid recipes of the mod. Cases: TESTING_canclaimsextended.md Z11 (the temple recipe had an
    // ingredient G that its pattern never used), D1 (wand recipe). No statics.
    public class RecipeTests
    {
        // Pattern characters that are not ingredients: row separator, empty cell, padding.
        private static readonly HashSet<char> NotIngredients = new() { ',', '_', ' ', '\t' };

        public static TheoryData<string> RecipeFiles()
        {
            var data = new TheoryData<string>();
            foreach (string file in Directory.EnumerateFiles(ModPaths.RecipeGridDir(), "*.json").OrderBy(f => f))
            {
                data.Add(Path.GetFileName(file));
            }
            return data;
        }

        /// <summary>A file holds one recipe or an array of them; the game accepts json5-style keys.</summary>
        private static List<JObject> Recipes(string fileName)
        {
            JToken root = JToken.Parse(File.ReadAllText(Path.Combine(ModPaths.RecipeGridDir(), fileName)));
            return root is JArray array ? array.Cast<JObject>().ToList() : new List<JObject> { (JObject)root };
        }

        private static string Name(JObject recipe, int index) => (string?)recipe["name"] ?? "#" + index;

        [Fact]
        public void RecipeDir_Should_HaveTheTempleAndWandRecipes()
        {
            string[] files = Directory.EnumerateFiles(ModPaths.RecipeGridDir(), "*.json").Select(f => Path.GetFileName(f)!).ToArray();

            Assert.Contains("cantempleblock.json", files);
            Assert.Contains("claimwand.json", files);
        }

        // Z11: an ingredient missing from the pattern makes the game warn and is a sign of a broken recipe.
        [Theory]
        [MemberData(nameof(RecipeFiles))]
        public void Ingredients_Should_AllBeUsedInThePattern(string fileName)
        {
            List<JObject> recipes = Recipes(fileName);
            Assert.NotEmpty(recipes);
            for (int i = 0; i < recipes.Count; i++)
            {
                JObject recipe = recipes[i];
                string pattern = (string?)recipe["ingredientPattern"] ?? "";
                var ingredients = (JObject?)recipe["ingredients"];
                Assert.True(ingredients != null, $"{fileName} {Name(recipe, i)}: no ingredients");

                string[] unused = ingredients!.Properties().Select(p => p.Name).Where(letter => !pattern.Contains(letter)).ToArray();

                Assert.True(unused.Length == 0, $"{fileName} {Name(recipe, i)}: not in the pattern: {string.Join(", ", unused)}");
            }
        }

        // Z11: a pattern letter without an ingredient makes the recipe unusable.
        [Theory]
        [MemberData(nameof(RecipeFiles))]
        public void PatternLetters_Should_AllHaveAnIngredient(string fileName)
        {
            List<JObject> recipes = Recipes(fileName);
            for (int i = 0; i < recipes.Count; i++)
            {
                JObject recipe = recipes[i];
                string pattern = (string?)recipe["ingredientPattern"] ?? "";
                var letters = ((JObject?)recipe["ingredients"])?.Properties().Select(p => p.Name).ToHashSet() ?? new HashSet<string>();

                string[] undefined = pattern.Where(c => !NotIngredients.Contains(c)).Select(c => c.ToString())
                    .Where(c => !letters.Contains(c)).Distinct().ToArray();

                Assert.True(undefined.Length == 0, $"{fileName} {Name(recipe, i)}: no ingredient for {string.Join(", ", undefined)}");
            }
        }

        // The pattern fits the declared width and height.
        [Theory]
        [MemberData(nameof(RecipeFiles))]
        public void Pattern_Should_MatchWidthAndHeight(string fileName)
        {
            List<JObject> recipes = Recipes(fileName);
            for (int i = 0; i < recipes.Count; i++)
            {
                JObject recipe = recipes[i];
                string[] rows = ((string?)recipe["ingredientPattern"] ?? "").Split(',');
                int width = (int?)recipe["width"] ?? 0;
                int height = (int?)recipe["height"] ?? 0;

                Assert.True(rows.Length == height, $"{fileName} {Name(recipe, i)}: {rows.Length} rows, height {height}");
                Assert.All(rows, row => Assert.True(row.Trim().Length == width, $"{fileName} {Name(recipe, i)}: row '{row}' is not {width} wide"));
            }
        }
    }
}
