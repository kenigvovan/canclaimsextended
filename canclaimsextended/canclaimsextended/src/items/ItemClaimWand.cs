using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.items
{
    /// <summary>
    /// Selection tool: left click on a block sets the first corner, right click the second,
    /// sneak + left click clears the selection. Breaks and uses nothing. While it is in hand
    /// the server shows the plots around and the selection, see SelectionHandler.
    /// </summary>
    public class ItemClaimWand : Item
    {
        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (api.Side != EnumAppSide.Server) return;
            if (byEntity is not EntityPlayer ep || ep.Player is not IServerPlayer player) return;

            if (byEntity.Controls.ShiftKey)
            {
                canclaimsextended.selections.Clear(player);
                return;
            }
            if (blockSel == null) return;
            canclaimsextended.selections.SetCorner(player, 1, blockSel.Position);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || blockSel == null) return;
            if (api.Side != EnumAppSide.Server) return;
            if (byEntity is not EntityPlayer ep || ep.Player is not IServerPlayer player) return;

            canclaimsextended.selections.SetCorner(player, 2, blockSel.Position);
        }
    }
}
