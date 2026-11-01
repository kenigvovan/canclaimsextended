using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// City ranks: creating and editing them (Q1), the rights they carry applying at once (Q2), their
/// gates (Q3) and delegation, where an editor can only pass on rights they hold (Q4).
/// Section Q of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class RankScenarios : CxScenarioBase
{
    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    /// <summary>The mayor makes a rank holding the given rights.</summary>
    private static async Task<CustomCityRank> GivenRank(CityScene s, string rank, params string[] permissions)
    {
        CommandResult create = await s.Mayor.ExecuteCommand($"{CityCmd} rank create {rank}");
        Assert.True(create.Ok, $"precondition: /city rank create {rank}: {create.Message}");
        foreach (string permission in permissions)
        {
            CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm {rank} {permission}");
            Assert.True(add.Ok, $"precondition: /city rank addperm {rank} {permission}: {add.Message}");
        }
        return s.City.CustomCityRanks[rank];
    }

    /// <summary>The mayor makes the rank and gives it to the player.</summary>
    private static async Task GiveRank(CityScene s, ITestPlayer to, string rank, params string[] permissions)
    {
        await GivenRank(s, rank, permissions);
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {rank} {Name(to)}");
        Assert.True(Info(to).hasCityTitle(rank), $"precondition: {Name(to)} holds rank {rank}");
    }

    private static bool Has(ITestPlayer player, EnumPlayerPermissions permission) =>
        Info(player).PlayerPermissionsHandler.HasPermission(permission);

    // ---- Q1 --------------------------------------------------------------------------------

    // Q1: create, add a right, take it away
    [AtlasScenario]
    public async Task Rank_Should_BeCreatedAndEdited()
    {
        CityScene s = await GivenCity();

        CommandResult create = await s.Mayor.ExecuteCommand($"{CityCmd} rank create Guard");
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Guard CITY_INVITE");
        Assert.True(create.Ok, "/city rank create: " + create.Message);
        Assert.True(add.Ok, "/city rank addperm: " + add.Message);
        Assert.True(s.City.HasCityRank("Guard"), "the rank was not created");
        Assert.Contains(EnumPlayerPermissions.CITY_INVITE, s.City.CustomCityRanks["Guard"].Permissions);

        CommandResult remove = await s.Mayor.ExecuteCommand($"{CityCmd} rank removeperm Guard CITY_INVITE");

        Assert.True(remove.Ok, "/city rank removeperm: " + remove.Message);
        Assert.DoesNotContain(EnumPlayerPermissions.CITY_INVITE, s.City.CustomCityRanks["Guard"].Permissions);
        AssertInvariants();
    }

    // Q1: deleting a rank takes it, and its rights, from whoever held it
    [AtlasScenario]
    public async Task RankDelete_Should_RemoveRankAndItsRightsFromHolders()
    {
        CityScene s = await GivenCity("holder");
        ITestPlayer holder = s.Citizens[0];
        await GiveRank(s, holder, "Guard", "CITY_INVITE");
        Assert.True(Has(holder, EnumPlayerPermissions.CITY_INVITE), "precondition: the holder has the rank's right");

        CommandResult delete = await s.Mayor.ExecuteCommand($"{CityCmd} rank delete Guard");

        Assert.True(delete.Ok, "/city rank delete: " + delete.Message);
        Assert.False(s.City.HasCityRank("Guard"), "the rank is still there");
        Assert.False(Info(holder).hasCityTitle("Guard"), "the holder still has the deleted rank");
        Assert.False(Has(holder, EnumPlayerPermissions.CITY_INVITE), "the holder kept the right of a deleted rank");
        AssertInvariants();
    }

    // ---- Q2 --------------------------------------------------------------------------------

    // Q2, H6: the right of a granted rank works at once - the citizen can invite
    [AtlasScenario]
    public async Task Grant_Should_GiveTheRightAtOnce()
    {
        // Arrange
        CityScene s = await GivenCity("recruiter");
        ITestPlayer recruiter = s.Citizens[0];
        ITestPlayer guest = await GivenStranger(s, "guest");
        await GivenRank(s, "Recruiter", "CITY_INVITE");

        // Act
        CommandResult grant = await s.Mayor.ExecuteCommand($"{CityCmd} rank grant Recruiter {Name(recruiter)}");
        CommandResult invite = await recruiter.ExecuteCommand($"{CityCmd} invite {Name(guest)}");

        // Assert
        Assert.True(grant.Ok, "/city rank grant: " + grant.Message);
        Assert.True(Info(recruiter).hasCityTitle("Recruiter"), "the citizen did not get the rank");
        Assert.Contains(Name(recruiter), s.City.CustomCityRanks["Recruiter"].CitizensNames);
        Assert.True(invite.Ok, "the rank's CITY_INVITE did not let the citizen invite: " + invite.Message);
        Assert.True(InvitationHandler.tryGetInvitation(s.City, Info(guest), out _), "no invitation was stored");
        AssertInvariants();
    }

    // Q2: a revoked rank takes its right with it at once
    [AtlasScenario]
    public async Task Revoke_Should_TakeTheRightAway()
    {
        // Arrange
        CityScene s = await GivenCity("recruiter");
        ITestPlayer recruiter = s.Citizens[0];
        ITestPlayer guest = await GivenStranger(s, "guest");
        await GiveRank(s, recruiter, "Recruiter", "CITY_INVITE");

        // Act
        CommandResult revoke = await s.Mayor.ExecuteCommand($"{CityCmd} rank revoke Recruiter {Name(recruiter)}");
        CommandResult invite = await recruiter.ExecuteCommand($"{CityCmd} invite {Name(guest)}");

        // Assert
        Assert.True(revoke.Ok, "/city rank revoke: " + revoke.Message);
        Assert.False(Info(recruiter).hasCityTitle("Recruiter"), "the citizen still holds the rank");
        AssertRefused(invite, "you_dont_have_right_for_that_command", "an invitation after the rank was revoked");
        Assert.False(InvitationHandler.tryGetInvitation(s.City, Info(guest), out _), "an invitation was stored without the right");
        AssertInvariants();
    }

    // ---- Q3 --------------------------------------------------------------------------------

    // Q3: a right missing from AVAILABLE_CITY_PERMISSIONS, or no right at all, cannot go into a rank
    [AtlasTheory]
    [InlineData("CITY_SEE_BALANCE")]
    [InlineData("NO_SUCH_RIGHT")]
    public async Task AddPerm_Should_BeRefused_When_PermissionIsNotAvailable(string permission)
    {
        // Arrange: CITY_SEE_BALANCE is a real right; make sure the host list does not hold it
        CityScene s = await GivenCity();
        bool wasAvailable = Mod.config.AVAILABLE_CITY_PERMISSIONS.Remove(EnumPlayerPermissions.CITY_SEE_BALANCE);
        AddCleanup(() => { if (wasAvailable) Mod.config.AVAILABLE_CITY_PERMISSIONS.Add(EnumPlayerPermissions.CITY_SEE_BALANCE); });
        CustomCityRank rank = await GivenRank(s, "Clerk");

        // Act
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Clerk {permission}");

        // Assert
        AssertRefused(add, "rank_bad_permission", $"adding {permission}");
        Assert.Empty(rank.Permissions);
    }

    // Q3: two ranks of one name
    [AtlasScenario]
    public async Task Create_Should_BeRefused_When_RankExists()
    {
        CityScene s = await GivenCity();
        CustomCityRank first = await GivenRank(s, "Guard", "CITY_INVITE");

        CommandResult again = await s.Mayor.ExecuteCommand($"{CityCmd} rank create Guard");

        AssertRefused(again, "rank_already_exists", "a second rank of the same name");
        Assert.Same(first, s.City.CustomCityRanks["Guard"]);
        Assert.Contains(EnumPlayerPermissions.CITY_INVITE, first.Permissions);
    }

    // Q3: the same right twice
    [AtlasScenario]
    public async Task AddPerm_Should_BeRefused_When_RankHasThePermission()
    {
        CityScene s = await GivenCity();
        await GivenRank(s, "Guard", "CITY_INVITE");

        CommandResult again = await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Guard CITY_INVITE");

        AssertRefused(again, "rank_has_perm", "adding a right the rank has");
    }

    // Q3: a rank only goes to a citizen of the city
    [AtlasScenario]
    public async Task Grant_Should_BeRefused_When_PlayerIsNoCitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        await GivenRank(s, "Guard", "CITY_INVITE");

        CommandResult grant = await s.Mayor.ExecuteCommand($"{CityCmd} rank grant Guard {Name(stranger)}");

        AssertRefused(grant, "player_should_be_citizen", "granting a rank to a stranger");
        Assert.False(Info(stranger).hasCityTitle("Guard"), "a stranger got the rank");
        Assert.Empty(s.City.CustomCityRanks["Guard"].CitizensNames);
    }

    // ---- Q4 --------------------------------------------------------------------------------

    /// <summary>A citizen who may create ranks, add rights to them and grant them - but not kick.</summary>
    private static async Task<ITestPlayer> GivenDeputy(CityScene s)
    {
        ITestPlayer deputy = s.Citizens[0];
        await GiveRank(s, deputy, "Deputy", "CITY_CREATE_CITY_RANK", "CITY_ADD_PERMISSION_TO_RANK", "CITY_SET_RANK");
        Assert.False(Has(deputy, EnumPlayerPermissions.CITY_KICK), "precondition: the deputy cannot kick");
        return deputy;
    }

    // Q4: an editor cannot write a right they lack into a rank
    [AtlasScenario]
    public async Task AddPerm_Should_BeRefused_When_EditorLacksThePermission()
    {
        // Arrange: the deputy makes a rank of their own
        CityScene s = await GivenCity("deputy");
        ITestPlayer deputy = await GivenDeputy(s);
        CommandResult create = await deputy.ExecuteCommand($"{CityCmd} rank create Bouncer");
        Assert.True(create.Ok, "precondition: the deputy may create ranks: " + create.Message);

        // Act
        CommandResult add = await deputy.ExecuteCommand($"{CityCmd} rank addperm Bouncer CITY_KICK");

        // Assert
        AssertRefused(add, "rank_perm_not_yours", "a deputy adding CITY_KICK they do not hold");
        Assert.DoesNotContain(EnumPlayerPermissions.CITY_KICK, s.City.CustomCityRanks["Bouncer"].Permissions);
        AssertInvariants();
    }

    // Q4: a right the editor holds can be passed on
    [AtlasScenario]
    public async Task AddPerm_Should_Work_When_EditorHoldsThePermission()
    {
        CityScene s = await GivenCity("deputy");
        ITestPlayer deputy = await GivenDeputy(s);
        await deputy.ExecuteCommand($"{CityCmd} rank create Helper");

        CommandResult add = await deputy.ExecuteCommand($"{CityCmd} rank addperm Helper CITY_SET_RANK");

        Assert.True(add.Ok, "a deputy could not pass on a right they hold: " + add.Message);
        Assert.Contains(EnumPlayerPermissions.CITY_SET_RANK, s.City.CustomCityRanks["Helper"].Permissions);
        AssertInvariants();
    }

    // Q4: a rank carrying a right the granter lacks cannot be handed out by them
    [AtlasScenario]
    public async Task Grant_Should_BeRefused_When_RankHoldsAPermissionTheGranterLacks()
    {
        // Arrange: the mayor's rank with CITY_KICK, a citizen the deputy would give it to
        CityScene s = await GivenCity("deputy", "friend");
        ITestPlayer deputy = await GivenDeputy(s);
        ITestPlayer friend = s.Citizens[1];
        await GivenRank(s, "Guard", "CITY_KICK");

        // Act
        CommandResult grant = await deputy.ExecuteCommand($"{CityCmd} rank grant Guard {Name(friend)}");

        // Assert
        AssertRefused(grant, "rank_perm_not_yours", "a deputy granting a rank with CITY_KICK");
        Assert.False(Info(friend).hasCityTitle("Guard"), "the friend got the rank");
        Assert.False(Has(friend, EnumPlayerPermissions.CITY_KICK), "the friend got CITY_KICK");
        AssertInvariants();
    }
}
