using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.network;
using canclaimsextended.src.part;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// City arms: /city set emblem with valid layers (O1), each strict refusal of EmblemHandler.TryParse
/// and the right (O2), removal without layers (O3), a palette of the host's own (O4) and the
/// EmblemsPacket every client gets on a change and on joining (O5). Section O of
/// TESTING_canclaimsextended.md.
///
/// What the GUI editor draws is client side and is not checked here; the packet it is drawn from is.
/// </summary>
[Trait("Category", "E2E")]
public class EmblemScenarios : CxScenarioBase
{
    private const string Valid = "color_red;lion_left_yellow;border_black";

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        var patterns = new HashSet<string>(c.EMBLEM_AVAILABLE_PATTERNS);
        var colors = new HashSet<string>(c.EMBLEM_AVAILABLE_COLORS);
        int maxLayers = c.EMBLEM_MAX_LAYERS;
        AddCleanup(() =>
        {
            c.EMBLEM_AVAILABLE_PATTERNS.Clear();
            c.EMBLEM_AVAILABLE_PATTERNS.UnionWith(patterns);
            c.EMBLEM_AVAILABLE_COLORS.Clear();
            c.EMBLEM_AVAILABLE_COLORS.UnionWith(colors);
            c.EMBLEM_MAX_LAYERS = maxLayers;
        });
        // The built-in palette (empty lists) and the default limit, whatever the host's file says.
        c.EMBLEM_AVAILABLE_PATTERNS.Clear();
        c.EMBLEM_AVAILABLE_COLORS.Clear();
        c.EMBLEM_MAX_LAYERS = 6;
    }

    /// <summary>The arms of the city in the latest EmblemsPacket the player got, null when none came or it lacks the city.</summary>
    private static string? LatestEmblemOf(ITestPlayer player, City city)
    {
        EmblemsPacket? last = player.Client.Packets<EmblemsPacket>(NetworkSetup.CHANNEL).LastOrDefault();
        if (last == null) return null;
        int i = last.CityGuids.IndexOf(city.Guid);
        return i < 0 ? null : last.Emblems[i];
    }

    private static string StoredEmblem(City city)
    {
        DbFile.Flush();
        return DbFile.Str(DbFile.Rows("SELECT emblem FROM CITIES WHERE guid=@g", ("@g", city.Guid)).Single(), "emblem");
    }

    // ---- O1 ----------------------------------------------------------------------------------

    // O1: valid layers become the city's arms, in memory and in the database
    [AtlasScenario]
    public async Task SetEmblem_Should_StoreTheLayers_When_TheyAreValid()
    {
        // Arrange
        CityScene s = await GivenCity();

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {Valid}");

        // Assert
        Assert.True(result.Ok, "/city set emblem: " + result.Message);
        Assert.Equal("canclaimsextended:emblem_set", result.Raw.StatusMessage);
        Assert.Equal(Valid, s.City.Emblem);
        Assert.Equal(Valid, StoredEmblem(s.City));
        AssertInvariants();
    }

    // O1: the change reaches every online client at once, the mayor and a citizen alike
    [AtlasScenario]
    public async Task SetEmblem_Should_ReachEveryClient()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        s.Mayor.Client.Clear();
        citizen.Client.Clear();

        // Act
        await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {Valid}");

        // Assert
        await World.Until(() => LatestEmblemOf(s.Mayor, s.City) == Valid && LatestEmblemOf(citizen, s.City) == Valid,
            "both clients to get the new arms", 20);
        EmblemsPacket packet = citizen.Client.Packets<EmblemsPacket>(NetworkSetup.CHANNEL).Last();
        Assert.Equal(6, packet.MaxLayers);
        Assert.Contains("lion_left", packet.Patterns);
        Assert.Contains("yellow", packet.Colors);
    }

    // O5: a player who comes in later gets the arms of every city on joining
    [AtlasScenario]
    public async Task Joining_Should_SendTheArmsOfEveryCity()
    {
        // Arrange
        CityScene s = await GivenCity();
        await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {Valid}");
        Assert.Equal(Valid, s.City.Emblem);

        // Act
        ITestPlayer newcomer = await GivenStranger(s, "newcomer");

        // Assert
        await World.Until(() => LatestEmblemOf(newcomer, s.City) == Valid, "the newcomer to get the arms", 40);
    }

    // ---- O2 ----------------------------------------------------------------------------------

    // O2: each strict refusal names its lang key and the layer (or the limit) at fault; nothing changes
    [AtlasTheory]
    [InlineData("dragon_red", "canclaimsextended:emblem_bad_layer", "dragon_red")]
    [InlineData("color_purplish", "canclaimsextended:emblem_bad_layer", "color_purplish")]
    [InlineData("color_red;../../x_red", "canclaimsextended:emblem_bad_layer", "../../x_red")]
    [InlineData("cross_white", "canclaimsextended:emblem_need_background", "cross_white")]
    [InlineData("color_red;cross_white;color_blue", "canclaimsextended:emblem_background_only_first", "color_blue")]
    public async Task SetEmblem_Should_BeRefused_When_LayersAreWrong(string layers, string key, string arg)
    {
        // Arrange: the city has arms already, so a refusal must leave them
        CityScene s = await GivenCity();
        await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {Valid}");
        Assert.Equal(Valid, s.City.Emblem);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {layers}");

        // Assert
        Assert.False(result.Ok, $"'{layers}' was taken: " + result.Message);
        Assert.Equal(key, result.Raw.StatusMessage);
        Assert.Equal(arg, Convert.ToString(Assert.Single(result.Raw.MessageParams)));
        Assert.Equal(Valid, s.City.Emblem);
        Assert.Equal(Valid, StoredEmblem(s.City));
    }

    // O2: EMBLEM_MAX_LAYERS layers go in, one more is emblem_too_many_layers with the limit
    [AtlasScenario]
    public async Task SetEmblem_Should_BeRefused_When_OverTheLayerLimit()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.config.EMBLEM_MAX_LAYERS = 3;
        const string three = "color_red;cross_white;border_black";
        const string four = three + ";skull_yellow";

        // Act
        CommandResult refused = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {four}");
        string afterRefusal = s.City.Emblem;
        CommandResult accepted = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {three}");

        // Assert
        Assert.False(refused.Ok, "four layers were taken under a limit of three: " + refused.Message);
        Assert.Equal("canclaimsextended:emblem_too_many_layers", refused.Raw.StatusMessage);
        Assert.Equal("3", Convert.ToString(Assert.Single(refused.Raw.MessageParams)));
        Assert.Equal("", afterRefusal);
        Assert.True(accepted.Ok, "exactly EMBLEM_MAX_LAYERS layers were refused: " + accepted.Message);
        Assert.Equal(three, s.City.Emblem);
    }

    // O2: a citizen without CITY_SET_EMBLEM changes nothing; with a rank holding it, he does
    [AtlasScenario]
    public async Task SetEmblem_Should_FollowTheRight()
    {
        // Arrange
        CityScene s = await GivenCity("herald");
        ITestPlayer herald = s.Citizens[0];

        // Act: without the right
        CommandResult refused = await herald.ExecuteCommand($"{CityCmd} set emblem {Valid}");

        // Assert
        Assert.False(refused.Ok, "a citizen without CITY_SET_EMBLEM set the arms: " + refused.Message);
        Assert.Equal("canclaimsextended:you_dont_have_right_for_that_command", refused.Raw.StatusMessage);
        Assert.Equal("", s.City.Emblem);

        // Act: with a rank holding it
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create Herald");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Herald CITY_SET_EMBLEM");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant Herald {Name(herald)}");
        Assert.True(Info(herald).hasCityTitle("Herald"), "precondition: the citizen holds the rank");
        CommandResult allowed = await herald.ExecuteCommand($"{CityCmd} set emblem {Valid}");

        // Assert
        Assert.True(allowed.Ok, "/city set emblem with the rank: " + allowed.Message);
        Assert.Equal(Valid, s.City.Emblem);
    }

    // ---- O3 ----------------------------------------------------------------------------------

    // O3: no layers remove the arms - in memory, in the database and from the next packet
    [AtlasScenario]
    public async Task SetEmblem_Should_RemoveTheArms_When_NoLayersAreGiven()
    {
        // Arrange
        CityScene s = await GivenCity();
        await s.Mayor.ExecuteCommand($"{CityCmd} set emblem {Valid}");
        Assert.Equal(Valid, s.City.Emblem);
        s.Mayor.Client.Clear();

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem");

        // Assert
        Assert.True(result.Ok, "/city set emblem without layers: " + result.Message);
        Assert.Equal("canclaimsextended:emblem_removed", result.Raw.StatusMessage);
        Assert.Equal("", s.City.Emblem);
        Assert.Equal("", StoredEmblem(s.City));
        // A packet came (the positive control) and it no longer lists the city.
        await World.Until(() => s.Mayor.Client.Packets<EmblemsPacket>(NetworkSetup.CHANNEL).Count > 0, "the packet after the removal", 20);
        Assert.Null(LatestEmblemOf(s.Mayor, s.City));
    }

    // ---- O4 ----------------------------------------------------------------------------------

    // O4: a palette of the host's own replaces the built-in one - its colors pass, the others do not
    [AtlasScenario]
    public async Task SetEmblem_Should_FollowTheHostsPalette()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.config.EMBLEM_AVAILABLE_COLORS.Add("red");
        Mod.config.EMBLEM_AVAILABLE_COLORS.Add("white");

        // Act
        CommandResult builtIn = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem color_blue");
        CommandResult own = await s.Mayor.ExecuteCommand($"{CityCmd} set emblem color_red;cross_white");

        // Assert
        Assert.False(builtIn.Ok, "a built-in color outside the host's palette was taken: " + builtIn.Message);
        Assert.Equal("canclaimsextended:emblem_bad_layer", builtIn.Raw.StatusMessage);
        Assert.True(own.Ok, "the host's own colors were refused: " + own.Message);
        Assert.Equal("color_red;cross_white", s.City.Emblem);
    }
}
