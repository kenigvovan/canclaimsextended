using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace canclaimsextended.src.client.gui
{
    /// <summary>SVG icons of the window, registered as "canclaimsextended:name", as claims does it.</summary>
    public static class GuiIcons
    {
        public const string City = "canclaimsextended:qaitbay-citadel";
        public const string Settings = "canclaimsextended:private";
        public const string Plot = "canclaimsextended:flat-platform";
        public const string Player = "canclaimsextended:magnifying-glass";
        public const string Ranks = "canclaimsextended:medal";
        public const string Found = "canclaimsextended:queen-crown";
        public const string Leave = "canclaimsextended:exit-door";
        public const string Invite = "canclaimsextended:envelope";
        public const string Remove = "canclaimsextended:cancel";
        public const string Accept = "canclaimsextended:check-mark";
        public const string Sell = "canclaimsextended:price-tag";
        public const string Buy = "canclaimsextended:receive-money";
        public const string Edit = "canclaimsextended:pencil";
        public const string Back = "canclaimsextended:fast-backward-button";
        public const string Grant = "canclaimsextended:id-card";
        public const string Info = "canclaimsextended:info";
        public const string Rights = "canclaimsextended:contract";
        public const string Wand = "canclaimsextended:highlighter";
        public const string Prison = "canclaimsextended:prisoner";
        public const string Summon = "canclaimsextended:magic-portal";
        public const string World = "canclaimsextended:soldering-iron";
        public const string PlotType = "canclaimsextended:large-paint-brush";
        public const string Grow = "canclaimsextended:expander";
        public const string Shrink = "canclaimsextended:convergence-target";
        public const string Extend = "canclaimsextended:stairs-goal";
        public const string Log = "canclaimsextended:files";
        public const string PlotsGroups = "canclaimsextended:huts-village";
        public const string Emblem = "canclaimsextended:vertical-banner";

        // An icon whose svg is missing draws nothing rather than failing.
        static readonly string[] Names =
        {
            "qaitbay-citadel", "private", "flat-platform", "magnifying-glass", "medal", "queen-crown",
            "exit-door", "envelope", "cancel", "check-mark", "price-tag", "receive-money", "pencil",
            "fast-backward-button", "id-card", "info", "contract", "highlighter",
            "prisoner", "magic-portal", "soldering-iron", "large-paint-brush",
            "expander", "convergence-target", "stairs-goal",
            "files", "huts-village", "vertical-banner"
        };

        public static void Register(ICoreClientAPI capi)
        {
            foreach (var name in Names)
            {
                string icon = name;
                capi.Gui.Icons.CustomIcons["canclaimsextended:" + icon] = (Context ctx, int x, int y, float w, float h, double[] rgba) =>
                {
                    IAsset svgAsset = capi.Assets.TryGet(new AssetLocation("canclaimsextended:textures/icons/" + icon + ".svg"), true);
                    if (svgAsset == null) return;
                    int color = ColorUtil.ColorFromRgba(175, 200, 175, 240);
                    capi.Gui.DrawSvg(svgAsset, ctx.GetTarget() as ImageSurface, x, y, (int)w, (int)h, color);
                };
            }
        }
    }
}
