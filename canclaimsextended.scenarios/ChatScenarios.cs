using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Section U of TESTING_canclaimsextended.md, the chat half (the city log is in CityLogScenarios):
/// /cc with a text sends one line to the city, /cc alone toggles city mode and /gc goes back to
/// general chat (U1); in general chat the city tag and the titles of /city set prefix|aftername go
/// around the name, unless SHOW_CITY_NAME_IN_CHAT is off (U2); city mode falls back to general
/// once the player has no city, and a disconnect resets it (U3).
///
/// Lines are read from what each test player's connection received (Client.Chat), so they are the
/// lines the server really sent. Plain chat goes through ITestPlayer.Say, the client's own packet,
/// so the server's PlayerChat event - where the mod reroutes and decorates - runs as in the game.
/// </summary>
[Trait("Category", "E2E")]
public class ChatScenarios : CxScenarioBase
{
    private static string CcCmd => "/" + Mod.config.COMMAND_CITY_CHAT;
    private static string GcCmd => "/" + Mod.config.COMMAND_GENERAL_CHAT;

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static ChatMode Mode(ITestPlayer p) => ChatModes.Get(p.Player.PlayerUID);

    /// <summary>City mode is held in memory per uid; put the player back to general after the scenario.</summary>
    private void ResetModeAfterwards(ITestPlayer p)
    {
        string uid = p.Player.PlayerUID;
        AddCleanup(() => ChatModes.Set(uid, ChatMode.General));
    }

    /// <summary>The city tag a line starts with: [City] in bold and the city color.</summary>
    private static string CityTag(City city) => MessageHandler.Tag(city.getPartNameReplaceUnder(), Mod.config.CITY_COLOR_NAME);

    /// <summary>The lines this player received that hold the text.</summary>
    private static List<string> Received(ITestPlayer p, string text) =>
        p.Client.ChatLines().Where(l => l.Contains(text, StringComparison.Ordinal)).ToList();

    private static void ClearChat(params ITestPlayer[] players)
    {
        foreach (ITestPlayer p in players) p.Client.Clear();
    }

    // ---- U1: /cc and /gc -------------------------------------------------------------------

    // U1: /cc with a text reaches every citizen online, with the city tag, and nobody else
    [AtlasScenario]
    public async Task CityChat_Should_ReachCitizensOnly()
    {
        // Arrange
        CityScene s = await GivenCity("listener");
        ITestPlayer listener = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        string text = "citynews" + s.Area;
        ClearChat(s.Mayor, listener, stranger);

        // Act
        CommandResult cc = await s.Mayor.ExecuteCommand($"{CcCmd} {text}");
        await World.Ticks(2);

        // Assert
        Assert.True(cc.Ok, "/cc with a text: " + cc.Message);
        foreach (ITestPlayer citizen in new[] { s.Mayor, listener })
        {
            string? line = Received(citizen, text).SingleOrDefault();
            Assert.True(line != null, $"{Name(citizen)} did not get the city line");
            Assert.StartsWith(CityTag(s.City), line);
            Assert.Contains(Name(s.Mayor), line);
        }
        Assert.Empty(Received(stranger, text));
        Assert.True(Mode(s.Mayor) == ChatMode.General, "/cc with a text switched the mode");
    }

    // U1: without a city there is nothing to talk to, neither a line nor the mode
    [AtlasScenario]
    public async Task CityChat_Should_BeRefused_When_PlayerHasNoCity()
    {
        CityScene s = await GivenCity();
        ITestPlayer loner = await GivenStranger(s, "loner");
        ResetModeAfterwards(loner);
        string text = "nobody" + s.Area;
        ClearChat(s.Mayor);

        CommandResult line = await loner.ExecuteCommand($"{CcCmd} {text}");
        CommandResult toggle = await loner.ExecuteCommand(CcCmd);
        await World.Ticks(2);

        AssertRefused(line, "you_dont_have_city", "/cc with a text without a city");
        AssertRefused(toggle, "you_dont_have_city", "/cc without a city");
        Assert.True(Mode(loner) == ChatMode.General, "a player of no city got city mode");
        Assert.Empty(Received(s.Mayor, text));
    }

    // U1: /cc alone toggles city mode on and off; /gc always goes back to general
    [AtlasScenario]
    public async Task CityChat_Should_ToggleTheMode_And_GeneralChat_Should_EndIt()
    {
        // Arrange
        CityScene s = await GivenCity("toggler");
        ITestPlayer toggler = s.Citizens[0];
        ResetModeAfterwards(toggler);

        // Act, Assert: on
        CommandResult on = await toggler.ExecuteCommand(CcCmd);
        Assert.True(on.Ok, "/cc: " + on.Message);
        Assert.Equal("canclaimsextended:chat_mode_city", on.Raw.StatusMessage);
        Assert.True(Mode(toggler) == ChatMode.City, $"after /cc the mode is {Mode(toggler)}");

        // Act, Assert: off again
        CommandResult off = await toggler.ExecuteCommand(CcCmd);
        Assert.True(off.Ok, "/cc again: " + off.Message);
        Assert.Equal("canclaimsextended:chat_mode_general", off.Raw.StatusMessage);
        Assert.True(Mode(toggler) == ChatMode.General, $"after /cc twice the mode is {Mode(toggler)}");

        // Act, Assert: /gc from city mode, and /gc when already general
        await toggler.ExecuteCommand(CcCmd);
        Assert.True(Mode(toggler) == ChatMode.City, "precondition: city mode again");
        CommandResult gc = await toggler.ExecuteCommand(GcCmd);
        Assert.True(gc.Ok, "/gc: " + gc.Message);
        Assert.Equal("canclaimsextended:chat_mode_general", gc.Raw.StatusMessage);
        Assert.True(Mode(toggler) == ChatMode.General, $"after /gc the mode is {Mode(toggler)}");
        CommandResult gcAgain = await toggler.ExecuteCommand(GcCmd);
        Assert.True(gcAgain.Ok && Mode(toggler) == ChatMode.General, "/gc in general chat: " + gcAgain.Message);
    }

    // U1: in city mode a plain chat line goes to the city and not to the world; after /gc it does
    [AtlasScenario]
    public async Task CityMode_Should_SendPlainLinesToTheCityOnly()
    {
        // Arrange
        CityScene s = await GivenCity("whisperer");
        ITestPlayer whisperer = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        ResetModeAfterwards(whisperer);
        await whisperer.ExecuteCommand(CcCmd);
        Assert.True(Mode(whisperer) == ChatMode.City, "precondition: city mode");
        string secret = "secret" + s.Area;
        string open = "open" + s.Area;
        ClearChat(s.Mayor, stranger);

        // Act
        await whisperer.Say(secret);
        await whisperer.ExecuteCommand(GcCmd);
        await whisperer.Say(open);
        await World.Ticks(2);

        // Assert: the city line
        string? cityLine = Received(s.Mayor, secret).SingleOrDefault();
        Assert.True(cityLine != null, "the mayor did not get the city line");
        Assert.StartsWith(CityTag(s.City), cityLine);
        Assert.Contains(Name(whisperer), cityLine);
        Assert.Empty(Received(stranger, secret));
        // Positive control: the stranger does hear general chat
        Assert.NotEmpty(Received(stranger, open));
    }

    // ---- U3: losing the mode ---------------------------------------------------------------

    // U3: city mode, then the player leaves the city: the next line goes to general chat, no tag
    [AtlasScenario]
    public async Task CityMode_Should_FallBackToGeneral_When_PlayerLeftTheCity()
    {
        // Arrange
        CityScene s = await GivenCity("emigrant");
        ITestPlayer emigrant = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        ResetModeAfterwards(emigrant);
        await emigrant.ExecuteCommand(CcCmd);
        Assert.True(Mode(emigrant) == ChatMode.City, "precondition: city mode");
        CommandResult leave = await emigrant.ExecuteCommand($"{CityCmd} leave");
        Assert.True(leave.Ok, "precondition: /city leave: " + leave.Message);
        string text = "bye" + s.Area;
        ClearChat(stranger, s.Mayor);

        // Act
        await emigrant.Say(text);
        await World.Ticks(2);

        // Assert
        Assert.True(Mode(emigrant) == ChatMode.General, $"the mode is still {Mode(emigrant)} after leaving the city");
        string? line = Received(stranger, text).SingleOrDefault();
        Assert.True(line != null, "the line of a player who left the city did not reach general chat");
        Assert.DoesNotContain(CityTag(s.City), line);
        Assert.False(Received(s.Mayor, text).Any(l => l.StartsWith(CityTag(s.City), StringComparison.Ordinal)),
            "the line still went to the city left behind");
        AssertInvariants();
    }

    // U3: the mode lives in memory per player; a disconnect resets it
    [AtlasScenario]
    public async Task CityMode_Should_Reset_When_PlayerDisconnects()
    {
        // Arrange
        CityScene s = await GivenCity("leaver");
        ITestPlayer leaver = s.Citizens[0];
        ResetModeAfterwards(leaver);
        await leaver.ExecuteCommand(CcCmd);
        Assert.True(Mode(leaver) == ChatMode.City, "precondition: city mode");

        // Act
        leaver.Player.Disconnect();
        await World.Until(() => !leaver.IsConnected, "the player to leave", 100);

        // Assert
        Assert.True(ChatModes.Get(leaver.Player.PlayerUID) == ChatMode.General, "city mode outlived the disconnect");
    }

    // ---- U2: tags in general chat ----------------------------------------------------------

    // U2: a citizen's general chat line starts with the city tag; a player of no city gets none
    [AtlasScenario]
    public async Task GeneralChat_Should_PutCityTagFirst_When_PlayerHasCity()
    {
        // Arrange
        CityScene s = await GivenCity("speaker");
        ITestPlayer speaker = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        string fromCitizen = "hellofrom" + s.Area;
        string fromStranger = "hiback" + s.Area;
        ClearChat(s.Mayor, stranger);

        // Act
        await speaker.Say(fromCitizen);
        await stranger.Say(fromStranger);
        await World.Ticks(2);

        // Assert
        string? tagged = Received(stranger, fromCitizen).SingleOrDefault();
        Assert.True(tagged != null, "the stranger did not get the citizen's general chat line");
        Assert.StartsWith(CityTag(s.City), tagged);
        Assert.Contains(Name(speaker), tagged);
        string? plain = Received(s.Mayor, fromStranger).SingleOrDefault();
        Assert.True(plain != null, "the mayor did not get the stranger's general chat line");
        Assert.DoesNotContain(CityTag(s.City), plain);
        Assert.DoesNotContain("<strong>[", plain);
    }

    // U2: SHOW_CITY_NAME_IN_CHAT off - no city tag
    [AtlasScenario]
    public async Task GeneralChat_Should_HaveNoCityTag_When_ShowCityNameIsOff()
    {
        // Arrange
        CityScene s = await GivenCity("quiet");
        ITestPlayer quiet = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        bool before = Mod.config.SHOW_CITY_NAME_IN_CHAT;
        AddCleanup(() => Mod.config.SHOW_CITY_NAME_IN_CHAT = before);
        Mod.config.SHOW_CITY_NAME_IN_CHAT = false;
        string text = "untagged" + s.Area;
        ClearChat(stranger);

        // Act
        await quiet.Say(text);
        await World.Ticks(2);

        // Assert
        string? line = Received(stranger, text).SingleOrDefault();
        Assert.True(line != null, "the stranger did not get the line");
        Assert.DoesNotContain(CityTag(s.City), line);
        Assert.DoesNotContain("[" + s.City.getPartNameReplaceUnder() + "]", line);
    }

    // U2: /city set prefix and aftername put the titles around the name, in their colors
    [AtlasScenario]
    public async Task GeneralChat_Should_WrapNameInTitles_When_TitlesAreSet()
    {
        // Arrange
        CityScene s = await GivenCity("knight");
        ITestPlayer knight = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        CommandResult prefix = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)} Sir");
        CommandResult after = await s.Mayor.ExecuteCommand($"{CityCmd} set aftername {Name(knight)} Bold");
        Assert.True(prefix.Ok && Info(knight).Prefix == "Sir", "precondition: /city set prefix: " + prefix.Message);
        Assert.True(after.Ok && Info(knight).AfterName == "Bold", "precondition: /city set aftername: " + after.Message);
        string text = "titled" + s.Area;
        ClearChat(stranger);

        // Act
        await knight.Say(text);
        await World.Ticks(2);

        // Assert
        string? line = Received(stranger, text).SingleOrDefault();
        Assert.True(line != null, "the stranger did not get the line");
        Assert.StartsWith(CityTag(s.City), line);
        string titled = MessageHandler.Colored("Sir", Mod.config.PREFIX_COLOR_PLAYER) + " " + Name(knight)
            + " " + MessageHandler.Colored("Bold", Mod.config.POSTFIX_COLOR_PLAYER);
        Assert.Contains(titled, line);
    }

    // U2: the titles were given by the city; leaving it drops them, and the next line shows none
    [AtlasScenario]
    public async Task Leave_Should_DropTitles_FromGeneralChat()
    {
        // Arrange
        CityScene s = await GivenCity("exknight");
        ITestPlayer exKnight = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);
        await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(exKnight)} Sir");
        await s.Mayor.ExecuteCommand($"{CityCmd} set aftername {Name(exKnight)} Bold");
        Assert.True(Info(exKnight).Prefix == "Sir" && Info(exKnight).AfterName == "Bold", "precondition: the titles are set");

        // Act
        CommandResult leave = await exKnight.ExecuteCommand($"{CityCmd} leave");
        string text = "untitled" + s.Area;
        ClearChat(stranger);
        await exKnight.Say(text);
        await World.Ticks(2);

        // Assert
        Assert.True(leave.Ok, "/city leave: " + leave.Message);
        Assert.Equal("", Info(exKnight).Prefix);
        Assert.Equal("", Info(exKnight).AfterName);
        string? line = Received(stranger, text).SingleOrDefault();
        Assert.True(line != null, "the stranger did not get the line");
        Assert.DoesNotContain("Sir", line);
        Assert.DoesNotContain("Bold", line);
        Assert.DoesNotContain(CityTag(s.City), line);
        AssertInvariants();
    }

    // U2: setting a title is the mayor's (or CITY_SET_OTHERS_PREFIX); a plain citizen is refused
    [AtlasScenario]
    public async Task SetPrefix_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("upstart", "target");
        ITestPlayer upstart = s.Citizens[0], target = s.Citizens[1];

        CommandResult set = await upstart.ExecuteCommand($"{CityCmd} set prefix {Name(target)} Lord");

        AssertRefused(set, "you_dont_have_right_for_that_command", "/city set prefix without the right");
        Assert.Equal("", Info(target).Prefix);
    }

    // U2: titles go to citizens of the city only
    [AtlasScenario]
    public async Task SetPrefix_Should_BeRefused_When_PlayerIsNotCitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer outsider = await GivenStranger(s, "outsider");

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(outsider)} Sir");

        AssertRefused(set, "player_should_be_in_same_city", "/city set prefix for a player of no city");
        Assert.Equal("", Info(outsider).Prefix);
    }
}
