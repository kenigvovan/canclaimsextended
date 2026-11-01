using canclaimsextended.src;
using canclaimsextended.src.database;
using canclaimsextended.src.part;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    // Opening the database and reading a city back. Cases: TESTING_canclaimsextended.md Y1, Y2.
    [Collection("ModStatics")]
    public class DatabaseTests : DatabaseTestBase
    {
        // Y1
        [Fact]
        public void NewFile_Should_HaveUserVersion101()
        {
            Open();

            Assert.Equal(101, SQLiteTables.SCHEMA_VERSION);
            Assert.Equal(101L, Count("PRAGMA user_version"));
            Assert.Equal(0, LoggedErrors());
        }

        [Fact]
        public void NewFile_Should_HaveNoAlliancesTable()
        {
            Open();

            Assert.False(TableExists("ALLIANCES"));
            Assert.DoesNotContain("alliance", Columns("CITIES"));
        }

        [Theory]
        [InlineData("CITIES")]
        [InlineData("PLAYERS")]
        [InlineData("PLOTS")]
        [InlineData("PLOTBOXES")]
        [InlineData("WORLDFLAGS")]
        [InlineData("INVITATIONS")]
        [InlineData("CITYCHUNKS")]
        [InlineData("CITYLOG")]
        [InlineData("PRISONERS")]
        [InlineData("CRIMINALS")]
        [InlineData("PLOTSGROUPS")]
        [InlineData("PLOTSGROUPMEMBERS")]
        [InlineData("PLOTEXTRAS")]
        public void NewFile_Should_HaveEveryTable(string table)
        {
            Open();

            Assert.True(TableExists(table), table + " was not created");
        }

        // Columns added after CITIES first shipped are there on a new file too.
        [Theory]
        [InlineData("fee")]
        [InlineData("emblem")]
        [InlineData("bonuschunks")]
        [InlineData("dailymsg")]
        [InlineData("ranks")]
        public void NewFile_Should_HaveTheCityColumn(string column)
        {
            Open();

            Assert.Contains(column, Columns("CITIES"));
        }

        // Every server start opens the same file again: nothing changes and nothing fails.
        [Fact]
        public void Open_Should_Succeed_When_FileWasOpenedBefore()
        {
            Open().close();

            Exception? error = Record.Exception(() => Open());

            Assert.Null(error);
            Assert.Equal(101L, Count("PRAGMA user_version"));
            Assert.Equal(0, LoggedErrors());
        }

        // A file of another format (claims' layout) is refused, not written into.
        [Fact]
        public void Open_Should_Refuse_When_FileHasAnotherFormat()
        {
            Exec("CREATE TABLE SOMETHING(a TEXT)", "PRAGMA user_version = 3");

            Assert.Throws<InvalidOperationException>(() => Open());
            Assert.Equal(3L, Count("PRAGMA user_version"));
        }

        // Y1: a city written and read back keeps every field. Columns are read by name, so fee,
        // emblem and bonuschunks land in their own fields.
        [Fact]
        public void Load_Should_ReadEveryCityField_When_CityWasSaved()
        {
            SQLiteDatabaseHandler handler = Open();
            var saved = new City("Alpha", "city-a")
            {
                TimeStampCreated = 100,
                openCity = true,
                invMsg = "welcome",
                cityColor = 123,
                DebtBalance = 4.5,
                fee = 9,
                Emblem = "color_red;cross_white",
                DailyMsg = "hello"
            };
            saved.setBonusPlots(2);
            saved.setBonusChunks(7);
            saved.getPermsHandler().setPerms("pvp;citizen:use;");
            handler.saveCity(saved);
            handler.saveEverything();

            Mod.dataStorage = new DataStorage();
            bool ok = handler.loadEverything();

            Assert.True(ok, "loadEverything reported a failure");
            Assert.Equal(0, LoggedErrors());
            Assert.True(Mod.dataStorage.GetCityByGUID("city-a", out City city), "city-a was not loaded");
            Assert.NotSame(saved, city);
            Assert.Equal("Alpha", city.GetPartName());
            Assert.Equal(100L, city.TimeStampCreated);
            Assert.True(city.openCity);
            Assert.Equal("welcome", city.invMsg);
            Assert.Equal(2, city.getBonusPlots());
            Assert.Equal(7, city.getBonusChunks());
            Assert.False(city.isTechnicalCity());
            Assert.Equal(123, city.cityColor);
            Assert.Equal(4.5, city.DebtBalance);
            Assert.Equal(9, city.fee);
            Assert.Equal("color_red;cross_white", city.Emblem);
            Assert.Equal("hello", city.DailyMsg);
            Assert.Equal("pvp;citizen:use;", city.getPermsHandler().ToString());
        }

        // Y1: the stored row itself, not only what the load makes of it.
        [Fact]
        public void Save_Should_WriteFeeEmblemAndBonusChunks_When_Flushed()
        {
            SQLiteDatabaseHandler handler = Open();
            var city = new City("Beta", "city-b", isTechnical: true) { fee = 3, Emblem = "color_blue" };
            city.setBonusChunks(5);

            handler.saveCity(city);
            handler.saveEverything();

            string[] row = Assert.Single(Rows("SELECT fee, emblem, bonuschunks, istechnical FROM CITIES WHERE guid='city-b'"));
            Assert.Equal(new[] { "3", "color_blue", "5", "1" }, row);
        }
    }
}
