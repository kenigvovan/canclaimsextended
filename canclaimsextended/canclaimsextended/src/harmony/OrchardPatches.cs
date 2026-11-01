using canclaimsextended.src.cropbehaviors;
using canclaimsextended.src.messages;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canclaimsextended.src.harmony
{
    public static class OrchardPatches
    {
        public static void Postfix_FruitTreeRootTick(FruitTreeRootBH __instance)
        {
            OrchardRules.StripFruitOutsideOrchard(__instance);
        }

        /// <summary>Warns at planting, so nobody waits a whole year to learn the tree will bear nothing.</summary>
        public static void Postfix_FruitTreeTryPlaceBlock(bool __result, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!__result || world?.Side != EnumAppSide.Server || blockSel?.Position == null) return;
            if (!canclaimsextended.config.FRUIT_ONLY_ON_ORCHARD_PLOTS) return;
            if (!OrchardRules.ShouldWarnOnPlanting(blockSel.Position)) return;
            if (byPlayer is IServerPlayer sp)
            {
                MessageHandler.sendMsgToPlayer(sp, Lang.Get("canclaimsextended:fruit_wont_grow_outside_orchard"));
            }
        }
    }
}
