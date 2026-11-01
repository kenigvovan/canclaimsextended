using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Mod = global::canclaimsextended.src.canclaimsextended;

namespace canclaimsextended.scenarios;

/// <summary>
/// Section N of TESTING_canclaimsextended.md: boats shared with the owner's city.
/// /city boatshare sets the mode and stamps the owner's city on the boat (N2), its gates (N4),
/// who counts as the owner on the server (BoatSharePatches.Postfix_Ownable_IsOwner, N2), the stamp
/// following an owner who changed cities, and the stamp on the player (cxCityGuid,
/// cxBoatShareAllowed) that the client compares the boat's with.
///
/// The client half (the Ctrl+B dialog, the tooltip, the client-side IsOwner on the helm) has no
/// client process here and is not covered. An unset boat (N1) is in DefaultsScenarios, a boat once
/// shared with the alliance (N3, S4) in RemovedFeaturesScenarios.
///
/// Vanilla 1.22.3 lists no "ownable" behavior for boat-raft/boat-sailed in its assets; when the
/// spawned raft has none, the scenario adds EntityBehaviorOwnable by hand, so the mod's command
/// and its harmony postfix have the behavior they look for.
/// </summary>
[Trait("Category", "E2E")]
public class BoatShareScenarios : CxScenarioBase
{
    private const string Raft = "game:boat-raft-aged";
    private const string OwnedBy = "ownedby";

    protected override void ResetConfig()
    {
        base.ResetConfig();
        Mod.config.BOAT_SHARE_WITH_CITY = true;
    }

    /// <summary>A refusal with exactly this lang key (the raw status message, before translation).</summary>
    private static void AssertRefused(CommandResult result, string key, string what)
    {
        Assert.False(result.Ok, $"{what} was not refused: {result.Message}");
        Assert.Equal("canclaimsextended:" + key, result.Raw.StatusMessage);
    }

    private static string BoatShareCmd(Entity boat, string mode) => $"{CityCmd} boatshare {boat.EntityId} {mode}";

    private static string Stamp(Entity boat) => boat.WatchedAttributes.GetString(BoatShare.SharedCityGuidAttr, "");
    private static string StampName(Entity boat) => boat.WatchedAttributes.GetString(BoatShare.SharedCityNameAttr, "");
    private static string PlayerStamp(ITestPlayer p) => p.Entity.WatchedAttributes.GetString(BoatShare.PlayerCityAttr, "");

    /// <summary>The boat's ownable behavior; added when the entity has none (see the class summary).</summary>
    private static EntityBehaviorOwnable Ownable(Entity boat)
    {
        EntityBehaviorOwnable? ownable = boat.GetBehavior<EntityBehaviorOwnable>();
        if (ownable == null)
        {
            ownable = new EntityBehaviorOwnable(boat);
            boat.AddBehavior(ownable);
        }
        return ownable;
    }

    /// <summary>What vanilla writes when a player claims the boat: ownedby { uid, name }.</summary>
    private static void SetOwner(Entity boat, ITestPlayer owner)
    {
        Ownable(boat);
        var tree = boat.WatchedAttributes.GetOrAddTreeAttribute(OwnedBy);
        tree.SetString("uid", owner.Player.PlayerUID);
        tree.SetString("name", Name(owner));
    }

    /// <summary>Whether the server counts the player as the boat's owner: the vanilla check plus the mod's postfix.</summary>
    private static bool CountsAsOwner(Entity boat, ITestPlayer player) => Ownable(boat).IsOwner(player.Entity);

    /// <summary>
    /// A raft owned by <paramref name="owner"/> next to them in the area; the owner stands at it
    /// (well inside the 12 blocks of BoatShare.Reach). The boat is removed after the body, on the
    /// game thread (Dispose is not on it).
    /// </summary>
    private async Task WithOwnedBoat(int area, ITestPlayer owner, System.Func<Entity, Task> body)
    {
        BlockPos at = AreaOrigin(area).Offset(2, 1, 0);
        Entity boat;
        try
        {
            boat = World.SpawnEntity(Raft, at);
        }
        catch (ArgumentException)
        {
            boat = World.SpawnEntity("game:strawdummy", at);
        }

        try
        {
            await World.Ticks(2);
            SetOwner(boat, owner);
            await owner.TeleportTo(boat.Pos.AsBlockPos);
            await body(boat);
        }
        finally
        {
            boat.Die(EnumDespawnReason.Removed);
        }
    }

    // ---- N2: the command -------------------------------------------------------------------

    // N2: "city" marks the boat and stamps the owner's city; "personal" takes both back
    [AtlasScenario]
    public async Task BoatShare_Should_StampCity_When_City_And_ClearIt_When_Personal()
    {
        CityScene s = await GivenCity("skipper");
        ITestPlayer skipper = s.Citizens[0];

        await WithOwnedBoat(s.Area, skipper, async boat =>
        {
            // Act
            CommandResult city = await skipper.ExecuteCommand(BoatShareCmd(boat, "city"));

            // Assert
            Assert.True(city.Ok, "/city boatshare ... city: " + city.Message);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, $"the boat is {BoatShare.Of(boat)}, expected CITY");
            Assert.Equal(s.City.Guid, Stamp(boat));
            Assert.Equal(s.City.GetPartName(), StampName(boat));

            // Act
            CommandResult personal = await skipper.ExecuteCommand(BoatShareCmd(boat, "personal"));

            // Assert
            Assert.True(personal.Ok, "/city boatshare ... personal: " + personal.Message);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.PERSONAL, $"the boat is {BoatShare.Of(boat)}, expected PERSONAL");
            Assert.Equal("", Stamp(boat));
            Assert.Equal("", StampName(boat));
        });
    }

    // N2: an owner without a city may set "city", but there is no city to stamp
    [AtlasScenario]
    public async Task BoatShare_Should_StampNothing_When_OwnerHasNoCity()
    {
        CityScene s = await GivenCity();
        ITestPlayer loner = await GivenStranger(s, "loner");

        await WithOwnedBoat(s.Area, loner, async boat =>
        {
            CommandResult city = await loner.ExecuteCommand(BoatShareCmd(boat, "city"));

            Assert.True(city.Ok, "/city boatshare ... city without a city: " + city.Message);
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, $"the boat is {BoatShare.Of(boat)}, expected CITY");
            Assert.Equal("", Stamp(boat));
        });
    }

    // ---- N4: the gates ---------------------------------------------------------------------

    // N4: BOAT_SHARE_WITH_CITY off refuses the command and every stored mode reads as personal
    [AtlasScenario]
    public async Task BoatShare_Should_BeRefused_And_ShareNothing_When_SharingIsDisabled()
    {
        // Arrange: a boat shared while sharing was on
        CityScene s = await GivenCity("rower");
        ITestPlayer rower = s.Citizens[0];
        bool before = Mod.config.BOAT_SHARE_WITH_CITY;
        AddCleanup(() => Mod.config.BOAT_SHARE_WITH_CITY = before);

        await WithOwnedBoat(s.Area, s.Mayor, async boat =>
        {
            CommandResult shared = await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "city"));
            Assert.True(shared.Ok && CountsAsOwner(boat, rower), "precondition: the boat is shared with the city: " + shared.Message);

            // Act
            Mod.config.BOAT_SHARE_WITH_CITY = false;
            CommandResult personal = await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "personal"));

            // Assert
            AssertRefused(personal, "boat-sharing-disabled", "/city boatshare with sharing off");
            Assert.Equal(0, BoatShare.AllowedOnServer());
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, "a refused command changed the stored mode");
            Assert.True(BoatShare.Effective(BoatShare.Of(boat), BoatShare.AllowedOnServer()) == BoatShareMode.PERSONAL,
                "with sharing off a city boat is not applied as personal");
            Assert.False(CountsAsOwner(boat, rower), "a citizen steers a city boat with sharing off");
            Assert.True(CountsAsOwner(boat, s.Mayor), "the owner lost his own boat");
            BoatShare.StampBoat(boat);
            Assert.Equal("", Stamp(boat));
        });
    }

    // N4: somebody else's boat
    [AtlasScenario]
    public async Task BoatShare_Should_BeRefused_When_BoatIsNotYours()
    {
        CityScene s = await GivenCity("pirate");
        ITestPlayer pirate = s.Citizens[0];

        await WithOwnedBoat(s.Area, s.Mayor, async boat =>
        {
            await pirate.TeleportTo(boat.Pos.AsBlockPos);

            CommandResult result = await pirate.ExecuteCommand(BoatShareCmd(boat, "city"));

            AssertRefused(result, "boat-not-yours", "a citizen sharing the mayor's boat");
            Assert.True(BoatShare.Of(boat) == BoatShareMode.PERSONAL, "the boat was shared by someone who does not own it");
            Assert.Equal("", Stamp(boat));
        });
    }

    // N4: farther than 12 blocks from the boat
    [AtlasScenario]
    public async Task BoatShare_Should_BeRefused_When_OwnerIsTooFar()
    {
        CityScene s = await GivenCity();

        await WithOwnedBoat(s.Area, s.Mayor, async boat =>
        {
            await s.Mayor.TeleportTo(boat.Pos.AsBlockPos.AddCopy(30, 0, 0));

            CommandResult result = await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "city"));

            AssertRefused(result, "boat-look-at-one", "/city boatshare 30 blocks away");
            Assert.True(BoatShare.Of(boat) == BoatShareMode.PERSONAL, "the boat was shared from 30 blocks away");
        });
    }

    // N4: an id that is no number (an unknown number is in RemovedFeaturesScenarios)
    [AtlasScenario]
    public async Task BoatShare_Should_BeRefused_When_IdIsNoNumber()
    {
        CityScene s = await GivenCity();

        CommandResult result = await s.Mayor.ExecuteCommand($"{CityCmd} boatshare boat city");

        AssertRefused(result, "boat-look-at-one", "/city boatshare with a word for an id");
    }

    // N4: an entity that cannot be owned is no boat
    [AtlasScenario]
    public async Task BoatShare_Should_BeRefused_When_EntityIsNotOwnable()
    {
        CityScene s = await GivenCity();
        Entity dummy = World.SpawnEntity("game:strawdummy", AreaOrigin(s.Area).Offset(2, 1, 0));
        try
        {
            await World.Ticks(2);
            Assert.Null(dummy.GetBehavior<EntityBehaviorOwnable>());

            CommandResult result = await s.Mayor.ExecuteCommand(BoatShareCmd(dummy, "city"));

            AssertRefused(result, "boat-look-at-one", "/city boatshare on a straw dummy");
            Assert.True(BoatShare.Of(dummy) == BoatShareMode.PERSONAL, "a straw dummy got a share mode");
        }
        finally
        {
            dummy.Die(EnumDespawnReason.Removed);
        }
    }

    // ---- N2: who may use a shared boat -----------------------------------------------------

    // N2: a city boat - a fellow citizen counts as its owner, a stranger does not
    [AtlasScenario]
    public async Task CityBoat_Should_LetCitizenIn_And_KeepStrangerOut()
    {
        // Arrange
        CityScene s = await GivenCity("crew");
        ITestPlayer crew = s.Citizens[0];
        ITestPlayer stranger = await GivenStranger(s);

        await WithOwnedBoat(s.Area, s.Mayor, async boat =>
        {
            CommandResult set = await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "city"));
            Assert.True(set.Ok, "precondition: /city boatshare ... city: " + set.Message);

            // Act, Assert
            Assert.True(CountsAsOwner(boat, s.Mayor), "the owner is not the owner");
            Assert.True(CountsAsOwner(boat, crew), "a citizen may not use the boat shared with his city");
            Assert.False(CountsAsOwner(boat, stranger), "a stranger may use a boat shared with a city");
        });
    }

    // N1, N2: a personal boat is the owner's alone, citizens included
    [AtlasScenario]
    public async Task PersonalBoat_Should_KeepCitizenOut()
    {
        CityScene s = await GivenCity("deckhand");
        ITestPlayer deckhand = s.Citizens[0];

        await WithOwnedBoat(s.Area, s.Mayor, async boat =>
        {
            // Arrange: shared, then personal again
            await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "city"));
            Assert.True(CountsAsOwner(boat, deckhand), "precondition: the citizen may use the city boat");

            // Act
            CommandResult personal = await s.Mayor.ExecuteCommand(BoatShareCmd(boat, "personal"));

            // Assert
            Assert.True(personal.Ok, "/city boatshare ... personal: " + personal.Message);
            Assert.False(CountsAsOwner(boat, deckhand), "a citizen may still use a personal boat");
            Assert.True(CountsAsOwner(boat, s.Mayor), "the owner lost his personal boat");
        });
    }

    // ---- the stamp follows the owner -------------------------------------------------------

    // The owner moved to another city: his old fellow citizens are out, and the next ownership
    // check on the server writes the new city onto the boat
    [AtlasScenario]
    public async Task CityBoat_Should_FollowOwner_When_OwnerMovesToAnotherCity()
    {
        // Arrange: the skipper of the first city shares his boat, then moves to a second city
        CityScene first = await GivenCity("mover");
        ITestPlayer mover = first.Citizens[0];
        await WithOwnedBoat(first.Area, mover, async boat =>
        {
            CommandResult set = await mover.ExecuteCommand(BoatShareCmd(boat, "city"));
            Assert.True(set.Ok && Stamp(boat) == first.City.Guid, "precondition: the boat carries the first city: " + set.Message);
            CityScene second = await GivenCity();
            CommandResult leave = await mover.ExecuteCommand($"{CityCmd} leave");
            Assert.True(leave.Ok, "precondition: /city leave: " + leave.Message);
            await AddCitizen(second.City, second.Mayor, mover);

            // Act
            bool oldMayor = CountsAsOwner(boat, first.Mayor);
            bool newMayor = CountsAsOwner(boat, second.Mayor);

            // Assert
            Assert.False(oldMayor, "the mayor of the city the owner left may still use the boat");
            Assert.True(newMayor, "the mayor of the owner's new city may not use the boat");
            Assert.Equal(second.City.Guid, Stamp(boat));
            Assert.Equal(second.City.GetPartName(), StampName(boat));
            Assert.True(BoatShare.Of(boat) == BoatShareMode.CITY, "moving cities changed the boat's mode");
        });
        AssertInvariants();
    }

    // The owner left his city for none: no stale stamp may stay behind, or the client of a former
    // fellow citizen still offers the helm (it compares the stamp with its own cxCityGuid).
    // SUSPECTED BUG: Postfix_Ownable_IsOwner returns before StampBoat when the owner (or the caller)
    // has no city, so nothing on the server refreshes the stamp until the boat is loaded again.
    [AtlasScenario]
    public async Task CityBoat_Should_DropTheStamp_When_OwnerLeavesForNoCity()
    {
        // Arrange
        CityScene s = await GivenCity("quitter");
        ITestPlayer quitter = s.Citizens[0];
        await WithOwnedBoat(s.Area, quitter, async boat =>
        {
            CommandResult set = await quitter.ExecuteCommand(BoatShareCmd(boat, "city"));
            Assert.True(set.Ok && Stamp(boat) == s.City.Guid, "precondition: the boat carries the city: " + set.Message);

            // Act: the owner leaves, a former fellow citizen tries the boat
            CommandResult leave = await quitter.ExecuteCommand($"{CityCmd} leave");
            Assert.True(leave.Ok, "precondition: /city leave: " + leave.Message);
            bool mayor = CountsAsOwner(boat, s.Mayor);

            // Assert
            Assert.False(mayor, "the mayor may still use the boat of a player who left the city");
            Assert.True(CountsAsOwner(boat, quitter), "the owner lost his boat by leaving the city");
            Assert.True(Stamp(boat) == "",
                $"the boat still carries the city the owner left ({Stamp(boat)}); a former citizen's client would offer the helm");
        });
    }

    // ---- the player's stamp ----------------------------------------------------------------

    // The player carries his city and what the server allows; joining and leaving rewrite it
    [AtlasScenario]
    public async Task PlayerStamp_Should_FollowJoinAndLeave()
    {
        // Arrange: a player of no city
        CityScene s = await GivenCity();
        ITestPlayer sailor = await GivenStranger(s, "sailor");
        Assert.Equal("", PlayerStamp(sailor));
        Assert.Equal(1, sailor.Entity.WatchedAttributes.GetInt(BoatShare.AllowedAttr, -1));
        Assert.Equal(Mod.config.COMMAND_CITY, sailor.Entity.WatchedAttributes.GetString(BoatShare.CityCommandAttr, ""));

        // Act, Assert: join
        await AddCitizen(s.City, s.Mayor, sailor);
        Assert.Equal(s.City.Guid, PlayerStamp(sailor));
        Assert.Equal(s.City.Guid, PlayerStamp(s.Mayor));

        // Act, Assert: leave
        CommandResult leave = await sailor.ExecuteCommand($"{CityCmd} leave");
        Assert.True(leave.Ok, "/city leave: " + leave.Message);
        Assert.Equal("", PlayerStamp(sailor));
        Assert.Equal(s.City.Guid, PlayerStamp(s.Mayor));
        AssertInvariants();
    }

    // The player's stamp says 0 when the server forbids sharing, so the client shows no sharing
    [AtlasScenario]
    public async Task PlayerStamp_Should_SayNothingAllowed_When_SharingIsDisabled()
    {
        // Arrange
        CityScene s = await GivenCity("watcher");
        ITestPlayer watcher = s.Citizens[0];
        bool before = Mod.config.BOAT_SHARE_WITH_CITY;
        AddCleanup(() => Mod.config.BOAT_SHARE_WITH_CITY = before);
        Assert.Equal(1, watcher.Entity.WatchedAttributes.GetInt(BoatShare.AllowedAttr, -1));

        // Act: the stamp is written on login and on rights changes; StampPlayer is that write
        Mod.config.BOAT_SHARE_WITH_CITY = false;
        BoatShare.StampPlayer(watcher.Player);

        // Assert
        Assert.Equal(0, watcher.Entity.WatchedAttributes.GetInt(BoatShare.AllowedAttr, -1));
        Assert.Equal(s.City.Guid, PlayerStamp(watcher));
        await World.Ticks(1);
    }
}
