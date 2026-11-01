using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Defaults a fresh server starts with, as changed by the fixes of section Z: blasts off (Z12, I5),
/// the first summon point at four citizens (Z3), an unset boat is personal (Z5), the rankable
/// rights of config version 2 (Z2), and the plot type numbers kept stable (Z10).
/// </summary>
[Trait("Category", "E2E")]
public class DefaultsScenarios : CxScenarioBase
{
    // Z12, I5: a new city, its new plot and a new plots group allow no blasts
    [AtlasScenario]
    public async Task BlastFlag_Should_BeOff_When_CityAndPlotAreNew()
    {
        // Arrange, Act
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        // Assert
        Assert.False(s.City.getPermsHandler().blastFlag, "a new city allows blasts");
        Assert.False(plot.getPermsHandler().blastFlag, "a new plot allows blasts");
        // A plots group starts from a fresh PermsHandler (PlotsGroup.Perms).
        Assert.False(new PermsHandler().blastFlag, "a fresh PermsHandler allows blasts");
        AssertInvariants();
    }

    // Z3: the default levels give the first summon point with four citizens, none before
    [AtlasScenario]
    public async Task SummonPoints_Should_StartAtFourCitizens_When_LevelsAreDefault()
    {
        // Act: the argument is the citizens count; the level of a city that size is returned
        int atOne = Settings.GetCityLevelInfo(1).SummonPoints;
        int atThree = Settings.GetCityLevelInfo(3).SummonPoints;
        int atFour = Settings.GetCityLevelInfo(4).SummonPoints;

        // Assert
        Assert.True(atOne == 0, $"a city of 1 citizen has {atOne} summon points, expected 0");
        Assert.True(atThree == 0, $"a city of 3 citizens has {atThree} summon points, expected 0");
        Assert.True(atFour == 1, $"a city of 4 citizens has {atFour} summon points, expected 1");
        await World.Ticks(1);
    }

    // Z5, N1: a boat without the share stamp is personal
    [AtlasScenario]
    public async Task BoatShare_Should_BePersonal_When_NothingIsSet()
    {
        BoatShareMode mode = BoatShare.Of(null);

        Assert.True(mode == BoatShareMode.PERSONAL, $"an unset boat is {mode}, expected PERSONAL");
        await World.Ticks(1);
    }

    // Z2: the default config is version 2 and offers the rights ranks may carry since then
    [AtlasScenario]
    public async Task AvailableCityPermissions_Should_HoldVersion2Rights_When_ConfigIsDefault()
    {
        var available = Mod.config.AVAILABLE_CITY_PERMISSIONS;

        Assert.True(Mod.config.CONFIG_VERSION == 2, $"CONFIG_VERSION is {Mod.config.CONFIG_VERSION}, expected 2");
        foreach (EnumPlayerPermissions perm in new[]
        {
            EnumPlayerPermissions.CITY_SEE_LOG, EnumPlayerPermissions.CITY_BUY_OUTPOST,
            EnumPlayerPermissions.CITY_PLOTSGROUP_CREATE, EnumPlayerPermissions.CITY_SET_ALL
        })
        {
            Assert.True(available.Contains(perm), $"AVAILABLE_CITY_PERMISSIONS lacks {perm}");
        }
        await World.Ticks(1);
    }

    // Z10: the plot type numbers stored in the database did not shift when dead code was removed
    [AtlasScenario]
    public async Task PlotTypeNumbers_Should_StayStable()
    {
        Assert.True((int)PlotType.TEMPLE == 6, $"TEMPLE is {(int)PlotType.TEMPLE}, expected 6");
        Assert.True((int)PlotType.PRISON == 9, $"PRISON is {(int)PlotType.PRISON}, expected 9");
        Assert.True((int)PlotType.ORCHARD == 10, $"ORCHARD is {(int)PlotType.ORCHARD}, expected 10");
        Assert.True((int)PlotType.SHOP == 12, $"SHOP is {(int)PlotType.SHOP}, expected 12");
        await World.Ticks(1);
    }
}
