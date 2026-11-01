using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.citylog;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Chunks of territory against the treasury and the limits: /city claim, /city extraclaim and
/// /city outpost at the price boundary, a payment that fails on /agree, which refusal wins, the
/// minimum distance to another city at its boundary, the level's chunk limit, and the rights of
/// the paid chunk commands. Ported from the chunk-claim cases of CAN_claims (CityClaimScenarios,
/// OutpostExtraScenarios), where a city's chunk was a plot.
///
/// Every chunk claim checks the rules first (ChunkRules) and the treasury after (PaidAction.CheckFunds),
/// then waits for /agree, checks the rules again and only then withdraws. TerritoryScenarios has
/// the free (no economy) claims; this class adds the money and the boundaries.
///
/// Areas lie on X only, 20 chunks apart, so chunks a few steps from the founding chunk are free.
/// </summary>
[Trait("Category", "E2E")]
public class ChunkClaimMoneyScenarios : CxScenarioBase
{
    // Prices pinned here, so "one short" always means something whatever the host's config says.
    private const double ChunkPrice = 10;
    private const double ExtraSurcharge = 30;
    private const double OutpostSurcharge = 150;

    private static string AgreeCmd => "/" + Mod.config.COMMAND_AGREE;

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        double chunkPrice = c.CHUNK_CLAIM_PRICE, extra = c.EXTRA_CHUNK_COST, outpost = c.OUTPOST_CHUNK_COST;
        int minOutpost = c.MIN_OUTPOST_DISTANCE_CHUNKS, maxOutpost = c.MAX_OUTPOST_DISTANCE_CHUNKS;
        int minCityDistance = c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY;
        AddCleanup(() =>
        {
            c.CHUNK_CLAIM_PRICE = chunkPrice;
            c.EXTRA_CHUNK_COST = extra;
            c.OUTPOST_CHUNK_COST = outpost;
            c.MIN_OUTPOST_DISTANCE_CHUNKS = minOutpost;
            c.MAX_OUTPOST_DISTANCE_CHUNKS = maxOutpost;
            c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = minCityDistance;
        });
        c.CHUNK_CLAIM_PRICE = ChunkPrice;
        c.EXTRA_CHUNK_COST = ExtraSurcharge;
        c.OUTPOST_CHUNK_COST = OutpostSurcharge;
        c.MIN_OUTPOST_DISTANCE_CHUNKS = 4;
        c.MAX_OUTPOST_DISTANCE_CHUNKS = 125;
    }

    // ---- helpers ---------------------------------------------------------------------------

    // Computed as CityCommand.ClaimChunkFor does: (decimal)(ChunkClaimCost() + surcharge).
    private static decimal ClaimCost => (decimal)PlotPricing.ChunkClaimCost();
    private static decimal ExtraCost => (decimal)(PlotPricing.ChunkClaimCost() + Mod.config.EXTRA_CHUNK_COST);
    private static decimal OutpostCost => (decimal)(PlotPricing.ChunkClaimCost() + Mod.config.OUTPOST_CHUNK_COST);

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

    private static bool Free(ChunkPos chunk) => !Mod.dataStorage.GetCityAtChunk(chunk, out _);

    private static string OtherCityName(int area, string prefix) => prefix + CityName(area).Substring("Town".Length);

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    /// <summary>No offer is left waiting: /agree finds nothing to agree to.</summary>
    private static async Task AssertNoOfferWaits(ITestPlayer player)
    {
        CommandResult agree = await player.ExecuteCommand(AgreeCmd);
        AssertRefused(agree, "no_agreements_awaits", "/agree after a refused claim");
    }

    private static bool Withdrew(FakeMoneyProvider money, string account) =>
        money.Log.Any(l => l.StartsWith("Withdraw " + account + " ") && l.EndsWith(": " + MoneyOperationResult.Success));

    /// <summary>
    /// The level key a city of its current size uses, the same lookup as Settings.GetCityLevelInfo.
    /// </summary>
    private static int LevelKeyOf(City city)
    {
        int citizens = city.getCityCitizens().Count;
        foreach (int key in Settings.cityLevelsDict.Keys.Reverse())
        {
            if (citizens >= key) return key;
        }
        return Settings.cityLevelsDict.Keys.First();
    }

    /// <summary>Sets one field of the city's current level until Dispose ("chunks", "extra").</summary>
    private void SetLevelField(City city, string field, int value)
    {
        int key = LevelKeyOf(city);
        CityLevelInfo level = Settings.cityLevelsDict[key];
        int previous = field switch
        {
            "chunks" => level.MaxChunks,
            "extra" => level.MaxExtraChunks,
            _ => throw new ArgumentException(field),
        };
        AddCleanup(() => Settings.TrySetLevelField(key, field, previous, out _, out _));
        Assert.True(Settings.TrySetLevelField(key, field, value, out string error, out _),
            $"precondition: level {key} {field} = {value}: {error}");
    }

    /// <summary>A city on the area's chunk whose level allows exactly <paramref name="maxChunks"/> chunks.</summary>
    private async Task<CityScene> GivenCityWithChunkLimit(int maxChunks, params string[] citizenNames)
    {
        CityScene s = await GivenCity(citizenNames);
        s.City.setBonusChunks(0);
        SetLevelField(s.City, "chunks", maxChunks);
        Assert.Equal(maxChunks, Settings.GetMaxChunksForCity(s.City));
        return s;
    }

    /// <summary>A city whose level limit never stops the few chunks a scenario claims.</summary>
    private async Task<CityScene> GivenRoomyCity(params string[] citizenNames)
    {
        CityScene s = await GivenCity(citizenNames);
        s.City.setBonusChunks(8);
        return s;
    }

    /// <summary>Claims the chunk with no economy (free, at once) and must get it.</summary>
    private async Task GivenClaimedChunk(CityScene s, int dx, int dz)
    {
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, dx, dz);
        CommandResult result = await RunPaid(s.Mayor, $"{CityCmd} claim", () => s.City.HasChunk(chunk), "the chunk to be claimed");
        Assert.True(s.City.HasChunk(chunk), $"precondition: {chunk} was claimed: {result.Message}");
    }

    /// <summary>Another city founded (with no economy) on the chunk dx, 0 of the scene's area.</summary>
    private async Task<City> GivenRivalCityAt(CityScene s, int dx)
    {
        ITestPlayer rival = await GivenStranger(s, "rival");
        ChunkPos theirs = await GoToChunk(rival, s.Area, dx, 0);
        City other = await FoundCity(rival, OtherCityName(s.Area, "Rival"));
        Assert.True(other.HasChunk(theirs), $"precondition: the rival city was founded on {theirs}");
        return other;
    }

    // ---- /city claim: the treasury ---------------------------------------------------------

    // Price boundary: a treasury of exactly the price makes the offer; /agree claims and empties it
    [AtlasScenario]
    public async Task Claim_Should_ClaimAndCharge_When_TreasuryHoldsExactlyThePrice()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = ClaimCost;
        money.SetBalance(s.City.MoneyAccountName, cost);

        // Act 1: the offer
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} claim");
        await World.Ticks(AgreeTimeoutTicks);

        // Assert 1: nothing yet
        Assert.True(offer.Ok, "/city claim with exactly the price: " + offer.Message);
        Assert.Equal("canclaimsextended:agree_chunk_claim", offer.Raw.StatusMessage);
        Assert.True(Free(chunk), $"{chunk} was claimed before /agree");
        Assert.Equal(cost, money.GetBalance(s.City.MoneyAccountName));

        // Act 2
        CommandResult agree = await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Until(() => s.City.HasChunk(chunk), "the chunk to be claimed on /agree", AgreeTimeoutTicks);

        // Assert 2
        Assert.True(agree.Ok, "/agree: " + agree.Message);
        Assert.True(s.City.TryGetChunk(chunk, out CityChunk held) && !held.ExtraBought && !held.IsOutpost,
            "a paid ordinary claim was marked as extra or outpost");
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Equal(2, s.City.ChunkCount);
        CityLogEntry? logged = s.City.EventLog.LastOrDefault(e => e.EventType == EnumCityLogEvent.ChunkClaimed);
        Assert.NotNull(logged);
        Assert.Equal(new[] { Name(s.Mayor), chunk.X + "," + chunk.Z }, logged!.Args);
        AssertInvariants();
    }

    // Price boundary: one short of the price is refused at once - no offer, no chunk, no withdrawal
    [AtlasScenario]
    public async Task Claim_Should_BeRefusedAndChangeNothing_When_TreasuryIsOneShort()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ClaimCost - 1;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        AssertRefused(result, "not_enough_money_city", "a claim one short of the price");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(chunk), $"{chunk} was claimed without the money");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        Assert.False(Withdrew(money, s.City.MoneyAccountName), "money was taken for a refused claim");
        AssertInvariants();
    }

    // PaidAction: the withdrawal on /agree fails - the chunk is not claimed, the treasury untouched
    [AtlasScenario]
    public async Task Claim_Should_ChangeNothing_When_TheWithdrawalFails()
    {
        // Arrange: the offer is made, then the economy refuses the payment
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ClaimCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} claim");
        Assert.Equal("canclaimsextended:agree_chunk_claim", offer.Raw.StatusMessage);
        money.FailNext(FakeMoneyOp.Withdraw, s.City.MoneyAccountName);
        int logBefore = s.City.EventLog.Count(e => e.EventType == EnumCityLogEvent.ChunkClaimed);

        // Act
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        Assert.Contains(money.Log, l => l.StartsWith("Withdraw " + s.City.MoneyAccountName + " ")
            && l.EndsWith(": " + MoneyOperationResult.FailedSourceWithdraw));
        Assert.True(Free(chunk), $"{chunk} was claimed although the payment failed");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        Assert.Equal(logBefore, s.City.EventLog.Count(e => e.EventType == EnumCityLogEvent.ChunkClaimed));
        AssertInvariants();
    }

    // PaidAction: the treasury was spent while the offer waited - /agree claims nothing, takes nothing
    [AtlasScenario]
    public async Task Claim_Should_ChangeNothing_When_TreasuryIsShortByTheAgree()
    {
        // Arrange: enough at the offer, one short at the agreement
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = ClaimCost;
        money.SetBalance(s.City.MoneyAccountName, cost);
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} claim");
        Assert.Equal("canclaimsextended:agree_chunk_claim", offer.Raw.StatusMessage);
        money.SetBalance(s.City.MoneyAccountName, cost - 1);

        // Act
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Ticks(AgreeTimeoutTicks);

        // Assert
        Assert.Contains(money.Log, l => l.StartsWith("Withdraw " + s.City.MoneyAccountName + " ")
            && l.EndsWith(": " + MoneyOperationResult.SourceNotEnoughMoney));
        Assert.True(Free(chunk), $"{chunk} was claimed with a treasury short of the price");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(cost - 1, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // Which refusal wins: a chunk touching the city only by a corner, with an empty treasury, is
    // refused as not next to the city - the rules are checked before the money
    [AtlasScenario]
    public async Task Claim_Should_SayNotNextToTheCity_When_AlsoShortOfMoney()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos diagonal = await GoToChunk(s.Mayor, s.Area, 1, 1);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 0);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        AssertRefused(result, "chunk_not_adjacent", "a diagonal chunk with an empty treasury");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(diagonal), $"{diagonal} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.False(Withdrew(money, s.City.MoneyAccountName), "money was taken for a refused claim");
        AssertInvariants();
    }

    // ---- /city claim: distance to another city --------------------------------------------

    // MIN_CHUNK_DISTANCE_FROM_OTHER_CITY boundary: exactly that many free chunks between is enough
    [AtlasScenario]
    public async Task Claim_Should_GoThrough_When_AnotherCityIsExactlyAtTheMinimumDistance()
    {
        // Arrange: the rival 5 chunks east; claiming chunk 1 leaves 2, 3, 4 free - exactly the 3
        // ResetConfig sets (ChunkPos.DistanceTo counts the free chunks between)
        CityScene s = await GivenRoomyCity();
        Assert.Equal(3, Mod.config.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY);
        City rival = await GivenRivalCityAt(s, 5);
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, ClaimCost);

        // Act
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} claim");
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Until(() => s.City.HasChunk(chunk), "the chunk at the minimum distance to be claimed", AgreeTimeoutTicks);

        // Assert
        Assert.True(offer.Ok, "/city claim at exactly the minimum distance: " + offer.Message);
        Assert.True(s.City.HasChunk(chunk), $"{chunk} was not claimed");
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Equal(1, rival.ChunkCount);
        AssertInvariants();
    }

    // MIN_CHUNK_DISTANCE_FROM_OTHER_CITY boundary: one chunk closer is refused, before the money
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_OneChunkCloserThanTheMinimumDistance()
    {
        // Arrange: the rival 4 chunks east; claiming chunk 1 would leave only 2, 3 free
        CityScene s = await GivenRoomyCity();
        Assert.Equal(3, Mod.config.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY);
        City rival = await GivenRivalCityAt(s, 4);
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ClaimCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        AssertRefused(result, "too_close_to_another_city", "a claim one chunk too close to another city");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(chunk), $"{chunk} was claimed next to another city");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(1, rival.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // ---- /city claim: the level's chunk limit ----------------------------------------------

    // The level's MaxChunks: claims go through up to it, the next one is refused and costs nothing
    [AtlasScenario]
    public async Task Claim_Should_StopAtTheLevelChunkLimit()
    {
        // Arrange: a limit of 3, the founding chunk and two more claimed
        CityScene s = await GivenCityWithChunkLimit(3);
        await GivenClaimedChunk(s, 1, 0);
        await GivenClaimedChunk(s, 2, 0);
        Assert.Equal(3, s.City.RegularChunksCount);
        ChunkPos over = await GoToChunk(s.Mayor, s.Area, 3, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ClaimCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        AssertRefused(result, "max_chunks_claimed", "a claim over the level's chunk limit");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(over), $"{over} was claimed over the limit");
        Assert.Equal(3, s.City.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // A city already over its limit (the level was lowered under it) claims nothing more, and
    // keeps what it holds
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_TheCityIsAlreadyOverTheLimit()
    {
        // Arrange: three chunks, then the level lowered to one
        CityScene s = await GivenCityWithChunkLimit(3);
        await GivenClaimedChunk(s, 1, 0);
        await GivenClaimedChunk(s, 2, 0);
        SetLevelField(s.City, "chunks", 1);
        Assert.True(s.City.RegularChunksCount > Settings.GetMaxChunksForCity(s.City), "precondition: the city is over its limit");
        ChunkPos next = await GoToChunk(s.Mayor, s.Area, 3, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ClaimCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} claim");

        // Assert
        AssertRefused(result, "max_chunks_claimed", "a claim of a city over its limit");
        Assert.True(Free(next), $"{next} was claimed by a city over its limit");
        Assert.Equal(3, s.City.ChunkCount);
        Assert.True(s.City.HasChunk(ChunkOf(s.Area, 1, 0)) && s.City.HasChunk(ChunkOf(s.Area, 2, 0)),
            "lowering the level took chunks away");
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // ---- /city extraclaim ------------------------------------------------------------------

    // Extra chunks must share a side with the city too: one apart and by a corner are refused,
    // before the money
    [AtlasScenario]
    public async Task ExtraClaim_Should_BeRefused_When_ChunkIsNotNextToTheCity()
    {
        // Arrange: the level allows an extra chunk, the treasury is empty
        CityScene s = await GivenRoomyCity();
        SetLevelField(s.City, "extra", 1);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(s.City.MoneyAccountName, 0);

        // Act
        ChunkPos apart = await GoToChunk(s.Mayor, s.Area, 2, 0);
        CommandResult apartResult = await s.Mayor.ExecuteCommand($"{CityCmd} extraclaim");
        ChunkPos diagonal = await GoToChunk(s.Mayor, s.Area, 1, 1);
        CommandResult diagonalResult = await s.Mayor.ExecuteCommand($"{CityCmd} extraclaim");

        // Assert
        AssertRefused(apartResult, "chunk_not_adjacent", "an extra chunk one apart from the city");
        AssertRefused(diagonalResult, "chunk_not_adjacent", "an extra chunk touching the city by a corner");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(apart), $"{apart} was claimed");
        Assert.True(Free(diagonal), $"{diagonal} was claimed");
        Assert.Equal(0, s.City.ExtraChunksCount);
        Assert.Equal(1, s.City.ChunkCount);
        AssertInvariants();
    }

    // Price boundary: the price plus EXTRA_CHUNK_COST, exactly, buys the extra chunk on /agree
    [AtlasScenario]
    public async Task ExtraClaim_Should_ClaimAndCharge_When_TreasuryHoldsExactlyThePrice()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        SetLevelField(s.City, "extra", 1);
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = ExtraCost;
        Assert.Equal((decimal)(ChunkPrice + ExtraSurcharge), cost);
        money.SetBalance(s.City.MoneyAccountName, cost);

        // Act 1: the offer
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} extraclaim");
        await World.Ticks(AgreeTimeoutTicks);

        // Assert 1: nothing yet
        Assert.True(offer.Ok, "/city extraclaim with exactly the price: " + offer.Message);
        Assert.Equal("canclaimsextended:agree_extra_chunk", offer.Raw.StatusMessage);
        Assert.True(Free(chunk), $"{chunk} was claimed before /agree");
        Assert.Equal(cost, money.GetBalance(s.City.MoneyAccountName));

        // Act 2
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Until(() => s.City.HasChunk(chunk), "the extra chunk to be claimed on /agree", AgreeTimeoutTicks);

        // Assert 2
        Assert.True(s.City.TryGetChunk(chunk, out CityChunk held) && held.ExtraBought && !held.IsOutpost,
            $"{chunk} is not marked as a bought extra chunk");
        Assert.Equal(1, s.City.ExtraChunksCount);
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // Price boundary: one short of the extra chunk's price is refused at once
    [AtlasScenario]
    public async Task ExtraClaim_Should_BeRefused_When_TreasuryIsOneShort()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        SetLevelField(s.City, "extra", 1);
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ExtraCost - 1;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} extraclaim");

        // Assert
        AssertRefused(result, "not_enough_money_city", "an extra chunk one short of the price");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(chunk), $"{chunk} was claimed without the money");
        Assert.Equal(0, s.City.ExtraChunksCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // CITY_BUY_EXTRA_CHUNK: a citizen without it buys nothing and the treasury pays nothing
    [AtlasScenario]
    public async Task ExtraClaim_Should_BeRefused_When_CitizenHasNoRight()
    {
        // Arrange
        CityScene s = await GivenRoomyCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        SetLevelField(s.City, "extra", 1);
        ChunkPos chunk = await GoToChunk(citizen, s.Area, 1, 0);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = ExtraCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await citizen.ExecuteCommand($"{CityCmd} extraclaim");

        // Assert
        AssertRefused(result, "you_dont_have_right_for_that_command", "an extra chunk bought by a citizen without the right");
        await AssertNoOfferWaits(citizen);
        Assert.True(Free(chunk), $"{chunk} was claimed");
        Assert.Equal(0, s.City.ExtraChunksCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // ---- /city outpost ---------------------------------------------------------------------

    // Price boundary: the price plus OUTPOST_CHUNK_COST, exactly, buys the outpost on /agree
    [AtlasScenario]
    public async Task Outpost_Should_ClaimAndCharge_When_TreasuryHoldsExactlyThePrice()
    {
        // Arrange: five chunks along Z leave four between, the minimum ResetConfig sets
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 5);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal cost = OutpostCost;
        Assert.Equal((decimal)(ChunkPrice + OutpostSurcharge), cost);
        money.SetBalance(s.City.MoneyAccountName, cost);

        // Act 1: the offer
        CommandResult offer = await s.Mayor.ExecuteCommand($"{CityCmd} outpost");
        await World.Ticks(AgreeTimeoutTicks);

        // Assert 1: nothing yet
        Assert.True(offer.Ok, "/city outpost with exactly the price: " + offer.Message);
        Assert.Equal("canclaimsextended:agree_outpost", offer.Raw.StatusMessage);
        Assert.True(Free(chunk), $"{chunk} was claimed before /agree");
        Assert.Equal(cost, money.GetBalance(s.City.MoneyAccountName));

        // Act 2
        await s.Mayor.ExecuteCommand(AgreeCmd);
        await World.Until(() => s.City.HasChunk(chunk), "the outpost to be claimed on /agree", AgreeTimeoutTicks);

        // Assert 2
        Assert.True(s.City.TryGetChunk(chunk, out CityChunk held) && held.IsOutpost && !held.ExtraBought,
            $"{chunk} is not marked as an outpost");
        Assert.Equal(2, s.City.ChunkCount);
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // Price boundary: one short of the outpost's price is refused at once
    [AtlasScenario]
    public async Task Outpost_Should_BeRefused_When_TreasuryIsOneShort()
    {
        // Arrange
        CityScene s = await GivenRoomyCity();
        ChunkPos chunk = await GoToChunk(s.Mayor, s.Area, 0, 5);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = OutpostCost - 1;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} outpost");

        // Assert
        AssertRefused(result, "not_enough_money_city", "an outpost one short of the price");
        await AssertNoOfferWaits(s.Mayor);
        Assert.True(Free(chunk), $"{chunk} was claimed without the money");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }

    // CITY_BUY_OUTPOST: a citizen without it claims no outpost and the treasury pays nothing
    [AtlasScenario]
    public async Task Outpost_Should_BeRefused_When_CitizenHasNoRight()
    {
        // Arrange
        CityScene s = await GivenRoomyCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        ChunkPos chunk = await GoToChunk(citizen, s.Area, 0, 5);
        FakeMoneyProvider money = UseFakeEconomy();
        decimal start = OutpostCost + 100;
        money.SetBalance(s.City.MoneyAccountName, start);

        // Act
        CommandResult result = await citizen.ExecuteCommand($"{CityCmd} outpost");

        // Assert
        AssertRefused(result, "you_dont_have_right_for_that_command", "an outpost claimed by a citizen without the right");
        await AssertNoOfferWaits(citizen);
        Assert.True(Free(chunk), $"{chunk} was claimed");
        Assert.Equal(1, s.City.ChunkCount);
        Assert.Equal(start, money.GetBalance(s.City.MoneyAccountName));
        AssertInvariants();
    }
}
