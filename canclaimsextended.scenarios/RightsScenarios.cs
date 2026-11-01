using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Rights on land (AccessRules) and rights to commands: the matrix by how a player stands to the
/// plot (H1), /city set perm and the plots it reaches (H2), the player cache (H4), command rights
/// handed out with a rank (H6) and friends (H7). Section H of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class RightsScenarios : CxScenarioBase
{
    private static readonly Dictionary<string, PermType> Types = new()
    {
        ["use"] = PermType.USE_PERM,
        ["build"] = PermType.BUILD_AND_DESTROY_PERM,
        ["attack"] = PermType.ATTACK_ANIMALS_PERM,
    };

    private static readonly string[] Groups = { "stranger", "citizen", "friend" };

    private static string CitizenCmd => "/" + Mod.config.COMMAND_CITIZEN;

    private static bool May(ITestPlayer player, BlockPos pos, PermType type) =>
        AccessRules.Check(player.Player, pos, type);

    /// <summary>The player who sets the plot's rights stands in it and switches every row off.</summary>
    private static async Task AllOff(ITestPlayer manager, Plot plot)
    {
        foreach (string group in Groups)
        {
            foreach (string type in Types.Keys)
            {
                CommandResult off = await manager.ExecuteCommand($"{PlotCmd} set perm {group} {type} off");
                Assert.True(off.Ok, $"precondition: /plot set perm {group} {type} off: {off.Message}");
            }
        }
        PermsHandler perms = plot.getPermsHandler();
        Assert.True(Groups.All(g => PermsHandler.TryParseGroup(g, out PermGroup pg) && Types.Values.All(t => !perms.getPerm(pg, t))),
            "precondition: every row of the plot is off");
    }

    /// <summary>The player who stands to the scene's plots as <paramref name="group"/>.</summary>
    private async Task<ITestPlayer> GivenPlayerOfGroup(CityScene s, string group) => group switch
    {
        "stranger" => await GivenStranger(s),
        "citizen" => s.Citizens[0],
        _ => throw new ArgumentException(group),
    };

    // ---- H1 ----------------------------------------------------------------------------------

    // H1: STRANGER and CITIZEN get exactly what their row of the plot says, for each of use, build, attack
    [AtlasTheory]
    [InlineData("stranger")]
    [InlineData("citizen")]
    public async Task Right_Should_FollowTheGroupRow_When_PlotPermIsToggled(string group)
    {
        // Arrange: a city plot with every row off, the player of the group standing in it
        CityScene s = await GivenCity("citizen");
        Plot plot = await GivenPlot(s);
        ITestPlayer player = await GivenPlayerOfGroup(s, group);
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        await AllOff(s.Mayor, plot);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        await player.TeleportTo(spot);

        foreach (var (word, type) in Types)
        {
            Assert.False(May(player, spot, type), $"{group} may {word} with every row off");

            // Act: the group's row on, then off again
            await s.Mayor.ExecuteCommand($"{PlotCmd} set perm {group} {word} on");
            bool on = May(player, spot, type);
            await s.Mayor.ExecuteCommand($"{PlotCmd} set perm {group} {word} off");
            bool off = May(player, spot, type);

            // Assert
            Assert.True(on, $"{group} may not {word} with perm {group} {word} on");
            Assert.False(off, $"{group} still may {word} with perm {group} {word} off");
        }
    }

    // H1: a row other than the player's own gives them nothing
    [AtlasScenario]
    public async Task Right_Should_NotCome_When_AnotherGroupRowIsOn()
    {
        CityScene s = await GivenCity("citizen");
        Plot plot = await GivenPlot(s);
        ITestPlayer stranger = await GivenStranger(s);
        await AllOff(s.Mayor, plot);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build on");

        Assert.False(May(stranger, spot, PermType.BUILD_AND_DESTROY_PERM), "a stranger builds by the citizen row");
        Assert.True(May(s.Citizens[0], spot, PermType.BUILD_AND_DESTROY_PERM), "the citizen row did not let a citizen build");
    }

    // H1: alliances are gone, so is the ally row - the word is refused and nothing changes
    [AtlasScenario]
    public async Task PlotPerm_Should_RefuseTheAllyGroup()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        string before = plot.getPermsHandler().ToString();

        CommandResult set = await s.Mayor.ExecuteCommand($"{PlotCmd} set perm ally build on");

        Assert.False(set.Ok, "/plot set perm still takes the ally group");
        Assert.Equal(before, plot.getPermsHandler().ToString());
        Assert.DoesNotContain("ally:", plot.getPermsHandler().ToString());
    }

    // H1: the owner and the mayor may everything, whatever the rows say
    [AtlasScenario]
    public async Task OwnerAndMayor_Should_MayEverything_When_EveryRowIsOff()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await AllOff(owner, plot);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        foreach (var (word, type) in Types)
        {
            Assert.True(May(owner, spot, type), $"the owner may not {word} on their plot");
            Assert.True(May(s.Mayor, spot, type), $"the mayor may not {word} on a plot of the city");
        }
    }

    // H1, H7: an owner's friend gets the friend row on the owner's plots
    [AtlasScenario]
    public async Task Friend_Should_GetTheFriendRow_When_OwnerAddsHim()
    {
        // Arrange: the owner's plot lets friends build, strangers not
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        ITestPlayer friend = await GivenStranger(s, "friend");
        await AllOff(owner, plot);
        await owner.ExecuteCommand($"{PlotCmd} set perm friend build on");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.False(May(friend, spot, PermType.BUILD_AND_DESTROY_PERM), "precondition: a stranger does not build");

        // Act
        CommandResult add = await owner.ExecuteCommand($"{CitizenCmd} friend add {Name(friend)}");

        // Assert
        Assert.True(add.Ok, "/citizen friend add: " + add.Message);
        Assert.True(May(friend, spot, PermType.BUILD_AND_DESTROY_PERM), "the friend row did not let the friend build");
        Assert.False(May(friend, spot, PermType.USE_PERM), "the friend got a right their row does not have");
    }

    // ---- H2 ----------------------------------------------------------------------------------

    // H2: /city set perm reaches the city land and every plot without an owner, but not an owned plot
    [AtlasScenario]
    public async Task CityPerm_Should_CopyToUnownedPlotsOnly()
    {
        // Arrange: one plot of the city, one of a citizen; the new value is the opposite of the city's
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot unowned = await GivenPlot(s, 0);
        Plot owned = await GivenPlot(s, 1);
        await GivenOwnedPlot(s, owned, owner);
        bool was = s.City.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM);
        Assert.Equal(was, owned.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM));
        Assert.Equal(was, unowned.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM));

        // Act
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set perm stranger use {(was ? "off" : "on")}");

        // Assert
        Assert.True(set.Ok, "/city set perm: " + set.Message);
        Assert.Equal(!was, s.City.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM));
        Assert.Equal(!was, unowned.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM));
        Assert.Equal(was, owned.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.USE_PERM));
    }

    // H2: on the city land outside the plots a stranger gets the city's row at once
    [AtlasScenario]
    public async Task CityPerm_Should_RuleTheCityLand()
    {
        // Arrange: a block of the city's chunk between the plot boxes
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        ChunkPos chunk = AreaChunk(s.Area);
        BlockPos land = new(chunk.MinBlockX + 8, Middle(PlotBox(s.Area)).Y, chunk.MinBlockZ + 8);
        Assert.True(Mod.dataStorage.GetPlotAt(land, out Plot here) && here.IsCityLand, "precondition: the block is city land");
        await s.Mayor.ExecuteCommand($"{CityCmd} set perm stranger build off");
        await stranger.TeleportTo(land);
        Assert.False(May(stranger, land, PermType.BUILD_AND_DESTROY_PERM), "precondition: a stranger does not build on the land");

        // Act
        await s.Mayor.ExecuteCommand($"{CityCmd} set perm stranger build on");

        // Assert
        Assert.True(May(stranger, land, PermType.BUILD_AND_DESTROY_PERM), "the city's row did not reach its land");
    }

    // H2: a plain citizen may not change the city's rights
    [AtlasScenario]
    public async Task CityPerm_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("citizen");
        bool was = s.City.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM);

        CommandResult set = await s.Citizens[0].ExecuteCommand($"{CityCmd} set perm stranger build {(was ? "off" : "on")}");

        Assert.False(set.Ok, "a citizen changed the city's rights: " + set.Message);
        Assert.Equal(was, s.City.getPermsHandler().getPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM));
    }

    // ---- H4 ----------------------------------------------------------------------------------

    // H4: a refusal the player's cache holds does not outlive /plot set perm - no rejoin needed
    [AtlasScenario]
    public async Task Cache_Should_FollowPlotPerm_WithoutRejoin()
    {
        // Arrange: the stranger stands in the plot and has been refused once (cached)
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer stranger = await GivenStranger(s);
        await AllOff(s.Mayor, plot);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        await stranger.TeleportTo(spot);
        Assert.False(May(stranger, spot, PermType.BUILD_AND_DESTROY_PERM), "precondition: refused");
        Assert.Same(plot, Info(stranger).PlayerCache.getLastPlot());

        // Act + Assert: on, then off again
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger build on");
        Assert.True(May(stranger, spot, PermType.BUILD_AND_DESTROY_PERM), "the cached refusal outlived the right being given");

        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm stranger build off");
        Assert.False(May(stranger, spot, PermType.BUILD_AND_DESTROY_PERM), "the cached right outlived being taken away");
    }

    // H4: joining the city switches the player from the stranger row to the citizen row at once
    [AtlasScenario]
    public async Task Cache_Should_FollowJoiningTheCity()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        ITestPlayer newcomer = await GivenStranger(s, "newcomer");
        await AllOff(s.Mayor, plot);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build on");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        await newcomer.TeleportTo(spot);
        Assert.False(May(newcomer, spot, PermType.BUILD_AND_DESTROY_PERM), "precondition: a stranger does not build");

        await AddCitizen(s.City, s.Mayor, newcomer);

        Assert.True(May(newcomer, spot, PermType.BUILD_AND_DESTROY_PERM), "the new citizen still gets the stranger row");
    }

    // H4, H7: removing a friend takes the friend row away at once
    [AtlasScenario]
    public async Task Cache_Should_FollowFriendRemoval()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        ITestPlayer friend = await GivenStranger(s, "friend");
        await AllOff(owner, plot);
        await owner.ExecuteCommand($"{PlotCmd} set perm friend build on");
        await owner.ExecuteCommand($"{CitizenCmd} friend add {Name(friend)}");
        BlockPos spot = Middle(plot.Shape.Boxes[0]);
        Assert.True(May(friend, spot, PermType.BUILD_AND_DESTROY_PERM), "precondition: the friend builds");

        await owner.ExecuteCommand($"{CitizenCmd} friend remove {Name(friend)}");

        Assert.False(May(friend, spot, PermType.BUILD_AND_DESTROY_PERM), "the removed friend still builds");
    }

    // ---- H6 ----------------------------------------------------------------------------------

    /// <summary>The mayor makes a rank holding one right and gives it to the player.</summary>
    private static async Task GiveRank(CityScene s, ITestPlayer to, string rank, string permission)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create {rank}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm {rank} {permission}");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant {rank} {Name(to)}");
        Assert.True(Info(to).hasCityTitle(rank), $"precondition: {Name(to)} holds rank {rank}");
    }

    /// <summary>A block of the chunk east of the city's founding chunk, free land next to the city.</summary>
    private BlockPos NextChunk(CityScene s)
    {
        BlockPos o = AreaOrigin(s.Area);
        return new BlockPos(o.X + 16, o.Y, o.Z);
    }

    private static ChunkPos ChunkOf(BlockPos pos) => ChunkPos.FromBlock(pos.X, pos.Z);

    // H6: a citizen without a rank cannot claim a chunk for the city
    [AtlasScenario]
    public async Task CityClaim_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        s.City.setBonusChunks(4);
        BlockPos next = NextChunk(s);
        await citizen.TeleportTo(next);

        CommandResult claim = await citizen.ExecuteCommand($"{CityCmd} claim");

        Assert.False(claim.Ok, "a citizen without CITY_CLAIM_CHUNK claimed a chunk: " + claim.Message);
        Assert.False(s.City.HasChunk(ChunkOf(next)), "the chunk became the city's");
    }

    // H6, Q: a rank with CITY_CLAIM_CHUNK lets the citizen claim
    [AtlasScenario]
    public async Task CityClaim_Should_GoThrough_When_RankGivesTheRight()
    {
        // Arrange
        CityScene s = await GivenCity("claimer");
        ITestPlayer claimer = s.Citizens[0];
        s.City.setBonusChunks(4);
        await GiveRank(s, claimer, "Surveyor", "CITY_CLAIM_CHUNK");
        BlockPos next = NextChunk(s);
        await claimer.TeleportTo(next);

        // Act
        CommandResult claim = await claimer.ExecuteCommand($"{CityCmd} claim");

        // Assert
        Assert.True(claim.Ok, "/city claim with the rank: " + claim.Message);
        Assert.True(s.City.HasChunk(ChunkOf(next)), "the chunk did not become the city's");
    }

    // H6: a citizen without a rank cannot switch the city's pvp
    [AtlasScenario]
    public async Task CitySetPvp_Should_BeRefused_When_CitizenHasNoRight()
    {
        CityScene s = await GivenCity("citizen");
        bool was = s.City.getPermsHandler().pvpFlag;

        CommandResult set = await s.Citizens[0].ExecuteCommand($"{CityCmd} set pvp {(was ? "off" : "on")}");

        Assert.False(set.Ok, "a citizen switched the city's pvp: " + set.Message);
        Assert.Equal(was, s.City.getPermsHandler().pvpFlag);
    }

    // ---- H7 ----------------------------------------------------------------------------------

    // H7: /citizen friend add puts the player on the list
    [AtlasScenario]
    public async Task FriendAdd_Should_PutPlayerOnTheList()
    {
        CityScene s = await GivenCity("buddy");
        ITestPlayer buddy = s.Citizens[0];

        CommandResult add = await s.Mayor.ExecuteCommand($"{CitizenCmd} friend add {Name(buddy)}");

        Assert.True(add.Ok, "/citizen friend add: " + add.Message);
        Assert.Contains(Info(buddy), Info(s.Mayor).Friends);
        Assert.DoesNotContain(Info(s.Mayor), Info(buddy).Friends);
    }

    // H7: oneself is refused
    [AtlasScenario]
    public async Task FriendAdd_Should_BeRefused_When_PlayerAddsHimself()
    {
        CityScene s = await GivenCity();

        CommandResult add = await s.Mayor.ExecuteCommand($"{CitizenCmd} friend add {Name(s.Mayor)}");

        Assert.False(add.Ok, "a player became their own friend: " + add.Message);
        Assert.DoesNotContain(Info(s.Mayor), Info(s.Mayor).Friends);
    }

    // H7: /citizen friend remove takes the player off the list
    [AtlasScenario]
    public async Task FriendRemove_Should_TakePlayerOffTheList()
    {
        CityScene s = await GivenCity("buddy");
        ITestPlayer buddy = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{CitizenCmd} friend add {Name(buddy)}");
        Assert.Contains(Info(buddy), Info(s.Mayor).Friends);

        CommandResult remove = await s.Mayor.ExecuteCommand($"{CitizenCmd} friend remove {Name(buddy)}");

        Assert.True(remove.Ok, "/citizen friend remove: " + remove.Message);
        Assert.DoesNotContain(Info(buddy), Info(s.Mayor).Friends);
    }

    // H7: /citizen friend list names the friends
    [AtlasScenario]
    public async Task FriendList_Should_NameTheFriends()
    {
        CityScene s = await GivenCity("buddy");
        ITestPlayer buddy = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{CitizenCmd} friend add {Name(buddy)}");

        CommandResult list = await s.Mayor.ExecuteCommand($"{CitizenCmd} friend list");

        Assert.True(list.Ok, "/citizen friend list: " + list.Message);
        Assert.Contains(Name(buddy), list.Message);
    }
}
