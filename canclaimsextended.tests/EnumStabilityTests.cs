using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rights;

namespace canclaimsextended.tests
{
    // Numbers that are stored in the database, in configs, in ranks or in boat attributes must never
    // move. Cases: TESTING_canclaimsextended.md S2, S3, S4, S6, Z10, N1, Y1. No statics.
    // Members are looked up by name so the obsolete ones do not raise warnings here.
    public class EnumStabilityTests
    {
        private static int Value<T>(string name) where T : struct, Enum => Convert.ToInt32(Enum.Parse<T>(name));

        // Z10, S6: PLOTS.type is a plain int.
        [Theory]
        [InlineData("DEFAULT", 0)]
        [InlineData("TAVERN", 1)]
        [InlineData("TOURNAMENT", 2)]
        [InlineData("FARM", 3)]
        [InlineData("CAMP", 4)]
        [InlineData("SUMMON", 5)]
        [InlineData("TEMPLE", 6)]
        [InlineData("EMBASSY", 7)]
        [InlineData("MAIN_CITY_PLOT", 8)]
        [InlineData("PRISON", 9)]
        [InlineData("ORCHARD", 10)]
        [InlineData("VILLAGE_MAIN", 11)]
        [InlineData("SHOP", 12)]
        public void PlotType_Should_KeepItsStoredNumber(string name, int expected)
        {
            Assert.Equal(expected, Value<PlotType>(name));
        }

        [Fact]
        public void PlotType_Should_HaveNoMembersBeyondThePinnedOnes()
        {
            Assert.Equal(13, Enum.GetNames<PlotType>().Length);
        }

        // Z10: the obsolete war and village types stay as numbers but are marked obsolete.
        [Theory]
        [InlineData("TAVERN")]
        [InlineData("TOURNAMENT")]
        [InlineData("CAMP")]
        [InlineData("EMBASSY")]
        [InlineData("MAIN_CITY_PLOT")]
        [InlineData("VILLAGE_MAIN")]
        public void PlotType_Should_MarkTheOldTypesObsolete(string name)
        {
            var field = typeof(PlotType).GetField(name)!;

            Assert.NotNull(Attribute.GetCustomAttribute(field, typeof(ObsoleteAttribute)));
        }

        /// <summary>
        /// Every member of EnumPlayerPermissions with its current number. Ranks and old configs may
        /// hold numbers; a new member is appended at the end of its block and added here.
        /// </summary>
        public static TheoryData<string, int> PlayerPermissions() => new()
        {
            { "PLOT_SET_FS", 0 }, { "PLOT_SET_NFS", 1 },
            { "PLOT_CLAIM", 2 }, { "PLOT_UNCLAIM", 3 },
            { "PLOT_SET_ALL_CITY_PLOTS", 4 },
            { "PLOT_SET_ALL_OWN_PLOT", 5 },
            { "PLOT_SET_NAME", 6 }, { "PLOT_SET_FEE", 7 }, { "PLOT_SET_TYPE", 8 }, { "PLOT_SET_PVP", 9 },
            { "PLOT_SET_FIRE", 10 }, { "PLOT_SET_BLAST", 11 }, { "PLOT_SET_PLOT_ACCESS_PERMISSIONS", 12 },
            { "PLOT_INNER_PLOT", 13 },
            { "PLAYER_INFO_OTHER", 512 },
            { "CITY_CLAIM_CHUNK", 1024 }, { "CITY_UNCLAIM_CHUNK", 1025 }, { "CITY_BUY_EXTRA_CHUNK", 1026 }, { "CITY_BUY_OUTPOST", 1027 },
            { "CITY_INVITE", 1028 }, { "CITY_KICK", 1029 }, { "CITY_UNINVITE", 1030 }, { "SHOW_INVITES_SENT", 1031 },
            { "CITY_SET_ALL", 1032 }, { "CITY_SET_NAME", 1033 }, { "CITY_SET_OPEN_STATE", 1034 }, { "CITY_SET_PVP", 1035 },
            { "CITY_SET_FIRE", 1036 }, { "CITY_SET_BLAST", 1037 }, { "CITY_SET_GLOBAL_FEE", 1038 }, { "CITY_SET_DAILY_MSG", 1039 },
            { "CITY_SET_PLOT_ACCESS_PERMISSIONS", 1040 },
            { "CITY_SET_INV_MSG", 1041 },
            { "CITY_INFO", 1042 }, { "CITY_HERE", 1043 },
            { "CITY_SHOW_RANK_OTHERS", 1044 }, { "CITY_SET_RANK", 1045 }, { "CITY_REMOVE_RANK", 1046 },
            { "CITY_PRISON_ALL", 1047 }, { "CITY_CRIMINAL_ALL", 1048 }, { "CITY_ADD_CRIMINAL", 1049 }, { "CITY_REMOVE_CRIMINAL", 1050 },
            { "CITY_PRISON_ADD_CELL", 1051 }, { "CITY_PRISON_REMOVE_CELL", 1052 }, { "CITY_PRISON_LIST", 1053 },
            { "CITY_SET_SUMMON", 1054 },
            { "CITY_SET_OTHERS_PREFIX", 1055 },
            { "CITY_PLOTSGROUP_CREATE", 1056 }, { "CITY_PLOTSGROUP_REMOVE", 1057 }, { "CITY_PLOTSGROUP_ADD_PLAYER", 1058 },
            { "CITY_PLOTSGROUP_KICK_PLAYER", 1059 },
            { "CITY_PLOTSGROUP_ADD_PLOT", 1060 }, { "CITY_PLOTSGROUP_REMOVE_PLOT", 1061 }, { "CITY_PLOTSGROUP_LIST", 1062 },
            { "CITY_PLOTSGROUP_SET", 1063 },
            { "CITY_PLOTSGROUP_SET_PVP", 1064 }, { "CITY_PLOTSGROUP_SET_FIRE", 1065 }, { "CITY_PLOTSGROUP_SET_BLAST", 1066 },
            { "CITY_SET_PLOTS_COLOR", 1067 },
            { "CITY_SEE_BALANCE", 1068 },
            { "CITY_WITHDRAW_MONEY", 1069 }, { "CITY_CREATE_CITY_RANK", 1070 }, { "CITY_DELETE_CITY_RANK", 1071 },
            { "CITY_SEE_CITY_RANKS", 1072 }, { "CITY_ADD_PERMISSION_TO_RANK", 1073 },
            { "CITY_REMOVE_PERMISSION_FROM_RANK", 1074 },
            { "CITY_SET_EMBLEM", 1075 },
            { "CITY_SELL_PLOT_TO_CITY", 1076 }, { "CITY_BUY_PLOT_FROM_CITY", 1077 },
            { "CITY_PLOTSGROUP_SET_FEE", 1078 },
            { "PLOT_SET_MOBSPAWN", 1079 }, { "CITY_SET_MOBSPAWN", 1080 }, { "CITY_PLOTSGROUP_SET_MOBSPAWN", 1081 },
            { "CITY_SET_NEUTRAL", 1082 },
            { "CITY_CREATE_PLOT", 1083 }, { "CITY_DELETE_PLOT", 1084 },
            { "CITY_SEE_LOG", 1085 },
        };

        // S3, Q5
        [Theory]
        [MemberData(nameof(PlayerPermissions))]
        public void PlayerPermission_Should_KeepItsNumber(string name, int expected)
        {
            Assert.Equal(expected, Value<EnumPlayerPermissions>(name));
        }

        // A member added without a pin here is caught.
        [Fact]
        public void PlayerPermissions_Should_AllBePinned()
        {
            var pinned = PlayerPermissions().Select(row => (string)row[0]).ToHashSet();

            string[] missing = Enum.GetNames<EnumPlayerPermissions>().Where(n => !pinned.Contains(n)).ToArray();

            Assert.True(missing.Length == 0, "pin these in PlayerPermissions(): " + string.Join(", ", missing));
        }

        // S3: the alliance rights are gone.
        [Fact]
        public void PlayerPermissions_Should_HaveNoAllianceMembers()
        {
            Assert.DoesNotContain(Enum.GetNames<EnumPlayerPermissions>(), n => n.StartsWith("ALLIANCE", StringComparison.Ordinal));
            Assert.DoesNotContain(Enum.GetValues<EnumPlayerPermissions>(), p => (int)p >= 2048);
        }

        // S6: the war's neutrality right stays only as a number.
        [Fact]
        public void CitySetNeutral_Should_BeObsolete()
        {
            var field = typeof(EnumPlayerPermissions).GetField("CITY_SET_NEUTRAL")!;

            Assert.NotNull(Attribute.GetCustomAttribute(field, typeof(ObsoleteAttribute)));
        }

        // N1, S4: stored on the boat as an int.
        [Fact]
        public void BoatShareMode_Should_BePersonal0AndCity1Only()
        {
            Assert.Equal(0, (int)BoatShareMode.PERSONAL);
            Assert.Equal(1, (int)BoatShareMode.CITY);
            Assert.Equal(new[] { "PERSONAL", "CITY" }, Enum.GetNames<BoatShareMode>());
        }

        // S2: three rows in the rights table.
        [Fact]
        public void PermGroup_Should_HaveNoAlly()
        {
            Assert.Equal(new[] { "STRANGER", "CITIZEN", "COMRADE" }, Enum.GetNames<PermGroup>());
        }

        [Fact]
        public void PermType_Should_KeepUseBuildAttackOrder()
        {
            Assert.Equal(0, (int)PermType.USE_PERM);
            Assert.Equal(1, (int)PermType.BUILD_AND_DESTROY_PERM);
            Assert.Equal(2, (int)PermType.ATTACK_ANIMALS_PERM);
        }

        // S3: alliance log entries are gone (the old rows are deleted on load, see DatabaseTests).
        [Theory]
        [InlineData("AllianceJoined")]
        [InlineData("AllianceLeft")]
        public void CityLogEvent_Should_HaveNoAllianceEvents(string name)
        {
            Assert.DoesNotContain(name, Enum.GetNames<EnumCityLogEvent>());
            Assert.False(Enum.TryParse<EnumCityLogEvent>(name, out _));
        }

        // Z13, U4: CITYLOG.event is stored by name.
        [Theory]
        [InlineData("CityCreated")]
        [InlineData("PlotSold")]
        [InlineData("PlotTypeChanged")]
        [InlineData("PlotReshaped")]
        [InlineData("RankGranted")]
        [InlineData("RankRevoked")]
        public void CityLogEvent_Should_KeepItsStoredName(string name)
        {
            Assert.Contains(name, Enum.GetNames<EnumCityLogEvent>());
        }
    }
}
