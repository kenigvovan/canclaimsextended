using System;
using System.Collections.Generic;
using System.Text;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canclaimsextended.src.blocks
{
    /// <summary>
    /// The temple block, copied from claims: vanilla gravestone shapes picked by the "type" attribute.
    /// It may only stand on a temple plot of its placer's city and makes that plot a respawn point.
    /// An empty-hand right click picks it as the citizen's own respawn temple.
    /// </summary>
    public class TempleBlock : BlockShapeFromAttributes
    {
        public Dictionary<string, ClutterTypeProps> clutterByCode = new Dictionary<string, ClutterTypeProps>();
        string basePath;

        public override string ClassType => "cantempleblock";

        public override IEnumerable<IShapeTypeProps> AllTypes => clutterByCode.Values;

        public override void LoadTypes()
        {
            ClutterTypeProps[] array = Attributes["types"].AsObject<ClutterTypeProps[]>(null);
            basePath = "game:shapes/" + Attributes["shapeBasePath"].AsString(null) + "/";
            List<JsonItemStack> stacks = new List<JsonItemStack>();
            ModelTransform defaultGui = ModelTransform.BlockDefaultGui();
            ModelTransform defaultFp = ModelTransform.BlockDefaultFp();
            ModelTransform defaultTp = ModelTransform.BlockDefaultTp();
            ModelTransform defaultGround = ModelTransform.BlockDefaultGround();
            foreach (ClutterTypeProps ct in array)
            {
                clutterByCode[ct.Code] = ct;
                if (ct.GuiTf != null) ct.GuiTransform = new ModelTransform(ct.GuiTf, defaultGui);
                if (ct.FpTf != null) ct.FpTtransform = new ModelTransform(ct.FpTf, defaultFp);
                if (ct.TpTf != null) ct.TpTransform = new ModelTransform(ct.TpTf, defaultTp);
                if (ct.GroundTf != null) ct.GroundTransform = new ModelTransform(ct.GroundTf, defaultGround);
                if (ct.ShapePath == null)
                {
                    ct.ShapePath = AssetLocation.Create(basePath + ct.Code + ".json", Code.Domain);
                }
                else if (ct.ShapePath.Path.StartsWith('/'))
                {
                    ct.ShapePath.WithPathPrefixOnce("shapes").WithPathAppendixOnce(".json");
                }
                else
                {
                    ct.ShapePath.WithPathPrefixOnce(basePath).WithPathAppendixOnce(".json");
                }
                JsonItemStack jstack = new JsonItemStack
                {
                    Code = Code,
                    Type = EnumItemClass.Block,
                    Attributes = new JsonObject(JToken.Parse("{ \"type\": \"" + ct.Code + "\" }"))
                };
                jstack.Resolve(api.World, ClassType + " type", true);
                stacks.Add(jstack);
            }
            CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList { Stacks = stacks.ToArray(), Tabs = new string[] { "general" } }
            };
        }

        public override bool IsClimbable(BlockPos pos)
        {
            BEBehaviorShapeFromAttributes bec = GetBEBehavior<BEBehaviorShapeFromAttributes>(pos);
            if (bec?.Type != null && clutterByCode.TryGetValue(bec.Type, out ClutterTypeProps props))
            {
                return props.Climbable;
            }
            return Climbable;
        }

        public override IShapeTypeProps GetTypeProps(string code, ItemStack stack, BEBehaviorShapeFromAttributes be)
        {
            if (code == null) return null;
            clutterByCode.TryGetValue(code, out ClutterTypeProps cprops);
            return cprops;
        }

        public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
        {
            return new BlockDropItemStack[] { new BlockDropItemStack(handbookStack, 1f) };
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("canclaimsextended:cantempleblock-desc"));
        }

        /// <summary>The temple plot of the placer's own city at this block; null when it is not one.</summary>
        static PlotDescTemple TempleAt(BlockPos pos, IPlayer byPlayer, out Plot plot)
        {
            plot = null;
            if (!canclaimsextended.dataStorage.GetPlotAt(pos, out plot) || plot.Type != PlotType.TEMPLE || !plot.hasCity()) return null;
            if (byPlayer != null)
            {
                if (!canclaimsextended.dataStorage.GetPlayerByUid(byPlayer.PlayerUID, out PlayerInfo playerInfo)
                    || !plot.getCity().Equals(playerInfo.City))
                {
                    return null;
                }
            }
            return plot.Desc as PlotDescTemple;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            // Only the server knows plot types; a refused placement is undone on the client.
            if (world.Side == EnumAppSide.Server && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative
                && TempleAt(blockSel.Position, byPlayer, out _) == null)
            {
                failureCode = "__ignore__";
                MessageHandler.sendMsgToPlayer(byPlayer as IServerPlayer, Lang.Get("canclaimsextended:temple_wrong_plot"));
                return false;
            }
            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            if (world.Side != EnumAppSide.Server) return;
            PlotDescTemple temple = TempleAt(blockPos, null, out Plot plot);
            if (temple == null) return;
            temple.Point = blockPos.Copy();
            plot.saveToDatabase();
            foreach (var pl in world.GetPlayersAround(blockPos.ToVec3d(), 10, 10))
            {
                world.PlaySoundAt(new AssetLocation("game:sounds/block/heavymetal-hit"), pl, null, true, 32f, 1f);
            }
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            if (world.Side != EnumAppSide.Server) return;
            PlotDescTemple temple = TempleAt(pos, null, out Plot plot);
            if (temple?.Point != null && temple.Point.Equals(pos))
            {
                // Another temple block on the same plot takes over; otherwise the plot has no point.
                temple.Point = FindTempleBlock(world, plot);
                plot.saveToDatabase();
            }
        }

        /// <summary>Any temple block left inside the plot, null when there is none (or it isn't loaded).</summary>
        static BlockPos FindTempleBlock(IWorldAccessor world, Plot plot)
        {
            if (plot.Shape == null) return null;
            BlockPos found = null;
            foreach (var box in plot.Shape.Boxes)
            {
                // WalkBlocks takes both corners inclusive; boxes are half-open.
                world.BlockAccessor.WalkBlocks(new BlockPos(box.MinX, box.MinY, box.MinZ), new BlockPos(box.MaxX - 1, box.MaxY - 1, box.MaxZ - 1),
                    (block, x, y, z) =>
                    {
                        if (found == null && block is TempleBlock) found = new BlockPos(x, y, z);
                    });
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Empty hand: pick this temple as the own respawn point, or drop the pick when it is this one.
        /// With an item in hand the block's own behaviours (wrench, repair) keep working.
        /// </summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null || byPlayer?.InventoryManager?.ActiveHotbarSlot?.Empty == false)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }
            // The client must claim the click for the server to hear about it; the server decides.
            if (world.Side != EnumAppSide.Server || byPlayer is not IServerPlayer player)
            {
                return true;
            }
            Config c = canclaimsextended.config;
            if (!c.TEMPLE_RESPAWN_ENABLED || !c.TEMPLE_RESPAWN_CHOICE_ENABLED)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }
            if (!canclaimsextended.dataStorage.GetPlotAt(blockSel.Position, out Plot plot) || plot.Type != PlotType.TEMPLE
                || !plot.hasCity() || plot.Desc is not PlotDescTemple temple)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }
            City city = plot.getCity();
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo) || !city.Equals(playerInfo.City))
            {
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:temple_respawn_not_citizen"));
                return true;
            }
            // A block that predates the plot type never registered itself; the clicked one does now.
            if (temple.Point == null)
            {
                temple.Point = blockSel.Position.Copy();
                plot.saveToDatabase();
            }
            if (TempleRespawn.TryReadPick(player, city, out string picked) && picked == plot.Guid)
            {
                TempleRespawn.ClearPick(player);
                MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:temple_respawn_cleared"));
                return true;
            }
            TempleRespawn.WritePick(player, city, plot);
            MessageHandler.sendMsgToPlayer(player, Lang.Get("canclaimsextended:temple_respawn_set", temple.Point.X, temple.Point.Y, temple.Point.Z));
            return true;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            ItemStack stack = new ItemStack(world.GetBlock(new AssetLocation("canclaimsextended:cantempleblock")), 1);
            stack.Attributes.SetString("type", GetBEBehavior<BEBehaviorShapeFromAttributes>(pos)?.Type);
            return new ItemStack[] { stack };
        }
    }
}
