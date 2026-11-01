using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src;
using canclaimsextended.src.part.structure;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// /cadmin: the daily cycle on demand (X4b), reload and the settings that only a restart applies
/// (Z4, X8), the plot dump (X4c), and the refusal of a player without the admin role (X1).
/// </summary>
[Trait("Category", "E2E")]
public class AdminScenarios : CxScenarioBase
{
    // X1: a player whose role is not in ROLE_CODES_WITH_ADMIN_RIGHTS is refused
    [AtlasScenario]
    public async Task Cadmin_Should_BeRefused_When_PlayerIsNoAdmin()
    {
        CityScene s = await GivenCity();
        string role = s.Mayor.Player.Role.Code;
        bool hadRole = Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Remove(role);
        AddCleanup(() => { if (hadRole) Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Add(role); });

        CommandResult result = await s.Mayor.ExecuteCommand($"{CadminCmd} getcfg");

        Assert.False(result.Ok, "a player without the admin role ran /cadmin");
    }

    // X4b, T3: /cadmin day collects the citizen fee now
    [AtlasScenario]
    public async Task Day_Should_CollectTheCitizenFee()
    {
        // Arrange: a fee of 10, a citizen with 100
        CityScene s = await GivenCity("payer");
        ITestPlayer payer = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 10");
        Assert.Equal(10, s.City.fee);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(payer.Player.PlayerUID, 100);
        AllowAdminCommands(s.Mayor);

        // Act
        CommandResult day = await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        // Assert
        Assert.True(day.Ok, "/cadmin day: " + day.Message);
        Assert.Equal(90m, money.GetBalance(payer.Player.PlayerUID));
        Assert.Contains($"Transfer {payer.Player.PlayerUID}->{s.City.MoneyAccountName} 10: Success", money.Log);
        Assert.True(s.City.Equals(Info(payer).City), "the citizen who paid was removed");
    }

    // X4b, T3: who cannot pay is removed with DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE
    [AtlasScenario]
    public async Task Day_Should_RemoveCitizen_When_HeCannotPayTheFee()
    {
        CityScene s = await GivenCity("broke");
        ITestPlayer broke = s.Citizens[0];
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 10");
        UseFakeEconomy();
        AllowAdminCommands(s.Mayor);

        await s.Mayor.ExecuteCommand($"{CadminCmd} day");

        Assert.False(Info(broke).hasCity(), "a citizen who could not pay stayed in the city");
        AssertInvariants();
    }

    // Z4, X8: a restart-only flag changed in the file keeps its start value on reload and is named
    [AtlasScenario]
    public async Task Reload_Should_KeepRestartOnlyFlags_And_NameThem()
    {
        // Arrange: the file says the opposite of what the server started with
        int area = NewArea();
        ITestPlayer admin = await JoinAt("admin" + area, area);
        bool atStart = Mod.config.FIRE_PROTECTION_PATCH;
        Mod.config.FIRE_PROTECTION_PATCH = !atStart;
        Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        Mod.config.FIRE_PROTECTION_PATCH = atStart;
        AddCleanup(() =>
        {
            Mod.config.FIRE_PROTECTION_PATCH = atStart;
            Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        });
        // After the file is written: reload swaps Mod.config for the file's copy, so a role added
        // before would be stored to the file and outlive the cleanup, which edits the new object.
        AllowAdminCommands(admin);

        // Act
        CommandResult reload = await admin.ExecuteCommand($"{CadminCmd} reload");

        // Assert
        Assert.True(reload.Ok, "/cadmin reload: " + reload.Message);
        Assert.Equal(atStart, Mod.config.FIRE_PROTECTION_PATCH);
        Assert.Contains(nameof(Config.FIRE_PROTECTION_PATCH), reload.Message);
    }

    // X8: an ordinary setting changed in the file applies on reload, without a restart note
    [AtlasScenario]
    public async Task Reload_Should_ApplyOrdinarySettings()
    {
        int area = NewArea();
        ITestPlayer admin = await JoinAt("admin" + area, area);
        double before = Mod.config.MAX_CITY_FEE;
        Mod.config.MAX_CITY_FEE = before + 7;
        Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        Mod.config.MAX_CITY_FEE = before;
        AddCleanup(() =>
        {
            Mod.config.MAX_CITY_FEE = before;
            Mod.sapi.StoreModConfig(Mod.config, Config.FILE_NAME);
        });
        // After the file is written, as in Reload_Should_KeepRestartOnlyFlags_And_NameThem.
        AllowAdminCommands(admin);

        CommandResult reload = await admin.ExecuteCommand($"{CadminCmd} reload");

        Assert.True(reload.Ok, "/cadmin reload: " + reload.Message);
        Assert.Equal(before + 7, Mod.config.MAX_CITY_FEE);
        Assert.DoesNotContain("FIRE_PROTECTION_PATCH", reload.Message);
    }

    // X4c: /cadmin plot dump is /cadmin plot info, with the group, the fee and the type's data
    [AtlasScenario]
    public async Task PlotDump_Should_ShowThePlotUnderTheAdmin()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        AllowAdminCommands(s.Mayor);

        CommandResult dump = await s.Mayor.ExecuteCommand($"{CadminCmd} plot dump");

        Assert.True(dump.Ok, "/cadmin plot dump: " + dump.Message);
        Assert.Contains(plot.Guid, dump.Message);
        Assert.Contains("number: " + plot.Number, dump.Message);
        Assert.Contains("group: -", dump.Message);
    }
}
