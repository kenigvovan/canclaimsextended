using System;
using canclaimsextended.src.messages;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace canclaimsextended.src.cropbehaviors
{
    /// <summary>With CROPS_ONLY_ON_FARM_PLOTS on, crops grow only on plots of type farm.</summary>
    public class BehaviorOnlyOnFarmPlot : CropBehavior
    {
        public BehaviorOnlyOnFarmPlot(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
        }

        public override bool TryGrowCrop(ICoreAPI api, IFarmlandBlockEntity farmland,
            double currentTotalHours, int newGrowthStage, ref EnumHandling handling)
        {
            if (!canclaimsextended.config.CROPS_ONLY_ON_FARM_PLOTS) return false;
            if (PlotTypeRules.IsFarmland(farmland.Pos)) return false;

            handling = EnumHandling.PreventDefault;
            return false;
        }

        public override void OnPlanted(ICoreAPI api, ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel)
        {
            if (api.Side != EnumAppSide.Server || blockSel == null) return;
            if (!canclaimsextended.config.CROPS_ONLY_ON_FARM_PLOTS) return;
            if (PlotTypeRules.IsFarmland(blockSel.Position)) return;

            if (byEntity is EntityPlayer ep && ep.Player is IServerPlayer sp)
            {
                MessageHandler.sendMsgToPlayer(sp, Lang.Get("canclaimsextended:crop_wont_grow_outside_farm"));
            }
        }

        /// <summary>Adds the behaviour to every crop. Run once the blocks are loaded (RunGame).</summary>
        public static void AttachToCrops(ICoreServerAPI sapi)
        {
            if (!canclaimsextended.config.CROPS_ONLY_ON_FARM_PLOTS) return;
            foreach (Block block in sapi.World.Blocks)
            {
                if (block?.CropProps == null) continue;
                CropBehavior[] existing = block.CropProps.Behaviors ?? Array.Empty<CropBehavior>();
                // Blocks outlive a world in single player; a second run must not add it again.
                if (Array.Exists(existing, b => b is BehaviorOnlyOnFarmPlot)) continue;
                CropBehavior[] extended = new CropBehavior[existing.Length + 1];
                Array.Copy(existing, extended, existing.Length);
                extended[existing.Length] = new BehaviorOnlyOnFarmPlot(block);
                block.CropProps.Behaviors = extended;
            }
        }
    }
}
