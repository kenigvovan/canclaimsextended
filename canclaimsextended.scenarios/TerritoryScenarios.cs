using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The city and its territory: founding and its gates (B2), deleting (B4), claiming a chunk next
/// to the city (C1, C2), giving one up (C3), extra chunks (C4) and outposts (C5). Sections B and C
/// of TESTING_canclaimsextended.md.
///
/// Areas lie on X only, 20 chunks apart, so chunks a few steps along Z of the founding chunk are
/// always free and far from any other city.
/// </summary>
[Trait("Category", "E2E")]
public class TerritoryScenarios : CxScenarioBase
{
    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        int minOutpost = c.MIN_OUTPOST_DISTANCE_CHUNKS, maxOutpost = c.MAX_OUTPOST_DISTANCE_CHUNKS;
        int minCityDistance = c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY;
        AddCleanup(() =>
        {
            c.MIN_OUTPOST_DISTANCE_CHUNKS = minOutpost;
            c.MAX_OUTPOST_DISTANCE_CHUNKS = maxOutpost;
            c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = minCityDistance;
        });
        c.MIN_OUTPOST_DISTANCE_CHUNKS = 4;
        c.MAX_OUTPOST_DISTANCE_CHUNKS = 125;
    }

    /// <summary>The chunk <paramref name="dx"/>, <paramref name="dz"/> chunks from the area's founding one.</summary>
    private ChunkPos ChunkOf(int area, int dx, int dz)
    {
        ChunkPos home = AreaChunk(area);
        return new ChunkPos(home.X + dx, home.Z + dz);
    }

    /// <summary>Puts the player in the middle of that chunk, at the height of the area's origin.</summary>
    private async Task<ChunkPos> GoToChunk(ITestPlayer player, int area, int dx, int dz)
    {
        ChunkPos chunk = ChunkOf(area, dx, dz);
        int size = ChunkPos.Size;
        await player.TeleportTo(new BlockPos(chunk.MinBlockX + size / 2, AreaOrigin(area).Y, chunk.MinBlockZ + size / 2));
        return chunk;
    }

    /// <summary>A city whose level limit never stops the few chunks a scenario claims.</summary>
    private async Task<CityScene> GivenRoomyCity(params string[] citizenNames)
    {
        CityScene s = await GivenCity(citizenNames);
        s.City.setBonusChunks(8);
        return s;
    }

    /// <summary>The mayor claims the chunk next to the city (/city claim) and must get it.</summary>
    private async Task<ChunkPos> GivenClaimedChunk(CityScene s, int dx, int dz)
    {
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, dx, dz);
        CommandResult result = await RunPaid(s.Mayor, $"{CityCmd} claim", () => s.City.HasChunk(chunk), "the chunk to be claimed");
        Assert.True(s.City.HasChunk(chunk), $"precondition: {chunk} was claimed: {result.Message}");
        return chunk;
    }

    private static bool Free(ChunkPos chunk) => !Mod.dataStorage.GetCityAtChunk(chunk, out _);

    private static string OtherCityName(int area, string prefix) => prefix + CityName(area).Substring("Town".Length);

    // ---- B2: founding gates ----------------------------------------------------------------

    // B2: a name that is taken is refused, and the founder stays without a city
    [AtlasScenario]
    public async Task CityCreate_Should_BeRefused_When_NameIsTaken()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ITestPlayer founder = await GivenStranger(s, "founder");
        ChunkPos far = await GoToChunk(founder, s.Area, 0, 10);

        // Act
        CommandResult result = await founder.ExecuteCommand($"{CityCmd} create {s.City.GetPartName()}");

        // Assert
        Assert.False(result.Ok, "a second city with a taken name was founded: " + result.Message);
        Assert.False(Info(founder).hasCity(), "the founder got a city with a taken name");
        Assert.True(Free(far), $"{far} was claimed by a city with a taken name");
        Assert.True(Mod.dataStorage.GetCityByName(s.City.GetPartName(), out City named) && named.Equals(s.City),
            "the name no longer leads to the first city");
        AssertInvariants();
    }

    // B2: closer than MIN_CHUNK_DISTANCE_FROM_OTHER_CITY to another city is refused
    [AtlasScenario]
    public async Task CityCreate_Should_BeRefused_When_TooCloseToAnotherCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ITestPlayer founder = await GivenStranger(s, "founder");
        // Two chunks away leaves one free chunk between, less than the 3 ResetConfig sets.
        ChunkPos near = await GoToChunk(founder, s.Area, 2, 0);
        string name = OtherCityName(s.Area, "Near");

        // Act
        CommandResult result = await founder.ExecuteCommand($"{CityCmd} create {name}");

        // Assert
        Assert.False(result.Ok, "a city was founded next to another one: " + result.Message);
        Assert.False(Info(founder).hasCity(), "the founder got a city too close to another one");
        Assert.False(Mod.dataStorage.CityExistsByName(name), $"{name} exists");
        Assert.True(Free(near), $"{near} was claimed");
        AssertInvariants();
    }

    // B2: a player who already has a city cannot found another one
    [AtlasScenario]
    public async Task CityCreate_Should_BeRefused_When_PlayerAlreadyHasCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos far = await GoToChunk(s.Mayor, s.Area, 0, 10);
        string name = OtherCityName(s.Area, "Second");

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} create {name}");

        // Assert
        Assert.False(result.Ok, "the mayor founded a second city: " + result.Message);
        Assert.True(s.City.Equals(Info(s.Mayor).City), "the mayor's city changed");
        Assert.False(Mod.dataStorage.CityExistsByName(name), $"{name} exists");
        Assert.True(Free(far), $"{far} was claimed by a second city of the mayor");
        AssertInvariants();
    }

    // ---- B4: deleting ----------------------------------------------------------------------

    // B4: /city delete without the city's name, or with another one, does nothing
    [AtlasScenario]
    public async Task CityDelete_Should_DoNothing_When_NameIsMissingOrWrong()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();

        // Act
        CommandResult bare = await s.Mayor.ExecuteCommand($"{CityCmd} delete");
        CommandResult wrong = await s.Mayor.ExecuteCommand($"{CityCmd} delete {OtherCityName(s.Area, "Wrong")}");

        // Assert
        Assert.False(bare.Ok, "/city delete without a name was not refused: " + bare.Message);
        Assert.False(wrong.Ok, "/city delete with a wrong name was not refused: " + wrong.Message);
        Assert.True(Mod.dataStorage.GetCitiesList().Contains(s.City), "the city is gone");
        Assert.True(s.City.HasChunk(AreaChunk(s.Area)), "the city lost its founding chunk");
        Assert.True(Info(s.Mayor).hasCity(), "the mayor lost the city");
        AssertInvariants();
    }

    // B4: /city delete <name> takes the city, its chunks and its plots out of dataStorage
    [AtlasScenario]
    public async Task CityDelete_Should_RemoveChunksAndPlots_When_NameIsExact()
    {
        // Arrange
        CityScene s = await GivenRoomyCity("citizen");
        Plot plot = await GivenPlot(s);
        ChunkPos home = AreaChunk(s.Area);
        ChunkPos second = await GivenClaimedChunk(s, 1, 0);
        string guid = s.City.Guid;

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} delete {s.City.GetPartName()}");

        // Assert
        Assert.True(result.Ok, "/city delete with the exact name failed: " + result.Message);
        Assert.False(Mod.dataStorage.GetCityByGUID(guid, out _), "the city is still known by its guid");
        Assert.DoesNotContain(s.City, Mod.dataStorage.GetCitiesList());
        Assert.False(Mod.dataStorage.CityExistsByName(s.City.GetPartName()), "the city's name is still taken");
        Assert.True(Free(home), $"{home} still belongs to a city");
        Assert.True(Free(second), $"{second} still belongs to a city");
        Assert.False(Mod.dataStorage.GetPlotByGuid(plot.Guid, out _), "the plot is still known by its guid");
        Assert.Null(PlotIn(PlotBox(s.Area)));
        Assert.False(Info(s.Mayor).hasCity(), "the mayor still has a city");
        Assert.False(Info(s.Citizens[0]).hasCity(), "the citizen still has a city");
        AssertInvariants();
    }

    // ---- C1, C2: claiming ------------------------------------------------------------------

    // C1: a chunk sharing a side with the city joins it, as an ordinary chunk, and is logged
    [AtlasScenario]
    public async Task Claim_Should_AddChunk_When_ItIsNextToTheCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);

        // Act
        CommandResult result = await RunPaid(s.Mayor, $"{CityCmd} claim", () => s.City.HasChunk(chunk), "the chunk to be claimed");

        // Assert
        Assert.True(result.Ok, "/city claim failed: " + result.Message);
        Assert.True(s.City.HasChunk(chunk), $"{chunk} was not claimed");
        Assert.True(Mod.dataStorage.GetCityAtChunk(chunk, out City owner) && owner.Equals(s.City),
            "the world-wide chunk index does not point to the city");
        Assert.True(s.City.TryGetChunk(chunk, out CityChunk held) && !held.ExtraBought && !held.IsOutpost,
            "an ordinary claim was marked as extra or outpost");
        Assert.Equal(2, s.City.ChunkCount);
        CityLogEntry? logged = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.ChunkClaimed);
        Assert.NotNull(logged);
        Assert.Equal(new[] { Name(s.Mayor), chunk.X + "," + chunk.Z }, logged!.Args);
        AssertInvariants();
    }

    // C2: a chunk one apart, or touching only by a corner, is not next to the city
    [AtlasScenario]
    public async Task Claim_Should_ClaimNothing_When_ChunkIsNotNextToTheCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();

        // Act
        ChunkPos apart = await GoToChunk(s.Mayor, s.Area, 2, 0);
        CommandResult apartResult = await s.Mayor.ExecuteCommand($"{CityCmd} claim");
        ChunkPos diagonal = await GoToChunk(s.Mayor, s.Area, 1, 1);
        CommandResult diagonalResult = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        Assert.False(apartResult.Ok, "a chunk one apart from the city was claimed: " + apartResult.Message);
        Assert.False(diagonalResult.Ok, "a chunk touching the city by a corner was claimed: " + diagonalResult.Message);
        Assert.True(Free(apart), $"{apart} was claimed");
        Assert.True(Free(diagonal), $"{diagonal} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // C2: the chunk of another city is not taken, even right next to the city
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_ChunkBelongsToAnotherCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        // No distance between cities, so the other one can be founded right next door.
        Mod.config.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = 0;
        ITestPlayer rival = await GivenStranger(s, "rival");
        ChunkPos theirs = await GoToChunk(rival, s.Area, 1, 0);
        City other = await FoundCity(rival, OtherCityName(s.Area, "Rival"));
        Assert.True(other.HasChunk(theirs), $"precondition: the other city was founded on {theirs}");

        // Act
        await GoToChunk(s.Mayor, s.Area, 1, 0);
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        Assert.False(result.Ok, "a chunk of another city was claimed: " + result.Message);
        Assert.False(s.City.HasChunk(theirs), "the city took the other city's chunk");
        Assert.True(Mod.dataStorage.GetCityAtChunk(theirs, out City owner) && owner.Equals(other),
            "the chunk no longer belongs to the other city");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // ---- C3: unclaiming --------------------------------------------------------------------

    // C3: the last chunk of a city cannot be given up
    [AtlasScenario]
    public async Task Unclaim_Should_BeRefused_When_ItIsTheLastChunk()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos home = await GoToChunk(s.Mayor, s.Area, 0, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} unclaim");

        // Assert
        Assert.False(result.Ok, "the last chunk was given up: " + result.Message);
        Assert.True(s.City.HasChunk(home), "the city lost its last chunk");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // C3: a chunk with a plot in it cannot be given up
    [AtlasScenario]
    public async Task Unclaim_Should_BeRefused_When_ChunkHasPlot()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        Plot plot = await GivenPlot(s);
        await GivenClaimedChunk(s, 1, 0);
        ChunkPos home = await GoToChunk(s.Mayor, s.Area, 0, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} unclaim");

        // Assert
        Assert.False(result.Ok, "a chunk with a plot was given up: " + result.Message);
        Assert.True(s.City.HasChunk(home), "the city lost the chunk with its plot");
        Assert.True(PlotIn(PlotBox(s.Area)) == plot, "the plot is gone");
        Assert.Equal(2, s.City.ChunkCount);
        AssertInvariants();
    }

    // C3: the middle of three chunks in a row cannot go - the city would fall in two
    [AtlasScenario]
    public async Task Unclaim_Should_BeRefused_When_ItSplitsTheCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos middle = await GivenClaimedChunk(s, 1, 0);
        await GivenClaimedChunk(s, 2, 0);
        await GoToChunk(s.Mayor, s.Area, 1, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} unclaim");

        // Assert
        Assert.False(result.Ok, "the chunk holding the city together was given up: " + result.Message);
        // Refused for the split, not for a wrong chunk under the mayor or a plot.
        Assert.Equal("canclaimsextended:unclaim_splits_city", result.Raw.StatusMessage);
        Assert.True(s.City.HasChunk(middle), "the city lost its middle chunk");
        Assert.Equal(3, s.City.ChunkCount);
        AssertInvariants();
    }

    // C3: an edge chunk without plots goes, and is logged
    [AtlasScenario]
    public async Task Unclaim_Should_FreeChunk_When_ItIsAnEdgeWithoutPlots()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        await GivenClaimedChunk(s, 1, 0);
        ChunkPos edge = await GivenClaimedChunk(s, 2, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} unclaim");

        // Assert
        Assert.True(result.Ok, "the edge chunk was not given up: " + result.Message);
        Assert.False(s.City.HasChunk(edge), $"the city still holds {edge}");
        Assert.True(Free(edge), $"{edge} still belongs to a city");
        Assert.Equal(2, s.City.ChunkCount);
        CityLogEntry? logged = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.ChunkUnclaimed);
        Assert.NotNull(logged);
        Assert.Equal(new[] { Name(s.Mayor), edge.X + "," + edge.Z }, logged!.Args);
        AssertInvariants();
    }

    // ---- C4: extra chunks ------------------------------------------------------------------

    // C4: extra chunks up to the level's MaxExtraChunks, then no more
    [AtlasScenario]
    public async Task ExtraClaim_Should_BeRefused_When_LevelExtraLimitIsReached()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        int max = Settings.GetMaxExtraChunksForCity(s.City);
        // The row of extra chunks has to stay far from the next area's city.
        Assert.True(max <= 10, $"precondition: the level allows a handful of extra chunks, not {max}");
        for (int dx = 1; dx <= max; dx++)
        {
            ChunkPos bought = await GoToChunk(s.Mayor, s.Area, dx, 0);
            CommandResult extra = await RunPaid(s.Mayor, $"{CityCmd} extraclaim", () => s.City.HasChunk(bought), "the extra chunk to be claimed");
            Assert.True(s.City.TryGetChunk(bought, out CityChunk held) && held.ExtraBought,
                $"precondition: extra chunk {dx} of {max} was bought: {extra.Message}");
        }
        ChunkPos over = await GoToChunk(s.Mayor, s.Area, max + 1, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} extraclaim");

        // Assert
        Assert.False(result.Ok, $"an extra chunk over the limit of {max} was bought: " + result.Message);
        Assert.True(Free(over), $"{over} was claimed");
        Assert.Equal(max, s.City.ExtraChunksCount);
        AssertInvariants();
    }

    // ---- C5: outposts ----------------------------------------------------------------------

    // C5: an outpost at MIN_OUTPOST_DISTANCE_CHUNKS from the city is claimed and marked
    [AtlasScenario]
    public async Task Outpost_Should_BeClaimed_When_WithinDistance()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        // Five chunks along Z leave four between: exactly the minimum.
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 5);

        // Act
        CommandResult result = await RunPaid(s.Mayor, $"{CityCmd} outpost", () => s.City.HasChunk(chunk), "the outpost to be claimed");

        // Assert
        Assert.True(result.Ok, "/city outpost failed: " + result.Message);
        Assert.True(s.City.TryGetChunk(chunk, out CityChunk held) && held.IsOutpost, $"{chunk} is not an outpost of the city");
        Assert.Equal(2, s.City.ChunkCount);
        AssertInvariants();
    }

    // C5: an outpost nearer than MIN_OUTPOST_DISTANCE_CHUNKS is refused
    [AtlasScenario]
    public async Task Outpost_Should_BeRefused_When_TooClose()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        // Three chunks along Z leave two between, under the 4 ResetConfig sets.
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 3);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} outpost");

        // Assert
        Assert.False(result.Ok, "an outpost too close to the city was claimed: " + result.Message);
        Assert.True(Free(chunk), $"{chunk} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // C5: an outpost farther than MAX_OUTPOST_DISTANCE_CHUNKS is refused
    [AtlasScenario]
    public async Task Outpost_Should_BeRefused_When_TooFar()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        Mod.config.MAX_OUTPOST_DISTANCE_CHUNKS = 6;
        // Eight chunks along Z leave seven between, over the 6 just set.
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 8);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} outpost");

        // Assert
        Assert.False(result.Ok, "an outpost too far from the city was claimed: " + result.Message);
        Assert.True(Free(chunk), $"{chunk} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // C5: a chunk sharing a side with the city is no outpost (outpost_touches_city). A neighbour is
    // also 0 chunks away, under any minimum, so the state cannot tell the two refusals apart; the
    // touch is checked first in ChunkRules.CheckOutpost.
    [AtlasScenario]
    public async Task Outpost_Should_BeRefused_When_ChunkTouchesTheCity()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 1);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} outpost");

        // Assert
        Assert.False(result.Ok, "a chunk touching the city was claimed as an outpost: " + result.Message);
        Assert.True(Free(chunk), $"{chunk} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }
}
