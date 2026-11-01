using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Boats shared with the owner's city. Both sides read stamps in WatchedAttributes: the boat
    /// carries its mode and the owner's city, the player carries their own city and what the server
    /// allows - the client has no config and no other cities.
    /// </summary>
    public static class BoatShare
    {
        // On the boat
        public const string ModeAttr = "cxShareMode";
        public const string SharedCityGuidAttr = "cxSharedCityGuid";
        public const string SharedCityNameAttr = "cxSharedCityName";
        // On the player
        public const string PlayerCityAttr = "cxCityGuid";
        /// <summary>What the server allows: 0 nothing, 1 city.</summary>
        public const string AllowedAttr = "cxBoatShareAllowed";
        /// <summary>The city command root as registered (with an "x" next to claims), for the boat window.</summary>
        public const string CityCommandAttr = "cxCityCommand";

        /// <summary>How far from a boat its sharing may still be changed, in blocks.</summary>
        const double Reach = 12;

        /// <summary>
        /// Mode a boat carries. Absent means PERSONAL: a boat is shared only when its owner says so.
        /// A boat once shared with the alliance (2) counts as shared with the city.
        /// </summary>
        public static BoatShareMode Of(Entity entity)
        {
            if (entity == null) return BoatShareMode.PERSONAL;
            int stored = entity.WatchedAttributes.GetInt(ModeAttr, (int)BoatShareMode.PERSONAL);
            return stored >= (int)BoatShareMode.CITY ? BoatShareMode.CITY : BoatShareMode.PERSONAL;
        }

        /// <summary>The mode as applied: personal when the server forbids sharing.</summary>
        public static BoatShareMode Effective(BoatShareMode stored, int allowed)
        {
            return allowed <= 0 ? BoatShareMode.PERSONAL : stored;
        }

        /// <summary>Server side: the level the config allows.</summary>
        public static int AllowedOnServer()
        {
            Config c = canclaimsextended.config;
            return c != null && c.BOAT_SHARE_WITH_CITY ? 1 : 0;
        }

        public static string CodeOf(BoatShareMode mode)
        {
            switch (mode)
            {
                case BoatShareMode.CITY: return "city";
                default: return "personal";
            }
        }

        public static string LangKeyOf(BoatShareMode mode)
        {
            return "canclaimsextended:boat-share-mode-" + CodeOf(mode);
        }

        /*==============================================================================================*/
        /*=====================================SERVER===================================================*/
        /*==============================================================================================*/
        public static void Init(ICoreServerAPI sapi)
        {
            DataStorage.PlayerRightsChanged -= OnRightsChanged;
            DataStorage.PlayerRightsChanged += OnRightsChanged;
            sapi.Event.PlayerNowPlaying += StampPlayer;
        }

        static void OnRightsChanged(string uid)
        {
            if (canclaimsextended.sapi?.World.PlayerByUid(uid) is IServerPlayer player)
            {
                StampPlayer(player);
            }
        }

        /// <summary>The player's city as the client may compare it with a boat's stamp.</summary>
        public static void StampPlayer(IServerPlayer player)
        {
            if (player?.Entity == null || canclaimsextended.dataStorage == null) return;
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo info);
            var wa = player.Entity.WatchedAttributes;
            SetIfChanged(wa, PlayerCityAttr, info?.City?.Guid ?? "");
            int allowed = AllowedOnServer();
            if (wa.GetInt(AllowedAttr, -1) != allowed) wa.SetInt(AllowedAttr, allowed);
            SetIfChanged(wa, CityCommandAttr, canclaimsextended.config?.COMMAND_CITY ?? "city");
        }

        /// <summary>
        /// Writes the owner's city onto the boat, or clears it when sharing is off, the boat is
        /// personal or the owner has no city. Written only on change: every write is a network sync.
        /// </summary>
        public static void StampBoat(Entity boat)
        {
            var wa = boat.WatchedAttributes;
            string cityGuid = "", cityName = "";
            var ownedby = wa.GetTreeAttribute("ownedby");
            BoatShareMode mode = Effective(Of(boat), AllowedOnServer());

            if (ownedby != null && mode != BoatShareMode.PERSONAL && canclaimsextended.dataStorage != null
                && canclaimsextended.dataStorage.GetPlayerByUid(ownedby.GetString("uid", ""), out PlayerInfo owner)
                && owner.City != null)
            {
                cityGuid = owner.City.Guid;
                cityName = owner.City.GetPartName();
            }
            SetIfChanged(wa, SharedCityGuidAttr, cityGuid);
            SetIfChanged(wa, SharedCityNameAttr, cityName);
        }

        static void SetIfChanged(Vintagestory.API.Datastructures.SyncedTreeAttribute wa, string key, string value)
        {
            if (wa.GetString(key, "") != value) wa.SetString(key, value);
        }

        /// <summary>
        /// /city boatshare &lt;entity id&gt; &lt;personal|city&gt;: sent by the boat dialog.
        /// Registered with the other /city commands (CityCommandsRegistrar.RegisterRunGame).
        /// </summary>
        public static TextCommandResult SetMode(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player || player.Entity == null)
            {
                return TextCommandResult.Error("canclaimsextended:no_such_player_info");
            }
            int allowed = AllowedOnServer();
            if (allowed <= 0)
            {
                return TextCommandResult.Error("canclaimsextended:boat-sharing-disabled");
            }
            if (!long.TryParse((string)args[0], out long entityId))
            {
                return TextCommandResult.Error("canclaimsextended:boat-look-at-one");
            }
            BoatShareMode mode = (string)args[1] == "city" ? BoatShareMode.CITY : BoatShareMode.PERSONAL;

            Entity boat = canclaimsextended.sapi.World.GetEntityById(entityId);
            if (boat?.GetBehavior<EntityBehaviorOwnable>() == null || player.Entity.Pos.DistanceTo(boat.Pos.XYZ) > Reach)
            {
                return TextCommandResult.Error("canclaimsextended:boat-look-at-one");
            }
            var ownedby = boat.WatchedAttributes.GetTreeAttribute("ownedby");
            if (ownedby == null || ownedby.GetString("uid", "") != player.PlayerUID)
            {
                return TextCommandResult.Error("canclaimsextended:boat-not-yours");
            }

            boat.WatchedAttributes.SetInt(ModeAttr, (int)mode);
            StampBoat(boat);
            return TextCommandResult.Success(Lang.Get("canclaimsextended:boat-share-set", Lang.Get(LangKeyOf(mode))));
        }
    }
}
