using canclaimsextended.src.items;
using canclaimsextended.src.part;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    /// <summary>The game's block use and break events; the rights themselves are in <see cref="AccessRules"/>.</summary>
    public static class OnBlockAction
    {
        const string CLAIMANT = "canclaimsextended";

        public static bool Event_OnBlockUse(IServerPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return true;
            }
            if (AlwaysAccessBlocks.Contains(canclaimsextended.sapi.World.BlockAccessor.GetBlock(blockSel.Position)))
            {
                return true;
            }
            return AccessRules.Check(byPlayer, blockSel.Position, PermType.USE_PERM);
        }

        public static bool Event_OnBlockDestroy(IServerPlayer byPlayer, BlockSelection blockSel, out string claimant)
        {
            claimant = "";
            // The wand only selects. The client never starts breaking with it; this covers the rest.
            if (byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemClaimWand)
            {
                claimant = CLAIMANT;
                return false;
            }
            if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return true;
            }
            if (!AccessRules.Check(byPlayer, blockSel.Position, PermType.BUILD_AND_DESTROY_PERM))
            {
                claimant = CLAIMANT;
                return false;
            }
            return true;
        }
    }
}
