using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Plots groups: creating and filling one (R1), membership (R2), the group's rights for its members
/// (R3), its flags over the plots' (R4), the daily fee (R5) and raising it (R6).
/// Section R of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class PlotsGroupScenarios : CxScenarioBase
{
    private const string GroupName = "grp";

    protected override void ResetConfig()
    {
        base.ResetConfig();
        int delay = Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS;
        AddCleanup(() => Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = delay);
    }

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    /// <summary>A plot of the city (the mayor stands in it) and a group holding it.</summary>
    private async Task<(PlotsGroup Group, Plot Plot)> GivenGroupWithPlot(CityScene s)
    {
        Plot plot = await GivenPlot(s);
        CommandResult create = await s.Mayor.ExecuteCommand($"{CityCmd} pg create {GroupName}");
        Assert.True(create.Ok, "precondition: /city pg create: " + create.Message);
        Assert.True(PlotsGroupHandler.TryFind(s.City, GroupName, out PlotsGroup group), "precondition: the group exists");
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} pg addplot {GroupName}");
        Assert.True(add.Ok, "precondition: /city pg addplot: " + add.Message);
        Assert.True(group.Equals(plot.Group), "precondition: the plot is in the group");
        return (group, plot);
    }

    /// <summary>The mayor invites the citizen into the group and they accept.</summary>
    private static async Task JoinGroup(CityScene s, PlotsGroup group, ITestPlayer member)
    {
        await s.Mayor.ExecuteCommand($"{CityCmd} pg invite {GroupName} {Name(member)}");
        await member.ExecuteCommand($"{CityCmd} pg join {GroupName}");
        Assert.True(group.Members.Contains(Info(member)), $"precondition: {Name(member)} is a member");
    }

    // ---- R1 --------------------------------------------------------------------------------

    // R1: a group is created and the plot the mayor stands in goes into it
    [AtlasScenario]
    public async Task Group_Should_BeCreated_And_TakeThePlot()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult create = await s.Mayor.ExecuteCommand($"{CityCmd} pg create {GroupName}");
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} pg addplot {GroupName}");

        Assert.True(create.Ok, "/city pg create: " + create.Message);
        Assert.True(add.Ok, "/city pg addplot: " + add.Message);
        PlotsGroup group = Assert.Single(s.City.PlotsGroups);
        Assert.Equal(GroupName, group.GetPartName());
        Assert.True(group.Equals(plot.Group), "the plot is not in the group");
        Assert.Contains(plot, group.Plots);
        Assert.True(group.HasLand, "the group holds no land");
        AssertInvariants();
    }

    // R1: the same plot twice, and a second group of the same name
    [AtlasScenario]
    public async Task Group_Should_RefuseDuplicates()
    {
        CityScene s = await GivenCity();
        await GivenGroupWithPlot(s);

        CommandResult addAgain = await s.Mayor.ExecuteCommand($"{CityCmd} pg addplot {GroupName}");
        CommandResult createAgain = await s.Mayor.ExecuteCommand($"{CityCmd} pg create {GroupName}");

        AssertRefused(addAgain, "plotsgroup_plot_already_in", "adding a plot twice");
        AssertRefused(createAgain, "plotsgroup_name_taken", "a second group of the same name");
        Assert.Single(s.City.PlotsGroups);
    }

    // ---- R2 --------------------------------------------------------------------------------

    // R2: invited citizen joins
    [AtlasScenario]
    public async Task Invite_Should_MakeMember_When_CitizenJoins()
    {
        CityScene s = await GivenCity("member");
        ITestPlayer member = s.Citizens[0];
        var (group, _) = await GivenGroupWithPlot(s);

        CommandResult invite = await s.Mayor.ExecuteCommand($"{CityCmd} pg invite {GroupName} {Name(member)}");
        CommandResult join = await member.ExecuteCommand($"{CityCmd} pg join {GroupName}");

        Assert.True(invite.Ok, "/city pg invite: " + invite.Message);
        Assert.True(join.Ok, "/city pg join: " + join.Message);
        Assert.Contains(Info(member), group.Members);
        Assert.Empty(group.GetSentInvitations());
        AssertInvariants();
    }

    // R2: a refused invitation leaves the citizen out
    [AtlasScenario]
    public async Task Deny_Should_KeepCitizenOutOfTheGroup()
    {
        CityScene s = await GivenCity("refuser");
        ITestPlayer refuser = s.Citizens[0];
        var (group, _) = await GivenGroupWithPlot(s);
        await s.Mayor.ExecuteCommand($"{CityCmd} pg invite {GroupName} {Name(refuser)}");

        CommandResult deny = await refuser.ExecuteCommand($"{CityCmd} pg deny {GroupName}");
        CommandResult join = await refuser.ExecuteCommand($"{CityCmd} pg join {GroupName}");

        Assert.True(deny.Ok, "/city pg deny: " + deny.Message);
        AssertRefused(join, "no_invitation", "joining after the refusal");
        Assert.DoesNotContain(Info(refuser), group.Members);
    }

    // R2: groups are for citizens of the city only
    [AtlasScenario]
    public async Task Invite_Should_BeRefused_When_PlayerIsNoCitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        var (group, _) = await GivenGroupWithPlot(s);

        CommandResult invite = await s.Mayor.ExecuteCommand($"{CityCmd} pg invite {GroupName} {Name(stranger)}");

        AssertRefused(invite, "player_should_be_in_same_city", "inviting a stranger into a group");
        Assert.Empty(group.GetSentInvitations());
    }

    // ---- R3 --------------------------------------------------------------------------------

    // R3: on a group plot the group's citizen row is what members get; other citizens keep the plot's
    [AtlasScenario]
    public async Task GroupRights_Should_ApplyToMembers_And_NotToOtherCitizens()
    {
        // Arrange: the plot forbids citizens to build, the group allows it
        CityScene s = await GivenCity("member", "outsider");
        ITestPlayer member = s.Citizens[0], outsider = s.Citizens[1];
        var (group, plot) = await GivenGroupWithPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build off");
        Assert.False(plot.getPermsHandler().getPerm(PermGroup.CITIZEN, PermType.BUILD_AND_DESTROY_PERM),
            "precondition: citizens may not build on the plot");
        await JoinGroup(s, group, member);
        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} pg set perm {GroupName} build on");
        Assert.True(set.Ok, "precondition: /city pg set perm: " + set.Message);
        BlockPos spot = Middle(plot.Shape.Boxes[0]);

        // Act
        bool memberMay = AccessRules.Check(member.Player, spot, PermType.BUILD_AND_DESTROY_PERM);
        bool outsiderMay = AccessRules.Check(outsider.Player, spot, PermType.BUILD_AND_DESTROY_PERM);

        // Assert
        Assert.True(memberMay, "a group member may not build on the group plot");
        Assert.False(outsiderMay, "a citizen outside the group got the group's right");
    }

    // R3: the group's row replaces the plot's for members, also when it is the stricter one
    [AtlasScenario]
    public async Task GroupRights_Should_ReplaceCitizenRights_When_GroupIsStricter()
    {
        CityScene s = await GivenCity("member");
        ITestPlayer member = s.Citizens[0];
        var (group, plot) = await GivenGroupWithPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set perm citizen build on");
        await JoinGroup(s, group, member);
        await s.Mayor.ExecuteCommand($"{CityCmd} pg set perm {GroupName} build off");

        bool memberMay = AccessRules.Check(member.Player, Middle(plot.Shape.Boxes[0]), PermType.BUILD_AND_DESTROY_PERM);

        Assert.False(memberMay, "the plot's citizen row won over the group's for a member");
    }

    // ---- R4 --------------------------------------------------------------------------------

    // R4: a group flag overwrites the flag of its plots
    [AtlasScenario]
    public async Task GroupFlag_Should_OverwritePlotFlags()
    {
        CityScene s = await GivenCity();
        var (_, plot) = await GivenGroupWithPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp off");
        Assert.False(plot.getPermsHandler().pvpFlag, "precondition: pvp is off on the plot");

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} pg set flag {GroupName} pvp on");

        Assert.True(set.Ok, "/city pg set flag: " + set.Message);
        Assert.True(plot.getPermsHandler().pvpFlag, "the group's pvp did not reach its plot");
        AssertInvariants();
    }

    // R4: a plot added to a group takes the group's flags, whatever it had
    [AtlasScenario]
    public async Task AddPlot_Should_GiveThePlotTheGroupFlags()
    {
        // Arrange: pvp on for the plot, off for the group
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await s.Mayor.ExecuteCommand($"{PlotCmd} set pvp on");
        Assert.True(plot.getPermsHandler().pvpFlag, "precondition: pvp is on on the plot");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg create {GroupName}");
        Assert.True(PlotsGroupHandler.TryFind(s.City, GroupName, out PlotsGroup group), "precondition: the group exists");
        await s.Mayor.ExecuteCommand($"{CityCmd} pg set flag {GroupName} pvp off");
        Assert.False(group.Perms.pvpFlag, "precondition: pvp is off for the group");

        // Act
        await s.Mayor.ExecuteCommand($"{CityCmd} pg addplot {GroupName}");

        // Assert
        Assert.True(group.Equals(plot.Group), "the plot is not in the group");
        Assert.False(plot.getPermsHandler().pvpFlag, "the plot kept its own pvp in the group");
        AssertInvariants();
    }

    // ---- R5 --------------------------------------------------------------------------------

    /// <summary>
    /// A group with a plot and a fee of 5 already in force: the raise from 0 is settled by /cadmin
    /// day before anyone joins, so nobody has to accept it.
    /// </summary>
    private async Task<PlotsGroup> GivenGroupWithFee(CityScene s)
    {
        Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 0;
        var (group, _) = await GivenGroupWithPlot(s);
        await s.Mayor.ExecuteCommand($"{CityCmd} pg set fee {GroupName} 5");
        AllowAdminCommands(s.Mayor);
        await s.Mayor.ExecuteCommand($"{CadminCmd} day");
        Assert.Equal(5d, group.Fee);
        Assert.False(group.HasPendingFee, "precondition: the raise to 5 is settled");
        return group;
    }

    // R5, X4b: on the daily cycle a member pays the group fee into the treasury
    [AtlasScenario]
    public async Task Day_Should_ChargeTheGroupFee()
    {
        // Arrange
        CityScene s = await GivenCity("member");
        ITestPlayer member = s.Citizens[0];
        PlotsGroup group = await GivenGroupWithFee(s);
        await JoinGroup(s, group, member);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(member.Player.PlayerUID, 100);
        money.SetBalance(s.City.MoneyAccountName, 100000); // the upkeep must not drive the city into debt

        // Act
        CommandResult day = await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        // Assert
        Assert.True(day.Ok, "/cadmin day: " + day.Message);
        Assert.Equal(95m, money.GetBalance(member.Player.PlayerUID));
        Assert.Contains($"Transfer {member.Player.PlayerUID}->{s.City.MoneyAccountName} 5: Success", money.Log);
        Assert.Contains(Info(member), group.Members);
        AssertInvariants();
    }

    // R5: who cannot pay leaves the group, and stays a citizen
    [AtlasScenario]
    public async Task Day_Should_RemoveMember_When_HeCannotPayTheGroupFee()
    {
        CityScene s = await GivenCity("broke");
        ITestPlayer broke = s.Citizens[0];
        PlotsGroup group = await GivenGroupWithFee(s);
        await JoinGroup(s, group, broke);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 100000);

        await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        Assert.DoesNotContain(Info(broke), group.Members);
        Assert.True(s.City.Equals(Info(broke).City), "the group fee took the player out of the city");
        AssertInvariants();
    }

    // ---- R6 --------------------------------------------------------------------------------

    // R6: a raise waits; when it is due, who accepted stays and who did not leaves
    [AtlasScenario]
    public async Task Raise_Should_KeepWhoAccepted_And_DropTheRest_When_Due()
    {
        // Arrange: two members at fee 0, a raise to 10 with an hour's notice
        CityScene s = await GivenCity("agrees", "refuses");
        ITestPlayer agrees = s.Citizens[0], refuses = s.Citizens[1];
        var (group, _) = await GivenGroupWithPlot(s);
        await JoinGroup(s, group, agrees);
        await JoinGroup(s, group, refuses);
        Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 1;
        await s.Mayor.ExecuteCommand($"{CityCmd} pg set fee {GroupName} 10");
        Assert.True(group.HasPendingFee, "the raise was applied at once instead of announced");
        Assert.Equal(0d, group.Fee);
        CommandResult accept = await agrees.ExecuteCommand($"{CityCmd} pg acceptfee {GroupName}");
        Assert.True(accept.Ok, "/city pg acceptfee: " + accept.Message);
        AllowAdminCommands(s.Mayor);
        // The hour is over (the 30 s timer would settle it too; /cadmin day does it now).
        group.PendingFeeAt = TimeFunctions.GetEpochSeconds() - 1;

        // Act
        await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        // Assert
        Assert.Equal(10d, group.Fee);
        Assert.False(group.HasPendingFee, "the raise is still pending");
        Assert.Contains(Info(agrees), group.Members);
        Assert.DoesNotContain(Info(refuses), group.Members);
        AssertInvariants();
    }

    // R6: with PLOTSGROUP_FEE_RAISE_DELAY_HOURS 0 the raise settles on the next check; a member
    // who did not accept is out
    [AtlasScenario]
    public async Task Raise_Should_DropMemberWithoutAcceptFee_When_DelayIsZero()
    {
        CityScene s = await GivenCity("silent");
        ITestPlayer silent = s.Citizens[0];
        var (group, _) = await GivenGroupWithPlot(s);
        await JoinGroup(s, group, silent);
        Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 0;
        AllowAdminCommands(s.Mayor);

        await s.Mayor.ExecuteCommand($"{CityCmd} pg set fee {GroupName} 10");
        await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        Assert.Equal(10d, group.Fee);
        Assert.DoesNotContain(Info(silent), group.Members);
        AssertInvariants();
    }

    // R6: lowering the fee applies at once
    [AtlasScenario]
    public async Task LowerFee_Should_ApplyAtOnce()
    {
        CityScene s = await GivenCity("member");
        ITestPlayer member = s.Citizens[0];
        PlotsGroup group = await GivenGroupWithFee(s);
        await JoinGroup(s, group, member);
        Mod.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS = 24;

        CommandResult lower = await s.Mayor.ExecuteCommand($"{CityCmd} pg set fee {GroupName} 2");

        Assert.True(lower.Ok, "/city pg set fee: " + lower.Message);
        Assert.Equal(2d, group.Fee);
        Assert.False(group.HasPendingFee, "a lower fee was announced instead of applied");
        Assert.Contains(Info(member), group.Members);
    }
}
