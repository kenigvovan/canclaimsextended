using System;
using canclaimsextended.src.selection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// The same selection the claim wand makes, from chat: for corners out of reach and for exact
    /// growing or shrinking of a face.
    /// </summary>
    public class SelectionCommand : BaseCommand
    {
        public static TextCommandResult Info(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            return TextCommandResult.Success(canclaimsextended.selections.Describe(player));
        }

        /// <summary>Corner at the block under the crosshair, or the one the player stands on.</summary>
        public static TextCommandResult SetCorner(TextCommandCallingArgs args, int corner)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            BlockPos pos = player.CurrentBlockSelection?.Position ?? player.Entity.Pos.AsBlockPos.DownCopy();
            canclaimsextended.selections.SetCorner(player, corner, pos);
            return TextCommandResult.Success();
        }

        public static TextCommandResult Grow(TextCommandCallingArgs args, int sign)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            PlotSelection sel = canclaimsextended.selections.Get(player);
            if (sel == null || !sel.IsComplete)
            {
                return ErrorWithParams("canclaimsextended:selection_needed", canclaimsextended.config.COMMAND_CITY);
            }
            BlockFacing facing = BlockFacing.FromCode((string)args[0]);
            int amount = (int)args[1];
            if (facing == null || amount <= 0)
            {
                return TextCommandResult.Error("canclaimsextended:selection_bad_direction");
            }
            sel.Grow(facing, sign * amount);
            if (sel.TryGetRawBox(out var raw) && Math.Max(raw.SizeX, Math.Max(raw.SizeY, raw.SizeZ)) > canclaimsextended.config.SELECTION_MAX_DIM)
            {
                sel.Grow(facing, -sign * amount);
                return ErrorWithParams("canclaimsextended:selection_too_long", canclaimsextended.config.SELECTION_MAX_DIM);
            }
            canclaimsextended.selections.Changed(player);
            return TextCommandResult.Success();
        }

        public static TextCommandResult Show(TextCommandCallingArgs args, bool show)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            canclaimsextended.selections.Get(player, true).AlwaysShow = show;
            canclaimsextended.selections.Refresh(player);
            return TextCommandResult.Success(show ? "canclaimsextended:selection_shown" : "canclaimsextended:selection_hidden");
        }

        /// <summary>/city borders on|off: territory edges stay visible without the wand.</summary>
        public static TextCommandResult Borders(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            string state = args.Parsers[0].IsMissing ? "" : ((string)args[0] ?? "").ToLowerInvariant();
            bool show = state == "on" || (state != "off" && !canclaimsextended.selections.BordersAlwaysShown(player));
            canclaimsextended.selections.SetBordersAlwaysShown(player, show);
            return TextCommandResult.Success(show ? "canclaimsextended:borders_shown" : "canclaimsextended:borders_hidden");
        }

        public static TextCommandResult Clear(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            canclaimsextended.selections.Clear(player);
            return TextCommandResult.Success();
        }

        /// <summary>Hands out a claim wand, unless the player already carries one.</summary>
        public static TextCommandResult GiveWand(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            Item wand = canclaimsextended.sapi.World.GetItem(new AssetLocation("canclaimsextended:claimwand"));
            if (wand == null)
            {
                return TextCommandResult.Error("canclaimsextended:wand_item_missing");
            }
            bool hasOne = false;
            player.Entity.WalkInventory(slot =>
            {
                if (slot.Itemstack?.Collectible == wand) hasOne = true;
                return !hasOne;
            });
            if (hasOne)
            {
                return TextCommandResult.Error("canclaimsextended:wand_already_have");
            }
            ItemStack stack = new ItemStack(wand);
            if (!player.InventoryManager.TryGiveItemstack(stack, true))
            {
                canclaimsextended.sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
            }
            return TextCommandResult.Success("canclaimsextended:wand_given");
        }
    }
}
