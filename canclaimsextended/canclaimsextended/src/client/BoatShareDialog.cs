using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.part;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace canclaimsextended.src.client
{
    /// <summary>
    /// Asks the owner of the boat under the crosshair who else may use it. The boat is remembered
    /// on open, so the player may look away; the server checks ownership and distance again.
    /// </summary>
    public class BoatShareDialog : GuiDialog
    {
        public const string HotkeyCode = "canclaimsextendedboatshare";

        private const double ButtonWidth = 170;
        private const double ButtonHeight = 28;
        private const double Gap = 6;

        private long boatEntityId;
        private BoatShareMode current;

        public BoatShareDialog(ICoreClientAPI capi) : base(capi)
        {
        }

        public override string ToggleKeyCombinationCode => HotkeyCode;

        /// <summary>The boat under the crosshair if this player owns it, null otherwise.</summary>
        static Entity OwnedBoatInSight(ICoreClientAPI capi)
        {
            Entity entity = capi.World.Player?.CurrentEntitySelection?.Entity;
            if (entity?.GetBehavior<EntityBehaviorOwnable>() == null) return null;
            var ownedby = entity.WatchedAttributes.GetTreeAttribute("ownedby");
            if (ownedby == null || ownedby.GetString("uid", "") != capi.World.Player.PlayerUID) return null;
            return entity;
        }

        /// <summary>What the server allows, from the stamp it keeps on our own player.</summary>
        int Allowed()
        {
            return capi.World.Player?.Entity?.WatchedAttributes.GetInt(BoatShare.AllowedAttr, 0) ?? 0;
        }

        /// <summary>
        /// Picks up the boat in sight and composes for it, refusing to open when there is none.
        /// GuiDialog binds its own handler to ToggleKeyCombinationCode, so both routes end here.
        /// </summary>
        public override bool TryOpen()
        {
            if (Allowed() <= 0)
            {
                capi.TriggerIngameError(this, "boatsharingoff", Lang.Get("canclaimsextended:boat-sharing-disabled"));
                return false;
            }
            Entity boat = OwnedBoatInSight(capi);
            if (boat == null)
            {
                capi.TriggerIngameError(this, "noboatinsight", Lang.Get("canclaimsextended:boat-look-at-one"));
                return false;
            }
            boatEntityId = boat.EntityId;
            current = BoatShare.Effective(BoatShare.Of(boat), Allowed());
            Compose(boat.GetName());
            return base.TryOpen();
        }

        private void Compose(string boatName)
        {
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            // The old composer goes before a new one: setting SingleComposer to null crashes the client.
            Composers["single"]?.Dispose();
            var composer = capi.Gui.CreateCompo("canclaimsextended-boat-share", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("canclaimsextended:boat-share-title"), () => TryClose())
                .BeginChildElements(bgBounds);

            ElementBounds row = ElementBounds.Fixed(0, 30, ButtonWidth, 20);
            composer.AddStaticText(boatName ?? "", CairoFont.WhiteSmallText().WithColor(GuiColors.Label), row, "boat-name");

            row = row.BelowCopy(0, 2);
            composer.AddStaticText(Lang.Get("canclaimsextended:boat-share-current", Lang.Get(BoatShare.LangKeyOf(current))),
                CairoFont.WhiteSmallishText().WithColor(GuiColors.Value), row, "boat-current");

            row = row.BelowCopy(0, Gap).WithFixedHeight(ButtonHeight);
            AddModeButton(composer, ref row, BoatShareMode.PERSONAL);
            AddModeButton(composer, ref row, BoatShareMode.CITY);

            SingleComposer = composer.EndChildElements().Compose();
        }

        private void AddModeButton(GuiComposer composer, ref ElementBounds row, BoatShareMode mode)
        {
            string code = BoatShare.CodeOf(mode);
            composer.AddButton(Lang.Get(BoatShare.LangKeyOf(mode)), () =>
            {
                Apply(mode);
                return true;
            }, row.FlatCopy(), mode == current ? EnumButtonStyle.Small : EnumButtonStyle.Normal, "boat-mode-" + code);
            row = row.BelowCopy(0, Gap);
        }

        private void Apply(BoatShareMode mode)
        {
            string city = capi.World.Player?.Entity?.WatchedAttributes.GetString(BoatShare.CityCommandAttr, "city") ?? "city";
            capi.SendChatMessage("/" + city + " boatshare " + boatEntityId + " " + BoatShare.CodeOf(mode));
            TryClose();
        }
    }
}
