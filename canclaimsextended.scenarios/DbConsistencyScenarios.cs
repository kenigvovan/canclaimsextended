using System.Text.Json;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;

namespace canclaimsextended.scenarios;

/// <summary>
/// Y1: the database after real commands. What is stored must be what the server holds in memory:
/// the mod queues its saves with the values of the moment and writes them every half second, and a
/// save that is lost, written in the wrong order or with a stale value only shows on the next start.
/// Each scenario runs its commands, flushes the queue (DbFile.Flush) and compares the rows of its
/// own city with memory, reading the file through a connection of its own.
///
/// One server for the class, as everywhere in CxScenarioBase: every scenario takes its own area and
/// city and reads only rows of that city and its players, so the order does not matter.
/// </summary>
[Trait("Category", "E2E")]
public class DbConsistencyScenarios : CxScenarioBase
{
    // Y1: founding, a citizen joining, the fee and a granted rank
    [AtlasScenario]
    public async Task CityRows_Should_MatchMemory_When_CityIsFoundedAndFeeAndRankAreSet()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];

        // Act
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 10");
        Assert.True(s.City.fee == 10, "precondition: /city set fee 10");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create Guard");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Guard CITY_INVITE");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant Guard {Name(citizen)}");
        Assert.True(Info(citizen).hasCityTitle("Guard"), "precondition: the citizen holds Guard");
        DbFile.Flush();

        // Assert
        var broken = new List<string>();
        CompareCity(broken, s.City);
        foreach (ITestPlayer player in s.Citizens.Prepend(s.Mayor)) ComparePlayer(broken, Info(player));
        CompareCityLog(broken, s.City);
        Assert.True(broken.Count == 0, "the database differs from memory:\n" + string.Join("\n", broken));
        AssertInvariants();
    }

    // Y1: a claimed chunk, a plot made and bought
    [AtlasScenario]
    public async Task LandRows_Should_MatchMemory_When_ChunkIsClaimedAndPlotIsBought()
    {
        // Arrange
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        s.City.setBonusChunks(8);

        // Act
        ChunkPos claimed = await ClaimChunkNextToCity(s);
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, buyer, price: 0);
        DbFile.Flush();

        // Assert
        var broken = new List<string>();
        if (DbFile.Count("SELECT count(*) FROM CITYCHUNKS WHERE cityguid=@c AND cx=@x AND cz=@z",
                ("@c", s.City.Guid), ("@x", claimed.X), ("@z", claimed.Z)) != 1)
            broken.Add($"the claimed chunk {claimed} is not stored");
        CompareChunks(broken, s.City);
        ComparePlots(broken, s.City);
        ComparePlayer(broken, Info(buyer));
        CompareCityLog(broken, s.City);
        Assert.True(broken.Count == 0, "the database differs from memory:\n" + string.Join("\n", broken));
        AssertInvariants();
    }

    // Y1: a player put on the city's criminal list
    [AtlasScenario]
    public async Task CriminalRows_Should_MatchMemory_When_CriminalIsAdded()
    {
        // Arrange
        CityScene s = await GivenCity();
        ITestPlayer outlaw = await GivenStranger(s, "outlaw");

        // Act
        await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(outlaw)}");
        Assert.True(s.City.Criminals.Contains(outlaw.Player.PlayerUID), "precondition: /city criminal add");
        DbFile.Flush();

        // Assert
        var stored = DbFile.Rows("SELECT uid FROM CRIMINALS WHERE cityguid=@c", ("@c", s.City.Guid))
            .Select(r => DbFile.Str(r, "uid")).OrderBy(u => u).ToList();
        Assert.Equal(s.City.Criminals.OrderBy(u => u).ToList(), stored);
        ComparePlayerOrFail(Info(outlaw));
    }

    // Y1: all of it in one city, then every table of that city at once
    [AtlasScenario]
    public async Task AllRows_Should_MatchMemory_When_TypicalCommandsRan()
    {
        // Arrange
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        ITestPlayer outlaw = await GivenStranger(s, "outlaw");
        s.City.setBonusChunks(8);

        // Act
        await ClaimChunkNextToCity(s);
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, citizen, price: 0);
        await s.Mayor.ExecuteCommand($"{CityCmd} set fee 5");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank create Keeper");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank addperm Keeper CITY_SEE_LOG");
        await s.Mayor.ExecuteCommand($"{CityCmd} rank grant Keeper {Name(citizen)}");
        await s.Mayor.ExecuteCommand($"{CityCmd} criminal add {Name(outlaw)}");
        Assert.True(s.City.fee == 5 && Info(citizen).hasCityTitle("Keeper") && s.City.Criminals.Contains(outlaw.Player.PlayerUID),
            "precondition: fee, rank and criminal were set");
        DbFile.Flush();

        // Assert
        var broken = new List<string>();
        CompareCity(broken, s.City);
        CompareChunks(broken, s.City);
        ComparePlots(broken, s.City);
        foreach (ITestPlayer player in new[] { s.Mayor, citizen, outlaw }) ComparePlayer(broken, Info(player));
        var criminals = DbFile.Rows("SELECT uid FROM CRIMINALS WHERE cityguid=@c", ("@c", s.City.Guid))
            .Select(r => DbFile.Str(r, "uid")).OrderBy(u => u);
        if (!criminals.SequenceEqual(s.City.Criminals.OrderBy(u => u)))
            broken.Add($"CRIMINALS: [{string.Join(",", criminals)}], memory [{string.Join(",", s.City.Criminals)}]");
        CompareCityLog(broken, s.City);
        Assert.True(broken.Count == 0, "the database differs from memory:\n" + string.Join("\n", broken));
        AssertInvariants();
    }

    // ---- commands --------------------------------------------------------------------------

    /// <summary>The mayor claims the chunk east of the founding one (/city claim) and must get it.</summary>
    private async Task<ChunkPos> ClaimChunkNextToCity(CityScene s)
    {
        ChunkPos home = AreaChunk(s.Area);
        var chunk = new ChunkPos(home.X + 1, home.Z);
        int size = ChunkPos.Size;
        await s.Mayor.TeleportTo(new BlockPos(chunk.MinBlockX + size / 2, AreaOrigin(s.Area).Y, chunk.MinBlockZ + size / 2));
        CommandResult result = await RunPaid(s.Mayor, $"{CityCmd} claim", () => s.City.HasChunk(chunk), "the chunk to be claimed");
        Assert.True(s.City.HasChunk(chunk), $"precondition: {chunk} was claimed: {result.Message}");
        return chunk;
    }

    // ---- comparisons -----------------------------------------------------------------------

    private static void Expect(List<string> broken, string what, object? memory, object? stored)
    {
        bool same = memory is double m && stored is double d ? Math.Abs(m - d) < 0.0001 : Equals(memory, stored);
        if (!same) broken.Add($"{what}: stored '{stored}', memory '{memory}'");
    }

    /// <summary>
    /// The CITIES row. Bonus plots and chunks are left out: CxScenarioBase raises them in memory
    /// only (setBonusPlots/setBonusChunks), a test shortcut no command takes.
    /// </summary>
    private static void CompareCity(List<string> broken, City city)
    {
        var rows = DbFile.Rows("SELECT * FROM CITIES WHERE guid=@g", ("@g", city.Guid));
        if (rows.Count != 1)
        {
            broken.Add($"CITIES: {rows.Count} rows for {city.GetPartName()}");
            return;
        }
        var row = rows[0];
        Expect(broken, "CITIES.name", city.GetPartName(), DbFile.Str(row, "name"));
        Expect(broken, "CITIES.mayor", city.getMayor()?.Guid ?? "", DbFile.Str(row, "mayor"));
        Expect(broken, "CITIES.timestampcreated", city.TimeStampCreated, DbFile.Long(row, "timestampcreated"));
        Expect(broken, "CITIES.perms", city.getPermsHandler().ToString(), DbFile.Str(row, "perms"));
        Expect(broken, "CITIES.opencity", city.openCity ? 1L : 0L, DbFile.Long(row, "opencity"));
        Expect(broken, "CITIES.invmsg", city.invMsg ?? "", DbFile.Str(row, "invmsg"));
        Expect(broken, "CITIES.citycolor", (long)city.cityColor, DbFile.Long(row, "citycolor"));
        Expect(broken, "CITIES.debtbalance", city.DebtBalance, DbFile.Dbl(row, "debtbalance"));
        Expect(broken, "CITIES.fee", (long)city.fee, DbFile.Long(row, "fee"));
        Expect(broken, "CITIES.emblem", city.Emblem ?? "", DbFile.Str(row, "emblem"));
        Expect(broken, "CITIES.dailymsg", city.DailyMsg ?? "", DbFile.Str(row, "dailymsg"));

        string ranks = DbFile.Str(row, "ranks");
        if (city.CustomCityRanks.Count == 0)
        {
            if (ranks.Length > 0 && ranks != "{}") broken.Add($"CITIES.ranks is '{ranks}', the city has no ranks");
            return;
        }
        using JsonDocument doc = JsonDocument.Parse(ranks);
        var storedNames = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n);
        Expect(broken, "CITIES.ranks names", string.Join(",", city.CustomCityRanks.Keys.OrderBy(n => n)), string.Join(",", storedNames));
        foreach (var (name, rank) in city.CustomCityRanks)
        {
            if (!doc.RootElement.TryGetProperty(name, out JsonElement stored)) continue;
            Expect(broken, $"CITIES.ranks {name} rights",
                string.Join(",", rank.Permissions.Select(p => p.ToString()).OrderBy(p => p)),
                string.Join(",", stored.GetProperty("Permissions").EnumerateArray().Select(p => p.GetString()).OrderBy(p => p)));
            Expect(broken, $"CITIES.ranks {name} holders",
                string.Join(",", rank.CitizensNames.OrderBy(n => n)),
                string.Join(",", stored.GetProperty("CitizensNames").EnumerateArray().Select(p => p.GetString()).OrderBy(n => n)));
        }
    }

    private static void ComparePlayer(List<string> broken, PlayerInfo player)
    {
        var rows = DbFile.Rows("SELECT * FROM PLAYERS WHERE uid=@u", ("@u", player.Guid));
        if (rows.Count != 1)
        {
            broken.Add($"PLAYERS: {rows.Count} rows for {player.GetPartName()}");
            return;
        }
        var row = rows[0];
        string who = "PLAYERS " + player.GetPartName();
        Expect(broken, who + ".name", player.GetPartName(), DbFile.Str(row, "name"));
        Expect(broken, who + ".city", player.City?.Guid ?? "", DbFile.Str(row, "city"));
        Expect(broken, who + ".perms", player.PermsHandler.ToString(), DbFile.Str(row, "perms"));
        Expect(broken, who + ".citytitles",
            string.Join(";", player.getCityTitles().OrderBy(t => t)),
            string.Join(";", DbFile.Str(row, "citytitles").Split(';', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t)));
        Expect(broken, who + ".friends",
            string.Join(";", player.Friends.Select(f => f.Guid).OrderBy(g => g)),
            string.Join(";", DbFile.Str(row, "friends").Split(';', StringSplitOptions.RemoveEmptyEntries).OrderBy(g => g)));
        Expect(broken, who + ".prefix", player.Prefix ?? "", DbFile.Str(row, "prefix"));
        Expect(broken, who + ".aftername", player.AfterName ?? "", DbFile.Str(row, "aftername"));
    }

    private static void ComparePlayerOrFail(PlayerInfo player)
    {
        var broken = new List<string>();
        ComparePlayer(broken, player);
        Assert.True(broken.Count == 0, "the database differs from memory:\n" + string.Join("\n", broken));
    }

    private static void CompareChunks(List<string> broken, City city)
    {
        string Line(long x, long z, long extra, long outpost, string group) => $"{x},{z} extra={extra} outpost={outpost} group={group}";
        var stored = DbFile.Rows("SELECT * FROM CITYCHUNKS WHERE cityguid=@c", ("@c", city.Guid))
            .Select(r => Line(DbFile.Long(r, "cx"), DbFile.Long(r, "cz"), DbFile.Long(r, "extrabought"), DbFile.Long(r, "outpost"), DbFile.Str(r, "groupguid")))
            .OrderBy(l => l);
        var memory = city.Chunks
            .Select(c => Line(c.Pos.X, c.Pos.Z, c.ExtraBought ? 1 : 0, c.IsOutpost ? 1 : 0, c.Group?.Guid ?? ""))
            .OrderBy(l => l);
        Expect(broken, "CITYCHUNKS", string.Join(" | ", memory), string.Join(" | ", stored));
    }

    private static void ComparePlots(List<string> broken, City city)
    {
        var rows = DbFile.Rows("SELECT * FROM PLOTS WHERE city=@c", ("@c", city.Guid));
        Expect(broken, "PLOTS of the city",
            string.Join(",", city.getCityPlots().Select(p => p.Guid).OrderBy(g => g)),
            string.Join(",", rows.Select(r => DbFile.Str(r, "guid")).OrderBy(g => g)));
        foreach (Plot plot in city.getCityPlots())
        {
            var row = rows.FirstOrDefault(r => DbFile.Str(r, "guid") == plot.Guid);
            if (row == null) continue;
            string which = $"PLOTS #{plot.Number}";
            Expect(broken, which + ".name", plot.GetPartName(), DbFile.Str(row, "name"));
            Expect(broken, which + ".owner", plot.getPlotOwner()?.Guid ?? "", DbFile.Str(row, "owner"));
            Expect(broken, which + ".type", (long)plot.Type, DbFile.Long(row, "type"));
            Expect(broken, which + ".price", (long)plot.Price, DbFile.Long(row, "price"));
            Expect(broken, which + ".customtax", plot.getCustomTax(), DbFile.Dbl(row, "customtax"));
            Expect(broken, which + ".perms", plot.getPermsHandler().ToString(), DbFile.Str(row, "perms"));
            Expect(broken, which + ".timestampclaimed", plot.TimeStampClaimed, DbFile.Long(row, "timestampclaimed"));
            Expect(broken, which + ".plotdesc", plot.Desc?.Serialize() ?? "", DbFile.Str(row, "plotdesc"));
            Expect(broken, which + ".number", (long)plot.Number, DbFile.Long(row, "number"));

            var boxes = DbFile.Rows("SELECT * FROM PLOTBOXES WHERE plotguid=@g ORDER BY idx", ("@g", plot.Guid))
                .Select(b => new BlockBox((int)DbFile.Long(b, "minx"), (int)DbFile.Long(b, "miny"), (int)DbFile.Long(b, "minz"),
                    (int)DbFile.Long(b, "maxx"), (int)DbFile.Long(b, "maxy"), (int)DbFile.Long(b, "maxz")));
            Expect(broken, which + " PLOTBOXES", string.Join(" + ", plot.Shape.Boxes), string.Join(" + ", boxes));
        }
    }

    /// <summary>Stored rows in the order they were written (rowid) against the log in memory.</summary>
    private static void CompareCityLog(List<string> broken, City city)
    {
        var stored = DbFile.Rows("SELECT * FROM CITYLOG WHERE cityguid=@c ORDER BY rowid", ("@c", city.Guid))
            .Select(r => $"{DbFile.Long(r, "ts")} {DbFile.Str(r, "event")}({DbFile.Str(r, "args").Replace('\u001f', ',')})");
        var memory = city.EventLog.Select(e => $"{e.Timestamp} {e.EventType}({string.Join(",", e.Args)})");
        Expect(broken, "CITYLOG", string.Join(" | ", memory), string.Join(" | ", stored));
    }
}
