using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios.Infrastructure;

/// <summary>
/// Shared ground for scenarios of the mod.
///
/// One server per class, and the mod's state is not rolled back between scenarios. So every
/// scenario takes its own area of the map (<see cref="NewArea"/>), joins its own players, and
/// <see cref="AssertInvariants"/> looks only at the players and cities this scenario created.
///
/// Land here is two layers: a city owns chunks (its territory), and plots are 3D boxes inside it.
/// A scene's city owns the chunk of its area's origin; <see cref="PlotBox"/> gives up to four
/// boxes in that chunk that never overlap.
/// </summary>
public abstract class CxScenarioBase : AtlasScenarioBase, IDisposable
{
    // 20 chunks of 16: far past MIN_CHUNK_DISTANCE_FROM_OTHER_CITY.
    private const int AreaSpacingBlocks = 320;
    // A paid action runs on /agree; give it a few ticks.
    protected const int AgreeTimeoutTicks = 20;

    private static int lastArea = -1;

    // Players of finished scenarios, per class. The server holds 16 clients, so they are
    // disconnected before the next join; Dispose runs off the game thread and cannot do it.
    private static readonly Dictionary<Type, (ICoreServerAPI Server, List<ITestPlayer> Players)> leftOnline = new();

    private readonly List<ITestPlayer> players = new();
    private readonly List<City> cities = new();
    private readonly List<Action> cleanups = new();

    protected static string CityCmd => "/" + Mod.config.COMMAND_CITY;
    protected static string PlotCmd => "/" + Mod.config.COMMAND_PLOT;
    protected static string CadminCmd => "/" + Mod.config.COMMAND_CADMIN;

    /// <summary>Config every scenario starts from. Override to add, call base first.</summary>
    protected virtual void ResetConfig()
    {
        var c = Mod.config;
        c.MIN_CHUNK_DISTANCE_FROM_OTHER_CITY = 3;
        c.PLOT_PARTS_MUST_TOUCH = false;
        c.PLOT_UNCLAIM_REFUND_PERCENT = 0;
        c.DELETE_CITIZEN_FROM_CITY_IF_DOESN_PAY_FEE = true;
        c.AGREEMENT_TIMEOUT_SECONDS = 120;
        c.TEMPLE_RESPAWN_ENABLED = true;
        c.TEMPLE_RESPAWN_CHOICE_ENABLED = true;
    }

    protected static int NewArea() => Interlocked.Increment(ref lastArea);

    protected static string CityName(int area) =>
        "Town" + new string(area.ToString().Select(d => (char)('a' + (d - '0'))).ToArray());

    /// <summary>Where the area starts: the founding chunk of its city is the chunk of this block.</summary>
    protected BlockPos AreaOrigin(int area) => World.Spawn.Offset(area * AreaSpacingBlocks, 0, 0);

    protected ChunkPos AreaChunk(int area)
    {
        BlockPos o = AreaOrigin(area);
        return ChunkPos.FromBlock(o.X, o.Z);
    }

    /// <summary>
    /// One of four 6 x 8 x 6 boxes inside the area's founding chunk (slot 0..3, a 2 x 2 grid with
    /// a gap between them). The player's feet height of <see cref="Middle"/> is inside the box.
    /// </summary>
    protected BlockBox PlotBox(int area, int slot = 0)
    {
        BlockPos o = AreaOrigin(area);
        ChunkPos chunk = AreaChunk(area);
        int x0 = chunk.MinBlockX + (slot % 2 == 0 ? 1 : 9);
        int z0 = chunk.MinBlockZ + (slot / 2 == 0 ? 1 : 9);
        return new BlockBox(x0, o.Y - 2, z0, x0 + 6, o.Y + 6, z0 + 6);
    }

    /// <summary>A block inside the box where a player can stand: feet two blocks above its floor.</summary>
    protected static BlockPos Middle(BlockBox b) => new(b.MinX + 3, b.MinY + 2, b.MinZ + 3);

    protected static Task StandIn(ITestPlayer player, BlockBox box) => player.TeleportTo(Middle(box));

    // ---- economy ---------------------------------------------------------------------------

    /// <summary>Replaces the economy for this scenario; the previous one comes back on Dispose.</summary>
    protected FakeMoneyProvider UseFakeEconomy()
    {
        IMoneyProvider previous = Mod.economyProvider;
        var fake = new FakeMoneyProvider();
        Mod.economyProvider = fake;
        cleanups.Add(() => Mod.economyProvider = previous);
        return fake;
    }

    // ---- players and cities ----------------------------------------------------------------

    protected async Task<ITestPlayer> JoinAt(string name, int area)
    {
        await DisconnectEarlierPlayers();
        ITestPlayer player = await World.JoinPlayer(name);
        await player.TeleportTo(AreaOrigin(area));
        players.Add(player);
        return player;
    }

    private async Task DisconnectEarlierPlayers()
    {
        List<ITestPlayer> stale;
        lock (leftOnline)
        {
            if (!leftOnline.Remove(GetType(), out var left)) return;
            // A restarted host has a new API; its old players are gone already.
            if (!ReferenceEquals(left.Server, World.Api)) return;
            stale = left.Players;
        }

        foreach (ITestPlayer p in stale)
        {
            if (p.IsConnected) p.Player.Disconnect();
        }
        await World.Until(() => stale.All(p => !p.IsConnected), "players of earlier scenarios to leave", 100);
    }

    protected static PlayerInfo Info(ITestPlayer player)
    {
        Assert.True(Mod.dataStorage.GetPlayerByUid(player.Player.PlayerUID, out PlayerInfo info),
            "no PlayerInfo for " + player.Player.PlayerName);
        return info;
    }

    protected static string Name(ITestPlayer player) => player.Player.PlayerName;

    /// <summary>
    /// Runs a command and, when it only made an offer (a paid action with an economy), /agree.
    /// With the default noop economy every action is free and runs at once.
    /// </summary>
    protected async Task<CommandResult> RunPaid(ITestPlayer player, string command, Func<bool> done, string what)
    {
        CommandResult result = await player.ExecuteCommand(command);
        if (!done())
        {
            await player.ExecuteCommand("/" + Mod.config.COMMAND_AGREE);
            await World.Until(done, what, AgreeTimeoutTicks);
        }
        return result;
    }

    protected async Task<City> FoundCity(ITestPlayer mayor, string name)
    {
        CommandResult result = await RunPaid(mayor, $"{CityCmd} create {name}", () => Info(mayor).hasCity(), name + " to be founded");
        Assert.True(Info(mayor).hasCity(), $"/city create {name}: {result.Message}");
        City city = Info(mayor).City;
        cities.Add(city);
        return city;
    }

    protected async Task AddCitizen(City city, ITestPlayer mayor, ITestPlayer player)
    {
        await mayor.ExecuteCommand($"{CityCmd} set open on");
        Assert.True(city.openCity, $"{city.GetPartName()} must be open for /city join");
        await player.ExecuteCommand($"{CityCmd} join {city.GetPartName()}");
        Assert.True(city.Equals(Info(player).City), $"{Name(player)} must have joined {city.GetPartName()}");
    }

    /// <summary>What most scenarios start from: a city on the area's chunk, its mayor and citizens.</summary>
    protected sealed record CityScene(int Area, ITestPlayer Mayor, City City, IReadOnlyList<ITestPlayer> Citizens);

    /// <summary>
    /// Resets the config, takes a new area, founds a city on its chunk and adds one citizen per
    /// name (the area number is appended: player names must be unique per class). The city gets
    /// bonus plots, so the level limit never refuses a plot the scenario is about.
    /// </summary>
    protected async Task<CityScene> GivenCity(params string[] citizenNames)
    {
        ResetConfig();
        int area = NewArea();
        ITestPlayer mayor = await JoinAt("mayor" + area, area);
        City city = await FoundCity(mayor, CityName(area));
        city.setBonusPlots(8);
        var citizens = new List<ITestPlayer>();
        foreach (string name in citizenNames)
        {
            ITestPlayer citizen = await JoinAt(name + area, area);
            await AddCitizen(city, mayor, citizen);
            citizens.Add(citizen);
        }
        return new CityScene(area, mayor, city, citizens);
    }

    /// <summary>A player of no city, in the area of the scene.</summary>
    protected async Task<ITestPlayer> GivenStranger(CityScene s, string name = "stranger") =>
        await JoinAt(name + s.Area, s.Area);

    // ---- plots -----------------------------------------------------------------------------

    /// <summary>Sets the player's selection to exactly the box, as the wand's two clicks would.</summary>
    protected static void Select(ITestPlayer player, BlockBox box)
    {
        Mod.selections.SetCorner(player.Player, 1, new BlockPos(box.MinX, box.MinY, box.MinZ));
        Mod.selections.SetCorner(player.Player, 2, new BlockPos(box.MaxX - 1, box.MaxY - 1, box.MaxZ - 1));
    }

    /// <summary>The mayor makes box <paramref name="slot"/> of the area a plot (/plot create) and stands in it.</summary>
    protected async Task<Plot> GivenPlot(CityScene s, int slot = 0)
    {
        BlockBox box = PlotBox(s.Area, slot);
        Select(s.Mayor, box);
        await StandIn(s.Mayor, box);
        CommandResult result = await RunPaid(s.Mayor, $"{PlotCmd} create", () => PlotIn(box) != null, "the plot to be created");
        Plot? plot = PlotIn(box);
        Assert.True(plot != null && s.City.Equals(plot.getCity()), $"precondition: /plot create made a plot of {s.City.GetPartName()}: {result.Message}");
        return plot!;
    }

    /// <summary>The mayor puts the plot up for <paramref name="price"/> and the player buys it.</summary>
    protected async Task GivenOwnedPlot(CityScene s, Plot plot, ITestPlayer buyer, int price = 0)
    {
        BlockBox box = plot.Shape.Boxes[0];
        await StandIn(s.Mayor, box);
        await s.Mayor.ExecuteCommand($"{PlotCmd} fs {price}");
        Assert.True(plot.Price == price, $"precondition: /plot fs {price} set the price");
        await StandIn(buyer, box);
        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim {price}");
        Assert.True(Info(buyer).Equals(plot.getPlotOwner()), $"precondition: {Name(buyer)} owns the plot: {claim.Message}");
    }

    /// <summary>The real plot at the box's middle, null when there is none.</summary>
    protected static Plot? PlotIn(BlockBox box) =>
        Mod.dataStorage.GetSubPlotAt(Middle(box), out Plot plot) ? plot : null;

    /// <summary>The real plot where the player stands.</summary>
    protected static Plot PlotAt(ITestPlayer player)
    {
        Assert.True(Mod.dataStorage.GetSubPlotAt(player.Position, out Plot plot),
            $"no plot where {Name(player)} stands");
        return plot;
    }

    // ---- admin -----------------------------------------------------------------------------

    /// <summary>Lets the player run /cadmin: adds their role to ROLE_CODES_WITH_ADMIN_RIGHTS until Dispose.</summary>
    protected void AllowAdminCommands(ITestPlayer player)
    {
        string role = player.Player.Role.Code;
        if (Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Contains(role)) return;
        Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Add(role);
        cleanups.Add(() => Mod.config.ROLE_CODES_WITH_ADMIN_RIGHTS.Remove(role));
    }

    /// <summary>Runs on Dispose, after the scenario - for test state the base class does not know about.</summary>
    protected void AddCleanup(Action cleanup) => cleanups.Add(cleanup);

    // ---- invariants ------------------------------------------------------------------------

    /// <summary>
    /// The links that must always agree, for the cities and players of this scenario:
    /// 1 city lists a plot = the plot points to the city; 2 a plot's owner lists it = the plot names
    /// the owner; 3 the mayor and every citizen point back to the city; 4 an owner is a citizen of
    /// the plot's city; 5 a price is -1 (not for sale) or above. Reports every broken one at once.
    /// </summary>
    protected void AssertInvariants()
    {
        var broken = new List<string>();
        var plots = new HashSet<Plot>();

        foreach (City city in cities.Where(c => Mod.dataStorage.GetCitiesList().Contains(c)))
        {
            string name = city.GetPartName();
            foreach (Plot plot in city.getCityPlots())
            {
                plots.Add(plot);
                if (!city.Equals(plot.getCity()))
                    broken.Add($"inv1: {name} lists plot #{plot.Number}, which points to {plot.getCity()?.GetPartName() ?? "no city"}");
            }

            PlayerInfo mayor = city.getMayor();
            if (mayor != null && !city.getCityCitizens().Contains(mayor))
                broken.Add($"inv3: mayor of {name} ({mayor.GetPartName()}) is not its citizen");
            foreach (PlayerInfo citizen in city.getCityCitizens())
            {
                if (!city.Equals(citizen.City))
                    broken.Add($"inv3: {name} lists {citizen.GetPartName()}, whose city is {citizen.City?.GetPartName() ?? "none"}");
            }
        }

        foreach (ITestPlayer p in players)
        {
            PlayerInfo info = Info(p);
            foreach (Plot plot in info.PlayerPlots)
            {
                plots.Add(plot);
                if (!info.Equals(plot.getPlotOwner()))
                    broken.Add($"inv2: plot #{plot.Number} is in PlayerPlots of {info.GetPartName()}, but its owner is {plot.getPlotOwner()?.GetPartName() ?? "nobody"}");
            }
        }

        foreach (Plot plot in plots)
        {
            City? city = plot.getCity();
            if (city != null && !city.getCityPlots().Contains(plot))
                broken.Add($"inv1: plot #{plot.Number} points to {city.GetPartName()}, which does not list it");

            PlayerInfo? owner = plot.getPlotOwner();
            if (owner != null)
            {
                if (!owner.PlayerPlots.Contains(plot))
                    broken.Add($"inv2: owner {owner.GetPartName()} of plot #{plot.Number} does not have it in PlayerPlots");
                if (city == null || !city.Equals(owner.City))
                    broken.Add($"inv4: owner {owner.GetPartName()} of plot #{plot.Number} is not a citizen of {city?.GetPartName() ?? "no city"}");
            }

            if (plot.Price < -1)
                broken.Add($"inv5: plot #{plot.Number} has price {plot.Price}");
        }

        Assert.True(broken.Count == 0, "invariants broken:\n" + string.Join("\n", broken));
    }

    // ---------------------------------------------------------------------------------------

    public void Dispose()
    {
        // Last in, first out: a later change may build on an earlier one.
        for (int i = cleanups.Count - 1; i >= 0; i--) cleanups[i]();
        cleanups.Clear();

        if (players.Count > 0)
        {
            lock (leftOnline)
            {
                if (leftOnline.TryGetValue(GetType(), out var left) && ReferenceEquals(left.Server, World.Api))
                    left.Players.AddRange(players);
                else
                    leftOnline[GetType()] = (World.Api, new List<ITestPlayer>(players));
            }
        }
        GC.SuppressFinalize(this);
    }
}
