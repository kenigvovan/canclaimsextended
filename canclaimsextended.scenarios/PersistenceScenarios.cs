using System.Text.Json;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Y1: a full world survives loading and a real server restart. The data is the fixture
/// FixtureGenerator writes from fixtures/sql/FullWorld.sql; no player joins here, which is what lets
/// Atlas restart the class host (ADR 0011 / AtlasScenarioAttribute.RestartWorld: joined players
/// make a restart a setup error). The expectations are written out (FullWorld), not "same as before":
/// a value saved and loaded back equally wrong would pass a before/after comparison.
///
/// Order (ADR 0010): xUnit gives none within a class, so no scenario here depends on another. The
/// first two only read memory. The third saves everything again, which must not change a value;
/// even if it ran first, the others would see the same memory. A RestartWorld host is booted again
/// with the data files seeded again: the restart scenario loads the fixture after a full shutdown
/// and a fresh boot of the mod (its statics, its handlers, the load order), while the write-back
/// scenario is the one that checks what the mod writes is what it read.
/// </summary>
[Trait("Category", "E2E")]
[AtlasDataFiles("fixtures/FullWorld", TargetPath = "ModConfig")]
public class PersistenceScenarios : AtlasScenarioBase
{
    // Y1: the stored world is loaded completely at boot
    [AtlasScenario]
    public async Task FullWorld_Should_BeLoaded_When_ServerBoots()
    {
        AssertFullWorldInMemory();
        await World.Ticks(1);
    }

    // Y1: and is still the same after the server was shut down and booted again
    [AtlasScenario(RestartWorld = true)]
    public async Task FullWorld_Should_StayTheSame_When_ServerRestarts()
    {
        AssertFullWorldInMemory();
        await World.Ticks(1);
    }

    // Y1: everything loaded and saved again gives back the stored rows: no column lost or reset on the way
    [AtlasScenario]
    public async Task FullWorld_Should_BeWrittenBackUnchanged_When_EverythingIsSavedAgain()
    {
        Fixtures.RequireDb(FullWorld.Folder);
        City city = TheCity();

        SaveEverythingOf(city);
        DbFile.Flush();

        AssertFullWorldOnDisk();
        await World.Ticks(1);
    }

    // ---- memory ----------------------------------------------------------------------------

    private static City TheCity()
    {
        if (!Mod.dataStorage.GetCityByName(FullWorld.City, out City city))
            Assert.Fail($"city {FullWorld.City} was not loaded");
        return city;
    }

    private static PlayerInfo ThePlayer(string name)
    {
        if (!Mod.dataStorage.GetPlayerByName(name, out PlayerInfo player))
            Assert.Fail($"player {name} was not loaded");
        return player;
    }

    private static void Expect(List<string> broken, string what, object? expected, object? actual)
    {
        bool same = expected is double e && actual is double a ? Math.Abs(e - a) < 0.0001 : Equals(expected, actual);
        if (!same) broken.Add($"{what} is '{actual}', expected '{expected}'");
    }

    private static void AssertFullWorldInMemory()
    {
        Fixtures.RequireDb(FullWorld.Folder);
        var broken = new List<string>();
        City city = TheCity();
        PlayerInfo mayor = ThePlayer(FullWorld.Mayor);
        PlayerInfo citizen = ThePlayer(FullWorld.Citizen);
        PlayerInfo outlaw = ThePlayer(FullWorld.Outlaw);
        PlayerInfo invitee = ThePlayer(FullWorld.Invitee);

        // The city row, every column
        Expect(broken, "city guid", FullWorld.CityGuid, city.Guid);
        Expect(broken, "mayor", FullWorld.Mayor, city.getMayor()?.GetPartName());
        Expect(broken, "created", FullWorld.Created, city.TimeStampCreated);
        Expect(broken, "city perms", FullWorld.CityPerms, city.getPermsHandler().ToString());
        Expect(broken, "open", FullWorld.Open, city.openCity);
        Expect(broken, "invmsg", FullWorld.InvMsg, city.invMsg);
        Expect(broken, "bonus plots", FullWorld.BonusPlots, city.getBonusPlots());
        Expect(broken, "bonus chunks", FullWorld.BonusChunks, city.getBonusChunks());
        Expect(broken, "technical", false, city.isTechnicalCity());
        Expect(broken, "color", FullWorld.CityColor, city.cityColor);
        Expect(broken, "debt", FullWorld.Debt, city.DebtBalance);
        Expect(broken, "fee", FullWorld.CityFee, city.fee);
        Expect(broken, "emblem", FullWorld.Emblem, city.Emblem);
        Expect(broken, "daily message", FullWorld.DailyMsg, city.DailyMsg);

        // People
        var citizens = city.getCityCitizens().Select(p => p.GetPartName()).OrderBy(n => n).ToList();
        Expect(broken, "citizens", string.Join(",", new[] { FullWorld.Citizen, FullWorld.Mayor }.OrderBy(n => n)), string.Join(",", citizens));
        Expect(broken, "citizen's city", FullWorld.City, citizen.City?.GetPartName());
        Expect(broken, "mayor's city", FullWorld.City, mayor.City?.GetPartName());
        Expect(broken, "citizen prefix", FullWorld.CitizenPrefix, citizen.Prefix);
        Expect(broken, "citizen after name", FullWorld.CitizenAfterName, citizen.AfterName);
        if (!citizen.Friends.Contains(mayor)) broken.Add("the citizen's friends lost the mayor");
        Expect(broken, "outlaw's city", null, outlaw.City?.GetPartName());

        // Rank and who holds it
        if (!city.CustomCityRanks.TryGetValue(FullWorld.Rank, out CustomCityRank? rank)) broken.Add($"rank {FullWorld.Rank} is missing");
        else
        {
            Expect(broken, "rank rights", string.Join(",", FullWorld.RankPermissions.OrderBy(p => p)),
                string.Join(",", rank.Permissions.Select(p => p.ToString()).OrderBy(p => p)));
            if (!rank.CitizensNames.Contains(FullWorld.Citizen)) broken.Add("the rank does not list the citizen");
        }
        if (!citizen.hasCityTitle(FullWorld.Rank)) broken.Add($"the citizen does not hold {FullWorld.Rank}");

        // Territory
        Expect(broken, "chunks", FullWorld.Chunks, city.ChunkCount);
        if (!city.TryGetChunk(FullWorld.HomeChunk, out CityChunk home)) broken.Add($"{FullWorld.HomeChunk} is missing");
        else if (home.ExtraBought || home.IsOutpost || home.Group != null) broken.Add($"{FullWorld.HomeChunk} has marks it was not stored with");
        if (!city.TryGetChunk(FullWorld.ExtraChunk, out CityChunk extra)) broken.Add($"{FullWorld.ExtraChunk} is missing");
        else
        {
            Expect(broken, "extra chunk bought over the limit", true, extra.ExtraBought);
            Expect(broken, "extra chunk's group", FullWorld.GroupGuid, extra.Group?.Guid);
        }
        if (!city.TryGetChunk(FullWorld.OutpostChunk, out CityChunk outpost)) broken.Add($"{FullWorld.OutpostChunk} is missing");
        else Expect(broken, "outpost mark", true, outpost.IsOutpost);
        if (!Mod.dataStorage.GetCityAtChunk(FullWorld.OutpostChunk, out City atOutpost) || !city.Equals(atOutpost))
            broken.Add("the world-wide chunk index does not give the city at its outpost");

        // Plots
        Expect(broken, "plots of the city", FullWorld.CityPlots, city.getCityPlots().Count);
        if (!Mod.dataStorage.GetPlotByGuid(FullWorld.TempleGuid, out Plot temple)) broken.Add("the temple plot is missing");
        else
        {
            Expect(broken, "temple's city", FullWorld.City, temple.getCity()?.GetPartName());
            Expect(broken, "temple owner", FullWorld.Citizen, temple.getPlotOwner()?.GetPartName());
            if (!citizen.PlayerPlots.Contains(temple)) broken.Add("the citizen's plots do not list the temple");
            Expect(broken, "temple name", FullWorld.TempleName, temple.GetPartName());
            Expect(broken, "temple number", FullWorld.TempleNumber, temple.Number);
            Expect(broken, "temple price", FullWorld.TemplePrice, temple.Price);
            Expect(broken, "temple tax", FullWorld.TempleTax, temple.getCustomTax());
            Expect(broken, "temple perms", FullWorld.TemplePerms, temple.getPermsHandler().ToString());
            Expect(broken, "temple type", PlotType.TEMPLE, temple.Type);
            BlockPos? point = (temple.Desc as PlotDescTemple)?.Point;
            Expect(broken, "temple point", FullWorld.TemplePoint, point == null ? ((int, int, int)?)null : (point.X, point.Y, point.Z));
            Expect(broken, "temple boxes", string.Join(" + ", FullWorld.TempleBoxes), string.Join(" + ", temple.Shape.Boxes));
            Expect(broken, "temple's group", FullWorld.GroupGuid, temple.Group?.Guid);
            // The spatial index, not only the guid list: both boxes find the plot.
            foreach (BlockBox box in FullWorld.TempleBoxes)
            {
                var inside = new BlockPos(box.MinX, box.MinY, box.MinZ);
                if (!Mod.dataStorage.GetSubPlotAt(inside, out Plot found) || !temple.Equals(found))
                    broken.Add($"no plot is found at {inside}, inside the temple");
            }
        }
        if (!Mod.dataStorage.GetPlotByGuid(FullWorld.PrisonGuid, out Plot prison)) broken.Add("the prison plot is missing");
        else
        {
            Expect(broken, "prison type", PlotType.PRISON, prison.Type);
            Expect(broken, "prison name", FullWorld.PrisonName, prison.GetPartName());
            Expect(broken, "prison number", FullWorld.PrisonNumber, prison.Number);
            Expect(broken, "prison owner", null, prison.getPlotOwner()?.GetPartName());
            Expect(broken, "prison price", -1, prison.Price);
            Expect(broken, "prison perms", FullWorld.PrisonPerms, prison.getPermsHandler().ToString());
            Expect(broken, "prison boxes", string.Join(" + ", FullWorld.PrisonBoxes), string.Join(" + ", prison.Shape.Boxes));
            var cells = (prison.Desc as PlotDescPrison)?.Cells.Select(c => (c.X, c.Y, c.Z)).ToList() ?? new();
            Expect(broken, "prison cells", string.Join(";", FullWorld.PrisonCells), string.Join(";", cells));
        }

        // Prison and criminals
        Expect(broken, "outlaw's prison", FullWorld.PrisonGuid, outlaw.PrisonPlotGuid);
        Expect(broken, "outlaw's release", FullWorld.PrisonReleaseAt, outlaw.PrisonReleaseAt);
        Expect(broken, "criminals", FullWorld.OutlawUid, string.Join(",", city.Criminals));

        // Plots group
        PlotsGroup? group = city.PlotsGroups.SingleOrDefault();
        if (group == null) broken.Add($"the city has {city.PlotsGroups.Count} plots groups, expected 1");
        else
        {
            Expect(broken, "group name", FullWorld.Group, group.GetPartName());
            Expect(broken, "group guid", FullWorld.GroupGuid, group.Guid);
            Expect(broken, "group members", FullWorld.Citizen, string.Join(",", group.Members.Select(m => m.GetPartName())));
            Expect(broken, "group perms", FullWorld.GroupPerms, group.Perms.ToString());
            Expect(broken, "group fee", FullWorld.GroupFee, group.Fee);
            Expect(broken, "group pending fee", FullWorld.GroupPendingFee, group.PendingFee);
            Expect(broken, "group pending at", FullWorld.GroupPendingAt, group.PendingFeeAt);
            Expect(broken, "group accepted", FullWorld.CitizenUid, string.Join(",", group.PendingFeeAccepted));
        }

        // Invitation
        List<Invitation> received = invitee.getReceivedInvitations();
        if (received.Count != 1) broken.Add($"the invitee has {received.Count} invitations, expected 1");
        else
        {
            if (!city.Equals(received[0].getSender())) broken.Add("the invitation is not from the city");
            Expect(broken, "invitation expiry", FullWorld.InvitationExpire, received[0].getTimeStamp());
        }
        Expect(broken, "invitations sent by the city", 1, city.GetSentInvitations().Count);

        // City log
        Expect(broken, "city log",
            string.Join(" | ", FullWorld.Log.Select(l => l.Event + ":" + string.Join(",", l.Args))),
            string.Join(" | ", city.EventLog.Select(e => e.EventType + ":" + string.Join(",", e.Args))));

        // World flags
        foreach (string code in WorldInfo.Codes)
        {
            Mod.dataStorage.World.TryGet(code, out bool on);
            Expect(broken, "world flag " + code, FullWorld.WorldFlagsOn.Contains(code), on);
        }

        Assert.True(broken.Count == 0, "the world was not loaded as stored:\n" + string.Join("\n", broken));
    }

    // ---- disk ------------------------------------------------------------------------------

    /// <summary>Every save the mod has for what the fixture holds, as the commands would call them.</summary>
    private static void SaveEverythingOf(City city)
    {
        var db = Mod.Db!;
        city.saveToDatabase();
        foreach (CityChunk chunk in city.Chunks) db.saveChunk(city, chunk);
        foreach (Plot plot in city.getCityPlots())
        {
            plot.saveToDatabase();
            db.savePlotExtra(plot);
        }
        foreach (PlotsGroup group in city.PlotsGroups) group.saveToDatabase();
        foreach (string name in new[] { FullWorld.Mayor, FullWorld.Citizen, FullWorld.Outlaw, FullWorld.Invitee })
        {
            PlayerInfo player = ThePlayer(name);
            player.saveToDatabase();
            db.savePrisoner(player);
        }
        foreach (string uid in city.Criminals) db.saveCriminal(city, uid);
        Mod.dataStorage.World.saveToDatabase();
    }

    private static Dictionary<string, object?> OneRow(List<string> broken, string what, string sql, params (string, object)[] p)
    {
        var rows = DbFile.Rows(sql, p);
        if (rows.Count != 1) broken.Add($"{what}: {rows.Count} rows, expected 1");
        return rows.FirstOrDefault() ?? new Dictionary<string, object?>();
    }

    private static string BoxesOf(string plotGuid) =>
        string.Join(" + ", DbFile.Rows("SELECT * FROM PLOTBOXES WHERE plotguid=@g ORDER BY idx", ("@g", plotGuid))
            .Select(b => new BlockBox((int)DbFile.Long(b, "minx"), (int)DbFile.Long(b, "miny"), (int)DbFile.Long(b, "minz"),
                (int)DbFile.Long(b, "maxx"), (int)DbFile.Long(b, "maxy"), (int)DbFile.Long(b, "maxz"))));

    private static void AssertFullWorldOnDisk()
    {
        var broken = new List<string>();

        var c = OneRow(broken, "CITIES", "SELECT * FROM CITIES WHERE guid=@g", ("@g", FullWorld.CityGuid));
        if (c.Count > 0)
        {
            Expect(broken, "CITIES.name", FullWorld.City, DbFile.Str(c, "name"));
            Expect(broken, "CITIES.mayor", FullWorld.MayorUid, DbFile.Str(c, "mayor"));
            Expect(broken, "CITIES.timestampcreated", FullWorld.Created, DbFile.Long(c, "timestampcreated"));
            Expect(broken, "CITIES.perms", FullWorld.CityPerms, DbFile.Str(c, "perms"));
            Expect(broken, "CITIES.opencity", 1L, DbFile.Long(c, "opencity"));
            Expect(broken, "CITIES.invmsg", FullWorld.InvMsg, DbFile.Str(c, "invmsg"));
            Expect(broken, "CITIES.bonusplots", (long)FullWorld.BonusPlots, DbFile.Long(c, "bonusplots"));
            Expect(broken, "CITIES.bonuschunks", (long)FullWorld.BonusChunks, DbFile.Long(c, "bonuschunks"));
            Expect(broken, "CITIES.istechnical", 0L, DbFile.Long(c, "istechnical"));
            Expect(broken, "CITIES.citycolor", (long)FullWorld.CityColor, DbFile.Long(c, "citycolor"));
            Expect(broken, "CITIES.debtbalance", FullWorld.Debt, DbFile.Dbl(c, "debtbalance"));
            Expect(broken, "CITIES.fee", (long)FullWorld.CityFee, DbFile.Long(c, "fee"));
            Expect(broken, "CITIES.emblem", FullWorld.Emblem, DbFile.Str(c, "emblem"));
            Expect(broken, "CITIES.dailymsg", FullWorld.DailyMsg, DbFile.Str(c, "dailymsg"));
            using JsonDocument ranks = JsonDocument.Parse(DbFile.Str(c, "ranks"));
            if (!ranks.RootElement.TryGetProperty(FullWorld.Rank, out JsonElement rank)) broken.Add("CITIES.ranks lost " + FullWorld.Rank);
            else
            {
                Expect(broken, "CITIES.ranks rights", string.Join(",", FullWorld.RankPermissions.OrderBy(p => p)),
                    string.Join(",", rank.GetProperty("Permissions").EnumerateArray().Select(p => p.GetString()).OrderBy(p => p)));
                Expect(broken, "CITIES.ranks holders", FullWorld.Citizen,
                    string.Join(",", rank.GetProperty("CitizensNames").EnumerateArray().Select(p => p.GetString())));
            }
        }

        var citizen = OneRow(broken, "PLAYERS citizen", "SELECT * FROM PLAYERS WHERE uid=@u", ("@u", FullWorld.CitizenUid));
        if (citizen.Count > 0)
        {
            Expect(broken, "citizen name", FullWorld.Citizen, DbFile.Str(citizen, "name"));
            Expect(broken, "citizen city", FullWorld.CityGuid, DbFile.Str(citizen, "city"));
            Expect(broken, "citizen friends", FullWorld.MayorUid, DbFile.Str(citizen, "friends"));
            Expect(broken, "citizen titles", FullWorld.Rank, DbFile.Str(citizen, "citytitles"));
            Expect(broken, "citizen prefix", FullWorld.CitizenPrefix, DbFile.Str(citizen, "prefix"));
            Expect(broken, "citizen aftername", FullWorld.CitizenAfterName, DbFile.Str(citizen, "aftername"));
        }
        var mayor = OneRow(broken, "PLAYERS mayor", "SELECT * FROM PLAYERS WHERE uid=@u", ("@u", FullWorld.MayorUid));
        if (mayor.Count > 0) Expect(broken, "mayor city", FullWorld.CityGuid, DbFile.Str(mayor, "city"));
        var outlaw = OneRow(broken, "PLAYERS outlaw", "SELECT * FROM PLAYERS WHERE uid=@u", ("@u", FullWorld.OutlawUid));
        if (outlaw.Count > 0) Expect(broken, "outlaw city", "", DbFile.Str(outlaw, "city"));

        var chunks = DbFile.Rows("SELECT * FROM CITYCHUNKS WHERE cityguid=@g ORDER BY cx, cz", ("@g", FullWorld.CityGuid))
            .Select(r => $"{DbFile.Long(r, "cx")},{DbFile.Long(r, "cz")} extra={DbFile.Long(r, "extrabought")} outpost={DbFile.Long(r, "outpost")} group={DbFile.Str(r, "groupguid")}");
        Expect(broken, "CITYCHUNKS",
            $"{FullWorld.HomeChunk.X},{FullWorld.HomeChunk.Z} extra=0 outpost=0 group= | " +
            $"{FullWorld.ExtraChunk.X},{FullWorld.ExtraChunk.Z} extra=1 outpost=0 group={FullWorld.GroupGuid} | " +
            $"{FullWorld.OutpostChunk.X},{FullWorld.OutpostChunk.Z} extra=0 outpost=1 group=",
            string.Join(" | ", chunks));

        var temple = OneRow(broken, "PLOTS temple", "SELECT * FROM PLOTS WHERE guid=@g", ("@g", FullWorld.TempleGuid));
        if (temple.Count > 0)
        {
            Expect(broken, "temple name", FullWorld.TempleName, DbFile.Str(temple, "name"));
            Expect(broken, "temple city", FullWorld.CityGuid, DbFile.Str(temple, "city"));
            Expect(broken, "temple owner", FullWorld.CitizenUid, DbFile.Str(temple, "owner"));
            Expect(broken, "temple type", (long)PlotType.TEMPLE, DbFile.Long(temple, "type"));
            Expect(broken, "temple price", (long)FullWorld.TemplePrice, DbFile.Long(temple, "price"));
            Expect(broken, "temple customtax", FullWorld.TempleTax, DbFile.Dbl(temple, "customtax"));
            Expect(broken, "temple perms", FullWorld.TemplePerms, DbFile.Str(temple, "perms"));
            Expect(broken, "temple plotdesc", $"{FullWorld.TemplePoint.X},{FullWorld.TemplePoint.Y},{FullWorld.TemplePoint.Z}", DbFile.Str(temple, "plotdesc"));
            Expect(broken, "temple number", (long)FullWorld.TempleNumber, DbFile.Long(temple, "number"));
        }
        Expect(broken, "temple PLOTBOXES", string.Join(" + ", FullWorld.TempleBoxes), BoxesOf(FullWorld.TempleGuid));

        var prison = OneRow(broken, "PLOTS prison", "SELECT * FROM PLOTS WHERE guid=@g", ("@g", FullWorld.PrisonGuid));
        if (prison.Count > 0)
        {
            Expect(broken, "prison type", (long)PlotType.PRISON, DbFile.Long(prison, "type"));
            Expect(broken, "prison owner", "", DbFile.Str(prison, "owner"));
            Expect(broken, "prison price", -1L, DbFile.Long(prison, "price"));
            Expect(broken, "prison plotdesc", string.Join(";", FullWorld.PrisonCells.Select(p => $"{p.X},{p.Y},{p.Z}")), DbFile.Str(prison, "plotdesc"));
            Expect(broken, "prison number", (long)FullWorld.PrisonNumber, DbFile.Long(prison, "number"));
        }
        Expect(broken, "prison PLOTBOXES", string.Join(" + ", FullWorld.PrisonBoxes), BoxesOf(FullWorld.PrisonGuid));

        var prisoner = OneRow(broken, "PRISONERS", "SELECT * FROM PRISONERS WHERE uid=@u", ("@u", FullWorld.OutlawUid));
        if (prisoner.Count > 0)
        {
            Expect(broken, "PRISONERS.plotguid", FullWorld.PrisonGuid, DbFile.Str(prisoner, "plotguid"));
            Expect(broken, "PRISONERS.releaseat", FullWorld.PrisonReleaseAt, DbFile.Long(prisoner, "releaseat"));
        }
        Expect(broken, "CRIMINALS", FullWorld.OutlawUid,
            string.Join(",", DbFile.Rows("SELECT uid FROM CRIMINALS WHERE cityguid=@g", ("@g", FullWorld.CityGuid)).Select(r => DbFile.Str(r, "uid"))));

        var group = OneRow(broken, "PLOTSGROUPS", "SELECT * FROM PLOTSGROUPS WHERE guid=@g", ("@g", FullWorld.GroupGuid));
        if (group.Count > 0)
        {
            Expect(broken, "group name", FullWorld.Group, DbFile.Str(group, "name"));
            Expect(broken, "group city", FullWorld.CityGuid, DbFile.Str(group, "city"));
            Expect(broken, "group perms", FullWorld.GroupPerms, DbFile.Str(group, "perms"));
            Expect(broken, "group fee", FullWorld.GroupFee, DbFile.Dbl(group, "fee"));
            Expect(broken, "group pendingfee", FullWorld.GroupPendingFee, DbFile.Dbl(group, "pendingfee"));
            Expect(broken, "group pendingat", FullWorld.GroupPendingAt, DbFile.Long(group, "pendingat"));
            Expect(broken, "group accepted", FullWorld.CitizenUid, DbFile.Str(group, "accepted"));
        }
        Expect(broken, "PLOTSGROUPMEMBERS", FullWorld.CitizenUid,
            string.Join(",", DbFile.Rows("SELECT uid FROM PLOTSGROUPMEMBERS WHERE groupguid=@g", ("@g", FullWorld.GroupGuid)).Select(r => DbFile.Str(r, "uid"))));
        Expect(broken, "PLOTEXTRAS", $"{FullWorld.TempleGuid}->{FullWorld.GroupGuid}",
            string.Join(",", DbFile.Rows("SELECT * FROM PLOTEXTRAS WHERE groupguid != ''").Select(r => DbFile.Str(r, "plotguid") + "->" + DbFile.Str(r, "groupguid"))));

        Expect(broken, "INVITATIONS", $"city {FullWorld.CityGuid} -> player {FullWorld.InviteeUid} until {FullWorld.InvitationExpire}",
            string.Join(" | ", DbFile.Rows("SELECT * FROM INVITATIONS").Select(r =>
                $"{DbFile.Str(r, "sendertype")} {DbFile.Str(r, "senderguid")} -> {DbFile.Str(r, "receivertype")} {DbFile.Str(r, "receiverguid")} until {DbFile.Long(r, "expire")}")));

        Expect(broken, "CITYLOG",
            string.Join(" | ", FullWorld.Log.Select(l => l.Event + ":" + string.Join(",", l.Args))),
            string.Join(" | ", DbFile.Rows("SELECT * FROM CITYLOG WHERE cityguid=@g ORDER BY ts, rowid", ("@g", FullWorld.CityGuid))
                .Select(r => DbFile.Str(r, "event") + ":" + string.Join(",", DbFile.Str(r, "args").Split('\u001f')))));

        foreach (string code in WorldInfo.Codes)
        {
            var flag = OneRow(broken, "WORLDFLAGS " + code, "SELECT * FROM WORLDFLAGS WHERE flag=@f", ("@f", code));
            if (flag.Count > 0) Expect(broken, "WORLDFLAGS " + code, FullWorld.WorldFlagsOn.Contains(code) ? 1L : 0L, DbFile.Long(flag, "value"));
        }

        Assert.True(broken.Count == 0, "saving the loaded world again changed the stored rows:\n" + string.Join("\n", broken));
    }
}
