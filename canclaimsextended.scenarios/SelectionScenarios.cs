using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.geometry;
using canclaimsextended.src.items;
using canclaimsextended.src.selection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// The wand and the box selection: /city wand (D1), the wand's clicks (D2), the highlight colors
/// (D3) and /city sel with pos1, pos2, grow, shrink, show, hide, clear and the info (D4). Section D
/// of TESTING_canclaimsextended.md. What the selection becomes as a plot is in PlotShapeScenarios.
///
/// The wand's clicks are called on the item itself (ItemClaimWand.OnHeldAttackStart and
/// OnHeldInteractStart), as the server's input handling would call them: a test player sends no
/// mouse input. The highlight is read from what the server sent the player (Client.Highlights).
/// </summary>
[Trait("Category", "E2E")]
public class SelectionScenarios : CxScenarioBase
{
    private const string WandCode = "canclaimsextended:claimwand";

    protected override void ResetConfig()
    {
        base.ResetConfig();
        var c = Mod.config;
        int maxDim = c.SELECTION_MAX_DIM;
        AddCleanup(() => c.SELECTION_MAX_DIM = maxDim);
        c.SELECTION_MAX_DIM = 256;
    }

    private static string SelCmd => CityCmd + " sel";

    private static PlotSelection? Sel(ITestPlayer player) => Mod.selections.Get(player.Player);

    private static BlockBox RawBox(ITestPlayer player)
    {
        PlotSelection? sel = Sel(player);
        Assert.True(sel != null && sel.TryGetRawBox(out _), $"{Name(player)} has no selection");
        sel!.TryGetRawBox(out BlockBox box);
        return box;
    }

    private static int CountWands(ITestPlayer player)
    {
        int count = 0;
        player.Entity.WalkInventory(slot =>
        {
            if (slot.Itemstack?.Collectible?.Code?.ToString() == WandCode) count += slot.StackSize;
            return true;
        });
        return count;
    }

    /// <summary>A player of no city in a new area.</summary>
    private async Task<(int Area, ITestPlayer Player)> GivenLoner()
    {
        ResetConfig();
        int area = NewArea();
        return (area, await JoinAt("loner" + area, area));
    }

    private static ItemClaimWand Wand()
    {
        Item item = Mod.sapi.World.GetItem(new AssetLocation(WandCode));
        Assert.True(item is ItemClaimWand, $"{WandCode} is not an ItemClaimWand: {item?.GetType().Name ?? "missing"}");
        return (ItemClaimWand)item;
    }

    private static string L(ITestPlayer player, string key, params object[] args) =>
        Lang.GetL(player.Player.LanguageCode, "canclaimsextended:" + key, args);

    // The color SelectionHandler sends for a config color {a, r, g, b}.
    private static int HighlightColor(int[] argb) => ColorUtil.ColorFromRgba(argb[1], argb[2], argb[3], argb[0]);

    private static bool Highlights(ITestPlayer player, BlockBox box, int color)
    {
        var shown = player.Client.Highlights(SelectionHandler.HIGHLIGHT_SLOT);
        bool At(HighlightedBlock h, int x, int y, int z) => h.Pos.X == x && h.Pos.Y == y && h.Pos.Z == z;
        // The box is sent as its two corners, max exclusive; with one box every position takes its color.
        return shown.Count == 2
            && At(shown[0], box.MinX, box.MinY, box.MinZ) && At(shown[1], box.MaxX, box.MaxY, box.MaxZ)
            && shown.All(h => h.Color == color);
    }

    // ---- D1: the wand -----------------------------------------------------------------------

    // D1: /city wand hands out one wand; a second call is wand_already_have and gives none
    [AtlasScenario]
    public async Task Wand_Should_BeGivenOnce()
    {
        // Arrange
        var (_, player) = await GivenLoner();
        Assert.Equal(0, CountWands(player));

        // Act
        CommandResult first = await player.ExecuteCommand($"{CityCmd} wand");
        int afterFirst = CountWands(player);
        CommandResult second = await player.ExecuteCommand($"{CityCmd} wand");

        // Assert
        Assert.True(first.Ok, "/city wand: " + first.Message);
        Assert.Equal("canclaimsextended:wand_given", first.Raw.StatusMessage);
        Assert.Equal(1, afterFirst);
        Assert.False(second.Ok, "a second wand was handed out: " + second.Message);
        Assert.Equal("canclaimsextended:wand_already_have", second.Raw.StatusMessage);
        Assert.Equal(1, CountWands(player));
    }

    // ---- D2: the wand's clicks ----------------------------------------------------------------

    // D2: left click sets corner 1, right click corner 2, shift + left click clears; neither click
    // lets the default action (breaking, placing) go on
    [AtlasScenario]
    public async Task WandClicks_Should_SetCornersAndClear()
    {
        // Arrange
        var (area, player) = await GivenLoner();
        await player.GiveItem(WandCode);
        ItemSlot slot = player.Player.InventoryManager.ActiveHotbarSlot;
        Assert.True(slot.Itemstack?.Collectible is ItemClaimWand, "precondition: the wand is in hand");
        ItemClaimWand wand = Wand();
        BlockPos o = AreaOrigin(area);
        BlockPos a = o.AddCopy(1, -1, 1), b = o.AddCopy(6, 3, 5);

        // Act: left click
        EnumHandHandling left = EnumHandHandling.NotHandled;
        wand.OnHeldAttackStart(slot, player.Entity, new BlockSelection { Position = a, Face = BlockFacing.UP }, null, ref left);
        // Act: right click, the first event and a repeat of a held button
        EnumHandHandling right = EnumHandHandling.NotHandled;
        wand.OnHeldInteractStart(slot, player.Entity, new BlockSelection { Position = b, Face = BlockFacing.UP }, null, true, ref right);
        EnumHandHandling repeat = EnumHandHandling.NotHandled;
        wand.OnHeldInteractStart(slot, player.Entity, new BlockSelection { Position = a, Face = BlockFacing.UP }, null, false, ref repeat);

        // Assert
        Assert.Equal(EnumHandHandling.PreventDefault, left);
        Assert.Equal(EnumHandHandling.PreventDefault, right);
        PlotSelection? sel = Sel(player);
        Assert.NotNull(sel);
        Assert.Equal(a, sel!.Corner1);
        Assert.Equal(b, sel.Corner2);
        Assert.Equal(BlockBox.FromCorners(a, b), RawBox(player));

        // Act: shift + left click
        player.Entity.Controls.ShiftKey = true;
        try
        {
            EnumHandHandling clear = EnumHandHandling.NotHandled;
            wand.OnHeldAttackStart(slot, player.Entity, new BlockSelection { Position = a, Face = BlockFacing.UP }, null, ref clear);
        }
        finally
        {
            player.Entity.Controls.ShiftKey = false;
        }

        // Assert
        Assert.Null(Sel(player));
    }

    // ---- D3: the highlight --------------------------------------------------------------------

    // D3: with the wand in hand a selection inside the own territory is drawn in SELECTION_COLOR_OK,
    // one in the wild in SELECTION_COLOR_BAD, a single corner in SELECTION_COLOR_CORNER
    [AtlasScenario]
    public async Task Highlight_Should_ColorTheSelectionByWhetherItCanBeClaimed()
    {
        // Arrange: a city without plots, the mayor holding the wand
        CityScene s = await GivenCity();
        await s.Mayor.GiveItem(WandCode);
        BlockBox inside = PlotBox(s.Area);
        // Ten chunks along Z: free land, far from any city.
        BlockBox wild = new(inside.MinX, inside.MinY, inside.MinZ + 160, inside.MaxX, inside.MaxY, inside.MaxZ + 160);
        var c = Mod.config;

        // Act + Assert: inside the territory
        Select(s.Mayor, inside);
        await World.Until(() => Highlights(s.Mayor, inside, HighlightColor(c.SELECTION_COLOR_OK)),
            "the selection inside the territory to be drawn in SELECTION_COLOR_OK", 40);

        // Act + Assert: in the wild
        Select(s.Mayor, wild);
        await World.Until(() => Highlights(s.Mayor, wild, HighlightColor(c.SELECTION_COLOR_BAD)),
            "the selection in the wild to be drawn in SELECTION_COLOR_BAD", 40);

        // Act + Assert: one corner only
        Mod.selections.Clear(s.Mayor.Player);
        var corner = new BlockPos(inside.MinX, inside.MinY, inside.MinZ);
        Mod.selections.SetCorner(s.Mayor.Player, 1, corner);
        await World.Until(() => Highlights(s.Mayor, BlockBox.FromCorners(corner, corner), HighlightColor(c.SELECTION_COLOR_CORNER)),
            "the single corner to be drawn in SELECTION_COLOR_CORNER", 40);
    }

    // ---- D4: /city sel ------------------------------------------------------------------------

    // D4: pos1 and pos2 take the block the player aims at
    [AtlasScenario]
    public async Task Pos1AndPos2_Should_TakeTheAimedBlock()
    {
        // Arrange
        var (area, player) = await GivenLoner();
        BlockPos o = AreaOrigin(area);
        BlockPos a = o.AddCopy(2, -1, 2), b = o.AddCopy(-3, -1, -4);

        // Act: LookAt sets the aim for the command run right after it, on the same tick
        player.LookAt(a);
        CommandResult pos1 = await player.ExecuteCommand($"{SelCmd} pos1");
        player.LookAt(b);
        CommandResult pos2 = await player.ExecuteCommand($"{SelCmd} pos2");

        // Assert
        Assert.True(pos1.Ok, "/city sel pos1: " + pos1.Message);
        Assert.True(pos2.Ok, "/city sel pos2: " + pos2.Message);
        PlotSelection? sel = Sel(player);
        Assert.NotNull(sel);
        Assert.Equal(new[] { a.X, a.Y, a.Z }, new[] { sel!.Corner1.X, sel.Corner1.Y, sel.Corner1.Z });
        Assert.Equal(new[] { b.X, b.Y, b.Z }, new[] { sel.Corner2.X, sel.Corner2.Y, sel.Corner2.Z });
    }

    // D4 (the noted peculiarity): pos1/pos2 do not check SELECTION_MAX_DIM, only grow does
    [AtlasScenario]
    public async Task Corners_Should_NotCheckSelectionMaxDim()
    {
        // Arrange
        var (area, player) = await GivenLoner();
        Mod.config.SELECTION_MAX_DIM = 20;
        BlockPos o = AreaOrigin(area);
        BlockPos far = o.AddCopy(60, -1, 0);

        // Act
        player.LookAt(o.DownCopy());
        await player.ExecuteCommand($"{SelCmd} pos1");
        player.LookAt(far);
        CommandResult pos2 = await player.ExecuteCommand($"{SelCmd} pos2");

        // Assert
        Assert.True(pos2.Ok, "/city sel pos2 far away: " + pos2.Message);
        Assert.True(RawBox(player).SizeX > 20, $"the selection is {RawBox(player).SizeX} wide, the corner was cut to the limit");
    }

    // D4: grow and shrink move one face by the given blocks; a shrink stops at the opposite face
    [AtlasScenario]
    public async Task GrowAndShrink_Should_MoveOneFace()
    {
        // Arrange: a 6 x 8 x 6 selection
        var (area, player) = await GivenLoner();
        BlockBox box = PlotBox(area);
        Select(player, box);
        Assert.Equal(box, RawBox(player));

        // Act
        CommandResult up = await player.ExecuteCommand($"{SelCmd} grow up 2");
        BlockBox grown = RawBox(player);
        CommandResult north = await player.ExecuteCommand($"{SelCmd} shrink north 1");
        BlockBox shrunk = RawBox(player);
        CommandResult west = await player.ExecuteCommand($"{SelCmd} grow west 3");
        BlockBox westward = RawBox(player);
        CommandResult past = await player.ExecuteCommand($"{SelCmd} shrink east 100");
        BlockBox slab = RawBox(player);

        // Assert
        Assert.True(up.Ok, "/city sel grow up 2: " + up.Message);
        Assert.Equal(new BlockBox(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY + 2, box.MaxZ), grown);
        Assert.True(north.Ok, "/city sel shrink north 1: " + north.Message);
        Assert.Equal(new BlockBox(box.MinX, box.MinY, box.MinZ + 1, box.MaxX, box.MaxY + 2, box.MaxZ), shrunk);
        Assert.True(west.Ok, "/city sel grow west 3: " + west.Message);
        Assert.Equal(new BlockBox(box.MinX - 3, box.MinY, box.MinZ + 1, box.MaxX, box.MaxY + 2, box.MaxZ), westward);
        Assert.True(past.Ok, "/city sel shrink east 100: " + past.Message);
        Assert.Equal(new BlockBox(box.MinX - 3, box.MinY, box.MinZ + 1, box.MinX - 2, box.MaxY + 2, box.MaxZ), slab);
    }

    // D4: grow up to SELECTION_MAX_DIM goes through, one block more is selection_too_long and undone
    [AtlasScenario]
    public async Task Grow_Should_BeRefused_When_SelectionGetsTooLong()
    {
        // Arrange: 6 wide, limit 20
        var (area, player) = await GivenLoner();
        Mod.config.SELECTION_MAX_DIM = 20;
        BlockBox box = PlotBox(area);
        Select(player, box);

        // Act
        CommandResult toLimit = await player.ExecuteCommand($"{SelCmd} grow east {20 - box.SizeX}");
        BlockBox atLimit = RawBox(player);
        CommandResult over = await player.ExecuteCommand($"{SelCmd} grow east 1");

        // Assert
        Assert.True(toLimit.Ok, "growing to exactly SELECTION_MAX_DIM was refused: " + toLimit.Message);
        Assert.Equal(20, atLimit.SizeX);
        Assert.False(over.Ok, "the selection grew past SELECTION_MAX_DIM: " + over.Message);
        Assert.Equal("canclaimsextended:selection_too_long", over.Raw.StatusMessage);
        Assert.Equal("20", Convert.ToString(Assert.Single(over.Raw.MessageParams)));
        Assert.Equal(atLimit, RawBox(player));
    }

    // D4: grow and shrink without a full selection are selection_needed
    [AtlasScenario]
    public async Task Grow_Should_BeRefused_When_SelectionIsMissingOrHalf()
    {
        // Arrange
        var (area, player) = await GivenLoner();
        Mod.selections.Clear(player.Player);

        // Act
        CommandResult none = await player.ExecuteCommand($"{SelCmd} grow up 2");
        Mod.selections.SetCorner(player.Player, 1, AreaOrigin(area));
        CommandResult half = await player.ExecuteCommand($"{SelCmd} shrink up 1");

        // Assert
        Assert.False(none.Ok, "grow without a selection went through: " + none.Message);
        Assert.Equal("canclaimsextended:selection_needed", none.Raw.StatusMessage);
        Assert.False(half.Ok, "shrink with one corner went through: " + half.Message);
        Assert.Equal("canclaimsextended:selection_needed", half.Raw.StatusMessage);
        PlotSelection? sel = Sel(player);
        Assert.True(sel != null && sel.Corner2 == null && AreaOrigin(area).Equals(sel.Corner1), "the half selection changed");
    }

    // D4: a negative or zero amount is selection_bad_direction and changes nothing
    [AtlasScenario]
    public async Task Grow_Should_BeRefused_When_AmountIsNotPositive()
    {
        var (area, player) = await GivenLoner();
        BlockBox box = PlotBox(area);
        Select(player, box);

        CommandResult zero = await player.ExecuteCommand($"{SelCmd} grow up 0");

        Assert.False(zero.Ok, "grow by 0 went through: " + zero.Message);
        Assert.Equal("canclaimsextended:selection_bad_direction", zero.Raw.StatusMessage);
        Assert.Equal(box, RawBox(player));
    }

    // D4: show and hide switch AlwaysShow; clear forgets the selection and says so
    [AtlasScenario]
    public async Task ShowHideAndClear_Should_ChangeTheSelection()
    {
        // Arrange
        var (area, player) = await GivenLoner();
        Select(player, PlotBox(area));
        player.Client.Clear();

        // Act + Assert: show, hide
        CommandResult show = await player.ExecuteCommand($"{SelCmd} show");
        Assert.True(show.Ok, "/city sel show: " + show.Message);
        Assert.Equal("canclaimsextended:selection_shown", show.Raw.StatusMessage);
        Assert.True(Sel(player)!.AlwaysShow, "show did not set AlwaysShow");
        CommandResult hide = await player.ExecuteCommand($"{SelCmd} hide");
        Assert.True(hide.Ok, "/city sel hide: " + hide.Message);
        Assert.Equal("canclaimsextended:selection_hidden", hide.Raw.StatusMessage);
        Assert.False(Sel(player)!.AlwaysShow, "hide did not clear AlwaysShow");

        // Act + Assert: clear
        CommandResult clear = await player.ExecuteCommand($"{SelCmd} clear");
        Assert.True(clear.Ok, "/city sel clear: " + clear.Message);
        Assert.Null(Sel(player));
        string cleared = L(player, "selection_cleared");
        await World.Until(() => player.Client.ChatLines().Any(l => l.Contains(cleared)), "the selection_cleared line", 20);
    }

    // D4: /city sel tells an empty selection, then the size and that the box can be claimed
    [AtlasScenario]
    public async Task Info_Should_DescribeTheSelection()
    {
        // Arrange
        CityScene s = await GivenCity();
        Mod.selections.Clear(s.Mayor.Player);
        BlockBox box = PlotBox(s.Area);

        // Act
        CommandResult empty = await s.Mayor.ExecuteCommand(SelCmd);
        Select(s.Mayor, box);
        CommandResult full = await s.Mayor.ExecuteCommand(SelCmd);

        // Assert
        Assert.True(empty.Ok, "/city sel: " + empty.Message);
        Assert.Equal(L(s.Mayor, "selection_empty", Mod.config.COMMAND_CITY), empty.Raw.StatusMessage);
        Assert.True(full.Ok, "/city sel: " + full.Message);
        Assert.Contains(L(s.Mayor, "selection_size", box.SizeX, box.SizeY, box.SizeZ, box.Volume), full.Raw.StatusMessage);
        Assert.Contains(L(s.Mayor, "selection_can_claim", Mod.config.COMMAND_PLOT), full.Raw.StatusMessage);
    }

    // D3, D4: a player of no city is told a selection needs a city
    [AtlasScenario]
    public async Task Info_Should_SayACityIsNeeded_When_PlayerHasNone()
    {
        var (area, player) = await GivenLoner();
        Select(player, PlotBox(area));

        CommandResult info = await player.ExecuteCommand(SelCmd);

        Assert.True(info.Ok, "/city sel: " + info.Message);
        string needsCity = L(player, "selection_needs_city", Mod.config.COMMAND_CITY);
        Assert.Contains(L(player, "selection_cannot_claim", needsCity), info.Raw.StatusMessage);
        Assert.DoesNotContain(L(player, "selection_can_claim", Mod.config.COMMAND_PLOT), info.Raw.StatusMessage);
    }
}
