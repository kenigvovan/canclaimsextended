using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace canclaimsextended.src.harmony
{
    public static class ApplyPatches
    {
        public const string HarmonyID = "canclaimsextended.Patches";

        /// <summary>
        /// Patches one target, skipping it with a warning when the method is gone after an engine
        /// update. Harmony throws on a null target, which would leave the rest unpatched.
        /// </summary>
        private static void TryPatch(Harmony harmony, MethodInfo target, string targetName,
            string prefix = null, string postfix = null, string finalizer = null, Type patchClass = null)
        {
            if (target == null)
            {
                canclaimsextended.sapi.Logger.Warning("[canclaimsextended] patch skipped: {0} not found (VS update?)", targetName);
                return;
            }
            patchClass ??= typeof(HarmonyPatches);
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(patchClass.GetMethod(prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(patchClass.GetMethod(postfix)),
                    finalizer: finalizer == null ? null : new HarmonyMethod(patchClass.GetMethod(finalizer)));
            }
            catch (Exception e)
            {
                canclaimsextended.sapi.Logger.Error("[canclaimsextended] patch of {0} failed, feature disabled: {1}", targetName, e);
            }
        }

        public static void ApplyServerPatches(Harmony harmony)
        {
            Config c = canclaimsextended.config;

            TryPatch(harmony, typeof(EntityAgent).GetMethod("ShouldReceiveDamage"),
                "EntityAgent.ShouldReceiveDamage", prefix: nameof(HarmonyPatches.Prefix_On_ReceiveDamage));

            if (c.FIRE_PROTECTION_PATCH)
            {
                TryPatch(harmony, typeof(BEBehaviorBurning).GetMethod("TrySpreadTo"),
                    "BEBehaviorBurning.TrySpreadTo", prefix: nameof(HarmonyPatches.Prefix_On_TrySpreadFireAllDirs));
                TryPatch(harmony, typeof(BEBehaviorBurning).GetMethod("OnFirePlaced",
                    new[] { typeof(BlockPos), typeof(BlockPos), typeof(string), typeof(bool) }),
                    "BEBehaviorBurning.OnFirePlaced", prefix: nameof(HarmonyPatches.Prefix_On_FirePlaced));
            }

            if (c.BLAST_PROTECTION_PATCH)
            {
                TryPatch(harmony, typeof(BlockEntityBomb).GetMethod("HasPermissionToUse"),
                    "BlockEntityBomb.HasPermissionToUse", prefix: nameof(HarmonyPatches.Prefix_HasPermissionToUse));
            }

            // Temple respawn: the flag is read inside, so it can be switched without a restart.
            TryPatch(harmony, typeof(ServerSystemEntitySimulation).GetMethod("OnPlayerRespawn", BindingFlags.NonPublic | BindingFlags.Instance),
                "ServerSystemEntitySimulation.OnPlayerRespawn", prefix: nameof(TemplePatches.Prefix_OnPlayerRespawn),
                finalizer: nameof(TemplePatches.Finalizer_OnPlayerRespawn), patchClass: typeof(TemplePatches));
            TryPatch(harmony, typeof(ServerPlayer).GetMethod("GetSpawnPosition", new[] { typeof(bool) }),
                "ServerPlayer.GetSpawnPosition", prefix: nameof(TemplePatches.Prefix_GetSpawnPosition), patchClass: typeof(TemplePatches));

            // Commands blocked in prison; the list is read inside, so it can change without a restart.
            TryPatch(harmony, typeof(Vintagestory.Common.ChatCommandApi).GetMethod("Execute",
                new[] { typeof(string), typeof(Vintagestory.API.Server.IServerPlayer), typeof(int), typeof(string), typeof(Action<TextCommandResult>) }),
                "ChatCommandApi.Execute", prefix: nameof(PrisonPatches.Prefix_Execute), patchClass: typeof(PrisonPatches));

            if (c.FRUIT_ONLY_ON_ORCHARD_PLOTS)
            {
                TryPatch(harmony, typeof(FruitTreeRootBH).GetMethod("onRootTick", BindingFlags.NonPublic | BindingFlags.Instance),
                    "FruitTreeRootBH.onRootTick", postfix: nameof(OrchardPatches.Postfix_FruitTreeRootTick), patchClass: typeof(OrchardPatches));
                TryPatch(harmony, typeof(BlockFruitTreeBranch).GetMethod("TryPlaceBlock",
                    new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(ItemStack), typeof(BlockSelection), typeof(string).MakeByRefType() }),
                    "BlockFruitTreeBranch.TryPlaceBlock", postfix: nameof(OrchardPatches.Postfix_FruitTreeTryPlaceBlock), patchClass: typeof(OrchardPatches));
            }

            if (c.WATER_FLOW_CITY_PLOTS_PATCH)
            {
                TryPatch(harmony, typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod("CanSpreadIntoBlock",
                    new[] { typeof(Block), typeof(Block), typeof(BlockPos), typeof(BlockPos), typeof(BlockFacing), typeof(IWorldAccessor) }),
                    "BlockBehaviorFiniteSpreadingLiquid.CanSpreadIntoBlock", postfix: nameof(HarmonyPatches.Postfix_CanSpreadIntoBlock));
                // Paths that only lead into a foreign plot are dropped, so the liquid spreads sideways instead.
                TryPatch(harmony, typeof(BlockBehaviorFiniteSpreadingLiquid).GetMethod("FindDownwardPaths"),
                    "BlockBehaviorFiniteSpreadingLiquid.FindDownwardPaths", postfix: nameof(HarmonyPatches.Postfix_FindDownwardPaths));
            }

            TryPatch(harmony, typeof(BlockEntityBarrel).GetMethod("OnReceivedClientPacket"),
                "BlockEntityBarrel.OnReceivedClientPacket", prefix: nameof(HarmonyPatches.Prefix_BarrelReceivedClientPacket));

            // Boats shared with the owner's city. The flags are read inside, so a reload applies them.
            TryPatch(harmony, typeof(EntityBehaviorOwnable).GetMethod("IsOwner"),
                "EntityBehaviorOwnable.IsOwner", postfix: nameof(BoatSharePatches.Postfix_Ownable_IsOwner), patchClass: typeof(BoatSharePatches));
            TryPatch(harmony, typeof(EntityBehaviorOwnable).GetMethod("verifyOwnership", BindingFlags.NonPublic | BindingFlags.Instance),
                "EntityBehaviorOwnable.verifyOwnership", postfix: nameof(BoatSharePatches.Postfix_Ownable_VerifyOwnership), patchClass: typeof(BoatSharePatches));

            if (c.FALLING_BLOCKS_PROTECTION_PATCH)
            {
                TryPatch(harmony, typeof(BlockBehaviorUnstableFalling).GetMethod("createFallingBlock"),
                    "BlockBehaviorUnstableFalling.createFallingBlock", prefix: nameof(HarmonyPatches.Prefix_CreateFallingBlock));
                TryPatch(harmony, typeof(EntityBlockFalling).GetMethod("UpdateBlock", BindingFlags.NonPublic | BindingFlags.Instance),
                    "EntityBlockFalling.UpdateBlock", prefix: nameof(HarmonyPatches.Prefix_FallingUpdateBlock));
            }
        }
    }
}
