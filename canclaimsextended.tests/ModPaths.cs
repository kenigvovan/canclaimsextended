using System.Reflection;

namespace canclaimsextended.tests
{
    /// <summary>
    /// Where the mod's files are. Lang files and recipes are copied next to the test assembly
    /// (see the csproj); the sources are read from the mod project, whose folder the csproj writes
    /// into the assembly. Walking up from the output is the fallback.
    /// </summary>
    internal static class ModPaths
    {
        private static readonly Lazy<string> projectDir = new(FindProjectDir);

        /// <summary>The folder of canclaimsextended.csproj.</summary>
        public static string ProjectDir => projectDir.Value;

        public static string SourceDir => Path.Combine(ProjectDir, "src");

        public static string LangFile(string name)
        {
            string copied = Path.Combine(AppContext.BaseDirectory, "assets", "lang", name);
            return File.Exists(copied) ? copied : Path.Combine(ProjectDir, "assets", "canclaimsextended", "lang", name);
        }

        public static string RecipeGridDir()
        {
            string copied = Path.Combine(AppContext.BaseDirectory, "assets", "recipes", "grid");
            return Directory.Exists(copied) ? copied : Path.Combine(ProjectDir, "assets", "canclaimsextended", "recipes", "grid");
        }

        private static string FindProjectDir()
        {
            string? fromBuild = typeof(ModPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ModProjectDir")?.Value;
            if (fromBuild != null && File.Exists(Path.Combine(fromBuild, "canclaimsextended.csproj"))) return fromBuild;

            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "canclaimsextended", "canclaimsextended");
                if (File.Exists(Path.Combine(candidate, "canclaimsextended.csproj"))) return candidate;
            }
            throw new DirectoryNotFoundException("canclaimsextended.csproj not found above " + AppContext.BaseDirectory);
        }
    }
}
