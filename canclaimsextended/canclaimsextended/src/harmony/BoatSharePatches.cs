using System.Text;
using canclaimsextended.src.part;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace canclaimsextended.src.harmony
{
    /// <summary>
    /// A fellow citizen of a boat's owner counts as its owner. Every vanilla ownership check - the
    /// helm, attached storage, roping, damage tolerance - goes through EntityBehaviorOwnable.IsOwner,
    /// so one postfix covers them. The server decides from live city data; the client compares the
    /// boat's stamp with its own player's (see BoatShare).
    /// </summary>
    public static class BoatSharePatches
    {
        public static void Postfix_Ownable_IsOwner(EntityBehaviorOwnable __instance, EntityAgent byEntity, ref bool __result)
        {
            // Vanilla already said yes: an unowned boat, or the caller is the owner.
            if (__result) return;
            if (byEntity is not EntityPlayer player) return;
            var wa = __instance.entity.WatchedAttributes;
            var ownedby = wa.GetTreeAttribute("ownedby");
            if (ownedby == null) return;

            if (__instance.entity.World.Side == EnumAppSide.Server)
            {
                BoatShareMode mode = BoatShare.Effective(BoatShare.Of(__instance.entity), BoatShare.AllowedOnServer());
                if (mode == BoatShareMode.PERSONAL || canclaimsextended.dataStorage == null) return;
                canclaimsextended.dataStorage.GetPlayerByUid(ownedby.GetString("uid", ""), out PlayerInfo owner);
                canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo caller);
                if (owner?.City == null || caller?.City == null) return;

                if (owner.City.Equals(caller.City))
                {
                    __result = true;
                }
                // Refreshed here too, so an owner who changed cities leaves no stale stamp.
                BoatShare.StampBoat(__instance.entity);
                return;
            }

            // The client checks the helm before asking the server; only its own player is judged.
            if (__instance.entity.Api is not ICoreClientAPI capi || player.PlayerUID != capi.World.Player?.PlayerUID) return;
            var own = player.WatchedAttributes;
            string ownCity = own.GetString(BoatShare.PlayerCityAttr, "");
            string boatCity = wa.GetString(BoatShare.SharedCityGuidAttr, "");
            if (boatCity.Length > 0 && boatCity == ownCity)
            {
                __result = true;
            }
        }

        /// <summary>Runs on entity load, so a boat carries its stamp before any client asks.</summary>
        public static void Postfix_Ownable_VerifyOwnership(EntityBehaviorOwnable __instance)
        {
            if (__instance.entity.World.Side != EnumAppSide.Server) return;
            BoatShare.StampBoat(__instance.entity);
        }

        /// <summary>Client: a sharing line in the boat's tooltip, under vanilla's "Owned by".</summary>
        public static void Postfix_Ownable_GetInfoText(EntityBehaviorOwnable __instance, StringBuilder infotext)
        {
            var wa = __instance.entity.WatchedAttributes;
            if (wa.GetTreeAttribute("ownedby") == null) return;

            string cityName = wa.GetString(BoatShare.SharedCityNameAttr, "");
            if (cityName.Length == 0)
            {
                // Owned and set to personal: said so, to tell it from an owner with no city -
                // unless the server has sharing off, where every boat is personal anyway.
                var self = (__instance.entity.Api as ICoreClientAPI)?.World.Player?.Entity;
                bool sharingOn = self != null && self.WatchedAttributes.GetInt(BoatShare.AllowedAttr, 0) > 0;
                if (sharingOn && BoatShare.Of(__instance.entity) == BoatShareMode.PERSONAL)
                {
                    infotext.AppendLine(Lang.Get("canclaimsextended:boat-shared-personal"));
                }
                return;
            }
            infotext.AppendLine(Lang.Get("canclaimsextended:boat-shared-city", cityName.Replace("_", " ")));
        }
    }
}
