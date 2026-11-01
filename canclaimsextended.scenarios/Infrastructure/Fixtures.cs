namespace canclaimsextended.scenarios.Infrastructure;

/// <summary>
/// The prebuilt databases under fixtures/. They are made once by FixtureGenerator from
/// fixtures/sql/*.sql; a class whose fixture was never generated still boots (the mod starts on an
/// empty database), so its scenarios check the file first and say what to do instead of failing on
/// a missing city.
/// </summary>
public static class Fixtures
{
    public const string DbFileName = "canclaimsextended.db";

    /// <summary>The fixtures folder next to the test assembly, where Atlas seeds from.</summary>
    public static string BinFolder(string fixture) =>
        Path.Combine(Path.GetDirectoryName(typeof(Fixtures).Assembly.Location)!, "fixtures", fixture);

    /// <summary>Fails with the way out when fixtures/<paramref name="fixture"/>/canclaimsextended.db was never generated.</summary>
    public static void RequireDb(string fixture)
    {
        string path = Path.Combine(BinFolder(fixture), DbFileName);
        if (File.Exists(path)) return;
        Assert.Fail(
            $"fixture fixtures/{fixture}/{DbFileName} does not exist, so the server booted on an empty database and this " +
            "scenario has nothing to check. Generate the fixtures once (see fixtures/FullWorld/README.md):\n" +
            $"  set {FixtureGenerator.EnvVar}=1\n" +
            "  dotnet test canclaimsextended.scenarios --filter \"Category=FixtureGen\"\n" +
            "It writes the .db files into the source tree (commit them) and next to the tests, so the next run finds them.");
    }
}
