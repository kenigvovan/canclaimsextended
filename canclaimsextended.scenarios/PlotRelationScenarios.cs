using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Which row of a plot a player gets when more than one relation fits (AccessRules). Ported from
/// CAN_claims PlotRelationScenarios (RL4a, RL5, RL6); the war case (RL3a) is gone with the wars.
///
/// Today's precedence (AccessRules.getPlotRelationForPlayerInfo): mayor, owner, group member,
/// owner's friend (COMRADE), citizen, stranger. A friend does not lose the row they would have
/// without the friendship: EvalPermission gives COMRADE the friend row OR the citizen row (for a
/// citizen of the plot's city) OR the stranger row (for anyone else). Friendship only comes from the
/// owner's own list, and only on a plot that has an owner.
/// </summary>
[Trait("Category", "E2E")]
public class PlotRelationScenarios : CxScenarioBase
{
    private static readonly string[] Groups = { "stranger", "citizen", "friend" };
    private static readonly string[] Types = { "use", "build", "attack" };

    private static string CitizenCmd => "/" + Mod.config.COMMAND_CITIZEN;

    private static bool MayBuild(ITestPlayer player, BlockPos pos) =>
        AccessRules.Check(player.Player, pos, PermType.BUILD_AND_DESTROY_PERM);

    private static bool MayUse(ITestPlayer player, BlockPos pos) =>
        AccessRules.Check(player.Player, pos, PermType.USE_PERM);

    /// <summary>The manager stands in the plot: every row off, then build on for the given groups.</summary>
    private static async Task OnlyBuildFor(ITestPlayer manager, Plot plot, params string[] buildGroups)
    {
        foreach (string group in Groups)
        {
            foreach (string type in Types)
            {
                bool on = type == "build" && buildGroups.Contains(group);
                CommandResult set = await manager.ExecuteCommand($"{PlotCmd} set perm {group} {type} {(on ? "on" : "off")}");
                Assert.True(set.Ok, $"precondition: /plot set perm {group} {type}: {set.Message}");
            }
        }
        PermsHandler perms = plot.getPermsHandler();
        foreach (string group in Groups)
        {
            Assert.True(PermsHandler.TryParseGroup(group, out PermGroup pg), "precondition: group " + group);
            Assert.Equal(buildGroups.Contains(group), perms.getPerm(pg, PermType.BUILD_AND_DESTROY_PERM));
            Assert.False(perms.getPerm(pg, PermType.USE_PERM), $"precondition: {group} use is off");
            Assert.False(perms.getPerm(pg, PermType.ATTACK_ANIMALS_PERM), $"precondition: {group} attack is off");
        }
    }

    /// <summary>/citizen friend add, checked on the list.</summary>
    private static async Task AddFriend(ITestPlayer who, ITestPlayer friend)
    {
        CommandResult add = await who.ExecuteCommand($"{CitizenCmd} friend add {Name(friend)}");
        Assert.True(add.Ok, "precondition: /citizen friend add: " + add.Message);
        Assert.Contains(Info(friend), Info(who).Friends);
    }

    /// <summary>A city with an owner and a neighbour (both citizens); the owner holds box 0 and stands in it.</summary>
    private async Task<(CityScene S, ITestPlayer Owner, ITestPlayer Neighbour, Plot Plot)> GivenOwnedPlotWithNeighbour()
    {
        CityScene s = await GivenCity("owner", "neighbour");
        ITestPlayer owner = s.Citizens[0], neighbour = s.Citizens[1];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        return (s, owner, neighbour, plot);
    }

    // RL4a: a citizen who is also the owner's friend gets the friend row on top of the citizen row
    [AtlasScenario]
    public async Task Citizen_Should_GetTheFriendRow_When_AlsoTheOwnersFriend()
    {
        // Arrange: friends build, citizens and strangers do not
        var (_, owner, neighbour, plot) = await GivenOwnedPlotWithNeighbour();
        await OnlyBuildFor(owner, plot, "friend");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.False(MayBuild(neighbour, spot), "precondition: a citizen does not build by the citizen row");

        // Act
        await AddFriend(owner, neighbour);

        // Assert: COMRADE wins over CITIZEN, and gets only what the rows hold
        Assert.Equal(PlotRelation.COMRADE, AccessRules.getPlotRelationForPlayerInfo(Info(neighbour), plot));
        Assert.True(MayBuild(neighbour, spot), "the owner's friend, a citizen, did not get the friend row");
        Assert.False(MayUse(neighbour, spot), "the citizen friend got a right no row of theirs has");
        AssertInvariants();
    }

    // RL4a: becoming the owner's friend never takes the citizen row away
    [AtlasScenario]
    public async Task Citizen_Should_KeepTheCitizenRow_When_AlsoTheOwnersFriend()
    {
        // Arrange: citizens build, friends and strangers do not
        var (_, owner, neighbour, plot) = await GivenOwnedPlotWithNeighbour();
        await OnlyBuildFor(owner, plot, "citizen");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.True(MayBuild(neighbour, spot), "precondition: a citizen builds by the citizen row");

        // Act
        await AddFriend(owner, neighbour);

        // Assert
        Assert.Equal(PlotRelation.COMRADE, AccessRules.getPlotRelationForPlayerInfo(Info(neighbour), plot));
        Assert.True(MayBuild(neighbour, spot), "becoming the owner's friend took the citizen row away");
        Assert.False(MayUse(neighbour, spot), "the citizen friend got a right no row of theirs has");
    }

    // RL4a: the friend row, and nothing else, makes the difference - both rows off, no build
    [AtlasScenario]
    public async Task Citizen_Should_NotBuild_When_AlsoTheOwnersFriend_AndOnlyTheStrangerRowIsOn()
    {
        // Arrange: only strangers build
        var (_, owner, neighbour, plot) = await GivenOwnedPlotWithNeighbour();
        await OnlyBuildFor(owner, plot, "stranger");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        await AddFriend(owner, neighbour);

        // Assert: a citizen friend falls back on the citizen row, not on the stranger one
        Assert.False(MayBuild(neighbour, spot), "a citizen friend built by the stranger row");
    }

    // RL4a for strangers: friendship never takes away what every stranger may do
    [AtlasScenario]
    public async Task Stranger_Should_KeepTheStrangerRow_When_AlsoTheOwnersFriend()
    {
        // Arrange: strangers build, friends do not
        var (s, owner, _, plot) = await GivenOwnedPlotWithNeighbour();
        ITestPlayer stranger = await GivenStranger(s);
        await OnlyBuildFor(owner, plot, "stranger");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.True(MayBuild(stranger, spot), "precondition: a stranger builds by the stranger row");

        // Act
        await AddFriend(owner, stranger);

        // Assert
        Assert.Equal(PlotRelation.COMRADE, AccessRules.getPlotRelationForPlayerInfo(Info(stranger), plot));
        Assert.True(MayBuild(stranger, spot), "becoming the owner's friend took the stranger row away");
        Assert.False(MayUse(stranger, spot), "the stranger friend got a right no row of theirs has");
    }

    // RL4a for strangers: a friend who is no citizen does not get the citizen row
    [AtlasScenario]
    public async Task Stranger_Should_NotGetTheCitizenRow_When_AlsoTheOwnersFriend()
    {
        // Arrange: only citizens build
        var (s, owner, neighbour, plot) = await GivenOwnedPlotWithNeighbour();
        ITestPlayer stranger = await GivenStranger(s);
        await OnlyBuildFor(owner, plot, "citizen");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.True(MayBuild(neighbour, spot), "precondition: a citizen builds by the citizen row");

        // Act
        await AddFriend(owner, stranger);

        // Assert
        Assert.False(MayBuild(stranger, spot), "the owner's friend, a stranger, built by the citizen row");
    }

    // RL5: on a city plot nobody owns, the mayor's friends are plain strangers
    [AtlasScenario]
    public async Task Friendship_Should_NotCount_OnAPlotWithoutOwner()
    {
        // Arrange: an unowned plot of the city where friends build and strangers do not
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        Assert.Null(plot.getPlotOwner());
        ITestPlayer stranger = await GivenStranger(s);
        await OnlyBuildFor(s.Mayor, plot, "friend");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        await AddFriend(s.Mayor, stranger);

        // Assert
        Assert.Equal(PlotRelation.STRANGER, AccessRules.getPlotRelationForPlayerInfo(Info(stranger), plot));
        Assert.False(MayBuild(stranger, spot), "the mayor's friend built on a city plot nobody owns");
    }

    // RL6: only the owner's list counts - listing the owner as one's own friend gives nothing
    [AtlasScenario]
    public async Task Friendship_Should_CountOnlyFromTheOwnersSide()
    {
        // Arrange: friends build, strangers do not
        var (s, owner, _, plot) = await GivenOwnedPlotWithNeighbour();
        ITestPlayer stranger = await GivenStranger(s);
        await OnlyBuildFor(owner, plot, "friend");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act: the stranger lists the owner, not the other way round
        await AddFriend(stranger, owner);

        // Assert
        Assert.DoesNotContain(Info(stranger), Info(owner).Friends);
        Assert.Equal(PlotRelation.STRANGER, AccessRules.getPlotRelationForPlayerInfo(Info(stranger), plot));
        Assert.False(MayBuild(stranger, spot), "listing the owner as a friend gave the stranger the friend row");
    }
}
