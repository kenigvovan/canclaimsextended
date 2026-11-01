using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.perms;
using canclaimsextended.src.perms.type;

namespace canclaimsextended.tests
{
    // The flags and the rights table of a city, plot or group, and the string they are stored as.
    // Cases: TESTING_canclaimsextended.md H1, H2, F7, I5, S2, S3, Z12. No statics are touched.
    public class PermsHandlerTests
    {
        private static readonly string[] Flags = { "pvp", "fire", "blast", "nomobspawn" };
        // ally was removed with the alliances (S2).
        private static readonly string[] Groups = { "stranger", "citizen", "friend" };
        private static readonly string[] Types = { "use", "build", "attack" };

        public static TheoryData<string> AllValues()
        {
            var data = new TheoryData<string>();
            foreach (string f in Flags) data.Add(f);
            foreach (string g in Groups)
                foreach (string t in Types)
                    data.Add(g + ":" + t);
            return data;
        }

        /// <summary>All 13 values by name.</summary>
        private static Dictionary<string, bool> Snapshot(PermsHandler h)
        {
            var s = new Dictionary<string, bool>
            {
                ["pvp"] = h.pvpFlag,
                ["fire"] = h.fireFlag,
                ["blast"] = h.blastFlag,
                ["nomobspawn"] = h.noMobSpawnFlag,
            };
            foreach (string g in Groups)
            {
                Assert.True(PermsHandler.TryParseGroup(g, out PermGroup group), "unknown group " + g);
                foreach (string t in Types)
                {
                    Assert.True(PermsHandler.TryParseType(t, out PermType type), "unknown type " + t);
                    s[g + ":" + t] = h.getPerm(group, type);
                }
            }
            return s;
        }

        /// <summary>Flips one value: flags by their chat name, rights by group and type.</summary>
        private static void Flip(PermsHandler h, string value)
        {
            bool now = Snapshot(h)[value];
            if (!value.Contains(':'))
            {
                Assert.True(h.trySetFlag(value, !now), "trySetFlag refused " + value);
                return;
            }
            string[] parts = value.Split(':');
            PermsHandler.TryParseGroup(parts[0], out PermGroup group);
            PermsHandler.TryParseType(parts[1], out PermType type);
            h.setPerm(group, type, !now);
        }

        private static PermsHandler AllFlipped()
        {
            var h = new PermsHandler();
            foreach (string value in Snapshot(h).Keys) Flip(h, value);
            return h;
        }

        // The stored words are part of the database format and command arguments.
        [Fact]
        public void Codes_Should_ListStrangerCitizenFriend_When_Read()
        {
            Assert.Equal(Groups, Codes.PermGroups);
            Assert.Equal(Types, Codes.PermTypes);
            Assert.Equal(Flags, Codes.Flags);
        }

        // H1, Y1: every value survives the database string.
        [Theory]
        [MemberData(nameof(AllValues))]
        public void Values_Should_SurviveSaveAndLoad_When_OneIsChanged(string value)
        {
            var saved = new PermsHandler();
            Flip(saved, value);

            var loaded = new PermsHandler();
            loaded.setPerms(saved.ToString());

            Assert.Equal(Snapshot(saved), Snapshot(loaded));
        }

        // H1, Y1
        [Fact]
        public void Values_Should_SurviveSaveAndLoad_When_AllAreOn()
        {
            PermsHandler saved = AllFlipped();

            var loaded = new PermsHandler();
            loaded.setPerms(saved.ToString());

            Assert.Equal(Snapshot(saved), Snapshot(loaded));
            Assert.All(Snapshot(loaded).Values, v => Assert.True(v));
        }

        // H1: one switch never moves another value.
        [Theory]
        [MemberData(nameof(AllValues))]
        public void Change_Should_TouchOnlyThatValue_When_OneValueIsFlipped(string value)
        {
            var h = new PermsHandler();
            Dictionary<string, bool> before = Snapshot(h);

            Flip(h, value);

            Dictionary<string, bool> after = Snapshot(h);
            string[] changed = before.Keys.Where(k => before[k] != after[k]).ToArray();
            Assert.Equal(new[] { value }, changed);
        }

        // H2, F7: a plot taking the city's rights copies every value, as an independent copy.
        [Fact]
        public void SetPerm_Should_CopyEverything_When_GivenAnotherHandler()
        {
            var plot = new PermsHandler();
            PermsHandler city = AllFlipped();

            plot.setPerm(city);

            Assert.Equal(Snapshot(city), Snapshot(plot));
            city.setPerm(PermGroup.CITIZEN, PermType.USE_PERM, false);
            Assert.True(plot.getPerm(PermGroup.CITIZEN, PermType.USE_PERM), "setPerm shared the array instead of copying it");
        }

        // Z12, I5: no blasts unless someone switches them on.
        [Fact]
        public void BlastFlag_Should_BeFalse_When_HandlerIsNew()
        {
            Assert.False(new PermsHandler().blastFlag);
        }

        // Z12: a new city starts without blasts too.
        [Fact]
        public void BlastFlag_Should_BeFalse_When_CityIsNew()
        {
            var city = new City("TestCity", "city-guid");

            Assert.False(city.getPermsHandler().blastFlag);
        }

        // S3: a right of the removed ally group in an old string loads without error, is dropped,
        // and everything else in the string stays.
        [Fact]
        public void Load_Should_DropAllyEntriesAndKeepTheRest_When_StoredStringHasAlly()
        {
            var h = new PermsHandler();

            Exception? error = Record.Exception(() => h.setPerms("pvp;ally:use;ally:build;citizen:use;stranger:attack;friend:build;"));

            Assert.Null(error);
            Dictionary<string, bool> s = Snapshot(h);
            string[] on = s.Keys.Where(k => s[k]).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { "citizen:use", "friend:build", "pvp", "stranger:attack" }, on);
            Assert.Equal("pvp;stranger:attack;citizen:use;friend:build;", h.ToString());
        }

        // S2: "ally" is no longer a group word.
        [Fact]
        public void TryParseGroup_Should_Refuse_When_GroupIsAlly()
        {
            Assert.False(PermsHandler.TryParseGroup("ally", out _));
        }

        // Unknown words in a stored string are skipped, not fatal.
        [Fact]
        public void Load_Should_SkipUnknownWords_When_StringHasThem()
        {
            var h = new PermsHandler();

            h.setPerms("nosuchflag;citizen:fly;ghost:use;;fire;");

            Assert.Equal("fire;", h.ToString());
        }

        // SUSPECTED BUG (documents current behaviour): setPerms resets the four flags but not the
        // rights, so loading a string into a handler that already had rights keeps the old ones.
        // Harmless while every load goes into a fresh handler. If this is fixed, flip the assert.
        [Fact]
        public void SetPerms_SuspectedBug_Should_KeepOldRights_When_LoadedIntoAUsedHandler()
        {
            var h = new PermsHandler();
            h.setPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM, true);
            h.pvpFlag = true;

            h.setPerms("citizen:use;");

            Assert.False(h.pvpFlag);                                                   // flags are reset
            Assert.True(h.getPerm(PermGroup.STRANGER, PermType.BUILD_AND_DESTROY_PERM)); // rights are not
        }
    }
}
