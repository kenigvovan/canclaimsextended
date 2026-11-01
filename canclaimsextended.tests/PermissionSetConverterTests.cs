using canclaimsextended.src.rights;
using Newtonsoft.Json;

namespace canclaimsextended.tests
{
    // Rank rights and AVAILABLE_CITY_PERMISSIONS as stored by any earlier build.
    // Cases: TESTING_canclaimsextended.md Q3, S3, S6, Z1, Z2. No statics.
    public class PermissionSetConverterTests
    {
        private readonly PermissionSetConverter converter = new();

        private HashSet<EnumPlayerPermissions> Read(string json) =>
            JsonConvert.DeserializeObject<HashSet<EnumPlayerPermissions>>(json, converter)!;

        // Q3: today's names load as they are and change nothing.
        [Fact]
        public void Read_Should_KeepKnownNames_When_AllAreCurrent()
        {
            HashSet<EnumPlayerPermissions> set = Read("[\"CITY_INVITE\",\"CITY_SEE_LOG\"]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_INVITE, EnumPlayerPermissions.CITY_SEE_LOG }, set);
            Assert.False(converter.Changed);
            Assert.Empty(converter.Unknown);
        }

        // Z1: claiming land was split into chunks and plots; the old right keeps both halves.
        [Fact]
        public void Read_Should_TranslateClaimPlot_When_NameIsOld()
        {
            HashSet<EnumPlayerPermissions> set = Read("[\"CITY_CLAIM_PLOT\"]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_CLAIM_CHUNK, EnumPlayerPermissions.CITY_CREATE_PLOT }, set);
            Assert.True(converter.Changed);
            Assert.Empty(converter.Unknown);
        }

        [Fact]
        public void Read_Should_TranslateUnclaimPlot_When_NameIsOld()
        {
            HashSet<EnumPlayerPermissions> set = Read("[\"CITY_UNCLAIM_PLOT\"]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_UNCLAIM_CHUNK, EnumPlayerPermissions.CITY_DELETE_PLOT }, set);
            Assert.True(converter.Changed);
        }

        [Fact]
        public void Read_Should_TranslateBuyExtraPlot_When_NameIsOld()
        {
            HashSet<EnumPlayerPermissions> set = Read("[\"CITY_BUY_EXTRA_PLOT\"]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK }, set);
            Assert.True(converter.Changed);
        }

        // S3: removed alliance rights and plain typos are dropped, reported, and the rest stays.
        [Theory]
        [InlineData("ALLIANCE_INVITE")]
        [InlineData("ALLIANCE_KICK")]
        [InlineData("ALLIANCE_SET_NAME")]
        [InlineData("NO_SUCH_RIGHT")]
        [InlineData("city_invite")] // names are case-sensitive
        public void Read_Should_DropAndReport_When_NameIsUnknown(string name)
        {
            HashSet<EnumPlayerPermissions> set = Read($"[\"CITY_KICK\",\"{name}\"]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_KICK }, set);
            Assert.True(converter.Changed);
            Assert.Equal(new[] { name }, converter.Unknown);
        }

        // S3: numbers of the old alliance block (2048 and up) and other unknown numbers are dropped.
        [Theory]
        [InlineData(2048)]
        [InlineData(2049)]
        [InlineData(2060)]
        [InlineData(99999)]
        [InlineData(-1)]
        public void Read_Should_DropAndReport_When_NumberIsUnknown(int value)
        {
            HashSet<EnumPlayerPermissions> set = Read($"[1028,{value}]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_INVITE }, set);
            Assert.True(converter.Changed);
            Assert.Equal(new[] { value.ToString() }, converter.Unknown);
        }

        // Numbers of today's rights are kept and are not a change.
        [Fact]
        public void Read_Should_KeepKnownNumbers_When_StoredAsNumbers()
        {
            HashSet<EnumPlayerPermissions> set = Read("[1024,1085]");

            Assert.Equal(new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_CLAIM_CHUNK, EnumPlayerPermissions.CITY_SEE_LOG }, set);
            Assert.False(converter.Changed);
        }

        [Fact]
        public void Read_Should_ReturnAnEmptySet_When_ValueIsNull()
        {
            Assert.Empty(Read("null"));
        }

        // Reset clears what an earlier read reported.
        [Fact]
        public void Reset_Should_ClearChangedAndUnknown_When_Called()
        {
            Read("[\"ALLIANCE_INVITE\"]");

            converter.Reset();

            Assert.False(converter.Changed);
            Assert.Empty(converter.Unknown);
        }

        // Written by name, so a reordering of the enum could never shift stored rights.
        [Fact]
        public void Write_Should_WriteNames_When_Serializing()
        {
            var set = new HashSet<EnumPlayerPermissions> { EnumPlayerPermissions.CITY_INVITE };

            string json = JsonConvert.SerializeObject(set, converter);

            Assert.Equal("[\"CITY_INVITE\"]", json);
        }

        [Fact]
        public void WriteThenRead_Should_GiveTheSameSet_When_ManyRights()
        {
            var set = new HashSet<EnumPlayerPermissions>
            {
                EnumPlayerPermissions.CITY_CREATE_PLOT, EnumPlayerPermissions.PLOT_SET_FEE, EnumPlayerPermissions.CITY_PLOTSGROUP_SET_MOBSPAWN
            };

            HashSet<EnumPlayerPermissions> back = Read(JsonConvert.SerializeObject(set, converter));

            Assert.Equal(set, back);
            Assert.False(converter.Changed);
        }
    }
}
