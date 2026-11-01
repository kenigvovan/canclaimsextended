using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Citizens and invitations: invite and join (P1), its gates (P2), withdrawing and refusing (P3),
/// open cities (P5), leaving and kicking (P6) and the default city of new players (P7).
/// Section P of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class MembershipScenarios : CxScenarioBase
{
    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static bool Invited(City city, ITestPlayer player) =>
        InvitationHandler.tryGetInvitation(city, Info(player), out _);

    private static CityLogEntry? Last(City city, EnumCityLogEvent type) =>
        city.EventLog.LastOrDefault(e => e.EventType == type);

    /// <summary>The mayor makes a rank holding the given rights and gives it to the player.</summary>
    private static async Task GiveRank(CityScene s, ITestPlayer to, string rank, params string[] permissions)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create {rank}");
        foreach (string permission in permissions)
        {
            await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm {rank} {permission}");
        }
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {rank} {Name(to)}");
        Assert.True(Info(to).hasCityTitle(rank), $"precondition: {Name(to)} holds rank {rank}");
    }

    /// <summary>The city takes nobody without an invitation.</summary>
    private static async Task CloseCity(CityScene s)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} set open off");
        Assert.False(s.City.openCity, "precondition: the city is closed");
    }

    // ---- P1 --------------------------------------------------------------------------------

    // P1: the mayor invites, the player sees it in /city invites and joins; the join is logged
    [AtlasScenario]
    public async Task Invitation_Should_MakeCitizen_When_PlayerJoins()
    {
        // Arrange
        CityScene s = await GivenCity();
        await CloseCity(s);
        ITestPlayer guest = await GivenStranger(s, "guest");

        // Act
        CommandResult invite = await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(guest)}");
        CommandResult invites = await guest.ExecuteCommand($"{CityCmd} invites");
        CommandResult join = await guest.ExecuteCommand($"{CityCmd} join {s.City.GetPartName()}");

        // Assert
        Assert.True(invite.Ok, "/city invite: " + invite.Message);
        Assert.True(invites.Ok, "/city invites: " + invites.Message);
        Assert.Contains(s.City.GetPartName(), invites.Message);
        Assert.True(join.Ok, "/city join: " + join.Message);
        Assert.True(s.City.Equals(Info(guest).City), "the invited player did not become a citizen");
        Assert.Contains(Info(guest), s.City.getCityCitizens());
        Assert.False(Invited(s.City, guest), "the invitation outlived the join");
        CityLogEntry? joined = Last(s.City, EnumCityLogEvent.CitizenJoined);
        Assert.NotNull(joined);
        Assert.Equal(new[] { Name(guest) }, joined!.Args);
        AssertInvariants();
    }

    // ---- P2 --------------------------------------------------------------------------------

    // P2: a player of another city cannot be invited
    [AtlasScenario]
    public async Task Invite_Should_BeRefused_When_PlayerHasCityAlready()
    {
        CityScene ours = await GivenCity();
        CityScene theirs = await GivenCity();

        CommandResult invite = await ours.Mayor.ExecuteCommand($"{CityCmd} invite {Name(theirs.Mayor)}");

        AssertRefused(invite, "player_has_city_already", "inviting a player of another city");
        Assert.False(Invited(ours.City, theirs.Mayor), "an invitation was stored for a player with a city");
    }

    // P2: the same player twice
    [AtlasScenario]
    public async Task Invite_Should_BeRefused_When_PlayerIsInvitedAlready()
    {
        CityScene s = await GivenCity();
        ITestPlayer guest = await GivenStranger(s, "twice");
        await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(guest)}");
        Assert.True(Invited(s.City, guest), "precondition: the first invitation was sent");

        CommandResult again = await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(guest)}");

        AssertRefused(again, "player_already_invited", "a second invitation");
        Assert.Single(Info(guest).getReceivedInvitations());
    }

    // ---- P3 --------------------------------------------------------------------------------

    // P3: a withdrawn invitation cannot be accepted
    [AtlasScenario]
    public async Task Join_Should_BeRefused_When_InvitationWasWithdrawn()
    {
        // Arrange
        CityScene s = await GivenCity();
        await CloseCity(s);
        ITestPlayer guest = await GivenStranger(s, "withdrawn");
        await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(guest)}");

        // Act
        CommandResult uninvite = await s.Mayor.ExecuteCommand($"{CityCmd} uninvite {Name(guest)}");
        CommandResult join = await guest.ExecuteCommand($"{CityCmd} join {s.City.GetPartName()}");

        // Assert
        Assert.True(uninvite.Ok, "/city uninvite: " + uninvite.Message);
        Assert.False(Invited(s.City, guest), "the invitation is still there");
        AssertRefused(join, "city_not_open", "joining a closed city after the invitation was withdrawn");
        Assert.False(Info(guest).hasCity(), "the player joined with a withdrawn invitation");
        AssertInvariants();
    }

    // P3: the player refuses; the invitation is gone and nothing else changes
    [AtlasScenario]
    public async Task Deny_Should_DropTheInvitation_And_KeepPlayerOut()
    {
        CityScene s = await GivenCity();
        await CloseCity(s);
        ITestPlayer guest = await GivenStranger(s, "refuser");
        await s.Mayor.ExecuteCommand($"{CityCmd} invite {Name(guest)}");

        CommandResult deny = await guest.ExecuteCommand($"{CityCmd} deny {s.City.GetPartName()}");

        Assert.True(deny.Ok, "/city deny: " + deny.Message);
        Assert.False(Invited(s.City, guest), "the refused invitation is still there");
        Assert.Empty(s.City.GetSentInvitations());
        Assert.False(Info(guest).hasCity(), "refusing made the player a citizen");
        AssertInvariants();
    }

    // ---- P5 --------------------------------------------------------------------------------

    // P5: an open city takes anyone without an invitation
    [AtlasScenario]
    public async Task Join_Should_MakeCitizen_When_CityIsOpen()
    {
        CityScene s = await GivenCity();
        ITestPlayer walkIn = await GivenStranger(s, "walkin");
        await s.Mayor.ExecuteCommand($"{CityCmd} set open on");
        Assert.True(s.City.openCity, "precondition: the city is open");

        CommandResult join = await walkIn.ExecuteCommand($"{CityCmd} join {s.City.GetPartName()}");

        Assert.True(join.Ok, "/city join of an open city: " + join.Message);
        Assert.True(s.City.Equals(Info(walkIn).City), "the player did not join the open city");
        AssertInvariants();
    }

    // P5: a closed city refuses a player without an invitation
    [AtlasScenario]
    public async Task Join_Should_BeRefused_When_CityIsClosedAndPlayerNotInvited()
    {
        CityScene s = await GivenCity();
        await CloseCity(s);
        ITestPlayer walkIn = await GivenStranger(s, "knocker");

        CommandResult join = await walkIn.ExecuteCommand($"{CityCmd} join {s.City.GetPartName()}");

        AssertRefused(join, "city_not_open", "joining a closed city uninvited");
        Assert.False(Info(walkIn).hasCity(), "the player got into a closed city");
        Assert.DoesNotContain(Info(walkIn), s.City.getCityCitizens());
    }

    // ---- P6 --------------------------------------------------------------------------------

    // P6, Q5: leaving gives the owned plots back to the city, drops the ranks and the plots groups
    [AtlasScenario]
    public async Task Leave_Should_ReturnPlots_And_DropRanksAndGroups()
    {
        // Arrange: the citizen owns a plot, holds a rank and is in a plots group
        CityScene s = await GivenCity("leaver");
        ITestPlayer leaver = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, leaver);
        await GiveRank(s, leaver, "Guard", "CITY_INVITE");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg create grp");
        Assert.True(PlotsGroupHandler.TryFind(s.City, "grp", out PlotsGroup group), "precondition: the group was created");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg invite grp {Name(leaver)}");
        await leaver.ExecuteCommand($"{CityCmd} pg join grp");
        Assert.Contains(Info(leaver), group.Members);

        // Act
        CommandResult leave = await leaver.ExecuteCommand($"{CityCmd} leave");

        // Assert
        Assert.True(leave.Ok, "/city leave: " + leave.Message);
        Assert.False(Info(leaver).hasCity(), "the player is still in the city");
        Assert.DoesNotContain(Info(leaver), s.City.getCityCitizens());
        Assert.Null(plot.getPlotOwner());
        Assert.Contains(plot, s.City.getCityPlots());
        Assert.Empty(Info(leaver).PlayerPlots);
        Assert.Empty(Info(leaver).getCityTitles());
        Assert.DoesNotContain(Name(leaver), s.City.CustomCityRanks["Guard"].CitizensNames);
        Assert.False(Info(leaver).PlayerPermissionsHandler.HasPermission(EnumPlayerPermissions.CITY_INVITE),
            "the rank's right stayed with the player who left");
        Assert.DoesNotContain(Info(leaver), group.Members);
        AssertInvariants();
    }

    // P6: the mayor cannot simply walk away
    [AtlasScenario]
    public async Task Leave_Should_BeRefused_When_PlayerIsMayor()
    {
        CityScene s = await GivenCity();

        CommandResult leave = await s.Mayor.ExecuteCommand($"{CityCmd} leave");

        AssertRefused(leave, "mayor_cannot_leave", "the mayor leaving");
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the mayor left the city");
        Assert.True(s.City.isMayor(Info(s.Mayor)), "the city lost its mayor");
        AssertInvariants();
    }

    // P6: the mayor kicks a citizen out
    [AtlasScenario]
    public async Task Kick_Should_RemoveCitizen()
    {
        CityScene s = await GivenCity("kicked");
        ITestPlayer kicked = s.Citizens[0];

        CommandResult kick = await s.Mayor.ExecuteCommand($"{CityCmd} kick {Name(kicked)}");

        Assert.True(kick.Ok, "/city kick: " + kick.Message);
        Assert.False(Info(kicked).hasCity(), "the kicked player is still in a city");
        Assert.DoesNotContain(Info(kicked), s.City.getCityCitizens());
        CityLogEntry? logged = Last(s.City, EnumCityLogEvent.CitizenKicked);
        Assert.NotNull(logged);
        Assert.Equal(new[] { Name(kicked), Name(s.Mayor) }, logged!.Args);
        AssertInvariants();
    }

    // P6: nobody kicks themselves
    [AtlasScenario]
    public async Task Kick_Should_BeRefused_When_TargetIsTheCaller()
    {
        CityScene s = await GivenCity("kicker");
        ITestPlayer kicker = s.Citizens[0];
        await GiveRank(s, kicker, "Bouncer", "CITY_KICK");

        CommandResult kick = await kicker.ExecuteCommand($"{CityCmd} kick {Name(kicker)}");

        AssertRefused(kick, "cannot_kick_yourself", "kicking oneself");
        Assert.True(s.City.Equals(Info(kicker).City), "the player kicked themselves out");
        AssertInvariants();
    }

    // P6: a citizen with the kick right still cannot kick the mayor
    [AtlasScenario]
    public async Task Kick_Should_BeRefused_When_TargetIsMayor()
    {
        CityScene s = await GivenCity("rebel");
        ITestPlayer rebel = s.Citizens[0];
        await GiveRank(s, rebel, "Bouncer", "CITY_KICK");

        CommandResult kick = await rebel.ExecuteCommand($"{CityCmd} kick {Name(s.Mayor)}");

        AssertRefused(kick, "cannot_kick_mayor", "kicking the mayor");
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the mayor was kicked");
        Assert.True(s.City.isMayor(Info(s.Mayor)), "the city lost its mayor");
        AssertInvariants();
    }

    // P6: a citizen without the right cannot kick at all
    [AtlasScenario]
    public async Task Kick_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("plain", "victim");
        ITestPlayer plain = s.Citizens[0], victim = s.Citizens[1];

        CommandResult kick = await plain.ExecuteCommand($"{CityCmd} kick {Name(victim)}");

        AssertRefused(kick, "you_dont_have_right_for_that_command", "a kick without CITY_KICK");
        Assert.True(s.City.Equals(Info(victim).City), "a citizen without the right kicked another");
    }

    // ---- P7 --------------------------------------------------------------------------------

    /// <summary>Puts NEW_PLAYERS_DEFAULT_CITY (and its file) back after the scenario.</summary>
    private void RestoreDefaultCityAfterwards()
    {
        string before = Mod.config.NEW_PLAYERS_DEFAULT_CITY;
        AddCleanup(() =>
        {
            Mod.config.NEW_PLAYERS_DEFAULT_CITY = before;
            Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        });
    }

    // P7: with a default city, a player new to the mod is its citizen on the first login
    [AtlasScenario]
    public async Task NewPlayer_Should_JoinTheDefaultCity_When_OneIsSet()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        RestoreDefaultCityAfterwards();
        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} city setdefault {s.City.GetPartName()}");
        Assert.True(set.Ok, "/cadmin city setdefault: " + set.Message);
        Assert.Equal(s.City.GetPartName(), Mod.config.NEW_PLAYERS_DEFAULT_CITY);

        // Act: a name never seen before
        ITestPlayer newcomer = await JoinAt("newcomer" + s.Area, s.Area);

        // Assert
        Assert.True(s.City.Equals(Info(newcomer).City), "the new player did not land in the default city");
        Assert.Contains(Info(newcomer), s.City.getCityCitizens());
        AssertInvariants();
    }

    // P7: /cadmin player joindefault puts a known player without a city into it
    [AtlasScenario]
    public async Task JoinDefault_Should_MakeExistingPlayerCitizen()
    {
        // Arrange: the player joins before the default is set, so stays without a city
        CityScene s = await GivenCity();
        ITestPlayer late = await GivenStranger(s, "late");
        AllowAdminCommands(s.Mayor);
        RestoreDefaultCityAfterwards();
        await s.Mayor.ExecuteCommand($"{CadminCmd} city setdefault {s.City.GetPartName()}");
        Assert.False(Info(late).hasCity(), "precondition: the player has no city");

        // Act
        CommandResult join = await s.Mayor.ExecuteCommand($"{CadminCmd} player joindefault {Name(late)}");

        // Assert
        Assert.True(join.Ok, "/cadmin player joindefault: " + join.Message);
        Assert.True(s.City.Equals(Info(late).City), "the player is not in the default city");
        AssertInvariants();
    }

    // P7: setdefault without a name clears it; a new player then stays without a city
    [AtlasScenario]
    public async Task SetDefault_Should_ClearTheDefault_When_NoNameIsGiven()
    {
        // Arrange
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);
        RestoreDefaultCityAfterwards();
        await s.Mayor.ExecuteCommand($"{CadminCmd} city setdefault {s.City.GetPartName()}");

        // Act
        CommandResult clear = await s.Mayor.ExecuteCommand($"{CadminCmd} city setdefault");
        ITestPlayer newcomer = await JoinAt("homeless" + s.Area, s.Area);

        // Assert
        Assert.True(clear.Ok, "/cadmin city setdefault without a name: " + clear.Message);
        Assert.Equal("", Mod.config.NEW_PLAYERS_DEFAULT_CITY);
        Assert.False(Info(newcomer).hasCity(), "a new player joined a cleared default city");
        AssertInvariants();
    }
}
