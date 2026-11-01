using System.Reflection;
using canclaimsextended.src;
using canclaimsextended.src.rights;
using Moq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.tests
{
    // Config.LoadConfig on a mocked api: migration of older files and the clamps of Validate.
    // Cases: TESTING_canclaimsextended.md Z1, Z2, U4, C6, E9, K (PRISON_SENTENCE_HOURS), V5, S5.
    // The file is the object LoadModConfig returns; files are read with Newtonsoft, as the game
    // does, so the attributes on the fields (Replace, PermissionSetConverter) take part.
    [Collection("ModStatics")]
    public class ConfigTests : IDisposable
    {
        private readonly Config previousConfig = Mod.config;
        private readonly ICoreServerAPI previousSapi = Mod.sapi;
        private readonly Mock<ICoreAPI> api = new();
        private readonly Mock<ILogger> logger = new();

        public ConfigTests()
        {
            api.Setup(a => a.Logger).Returns(logger.Object);
        }

        public void Dispose()
        {
            Mod.config = previousConfig;
            Mod.sapi = previousSapi;
        }

        /// <summary>The rights config version 2 made rankable (Config.RankableSinceVersion2, private).</summary>
        private static EnumPlayerPermissions[] RankableSinceVersion2() =>
            (EnumPlayerPermissions[])typeof(Config)
                .GetField("RankableSinceVersion2", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

        private static Config FromFile(string json) => JsonConvert.DeserializeObject<Config>(json)!;

        private Config Load(Config? file)
        {
            api.Setup(a => a.LoadModConfig<Config>(Config.FILE_NAME)).Returns(file!);
            Config.LoadConfig(api.Object);
            return Mod.config;
        }

        private int LoggedErrors() => logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Error));

        // Z2, U4: CITY_SEE_LOG is rankable by default.
        [Fact]
        public void Defaults_Should_ContainSeeLogAndEveryV2Right_When_ConfigIsNew()
        {
            var config = new Config();

            Assert.Contains(EnumPlayerPermissions.CITY_SEE_LOG, config.AVAILABLE_CITY_PERMISSIONS);
            Assert.All(RankableSinceVersion2(), p => Assert.Contains(p, config.AVAILABLE_CITY_PERMISSIONS));
        }

        [Fact]
        public void RankableSinceVersion2_Should_ContainSeeLog()
        {
            Assert.Contains(EnumPlayerPermissions.CITY_SEE_LOG, RankableSinceVersion2());
        }

        // Z2: a CONFIG_VERSION 1 file gets the rights of version 2 added and becomes version 2.
        [Fact]
        public void Load_Should_AddV2RightsAndSetVersion2_When_FileIsVersion1()
        {
            Config file = FromFile("{ \"CONFIG_VERSION\": 1, \"AVAILABLE_CITY_PERMISSIONS\": [\"CITY_INVITE\"] }");
            Assert.DoesNotContain(EnumPlayerPermissions.CITY_SEE_LOG, file.AVAILABLE_CITY_PERMISSIONS); // precondition: Replace

            Config loaded = Load(file);

            Assert.Equal(2, loaded.CONFIG_VERSION);
            Assert.Contains(EnumPlayerPermissions.CITY_INVITE, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Contains(EnumPlayerPermissions.CITY_SEE_LOG, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.All(RankableSinceVersion2(), p => Assert.Contains(p, loaded.AVAILABLE_CITY_PERMISSIONS));
            // Only those: rights the admin left out before version 2 stay out.
            Assert.DoesNotContain(EnumPlayerPermissions.CITY_KICK, loaded.AVAILABLE_CITY_PERMISSIONS);
            api.Verify(a => a.StoreModConfig(loaded, Config.FILE_NAME), Times.Once());
        }

        // Z2: once at version 2, an admin's removal is not undone on the next start.
        [Fact]
        public void Load_Should_KeepAnAdminsRemoval_When_FileIsVersion2()
        {
            Config file = FromFile("{ \"CONFIG_VERSION\": 2, \"AVAILABLE_CITY_PERMISSIONS\": [\"CITY_INVITE\", \"CITY_KICK\"] }");

            Config loaded = Load(file);

            Assert.Equal(2, loaded.CONFIG_VERSION);
            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_INVITE, EnumPlayerPermissions.CITY_KICK },
                loaded.AVAILABLE_CITY_PERMISSIONS);
        }

        // Z1: before version 1 claiming was one right; whoever could hand it out gets the plot half too.
        [Fact]
        public void Load_Should_AddPlotHalves_When_FileIsVersion0()
        {
            Config file = FromFile("{ \"AVAILABLE_CITY_PERMISSIONS\": [\"CITY_CLAIM_PLOT\", \"CITY_UNCLAIM_PLOT\"] }");

            Config loaded = Load(file);

            Assert.Contains(EnumPlayerPermissions.CITY_CLAIM_CHUNK, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Contains(EnumPlayerPermissions.CITY_CREATE_PLOT, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Contains(EnumPlayerPermissions.CITY_UNCLAIM_CHUNK, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Contains(EnumPlayerPermissions.CITY_DELETE_PLOT, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Equal(2, loaded.CONFIG_VERSION);
        }

        // S3, S5: removed ALLIANCE_* rights and alliance keys in an old file do not break it.
        [Fact]
        public void Load_Should_IgnoreAllianceLeftovers_When_FileIsOld()
        {
            Config file = FromFile("{ \"CONFIG_VERSION\": 2, \"NEW_ALLIANCE_COST\": 100, \"COMMAND_ALLIANCE\": \"alliance\", " +
                                   "\"BOAT_SHARE_WITH_ALLIANCE\": true, \"AVAILABLE_CITY_PERMISSIONS\": [\"CITY_INVITE\", \"ALLIANCE_INVITE\"] }");

            Config loaded = Load(file);

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_INVITE }, loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.Equal(0, LoggedErrors());
        }

        // A missing file gives the defaults, and they are written.
        [Fact]
        public void Load_Should_UseAndStoreDefaults_When_ThereIsNoFile()
        {
            Config loaded = Load(null);

            Assert.Equal(2, loaded.CONFIG_VERSION);
            Assert.Equal(16, loaded.CLAIM_CHUNK_SIZE);
            api.Verify(a => a.StoreModConfig(loaded, Config.FILE_NAME), Times.Once());
        }

        // A file that cannot be read is not overwritten with defaults.
        [Fact]
        public void Load_Should_NotStore_When_FileCannotBeRead()
        {
            Mod.config = null!;
            api.Setup(a => a.LoadModConfig<Config>(Config.FILE_NAME)).Throws(new JsonReaderException("typo"));

            Config.LoadConfig(api.Object);

            Assert.NotNull(Mod.config);
            Assert.Equal(1, LoggedErrors());
            api.Verify(a => a.StoreModConfig(It.IsAny<Config>(), It.IsAny<string>()), Times.Never());
        }

        // C6: only 16 or 32; anything else is reported and replaced by 16.
        [Theory]
        [InlineData(20)]
        [InlineData(0)]
        [InlineData(64)]
        public void Load_Should_UseChunkSize16AndLogAnError_When_SizeIsNot16Or32(int size)
        {
            Config loaded = Load(new Config { CONFIG_VERSION = 2, CLAIM_CHUNK_SIZE = size });

            Assert.Equal(16, loaded.CLAIM_CHUNK_SIZE);
            Assert.Equal(1, LoggedErrors());
        }

        [Fact]
        public void Load_Should_KeepChunkSize32_When_Configured()
        {
            Config loaded = Load(new Config { CONFIG_VERSION = 2, CLAIM_CHUNK_SIZE = 32 });

            Assert.Equal(32, loaded.CLAIM_CHUNK_SIZE);
            Assert.Equal(0, LoggedErrors());
        }

        // E9: above 100% a claim and unclaim round would earn money.
        [Theory]
        [InlineData(150.0, 100.0)]
        [InlineData(-5.0, 0.0)]
        [InlineData(50.0, 50.0)]
        public void Load_Should_ClampRefundPercent_When_OutOfRange(double percent, double expected)
        {
            Config loaded = Load(new Config { CONFIG_VERSION = 2, PLOT_UNCLAIM_REFUND_PERCENT = percent });

            Assert.Equal(expected, loaded.PLOT_UNCLAIM_REFUND_PERCENT);
        }

        // K, X4a: a sentence is at least an hour.
        [Theory]
        [InlineData(0, 1)]
        [InlineData(-3, 1)]
        [InlineData(5, 5)]
        public void Load_Should_KeepPrisonSentenceAtLeastOneHour(int hours, int expected)
        {
            Config loaded = Load(new Config { CONFIG_VERSION = 2, PRISON_SENTENCE_HOURS = hours });

            Assert.Equal(expected, loaded.PRISON_SENTENCE_HOURS);
        }

        // V5: a tick listener of 0 ms would run every tick.
        [Theory]
        [InlineData(0, 50)]
        [InlineData(10, 50)]
        [InlineData(500, 500)]
        public void Load_Should_KeepMovementCheckAtLeast50Ms(int ms, int expected)
        {
            Config loaded = Load(new Config { CONFIG_VERSION = 2, MOVEMENT_CHECK_MS = ms });

            Assert.Equal(expected, loaded.MOVEMENT_CHECK_MS);
        }

        // A json null in a list becomes something usable rather than a crash later.
        [Fact]
        public void Load_Should_ReplaceNullLists_When_FileHasNulls()
        {
            Config file = FromFile("{ \"CONFIG_VERSION\": 2, \"AVAILABLE_CITY_PERMISSIONS\": null, \"PROTECTED_MOB_TYPES\": null, \"BACKUP_TIMES\": null }");

            Config loaded = Load(file);

            Assert.NotNull(loaded.AVAILABLE_CITY_PERMISSIONS);
            Assert.NotNull(loaded.PROTECTED_MOB_TYPES);
            Assert.NotNull(loaded.BACKUP_TIMES);
        }

        // A plot type added later (shop) gets its default price in an older file; the host's prices stay.
        [Fact]
        public void Load_Should_FillMissingPlotTypePrices_When_FileIsOlder()
        {
            Config file = FromFile("{ \"CONFIG_VERSION\": 2, \"PLOT_TYPE_SET_COSTS\": { \"farm\": 99 } }");

            Config loaded = Load(file);

            Assert.Equal(99.0, loaded.PLOT_TYPE_SET_COSTS["farm"]);
            Assert.Equal(new Config().PLOT_TYPE_SET_COSTS["shop"], loaded.PLOT_TYPE_SET_COSTS["shop"]);
        }
    }
}
