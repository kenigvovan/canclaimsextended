using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The settings of a city and how it is shown: /city set name (B7, B2 name gates), color,
/// dailymsg, prefix and aftername (B7), open, invmsg and fee (B6), and /city info, here and list
/// (B5). Each setter is refused to a citizen without the right, and what it sets is what the
/// database holds. Section B of TESTING_canclaimsextended.md.
///
/// The paid rename after /agree, a treasury that cannot pay and the default city following the
/// rename are in CityLifecycleScenarios; the outdated rename offer is in AgreeEdgeScenarios. With
/// the default noop economy every rename here is free and runs at once (RunPaid covers both).
/// </summary>
[Trait("Category", "E2E")]
public class CitySettingsScenarios : CxScenarioBase
{
    private const string NoRight = "canclaimsextended:you_dont_have_right_for_that_command";
    // CityCommandSettings.INV_MSG_MAX_LENGTH and CityCommandIdentity.DAILY_MSG_MAX_LENGTH.
    private const int MessageMaxLength = 100;

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        int maxName = c.MAX_LENGTH_CITY_NAME, maxTitle = c.MAX_CITIZEN_TITLE_LENGTH;
        double maxFee = c.MAX_CITY_FEE;
        var blocked = new HashSet<string>(c.BLOCKED_NAMES);
        AddCleanup(() =>
        {
            c.MAX_LENGTH_CITY_NAME = maxName;
            c.MAX_CITIZEN_TITLE_LENGTH = maxTitle;
            c.MAX_CITY_FEE = maxFee;
            c.BLOCKED_NAMES.Clear();
            c.BLOCKED_NAMES.UnionWith(blocked);
        });
        // The defaults of Config, so a host's own file does not change what the cases below mean.
        c.MAX_LENGTH_CITY_NAME = 40;
        c.MAX_CITIZEN_TITLE_LENGTH = 16;
        c.MAX_CITY_FEE = 50;
    }

    /// <summary>Everything a /city set command of this file can change, in one line.</summary>
    private static string Snapshot(City city, PlayerInfo titled) =>
        $"name={city.GetPartName()} color={city.cityColor} daily={city.DailyMsg} inv={city.invMsg} open={city.openCity} " +
        $"fee={city.fee} emblem={city.Emblem} prefix={titled.Prefix} after={titled.AfterName}";

    /// <summary>The mayor makes a rank holding one right and gives it to the citizen.</summary>
    private static async Task GiveRank(CityScene s, ITestPlayer to, string rank, string permission)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create {rank}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm {rank} {permission}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {rank} {Name(to)}");
        Assert.True(Info(to).hasCityTitle(rank), $"precondition: {Name(to)} holds rank {rank}");
    }

    private Task<CommandResult> Rename(CityScene s, string name) =>
        RunPaid(s.Mayor, $"{CityCmd} set name {name}", () => s.City.GetPartName() == name, "the city to be renamed to " + name);

    // ---- rights ------------------------------------------------------------------------------

    // B6, B7, O2: a plain citizen changes none of the city's settings, nor a title
    [AtlasTheory]
    [InlineData("set name {city}x")]
    [InlineData("set color red")]
    [InlineData("set dailymsg Hello citizens")]
    [InlineData("set prefix {mayor} Sir")]
    [InlineData("set aftername {mayor} Wise")]
    [InlineData("set open off")]
    [InlineData("set invmsg Welcome")]
    [InlineData("set fee 5")]
    [InlineData("set emblem color_red")]
    public async Task Setter_Should_BeRefused_When_CitizenHasNoRight(string sub)
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        string before = Snapshot(s.City, Info(s.Mayor));
        string command = sub.Replace("{city}", s.City.GetPartName()).Replace("{mayor}", Name(s.Mayor));

        // Act
        CommandResult result = await citizen.ExecuteCommand($"{CityCmd} {command}");

        // Assert
        Assert.False(result.Ok, $"a citizen without the right ran /city {command}: {result.Message}");
        Assert.Equal(NoRight, result.Raw.StatusMessage);
        Assert.Equal(before, Snapshot(s.City, Info(s.Mayor)));
        AssertInvariants();
    }

    // B7, H6: a rank holding CITY_SET_PLOTS_COLOR lets the citizen recolor the city
    [AtlasScenario]
    public async Task SetColor_Should_GoThrough_When_RankGivesTheRight()
    {
        // Arrange
        CityScene s = await GivenCity("painter");
        ITestPlayer painter = s.Citizens[0];
        await GiveRank(s, painter, "Painter", "CITY_SET_PLOTS_COLOR");
        int expected = ColorUtil.ReverseColorBytes(unchecked((int)0xFF0000FF));

        // Act
        CommandResult result = await painter.ExecuteCommand($"{CityCmd} set color blue");

        // Assert
        Assert.True(result.Ok, "/city set color with the rank: " + result.Message);
        Assert.Equal(expected, s.City.cityColor);
    }

    // ---- B7: name -----------------------------------------------------------------------------

    // B7: a valid new name replaces the old one everywhere: the city, the lookup by name, the database
    [AtlasScenario]
    public async Task SetName_Should_Rename_When_NameIsValidAndFree()
    {
        // Arrange
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        string newName = oldName + "Renamed";

        // Act
        CommandResult result = await Rename(s, newName);

        // Assert
        Assert.True(result.Ok, "/city set name: " + result.Message);
        Assert.Equal(newName, s.City.GetPartName());
        Assert.True(Mod.dataStorage.GetCityByName(newName, out City found) && found.Equals(s.City), "the new name does not lead to the city");
        Assert.False(Mod.dataStorage.CityExistsByName(oldName), "the old name is still taken");
        DbFile.Flush();
        Assert.Equal(newName, DbFile.Str(DbFile.Rows("SELECT name FROM CITIES WHERE guid=@g", ("@g", s.City.Guid)).Single(), "name"));
        AssertInvariants();
    }

    // B7, B2: MAX_LENGTH_CITY_NAME letters are taken, one more is city_name_is_too_long
    [AtlasScenario]
    public async Task SetName_Should_BeRefused_When_NameIsTooLong()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.config.MAX_LENGTH_CITY_NAME = 12;
        string oldName = s.City.GetPartName();
        Assert.True(oldName.Length < 12, "precondition: the city's name is shorter than the limit");
        string tooLong = oldName + new string('q', 13 - oldName.Length);
        string longest = oldName + new string('w', 12 - oldName.Length);

        // Act
        CommandResult refused = await s.Mayor.ExecuteCommand($"{CityCmd} set name {tooLong}");
        string afterRefusal = s.City.GetPartName();
        CommandResult accepted = await Rename(s, longest);

        // Assert
        Assert.False(refused.Ok, $"a name of {tooLong.Length} letters was taken: " + refused.Message);
        Assert.Equal("canclaimsextended:city_name_is_too_long", refused.Raw.StatusMessage);
        Assert.Equal(oldName, afterRefusal);
        Assert.False(Mod.dataStorage.CityExistsByName(tooLong), "the too long name exists");
        Assert.True(accepted.Ok, "a name of exactly MAX_LENGTH_CITY_NAME letters was refused: " + accepted.Message);
        Assert.Equal(longest, s.City.GetPartName());
    }

    // B7, B2: a name with nothing allowed in it (letters, digits, - and _) is invalid_new_city_name
    [AtlasScenario]
    public async Task SetName_Should_BeRefused_When_NameHasOnlyInvalidCharacters()
    {
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set name !!$$%%");

        Assert.False(result.Ok, "a name of special characters was taken: " + result.Message);
        Assert.Equal("canclaimsextended:invalid_new_city_name", result.Raw.StatusMessage);
        Assert.Equal(oldName, s.City.GetPartName());
    }

    // B2 says a name with special characters is invalid_new_city_name. The filter
    // (Filter.FilterName) strips them instead, so "Ab$cd" would rename the city to "Abcd", a name
    // the mayor never typed. This scenario holds the test plan's rule; it fails while the mod strips.
    [AtlasScenario]
    public async Task SetName_Should_BeRefused_When_NameMixesInvalidCharacters()
    {
        // Arrange
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        string typed = oldName + "$x";
        string stripped = oldName + "x";

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set name {typed}");

        // Assert
        Assert.False(result.Ok, $"'{typed}' was taken (the city is now '{s.City.GetPartName()}'): " + result.Message);
        Assert.Equal("canclaimsextended:invalid_new_city_name", result.Raw.StatusMessage);
        Assert.Equal(oldName, s.City.GetPartName());
        Assert.False(Mod.dataStorage.CityExistsByName(stripped), $"the city was renamed to '{stripped}', which nobody typed");
    }

    // B7, B2: a name of BLOCKED_NAMES is refused
    [AtlasScenario]
    public async Task SetName_Should_BeRefused_When_NameIsBlocked()
    {
        CityScene s = await GivenCity();
        string oldName = s.City.GetPartName();
        string blocked = oldName + "Blocked";
        Mod.config.BLOCKED_NAMES.Add(blocked);

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set name {blocked}");

        Assert.False(result.Ok, "a blocked name was taken: " + result.Message);
        Assert.Equal("canclaimsextended:invalid_new_city_name", result.Raw.StatusMessage);
        Assert.Equal(oldName, s.City.GetPartName());
    }

    // B7, B2: the name of another city is city_name_is_already_taken, and both cities keep theirs
    [AtlasScenario]
    public async Task SetName_Should_BeRefused_When_NameIsTaken()
    {
        // Arrange
        CityScene first = await GivenCity();
        CityScene second = await GivenCity();
        string firstName = first.City.GetPartName(), secondName = second.City.GetPartName();

        // Act
        CommandResult result = await second.Mayor.ExecuteCommand($"{CityCmd} set name {firstName}");

        // Assert
        Assert.False(result.Ok, "a city took the name of another one: " + result.Message);
        Assert.Equal("canclaimsextended:city_name_is_already_taken", result.Raw.StatusMessage);
        Assert.Equal(secondName, second.City.GetPartName());
        Assert.True(Mod.dataStorage.GetCityByName(firstName, out City named) && named.Equals(first.City),
            "the name no longer leads to the first city");
        AssertInvariants();
    }

    // ---- B7: color ----------------------------------------------------------------------------

    // B7: a color name and #rrggbb are stored as the map draws them (ARGB, bytes reversed)
    [AtlasTheory]
    [InlineData("red", 0xFFFF0000u)]
    [InlineData("#ff8800", 0xFFFF8800u)]
    public async Task SetColor_Should_StoreTheColor_When_ItIsKnown(string typed, uint argb)
    {
        // Arrange
        CityScene s = await GivenCity();
        int expected = ColorUtil.ReverseColorBytes(unchecked((int)argb));

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set color {typed}");

        // Assert
        Assert.True(result.Ok, $"/city set color {typed}: " + result.Message);
        Assert.Equal("canclaimsextended:city_color_set", result.Raw.StatusMessage);
        Assert.Equal(expected, s.City.cityColor);
        DbFile.Flush();
        Assert.Equal((long)expected, DbFile.Long(DbFile.Rows("SELECT citycolor FROM CITIES WHERE guid=@g", ("@g", s.City.Guid)).Single(), "citycolor"));
    }

    // B7: an unknown name or a broken #code is city_color_bad and the color stays
    [AtlasTheory]
    [InlineData("xyz")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    public async Task SetColor_Should_BeRefused_When_ColorIsUnknown(string typed)
    {
        CityScene s = await GivenCity();
        int before = s.City.cityColor;

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set color {typed}");

        Assert.False(result.Ok, $"color '{typed}' was taken: " + result.Message);
        Assert.Equal("canclaimsextended:city_color_bad", result.Raw.StatusMessage);
        Assert.Equal(before, s.City.cityColor);
    }

    // ---- B7: daily message -------------------------------------------------------------------

    // B7: a message up to 100 letters is set, one letter more is refused, and nothing removes it
    [AtlasScenario]
    public async Task DailyMsg_Should_BeSet_UpToTheLimit_And_RemovedWithoutText()
    {
        // Arrange
        CityScene s = await GivenCity();
        string longest = new('a', MessageMaxLength);
        string tooLong = new('b', MessageMaxLength + 1);

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg Hello citizens");
        string afterSet = s.City.DailyMsg;
        CommandResult refused = await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg {tooLong}");
        string afterRefusal = s.City.DailyMsg;
        CommandResult atLimit = await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg {longest}");
        string afterLimit = s.City.DailyMsg;
        CommandResult reset = await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg");

        // Assert
        Assert.True(set.Ok, "/city set dailymsg: " + set.Message);
        Assert.Equal("Hello citizens", afterSet);
        Assert.False(refused.Ok, $"a daily message of {tooLong.Length} letters was taken: " + refused.Message);
        Assert.Equal("canclaimsextended:inv_msg_too_long", refused.Raw.StatusMessage);
        Assert.Equal("Hello citizens", afterRefusal);
        Assert.True(atLimit.Ok, $"a daily message of {MessageMaxLength} letters was refused: " + atLimit.Message);
        Assert.Equal(longest, afterLimit);
        Assert.True(reset.Ok, "/city set dailymsg without text: " + reset.Message);
        Assert.Equal("canclaimsextended:city_daily_msg_reset", reset.Raw.StatusMessage);
        Assert.Equal("", s.City.DailyMsg);
    }

    // B7 sets "/city set dailymsg Привет" and expects it on the next login. The filter
    // (Filter.FilterNameWithSpaces) keeps only a-z, 0-9, '-', '_' and spaces, so a Cyrillic text
    // filters down to nothing and is refused as invalid_string. This scenario holds the test
    // plan's rule; it fails while the filter drops non-Latin letters.
    [AtlasScenario]
    public async Task DailyMsg_Should_KeepCyrillicText()
    {
        CityScene s = await GivenCity();

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg Привет");

        Assert.True(result.Ok, "a Cyrillic daily message was refused: " + result.Raw.StatusMessage);
        Assert.Equal("Привет", s.City.DailyMsg);
    }

    // ---- B7: titles ---------------------------------------------------------------------------

    // B7: prefix and aftername are set on the citizen, kept in the database, and removed without text
    [AtlasScenario]
    public async Task Titles_Should_BeSetOnTheCitizen_And_RemovedWithoutText()
    {
        // Arrange
        CityScene s = await GivenCity("knight");
        ITestPlayer knight = s.Citizens[0];

        // Act
        CommandResult prefix = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)} Sir");
        CommandResult after = await s.Mayor.ExecuteCommand($"{CityCmd} set aftername {Name(knight)} the Wise");

        // Assert
        Assert.True(prefix.Ok, "/city set prefix: " + prefix.Message);
        Assert.True(after.Ok, "/city set aftername: " + after.Message);
        Assert.Equal("canclaimsextended:title_set", prefix.Raw.StatusMessage);
        Assert.Equal("Sir", Info(knight).Prefix);
        Assert.Equal("the Wise", Info(knight).AfterName);
        DbFile.Flush();
        var row = DbFile.Rows("SELECT prefix, aftername FROM PLAYERS WHERE uid=@u", ("@u", Info(knight).Guid)).Single();
        Assert.Equal("Sir", DbFile.Str(row, "prefix"));
        Assert.Equal("the Wise", DbFile.Str(row, "aftername"));

        // Act: no text takes the prefix off
        CommandResult removed = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)}");

        // Assert
        Assert.True(removed.Ok, "/city set prefix without text: " + removed.Message);
        Assert.Equal("canclaimsextended:title_removed", removed.Raw.StatusMessage);
        Assert.Equal("", Info(knight).Prefix);
        Assert.Equal("the Wise", Info(knight).AfterName);
    }

    // B7: a title longer than MAX_CITIZEN_TITLE_LENGTH is title_too_long; exactly the limit goes in
    [AtlasScenario]
    public async Task Title_Should_BeRefused_When_LongerThanTheLimit()
    {
        // Arrange
        CityScene s = await GivenCity("knight");
        ITestPlayer knight = s.Citizens[0];
        int max = Mod.config.MAX_CITIZEN_TITLE_LENGTH;
        string longest = new('a', max), tooLong = new('b', max + 1);

        // Act
        CommandResult refused = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)} {tooLong}");
        string afterRefusal = Info(knight).Prefix;
        CommandResult accepted = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)} {longest}");

        // Assert
        Assert.False(refused.Ok, $"a title of {tooLong.Length} letters was taken: " + refused.Message);
        Assert.Equal("canclaimsextended:title_too_long", refused.Raw.StatusMessage);
        Assert.Equal("", afterRefusal ?? "");
        Assert.True(accepted.Ok, $"a title of {max} letters was refused: " + accepted.Message);
        Assert.Equal(longest, Info(knight).Prefix);
    }

    // B7: a player who is not a citizen of the city gets no title from it
    [AtlasScenario]
    public async Task Title_Should_BeRefused_When_TargetIsNotACitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        string before = Info(stranger).Prefix ?? "";

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(stranger)} Sir");

        Assert.False(result.Ok, "a stranger got a title from the city: " + result.Message);
        Assert.Equal("canclaimsextended:player_should_be_in_same_city", result.Raw.StatusMessage);
        Assert.Equal(before, Info(stranger).Prefix ?? "");
    }

    // ---- B6: open, invmsg, fee ----------------------------------------------------------------

    // B6: /city set open off|on switches the city, with city_now_closed and city_now_open
    [AtlasScenario]
    public async Task SetOpen_Should_SwitchTheCity()
    {
        // Arrange: GivenCity opened it for the citizens to join
        CityScene s = await GivenCity();
        Assert.True(s.City.openCity, "precondition: the city is open");

        // Act
        CommandResult close = await s.Mayor.ExecuteCommand($"{CityCmd} set open off");
        bool afterClose = s.City.openCity;
        CommandResult open = await s.Mayor.ExecuteCommand($"{CityCmd} set open on");

        // Assert
        Assert.True(close.Ok, "/city set open off: " + close.Message);
        Assert.Equal("canclaimsextended:city_now_closed", close.Raw.StatusMessage);
        Assert.False(afterClose, "the city is still open");
        Assert.True(open.Ok, "/city set open on: " + open.Message);
        Assert.Equal("canclaimsextended:city_now_open", open.Raw.StatusMessage);
        Assert.True(s.City.openCity, "the city is still closed");
    }

    // B6: an invitation message up to 100 letters, one more is inv_msg_too_long, nothing removes it
    [AtlasScenario]
    public async Task InvMsg_Should_BeSet_UpToTheLimit_And_RemovedWithoutText()
    {
        // Arrange
        CityScene s = await GivenCity();
        string tooLong = new('c', MessageMaxLength + 1);
        string longest = new('d', MessageMaxLength);

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg Come and live with us");
        string afterSet = s.City.invMsg;
        CommandResult refused = await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg {tooLong}");
        string afterRefusal = s.City.invMsg;
        CommandResult atLimit = await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg {longest}");
        string afterLimit = s.City.invMsg;
        CommandResult reset = await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg");

        // Assert
        Assert.True(set.Ok, "/city set invmsg: " + set.Message);
        Assert.Equal("Come and live with us", afterSet);
        Assert.False(refused.Ok, $"an invitation message of {tooLong.Length} letters was taken: " + refused.Message);
        Assert.Equal("canclaimsextended:inv_msg_too_long", refused.Raw.StatusMessage);
        Assert.Equal(MessageMaxLength, Convert.ToInt32(refused.Raw.MessageParams[0]));
        Assert.Equal("Come and live with us", afterRefusal);
        Assert.True(atLimit.Ok, $"an invitation message of {MessageMaxLength} letters was refused: " + atLimit.Message);
        Assert.Equal(longest, afterLimit);
        Assert.True(reset.Ok, "/city set invmsg without text: " + reset.Message);
        Assert.Equal("canclaimsextended:city_inv_msg_reset", reset.Raw.StatusMessage);
        Assert.Equal("", s.City.invMsg);
    }

    // B6: a fee from 0 to MAX_CITY_FEE is taken and logged as FeeChanged; above or below is out of range
    [AtlasScenario]
    public async Task SetFee_Should_KeepWithinZeroAndMaxCityFee()
    {
        // Arrange
        CityScene s = await GivenCity();
        int max = (int)Mod.config.MAX_CITY_FEE;

        // Act
        CommandResult atMax = await s.Mayor.ExecuteCommand($"{CityCmd} set fee {max}");
        int afterMax = s.City.fee;
        CityLogEntry? logged = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.FeeChanged);
        CommandResult over = await s.Mayor.ExecuteCommand($"{CityCmd} set fee {max + 1}");
        CommandResult negative = await s.Mayor.ExecuteCommand($"{CityCmd} set fee -1");
        int afterRefusals = s.City.fee;
        CommandResult zero = await s.Mayor.ExecuteCommand($"{CityCmd} set fee 0");

        // Assert
        Assert.True(atMax.Ok, $"a fee of exactly MAX_CITY_FEE ({max}) was refused: " + atMax.Message);
        Assert.Equal(max, afterMax);
        Assert.NotNull(logged);
        Assert.Equal(new[] { Name(s.Mayor), max.ToString() }, logged!.Args);
        Assert.False(over.Ok, $"a fee of {max + 1} was taken: " + over.Message);
        Assert.Equal("canclaimsextended:city_fee_out_of_range", over.Raw.StatusMessage);
        Assert.False(negative.Ok, "a negative fee was taken: " + negative.Message);
        Assert.Equal(max, afterRefusals);
        Assert.True(zero.Ok, "/city set fee 0: " + zero.Message);
        Assert.Equal(0, s.City.fee);
    }

    // ---- B5: info, here, list -----------------------------------------------------------------

    // B5: /city info shows the own city: its name, mayor and invitation message
    [AtlasScenario]
    public async Task Info_Should_ShowTheOwnCity()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg Join us");

        // Act
        CommandResult result = await s.Citizens[0].ExecuteCommand($"{CityCmd} info");

        // Assert: the status is built as text, not a lang key, so StatusMessage is the text itself
        Assert.True(result.Ok, "/city info: " + result.Message);
        string text = result.Raw.StatusMessage;
        Assert.Contains("[" + s.City.GetPartName() + "]", text);
        Assert.Contains(Name(s.Mayor), text);
        Assert.Contains(Name(s.Citizens[0]), text);
        Assert.Contains("Join us", text);
    }

    // B5: /city info <name> shows another city to anyone; an unknown name is no_such_city;
    // without a name a player of no city is told so
    [AtlasScenario]
    public async Task Info_Should_ShowAnyCityByName_And_RefuseUnknownNames()
    {
        // Arrange
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);

        // Act
        CommandResult named = await stranger.ExecuteCommand($"{CityCmd} info {s.City.GetPartName()}");
        CommandResult unknown = await stranger.ExecuteCommand($"{CityCmd} info {s.City.GetPartName()}Nowhere");
        CommandResult own = await stranger.ExecuteCommand($"{CityCmd} info");

        // Assert
        Assert.True(named.Ok, "/city info <name>: " + named.Message);
        Assert.Contains("[" + s.City.GetPartName() + "]", named.Raw.StatusMessage);
        Assert.False(unknown.Ok, "an unknown city was shown: " + unknown.Message);
        Assert.Equal("canclaimsextended:no_such_city", unknown.Raw.StatusMessage);
        Assert.False(own.Ok, "/city info without a city was not refused: " + own.Message);
        Assert.Equal("canclaimsextended:you_dont_have_city", own.Raw.StatusMessage);
    }

    // B5: /city here names the city whose land the player stands on, and no_city_here in the wild
    [AtlasScenario]
    public async Task Here_Should_NameTheCityUnderfoot_And_NothingInTheWild()
    {
        // Arrange
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        await stranger.TeleportTo(AreaOrigin(s.Area));

        // Act
        CommandResult inCity = await stranger.ExecuteCommand($"{CityCmd} here");
        // Ten chunks along Z: free land, far from any city (areas lie on X).
        await stranger.TeleportTo(AreaOrigin(s.Area).AddCopy(0, 0, 10 * 16));
        CommandResult inWild = await stranger.ExecuteCommand($"{CityCmd} here");

        // Assert
        Assert.True(inCity.Ok, "/city here on the city's land: " + inCity.Message);
        Assert.Contains("[" + s.City.GetPartName() + "]", inCity.Raw.StatusMessage);
        Assert.False(inWild.Ok, "/city here in the wild named a city: " + inWild.Message);
        Assert.Equal("canclaimsextended:no_city_here", inWild.Raw.StatusMessage);
    }

    // B5: /city list names every city
    [AtlasScenario]
    public async Task List_Should_NameEveryCity()
    {
        // Arrange
        CityScene first = await GivenCity();
        CityScene second = await GivenCity();

        // Act
        CommandResult result = await first.Mayor.ExecuteCommand($"{CityCmd} list");

        // Assert
        Assert.True(result.Ok, "/city list: " + result.Message);
        Assert.Equal("canclaimsextended:cities_list", result.Raw.StatusMessage);
        string names = Convert.ToString(result.Raw.MessageParams[0]) ?? "";
        Assert.Contains(first.City.GetPartName(), names.Split(", "));
        Assert.Contains(second.City.GetPartName(), names.Split(", "));
    }

    // ---- Y1: persisted ------------------------------------------------------------------------

    // B6, B7, Y1: after every setter the CITIES row and the titled citizen's PLAYERS row hold what memory holds
    [AtlasScenario]
    public async Task Settings_Should_BeStoredAsInMemory()
    {
        // Arrange
        CityScene s = await GivenCity("knight");
        ITestPlayer knight = s.Citizens[0];
        string newName = s.City.GetPartName() + "Stored";

        // Act
        await Rename(s, newName);
        await s.Mayor.ExecuteCommand($"{CityCmd} set color #336699");
        await s.Mayor.ExecuteCommand($"{CityCmd} set dailymsg Good morning");
        await s.Mayor.ExecuteCommand($"{CityCmd} set invmsg Come in");
        await s.Mayor.ExecuteCommand($"{CityCmd} set open off");
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 7");
        await s.Mayor.ExecuteCommand($"{CityCmd} set emblem color_blue;cross_white");
        await s.Mayor.ExecuteCommand($"{CityCmd} set prefix {Name(knight)} Sir");
        await s.Mayor.ExecuteCommand($"{CityCmd} set aftername {Name(knight)} Bold");
        Assert.True(s.City.GetPartName() == newName && s.City.DailyMsg == "Good morning" && s.City.invMsg == "Come in"
            && !s.City.openCity && s.City.fee == 7 && s.City.Emblem == "color_blue;cross_white"
            && Info(knight).Prefix == "Sir" && Info(knight).AfterName == "Bold",
            "precondition: every setter went through: " + Snapshot(s.City, Info(knight)));
        DbFile.Flush();

        // Assert
        var city = DbFile.Rows("SELECT * FROM CITIES WHERE guid=@g", ("@g", s.City.Guid)).Single();
        Assert.Equal(newName, DbFile.Str(city, "name"));
        Assert.Equal((long)s.City.cityColor, DbFile.Long(city, "citycolor"));
        Assert.Equal("Good morning", DbFile.Str(city, "dailymsg"));
        Assert.Equal("Come in", DbFile.Str(city, "invmsg"));
        Assert.Equal(0L, DbFile.Long(city, "opencity"));
        Assert.Equal(7L, DbFile.Long(city, "fee"));
        Assert.Equal("color_blue;cross_white", DbFile.Str(city, "emblem"));
        var player = DbFile.Rows("SELECT * FROM PLAYERS WHERE uid=@u", ("@u", Info(knight).Guid)).Single();
        Assert.Equal("Sir", DbFile.Str(player, "prefix"));
        Assert.Equal("Bold", DbFile.Str(player, "aftername"));
        AssertInvariants();
    }
}
