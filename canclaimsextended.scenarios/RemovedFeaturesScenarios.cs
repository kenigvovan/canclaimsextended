using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Section S of TESTING_canclaimsextended.md: alliances and war are gone and leave nothing behind.
/// No alliance commands, also not through /cadmin as (S1); no ally rights group (S2); a boat once
/// shared with an alliance counts as shared with the city (S4); no alliance settings (S5); the war
/// plot types and CITY_SET_NEUTRAL cannot be chosen any more (S6). /plot set perm ally is in
/// RightsScenarios, /plot set type tavern in PlotTypeScenarios.
/// </summary>
[Trait("Category", "E2E")]
public class RemovedFeaturesScenarios : CxScenarioBase
{
    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    // ---- S1 ----------------------------------------------------------------------------------

    // S1: /alliance and the alliance chat /ac are no commands any more
    [AtlasTheory]
    [InlineData("/alliance list")]
    [InlineData("/alliance create Ally")]
    public async Task AllianceCommand_Should_BeUnknown(string command)
    {
        int area = NewArea();
        ITestPlayer player = await JoinAt("player" + area, area);

        CommandResult result = await player.ExecuteCommand(command);

        Assert.False(result.Ok, $"{command} ran: {result.Message}");
        Assert.True(result.Status == EnumCommandStatus.NoSuchCommand, $"{command}: status {result.Status}, expected NoSuchCommand");
    }

    // S1: the alliance chat
    [AtlasScenario]
    public async Task AllianceChat_Should_BeUnknown()
    {
        CityScene s = await GivenCity();

        CommandResult result = await s.Mayor.ExecuteCommand("/ac hi");

        Assert.False(result.Ok, "/ac ran: " + result.Message);
        Assert.True(result.Status == EnumCommandStatus.NoSuchCommand, $"/ac: status {result.Status}, expected NoSuchCommand");
    }

    // S1: /cadmin as runs only /city and /plot, so no alliance command gets through it
    [AtlasScenario]
    public async Task CadminAs_Should_RefuseAllianceCommand_And_RunCityCommand()
    {
        // Arrange: an admin of no city acting for the scene's city
        CityScene s = await GivenCity();
        ITestPlayer admin = await GivenStranger(s, "admin");
        AllowAdminCommands(admin);
        string city = s.City.GetPartName();

        // Act
        CommandResult alliance = await admin.ExecuteCommand($"{CadminCmd} as {city} alliance list");
        CommandResult info = await admin.ExecuteCommand($"{CadminCmd} as {city} {Mod.config.COMMAND_CITY} info");

        // Assert
        AssertRefused(alliance, "admin_as_only_city_plot", "/cadmin as ... alliance list");
        Assert.Equal(new object[] { Mod.config.COMMAND_CITY, Mod.config.COMMAND_PLOT }, alliance.Raw.MessageParams);
        Assert.True(info.Ok, "/cadmin as ... city info: " + info.Message);
        Assert.False(Info(admin).hasCity(), "acting for the city made the admin its citizen");
    }

    // ---- S2 ----------------------------------------------------------------------------------

    // S2: /city set perm takes no ally group; the city's rights stay
    [AtlasScenario]
    public async Task CityPerm_Should_RefuseTheAllyGroup()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        string city = s.City.getPermsHandler().ToString();
        string plotRights = plot.getPermsHandler().ToString();

        CommandResult set = await s.Mayor.ExecuteCommand($"{CityCmd} set perm ally use on");

        Assert.False(set.Ok, "/city set perm still takes the ally group: " + set.Message);
        Assert.Equal(city, s.City.getPermsHandler().ToString());
        Assert.Equal(plotRights, plot.getPermsHandler().ToString());
    }

    // S2: neither does /cadmin plot perm
    [AtlasScenario]
    public async Task CadminPlotPerm_Should_RefuseTheAllyGroup()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        AllowAdminCommands(s.Mayor);
        string before = plot.getPermsHandler().ToString();

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} plot perm ally use on");

        Assert.False(set.Ok, "/cadmin plot perm still takes the ally group: " + set.Message);
        Assert.Equal(before, plot.getPermsHandler().ToString());
    }

    // S2: the groups themselves - three words, three values, none of them an ally
    [AtlasScenario]
    public async Task PermGroups_Should_HaveNoAlly()
    {
        Assert.Equal(new[] { "stranger", "citizen", "friend" }, Codes.PermGroups);
        Assert.DoesNotContain(Enum.GetNames(typeof(PermGroup)), n => n.Contains("ALLY", StringComparison.OrdinalIgnoreCase));
        Assert.False(PermsHandler.TryParseGroup("ally", out _), "\"ally\" still parses as a rights group");
        await World.Ticks(1);
    }

    // ---- S4 ----------------------------------------------------------------------------------

    // S4: the boat command takes personal or city only; the word fails before any boat is looked up
    [AtlasScenario]
    public async Task BoatShare_Should_RefuseAlliance()
    {
        CityScene s = await GivenCity();

        CommandResult alliance = await s.Mayor.ExecuteCommand($"{CityCmd} boatshare 123456 alliance");
        CommandResult city = await s.Mayor.ExecuteCommand($"{CityCmd} boatshare 123456 city");

        Assert.False(alliance.Ok, "/city boatshare ... alliance ran: " + alliance.Message);
        // With "city" the handler runs and finds no boat; with "alliance" it must not even run.
        Assert.Equal("canclaimsextended:boat-look-at-one", city.Raw.StatusMessage);
        Assert.NotEqual("canclaimsextended:boat-look-at-one", alliance.Raw.StatusMessage);
    }

    // S4, N3: a boat stamped 2 (shared with the alliance) is shared with the city; 1 too, 0 is personal
    [AtlasScenario]
    public async Task BoatShareOf_Should_ReadStoredAllianceAsCity()
    {
        // Arrange: a vanilla boat (or, should the code not resolve, any entity: Of only reads the stamp)
        int area = NewArea();
        Entity boat;
        try
        {
            boat = World.SpawnEntity("game:boat-raft-aged", AreaOrigin(area).UpCopy(2));
        }
        catch (ArgumentException)
        {
            boat = World.SpawnEntity("game:strawdummy", AreaOrigin(area).UpCopy(2));
        }

        try
        {
            // Act + Assert
            boat.WatchedAttributes.SetInt(BoatShare.ModeAttr, 2);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, $"a boat stored as 2 reads {BoatShare.Of(boat)}, expected CITY");

            boat.WatchedAttributes.SetInt(BoatShare.ModeAttr, 1);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, $"a boat stored as 1 reads {BoatShare.Of(boat)}, expected CITY");

            boat.WatchedAttributes.SetInt(BoatShare.ModeAttr, 0);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.PERSONAL, $"a boat stored as 0 reads {BoatShare.Of(boat)}, expected PERSONAL");

            Assert.Equal(new[] { "PERSONAL", "CITY" }, Enum.GetNames(typeof(BoatShareMode)));
        }
        finally
        {
            boat.Die(EnumDespawnReason.Removed);
        }
        await World.Ticks(1);
    }

    // ---- S5 ----------------------------------------------------------------------------------

    // S5: the config has no alliance setting left
    [AtlasScenario]
    public async Task Config_Should_HaveNoAllianceMembers()
    {
        var members = typeof(Config)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m is FieldInfo or PropertyInfo)
            .Select(m => m.Name)
            .Where(n => n.Contains("ALLIANCE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(members.Count == 0, "Config still has: " + string.Join(", ", members));
        await World.Ticks(1);
    }

    // S5: /cadmin getcfg lists no alliance price
    [AtlasScenario]
    public async Task Getcfg_Should_ListNoAlliancePrice()
    {
        CityScene s = await GivenCity();
        AllowAdminCommands(s.Mayor);

        CommandResult cfg = await s.Mayor.ExecuteCommand($"{CadminCmd} getcfg");

        Assert.True(cfg.Ok, "/cadmin getcfg: " + cfg.Message);
        Assert.Contains("new_city_cost", cfg.Message);
        Assert.DoesNotContain("alliance", cfg.Message, StringComparison.OrdinalIgnoreCase);
    }

    // S3, S6: no right of the alliances is left in the enum
    [AtlasScenario]
    public async Task Permissions_Should_HaveNoAllianceRight()
    {
        Assert.DoesNotContain(Enum.GetNames(typeof(EnumPlayerPermissions)), n => n.StartsWith("ALLIANCE"));
        await World.Ticks(1);
    }

    // ---- S6 ----------------------------------------------------------------------------------

    // S6: the war plot types cannot be set; the plot keeps its type
    [AtlasTheory]
    [InlineData("tournament")]
    [InlineData("camp")]
    [InlineData("embassy")]
    public async Task SetType_Should_RefuseWarPlotType(string code)
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult set = await s.Mayor.ExecuteCommand($"{PlotCmd} set type {code}");

        AssertRefused(set, "unknown_plot_type", $"/plot set type {code}");
        Assert.Equal(PlotType.DEFAULT, plot.Type);
        Assert.DoesNotContain(code, PlotTypes.EnabledCodes);
        AssertInvariants();
    }

    // S6: a plot stored with a war type (2 tournament, 4 camp, 7 embassy) reads as a plain plot
    [AtlasScenario]
    public async Task StoredWarPlotType_Should_ReadAsDefault()
    {
        foreach (int stored in new[] { 2, 4, 7 })
        {
            string code = PlotTypes.Of((PlotType)stored).Code;
            Assert.True(code == "default", $"plot type {stored} reads as {code}, expected default");
        }
        await World.Ticks(1);
    }

    // S6: CITY_SET_NEUTRAL is not rankable any more
    [AtlasScenario]
    public async Task RankAddPerm_Should_RefuseCitySetNeutral()
    {
        // Arrange
        CityScene s = await GivenCity();
        var neutral = Enum.Parse<EnumPlayerPermissions>("CITY_SET_NEUTRAL");
        Assert.DoesNotContain(neutral, Mod.config.AVAILABLE_CITY_PERMISSIONS);
        CommandResult create = await s.Mayor.ExecuteCommand($"{CityCmd} rank create Herald");
        Assert.True(create.Ok && s.City.CustomCityRanks.ContainsKey("Herald"), "precondition: /city rank create: " + create.Message);

        // Act
        CommandResult add = await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Herald CITY_SET_NEUTRAL");

        // Assert
        AssertRefused(add, "rank_bad_permission", "/city rank addperm ... CITY_SET_NEUTRAL");
        Assert.Empty(s.City.CustomCityRanks["Herald"].Permissions);
        AssertInvariants();
    }
}
