using System;
using System.Collections.Generic;
using System.Reflection;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.perms.type;
using canclaimsextended.src.rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canclaimsextended.src.harmony
{
    public static class HarmonyPatches
    {
        /*==============================================================================================*/
        /*=====================================DAMAGE===================================================*/
        /*==============================================================================================*/
        /// <summary>
        /// Players hurt each other on a city plot only where pvp is on; protected animals there are
        /// hurt only by those with the attack right. The patch only forbids: an allowed hit still
        /// goes through vanilla, which checks the attack privileges.
        /// </summary>
        public static bool Prefix_On_ReceiveDamage(Entity __instance, DamageSource damageSource, ref bool __result)
        {
            if (__instance?.Api == null || __instance.Api.Side != EnumAppSide.Server || canclaimsextended.dataStorage == null)
            {
                return true;
            }
            if (damageSource == null)
            {
                return true;
            }
            // The cause is the thrower for projectiles.
            if (damageSource.GetCauseEntity() is not EntityPlayer attacker || attacker.Player is not IServerPlayer attackerPlr)
            {
                return true;
            }

            bool allowed = __instance is EntityPlayer victim
                ? victim.Player is not IServerPlayer victimPlr || CanPvpHere(attackerPlr, victimPlr)
                : !IsProtectedMob(__instance.Code) || AccessRules.CheckAt(attackerPlr, __instance.Pos.XYZ, PermType.ATTACK_ANIMALS_PERM);
            if (allowed)
            {
                return true;
            }
            __result = false;
            return false;
        }

        public static bool CanPvpHere(IServerPlayer attacker, IServerPlayer victim)
        {
            // Hurting oneself (a hot item off the anvil) is not pvp.
            if (attacker.PlayerUID == victim.PlayerUID || victim.Entity == null)
            {
                return true;
            }
            // As in claims: during the daily pvp window fighting is allowed everywhere, before the
            // world flags are even looked at.
            if (WorldInfo.IsPvpTime())
            {
                return true;
            }
            WorldInfo world = canclaimsextended.dataStorage.World;
            if (world.pvpEverywhere)
            {
                return true;
            }
            if (world.pvpForbidden)
            {
                return false;
            }
            if (!canclaimsextended.dataStorage.GetPlotAt(victim.Entity.Pos.XYZ, out Plot plot) || !plot.hasCity())
            {
                return true;
            }
            return PvpRules.IsPvpOn(plot);
        }

        /// <summary>PROTECTED_MOB_TYPES entries: "pig-*" matches by path prefix, "game:pig-*" by full code.</summary>
        public static bool IsProtectedMob(AssetLocation code)
        {
            if (code == null) return false;
            foreach (string raw in canclaimsextended.config.PROTECTED_MOB_TYPES)
            {
                string pattern = raw.Trim();
                if (pattern.Length == 0) continue;
                string subject = pattern.Contains(':') ? code.ToString() : code.Path;
                if (pattern.EndsWith("*") ? subject.StartsWith(pattern[..^1]) : subject == pattern)
                {
                    return true;
                }
            }
            return false;
        }

        /*==============================================================================================*/
        /*=====================================FIRE=====================================================*/
        /*==============================================================================================*/
        /// <summary>Fire spreads into a plot only when its fire flag is on, or the world forces fire.</summary>
        public static bool Prefix_On_TrySpreadFireAllDirs(BlockPos pos, BEBehaviorBurning __instance)
        {
            if (canclaimsextended.dataStorage == null || !canclaimsextended.dataStorage.GetPlotAt(pos, out Plot plot))
            {
                return true;
            }
            if (canclaimsextended.dataStorage.World.fireEverywhere || PlotFireFlag(plot))
            {
                return true;
            }
            // A kiln or a pile keeps burning itself; a loose fire next to the plot goes out.
            BlockEntity below = __instance.Api.World.BlockAccessor.GetBlockEntity(__instance.FirePos.DownCopy(1));
            if (below is not BlockEntityPitKiln && __instance.Blockentity != null && __instance.Blockentity is not BlockEntityGroundStorage)
            {
                __instance.KillFire(false);
            }
            return false;
        }

        /// <summary>
        /// Every fire goes through OnFirePlaced, whatever lit it. A fire block burns its neighbour,
        /// so on a plot without the fire flag it needs the build right of whoever lit it; a kiln or a
        /// pile lit directly burns only itself, so the use right is enough there.
        /// </summary>
        public static bool Prefix_On_FirePlaced(BlockPos firePos, BlockPos fuelPos, string startedByPlayerUid, BEBehaviorBurning __instance)
        {
            if (__instance?.Api == null || __instance.Api.Side != EnumAppSide.Server || canclaimsextended.dataStorage == null)
            {
                return true;
            }
            if (canclaimsextended.dataStorage.World.fireEverywhere)
            {
                return true;
            }
            PlayerInfo starter = null;
            if (startedByPlayerUid != null)
            {
                canclaimsextended.dataStorage.GetPlayerByUid(startedByPlayerUid, out starter);
            }
            BlockEntity be = __instance.Blockentity;
            bool burnsItself = be is BlockEntityPitKiln || be is BlockEntityGroundStorage || be is BlockEntityItemPile;
            if (IsFireAllowedAt(firePos, starter, burnsItself) && IsFireAllowedAt(fuelPos, starter, burnsItself))
            {
                return true;
            }
            // The fire block is already placed; only it is removed, a kiln or pile stays.
            IBlockAccessor accessor = __instance.Api.World.BlockAccessor;
            Block fireBlock = __instance.Api.World.GetBlock(new AssetLocation("fire"));
            if (be?.Pos != null && fireBlock != null && accessor.GetBlock(be.Pos).BlockId == fireBlock.BlockId)
            {
                accessor.SetBlock(0, be.Pos);
                accessor.TriggerNeighbourBlockUpdate(be.Pos);
            }
            return false;
        }

        private static bool IsFireAllowedAt(BlockPos pos, PlayerInfo starter, bool burnsItself)
        {
            if (pos == null || !canclaimsextended.dataStorage.GetPlotAt(pos, out Plot plot))
            {
                return true;
            }
            if (PlotFireFlag(plot))
            {
                return true;
            }
            if (starter == null)
            {
                return false;
            }
            return AccessRules.CanWithoutCache(starter, plot, burnsItself ? PermType.USE_PERM : PermType.BUILD_AND_DESTROY_PERM);
        }

        /// <summary>The plot's fire flag, unless the world forbids fire on claimed land.</summary>
        private static bool PlotFireFlag(Plot plot)
        {
            return !canclaimsextended.dataStorage.World.fireForbidden && plot.getPermsHandler().fireFlag;
        }

        /*==============================================================================================*/
        /*=====================================BLAST====================================================*/
        /*==============================================================================================*/
        /// <summary>
        /// A bomb goes off when every plot in its reach allows blasts or lets the one who lit it
        /// build. With no plot in reach there is nothing to protect.
        /// </summary>
        public static bool Prefix_HasPermissionToUse(BlockEntityBomb __instance, string ___ignitedByPlayerUid, ref bool __result)
        {
            if (canclaimsextended.dataStorage == null)
            {
                return true;
            }
            // World flags first: forced on, then forbidden everywhere, then what the land says.
            WorldInfo world = canclaimsextended.dataStorage.World;
            if (world.blastEverywhere || world.blastForbidden)
            {
                __result = world.blastEverywhere;
                return false;
            }
            int rad = (int)Math.Ceiling(__instance.BlastRadius);
            BlockPos p = __instance.Pos;
            BlockBox reach = new BlockBox(p.X - rad, p.Y - rad, p.Z - rad, p.X + rad + 1, p.Y + rad + 1, p.Z + rad + 1);

            PlayerInfo igniter = null;
            if (___ignitedByPlayerUid != null)
            {
                canclaimsextended.dataStorage.GetPlayerByUid(___ignitedByPlayerUid, out igniter);
            }
            foreach (Plot plot in canclaimsextended.dataStorage.QueryPlotsAndLand(reach))
            {
                if (plot.getPermsHandler().blastFlag) continue;
                if (igniter != null && AccessRules.CanWithoutCache(igniter, plot, PermType.BUILD_AND_DESTROY_PERM)) continue;
                __result = false;
                return false;
            }
            __result = true;
            return false;
        }

        /*==============================================================================================*/
        /*=====================================LIQUIDS==================================================*/
        /*==============================================================================================*/
        /// <summary>A liquid may flow into free land or within one city, not into a city from outside.</summary>
        private static bool LiquidMayFlow(BlockPos from, BlockPos to)
        {
            if (!canclaimsextended.dataStorage.GetPlotAt(to, out Plot dest))
            {
                return true;
            }
            return canclaimsextended.dataStorage.GetPlotAt(from, out Plot source) && SameCity(source, dest);
        }

        private static bool SameCity(Plot a, Plot b)
        {
            return a.hasCity() && a.getCity().Equals(b.getCity());
        }

        /// <summary>
        /// Every step of a liquid - sideways, straight down, along a downward path - asks
        /// CanSpreadIntoBlock first, so refusing here covers a plot hanging in the air too, and
        /// vanilla keeps its own choice of direction.
        /// </summary>
        public static void Postfix_CanSpreadIntoBlock(BlockPos pos, BlockPos npos, ref bool __result)
        {
            if (__result && canclaimsextended.dataStorage != null && !LiquidMayFlow(pos, npos))
            {
                __result = false;
            }
        }

        public static void Postfix_FindDownwardPaths(BlockPos pos, List<PosAndDist> __result)
        {
            if (__result == null || canclaimsextended.dataStorage == null)
            {
                return;
            }
            __result.RemoveAll(it => !LiquidMayFlow(pos, it.pos));
        }

        /*==============================================================================================*/
        /*=====================================BARREL===================================================*/
        /*==============================================================================================*/
        /// <summary>
        /// Sealing a barrel arrives as a packet of its own, past the use check of opening it; it
        /// needs the use right on the barrel's plot as well.
        /// </summary>
        public static bool Prefix_BarrelReceivedClientPacket(BlockEntityBarrel __instance, IPlayer player)
        {
            if (player is not IServerPlayer serverPlayer || canclaimsextended.dataStorage == null || __instance?.Pos == null)
            {
                return true;
            }
            if (serverPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                return true;
            }
            return AccessRules.Check(serverPlayer, __instance.Pos, PermType.USE_PERM);
        }

        /*==============================================================================================*/
        /*=====================================FALLING BLOCKS===========================================*/
        /*==============================================================================================*/
        static readonly MethodInfo FallingDropItems = AccessTools.Method(typeof(EntityBlockFalling), "DropItems");

        /// <summary>
        /// A loose block of a plot falls only within its city. A plot can hang in the air, and
        /// digging under it from outside must not pour its sand out: the block stays where it is.
        /// </summary>
        public static bool Prefix_CreateFallingBlock(BlockPos ourPos)
        {
            if (canclaimsextended.dataStorage == null || !canclaimsextended.dataStorage.GetPlotAt(ourPos, out Plot source))
            {
                return true;
            }
            return canclaimsextended.dataStorage.GetPlotAt(ourPos.X, ourPos.Y - 1, ourPos.Z, out Plot below) && SameCity(source, below);
        }

        /// <summary>
        /// A block falling into a plot from outside its city lands as items, the way vanilla drops
        /// a falling block that finds no room.
        /// </summary>
        public static bool Prefix_FallingUpdateBlock(EntityBlockFalling __instance, bool remove, BlockPos pos)
        {
            if (remove || canclaimsextended.dataStorage == null || __instance.Api?.Side != EnumAppSide.Server)
            {
                return true;
            }
            if (!canclaimsextended.dataStorage.GetPlotAt(pos, out Plot dest))
            {
                return true;
            }
            if (__instance.initialPos != null && canclaimsextended.dataStorage.GetPlotAt(__instance.initialPos, out Plot source) && SameCity(source, dest))
            {
                return true;
            }
            if (FallingDropItems == null)
            {
                return true;
            }
            FallingDropItems.Invoke(__instance, new object[] { pos });
            return false;
        }
    }
}
