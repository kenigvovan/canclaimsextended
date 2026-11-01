using canclaimsextended.src.client.gui.widgets;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>World flags for admins: each switch runs /cadmin world set at once.</summary>
    public sealed class WorldPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p) => ShowAdminTabs(p);

        private const string Prefix = "world";
        private const double RowHeight = 26;
        private const double SwitchSize = 22;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double height = Card.HeaderHeight + RowHeight * P.WorldFlags.Count + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, left, left.fixedY, height, T("gui-section-world"));
            var labelFont = CairoFont.WhiteSmallText().WithColor(GuiColors.Label);

            for (int i = 0; i < P.WorldFlags.Count; i++)
            {
                string code = P.WorldFlags[i].Code;
                double rowY = inner.fixedY + i * RowHeight;

                var sw = inner.FlatCopy().WithFixedSize(SwitchSize, SwitchSize);
                sw.fixedY = rowY;
                compo.AddSwitch(on => Cadmin($"world set {code} {OnOff(on)}"), sw, Prefix + "-" + code, SwitchSize);

                var label = inner.FlatCopy().WithFixedSize(inner.fixedWidth - SwitchSize - 8, RowHeight);
                label.fixedX += SwitchSize + 6;
                label.fixedY = rowY + 4;
                compo.AddStaticText(T("gui-world-" + code), labelFont, label, Prefix + "-label-" + code);
                Tooltip.Add(compo, T("gui-world-" + code + "-tooltip"), label, Prefix + "-tip-" + code);
            }

            Card.Note(compo, right, right.fixedY, null, T("gui-world-hint"));

            ctx.AfterCompose(() =>
            {
                foreach (var flag in P.WorldFlags)
                {
                    compo.GetSwitch(Prefix + "-" + flag.Code)?.SetValue(flag.Value);
                }
            });
        }
    }
}
