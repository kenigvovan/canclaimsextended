using System.Globalization;
using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.playerMovements;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// How much the mod slows the server down, ported from claims.scenarios' PerfScenarios. The same
/// workload runs on a vanilla server (PerfVanillaScenarios) and on one with the mod
/// (PerfModScenarios); each writes its numbers to bin/perf/, and the mod run compares itself with
/// the vanilla numbers when they are there.
/// Run on purpose and more than once - one window is noise:
///   dotnet test canclaimsextended.scenarios --filter "Category=Perf"
/// and keep it out of ordinary runs:
///   dotnet test canclaimsextended.scenarios --filter "Category!=Perf"
/// Oracle (as in CAN_claims): p95 and mean of a tick with the mod at most 20 % above vanilla.
///
/// The workload: four towns in a row along X, 320 blocks apart (the spacing of CxScenarioBase's
/// areas), each with four chunks and sixteen 6 x 8 x 6 box plots in a 2 x 8 grid. Twelve players
/// (three per town) walk back and forth through every town, from the free chunk before it to the
/// free chunk after it, so each leg crosses the town border twice and eight plot borders. What
/// the mod does per tick on that: the movement check (entered/left city messages), the plot-sync
/// zone check, the selection highlight refresh (every walker has it switched on), and an access
/// check on the block the walker stands in, for use and for build, every tick. The vanilla run
/// walks the same twelve paths at the same coordinates; its per-tick probe reads the position only.
/// </summary>
public static class PerfWorkload
{
    public const int Ticks = 600;
    public const double AllowedSlowdown = 1.2;

    public const int Towns = 4;
    public const int WalkersPerTown = 3;
    public const int Walkers = Towns * WalkersPerTown;

    /// <summary>The mod's default CLAIM_CHUNK_SIZE; the mod run checks it.</summary>
    public const int ChunkSize = 16;
    /// <summary>Chunks of a town along X: the founding one and three claimed after it.</summary>
    public const int TownChunks = 4;
    /// <summary>CxScenarioBase's area spacing: town k is founded on the chunk of spawn + k * this along X.</summary>
    public const int TownSpacingBlocks = 320;

    private const int WarmupTicks = 150;

    public static BlockPos TownOrigin(BlockPos spawn, int town) => spawn.AddCopy(town * TownSpacingBlocks, 0, 0);

    private static int ChunkMin(int block) => (int)Math.Floor(block / (double)ChunkSize) * ChunkSize;

    /// <summary>The row of plots a walker goes along: z of the middle of the plot boxes of row 0 or 1.</summary>
    private static int RowZ(BlockPos origin, int row) => ChunkMin(origin.Z) + (row == 0 ? 4 : 12);

    /// <summary>Walker <paramref name="walker"/> (0..Walkers-1) starts in the free chunk before its town ...</summary>
    public static BlockPos WalkStart(BlockPos spawn, int walker)
    {
        BlockPos o = TownOrigin(spawn, walker / WalkersPerTown);
        return new BlockPos(ChunkMin(o.X) - ChunkSize / 2, o.Y, RowZ(o, walker % WalkersPerTown % 2));
    }

    /// <summary>... and turns in the free chunk after it.</summary>
    public static BlockPos WalkEnd(BlockPos spawn, int walker)
    {
        BlockPos o = TownOrigin(spawn, walker / WalkersPerTown);
        return new BlockPos(ChunkMin(o.X) + TownChunks * ChunkSize + ChunkSize / 2, o.Y, RowZ(o, walker % WalkersPerTown % 2));
    }

    /// <summary>
    /// Puts each walker at its start, lets the world settle, then measures the ticks while all of
    /// them walk back and forth and <paramref name="probe"/> runs for each of them every tick.
    /// </summary>
    public static async Task<TickMeasurement> Run(IWorldSession world, IReadOnlyList<ITestPlayer> walkers, Action<ITestPlayer> probe)
    {
        Assert.Equal(Walkers, walkers.Count);
        BlockPos spawn = world.Spawn;
        for (int i = 0; i < walkers.Count; i++)
        {
            // Straight lines through whatever terrain lies there: without collisions every walk is
            // the full length on both servers, so both measure the same work.
            walkers[i].Entity.Controls.NoClip = true;
            await walkers[i].TeleportTo(WalkStart(spawn, i));
        }
        await world.Ticks(WarmupTicks);

        // Every tick (the engine ticks about every 33 ms, so a 20 ms interval fires on each one).
        long probeId = world.Api.Event.RegisterGameTickListener(_ =>
        {
            foreach (ITestPlayer p in walkers) probe(p);
        }, 20);

        using var stop = new CancellationTokenSource();
        var walks = new List<Task>();
        for (int i = 0; i < walkers.Count; i++)
        {
            walks.Add(WalkBackAndForth(walkers[i], WalkStart(spawn, i), WalkEnd(spawn, i), stop.Token));
        }
        TickMeasurement m;
        try
        {
            m = await world.MeasureTicks(Ticks);
        }
        finally
        {
            stop.Cancel();
            world.Api.Event.UnregisterGameTickListener(probeId);
        }
        // Each walk ends with the leg it is on.
        await Task.WhenAll(walks);
        return m;
    }

    private static async Task WalkBackAndForth(ITestPlayer p, BlockPos a, BlockPos b, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await p.WalkTo(b);
            if (stop.IsCancellationRequested) break;
            await p.WalkTo(a);
        }
    }

    public static string ResultPath(string name) =>
        Path.Combine(Path.GetDirectoryName(typeof(PerfWorkload).Assembly.Location)!, "perf", name + ".txt");

    public static void Save(string name, TickMeasurement m)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath(name))!);
        File.WriteAllText(ResultPath(name), string.Join(";",
            m.BusyTime.MedianMs, m.BusyTime.P95Ms,
            m.BusyTime.MeanMicroseconds.ToString(CultureInfo.InvariantCulture), m.AllocatedBytes));
    }

    public static bool TryLoad(string name, out long p95Ms, out double meanUs)
    {
        p95Ms = 0; meanUs = 0;
        if (!File.Exists(ResultPath(name))) return false;
        string[] parts = File.ReadAllText(ResultPath(name)).Split(';');
        p95Ms = long.Parse(parts[1], CultureInfo.InvariantCulture);
        meanUs = double.Parse(parts[2], CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>The baseline: the same server without any mod of this assembly, the same walkers and paths.</summary>
[Trait("Category", "Perf")]
[AtlasWorld(ExcludeAssemblyMods = true)]
public class PerfVanillaScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Vanilla_Baseline()
    {
        var walkers = new List<ITestPlayer>();
        for (int i = 0; i < PerfWorkload.Walkers; i++) walkers.Add(await World.JoinPlayer("walker" + i));

        // No mod here, so nothing of it may be touched: the probe only reads where the walker is,
        // as the mod's probe does before its checks.
        TickMeasurement m = await PerfWorkload.Run(World, walkers, p => _ = p.Position);

        PerfWorkload.Save("vanilla", m);
    }
}

[Trait("Category", "Perf")]
public class PerfModScenarios : CxScenarioBase
{
    private const int PlotSlotsPerChunk = 4;
    private const int TownChunks = PerfWorkload.TownChunks;

    /// <summary>Box <paramref name="slot"/> (0..3, a 2 x 2 grid) of the chunk <paramref name="dx"/> chunks east of the town's founding one, as PlotBox lays them out.</summary>
    private BlockBox TownPlotBox(int town, int dx, int slot)
    {
        BlockPos o = AreaOrigin(town);
        ChunkPos home = AreaChunk(town);
        ChunkPos chunk = new ChunkPos(home.X + dx, home.Z);
        int x0 = chunk.MinBlockX + (slot % 2 == 0 ? 1 : 9);
        int z0 = chunk.MinBlockZ + (slot / 2 == 0 ? 1 : 9);
        return new BlockBox(x0, o.Y - 2, z0, x0 + 6, o.Y + 6, z0 + 6);
    }

    private async Task ClaimChunk(int town, ITestPlayer mayor, City city, int dx)
    {
        ChunkPos home = AreaChunk(town);
        ChunkPos chunk = new ChunkPos(home.X + dx, home.Z);
        await mayor.TeleportTo(new BlockPos(chunk.MinBlockX + ChunkPos.Size / 2, AreaOrigin(town).Y, chunk.MinBlockZ + ChunkPos.Size / 2));
        CommandResult result = await RunPaid(mayor, $"{CityCmd} claim", () => city.HasChunk(chunk), "the chunk to be claimed");
        Assert.True(city.HasChunk(chunk), $"precondition: {chunk} was claimed: {result.Message}");
    }

    private async Task<Plot> CreatePlot(ITestPlayer mayor, City city, BlockBox box)
    {
        Select(mayor, box);
        await StandIn(mayor, box);
        CommandResult result = await RunPaid(mayor, $"{PlotCmd} create", () => PlotIn(box) != null, "the plot to be created");
        Plot? plot = PlotIn(box);
        Assert.True(plot != null && city.Equals(plot.getCity()), $"precondition: /plot create made a plot of {city.GetPartName()}: {result.Message}");
        return plot!;
    }

    /// <summary>A town of mayor and two citizens on area <paramref name="town"/>: four chunks, sixteen plots, one of them a citizen's.</summary>
    private async Task<CityScene> GivenTown(int town)
    {
        // Fixed areas, not NewArea(): the vanilla run walks the same coordinates. A fresh server per
        // class, so the chunks are free; checked all the same.
        Assert.False(Mod.dataStorage.GetCityAtChunk(AreaChunk(town), out _), $"precondition: area {town} is free");
        ITestPlayer mayor = await JoinAt("perfmayor" + town, town);
        City city = await FoundCity(mayor, CityName(town));
        city.setBonusChunks(TownChunks);
        city.setBonusPlots(TownChunks * PlotSlotsPerChunk);
        var citizens = new List<ITestPlayer>();
        foreach (string name in new[] { "perfa", "perfb" })
        {
            ITestPlayer citizen = await JoinAt(name + town, town);
            await AddCitizen(city, mayor, citizen);
            citizens.Add(citizen);
        }
        var scene = new CityScene(town, mayor, city, citizens);

        for (int dx = 1; dx < TownChunks; dx++) await ClaimChunk(town, mayor, city, dx);

        var plots = new List<Plot>();
        for (int dx = 0; dx < TownChunks; dx++)
        {
            for (int slot = 0; slot < PlotSlotsPerChunk; slot++)
            {
                plots.Add(await CreatePlot(mayor, city, TownPlotBox(town, dx, slot)));
            }
        }
        // One plot with an owner, so the access checks meet an owner, citizens and strangers.
        await GivenOwnedPlot(scene, plots[PlotSlotsPerChunk], citizens[0]);
        return scene;
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Mod_Should_NotSlowTheTickDownMoreThanAllowed()
    {
        // Arrange: four towns with four chunks and sixteen plots each, twelve walkers.
        ResetConfig();
        Assert.True(ChunkPos.Size == PerfWorkload.ChunkSize, $"precondition: CLAIM_CHUNK_SIZE is {PerfWorkload.ChunkSize}, not {ChunkPos.Size}");

        // Movement messages are off by default and their check is only registered at boot when on;
        // a server that uses them is the one worth measuring.
        if (!Mod.config.MOVEMENT_MESSAGES)
        {
            Mod.config.MOVEMENT_MESSAGES = true;
            PlayerMovementTracker.Init(World.Api);
            AddCleanup(() => Mod.config.MOVEMENT_MESSAGES = false);
        }

        var towns = new List<CityScene>();
        for (int town = 0; town < PerfWorkload.Towns; town++) towns.Add(await GivenTown(town));
        AssertInvariants();

        // Walker i walks the row of town i / 3: its mayor, its first citizen, and the second
        // citizen of the town before it - a stranger there, who gets "entered city" and is refused.
        var walkers = new List<ITestPlayer>();
        for (int town = 0; town < PerfWorkload.Towns; town++)
        {
            walkers.Add(towns[town].Mayor);
            walkers.Add(towns[town].Citizens[0]);
            walkers.Add(towns[(town + PerfWorkload.Towns - 1) % PerfWorkload.Towns].Citizens[1]);
        }
        // Everyone sees the plots around them, as with the wand in hand: the highlight is rebuilt every second.
        foreach (ITestPlayer w in walkers) Mod.selections.Get(w.Player, true).AlwaysShow = true;
        AddCleanup(() =>
        {
            foreach (ITestPlayer w in walkers)
            {
                var sel = Mod.selections.Get(w.Player);
                if (sel != null) sel.AlwaysShow = false;
            }
        });

        // Act
        TickMeasurement m = await PerfWorkload.Run(World, walkers, p =>
        {
            BlockPos at = p.Position;
            AccessRules.Check(p.Player, at, PermType.USE_PERM);
            AccessRules.Check(p.Player, at, PermType.BUILD_AND_DESTROY_PERM);
        });
        PerfWorkload.Save("mod", m);

        // Assert: only when the vanilla numbers exist - classes run in no guaranteed order
        if (!PerfWorkload.TryLoad("vanilla", out long vanillaP95, out double vanillaMeanUs)) return;
        // Ticks are timed in whole milliseconds, so a slack of 1 ms keeps 0 ms vs 1 ms from failing.
        Assert.True(m.BusyTime.P95Ms <= vanillaP95 * PerfWorkload.AllowedSlowdown + 1,
            $"p95 {m.BusyTime.P95Ms} ms vs vanilla {vanillaP95} ms");
        Assert.True(m.BusyTime.MeanMicroseconds <= vanillaMeanUs * PerfWorkload.AllowedSlowdown + 1000,
            $"mean {m.BusyTime.MeanMicroseconds:0} us vs vanilla {vanillaMeanUs:0} us");
    }
}
