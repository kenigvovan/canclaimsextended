using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// The plot the admin stands in, of any city, managed as its mayor would: name, type, shape,
    /// rights and flags, without money or limits. Every button runs /cadmin plot.
    /// </summary>
    public sealed class AdminPlotPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => ShowAdminTabs(p);
        public override bool Polls(GuiStatePacket p) => true;

        private const string Prefix = "admplot";
        private static readonly string[] Faces = Codes.Directions;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            if (string.IsNullOrEmpty(P.PlotGuid))
            {
                Card.Note(compo, left, left.fixedY, T("gui-section-admin-plot"), T("gui-admin-plot-none"));
                return;
            }

            bool cityLand = string.IsNullOrEmpty(P.PlotOwner);
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-name"), Key = "apname",
                              Value = PlotLabel() },
                new CardRow { Label = T("gui-label-city"), Value = Pretty(P.PlotCity), Key = "apcity" },
                new CardRow { Label = T("gui-label-owner"), Value = cityLand ? T("gui-value-city-land") : P.PlotOwner, Key = "apowner" },
                new CardRow { Label = T("gui-label-plot-type"), Value = PlotTypeName(P.PlotType), Key = "aptype" },
                new CardRow { Label = T("gui-label-size"), Value = $"{P.PlotSizeX} x {P.PlotSizeY} x {P.PlotSizeZ}", Key = "apsize" },
                new CardRow { Label = T("gui-label-volume-plot"), Value = P.PlotVolume.ToString(), Key = "apvolume" },
                new CardRow { Label = T("gui-label-parts"), Value = P.PlotBoxes.ToString(), Key = "apboxes" },
            };
            double yLeft = Card.RowsWithActions(compo, left, left.fixedY, T("gui-section-admin-plot"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Edit, "ap-rename", () =>
                    Open(new TextInputDialog(T("gui-prompt-plot-name"), T("gui-button-save"), name => Cadmin("plot name " + name))),
                    T("gui-action-plot-name"));
                if (P.PlotName.Length > 0)
                {
                    actions.Add(GuiIcons.Remove, "ap-unname", () => Cadmin("plot name"), T("gui-admin-action-plot-unname"));
                }
                actions.Add(GuiIcons.PlotType, "ap-type", OpenTypePicker, T("gui-action-plot-type"));
                actions.Add(GuiIcons.Leave, "ap-delete", () =>
                    Open(new ConfirmDialog(T(cityLand ? "gui-admin-confirm-plot-delete" : "gui-admin-confirm-plot-delete-owned", P.PlotOwner),
                        () => Cadmin("plot delete"))), T("gui-admin-action-plot-delete"));
            });
            // The parts go on the right, under the flags: the left column is full with these two.
            OwnershipCard(compo, left, yLeft, cityLand);

            bool[] flags = { P.PlotPvp, P.PlotFire, P.PlotBlast, P.PlotNoMobSpawn };
            double yRight = PermTable.Rights(compo, right, right.fixedY, Prefix, P.PlotPerms, false,
                (group, type, on) => Cadmin($"plot perm {group} {type} {OnOff(on)}"));
            yRight = PermTable.FlagSwitches(compo, right, yRight, Prefix, flags, false,
                (flag, on) => Cadmin($"plot flag {flag} {OnOff(on)}"));
            PartsCard(compo, right, yRight);
            ctx.AfterCompose(() => PermTable.ApplyValues(compo, Prefix, P.PlotPerms, flags));
        }

        /// <summary>
        /// Every part of the plot as /plot boxes numbers them, each one that may go with its cut
        /// button; the part the admin stands in is marked, and its faces move from the strip below.
        /// </summary>
        private void PartsCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool standsInPart = P.PlotParts.Any(b => b.IsHere);
            var items = new List<ItemRow>();
            for (int i = 0; i < P.PlotParts.Count; i++)
            {
                GuiBoxEntry b = P.PlotParts[i];
                int number = i + 1;
                var row = new ItemRow
                {
                    Label = T(b.IsHere ? "gui-land-part-here" : "gui-land-part", number, b.SizeX, b.SizeY, b.SizeZ),
                    Value = b.Volume.ToString(),
                    ValueColor = b.IsHere ? GuiColors.Success : null,
                    Tooltip = T("gui-land-part-at", number, b.SizeX, b.SizeY, b.SizeZ, b.MinX, b.MinY, b.MinZ),
                    Key = "ap-part-" + i
                };
                if (b.CanCut)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () =>
                        Open(new ConfirmDialog(T("gui-admin-confirm-plot-cut-n", number, b.Volume), () => Cadmin("plot cut " + number))),
                        T("gui-admin-action-plot-cut")));
                }
                items.Add(row);
            }
            System.Action<ElementBounds> faces = null;
            if (standsInPart)
            {
                faces = slot =>
                {
                    var actions = new ActionRow(compo, slot);
                    actions.Add(GuiIcons.Grow, "ap-grow", () => PickFace("grow"), T("gui-admin-action-plot-grow"));
                    actions.Add(GuiIcons.Shrink, "ap-shrink", () => PickFace("shrink"), T("gui-admin-action-plot-shrink"));
                };
            }
            Card.Items(compo, column, y, T("gui-section-parts"), Limit(items, 4), T("gui-admin-plot-part-none"), faces);
        }

        /// <summary>Owner, sale price and daily fee, each settable without the owner's or mayor's say.</summary>
        private double OwnershipCard(GuiComposer compo, ElementBounds column, double y, bool cityLand)
        {
            bool forSale = P.PlotPrice >= 0;
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-price"), Value = forSale ? P.PlotPrice.ToString() : T("gui-value-not-for-sale"), Key = "apprice",
                              ValueColor = forSale ? GuiColors.Success : GuiColors.Label },
                new CardRow { Label = T("gui-admin-label-plot-fee"), Value = P.PlotFee > 0 ? P.PlotFee.ToString() : T("gui-admin-none"), Key = "apfee" },
            };
            return Card.RowsWithActions(compo, column, y, T("gui-section-admin-plot-owner"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Grant, "ap-owner", () =>
                    Open(new TextInputDialog(T("gui-admin-prompt-plot-owner", Pretty(P.PlotCity)), T("gui-button-save"), name => Cadmin("plot owner " + name))),
                    T("gui-admin-action-plot-owner"));
                if (!cityLand)
                {
                    actions.Add(GuiIcons.Leave, "ap-takeback", () =>
                        Open(new ConfirmDialog(T("gui-admin-confirm-plot-takeback", P.PlotOwner), () => Cadmin("plot takeback"))),
                        T("gui-admin-action-plot-takeback"));
                }
                actions.Add(GuiIcons.Sell, "ap-sell", () =>
                    Open(new TextInputDialog(T("gui-prompt-price"), T("gui-button-sell"), price => Cadmin("plot sell " + price), true)),
                    T("gui-action-sell"));
                if (forSale)
                {
                    actions.Add(GuiIcons.Remove, "ap-nosell", () => Cadmin("plot nosell"), T("gui-action-nfs"));
                }
                actions.Add(GuiIcons.Buy, "ap-fee", () =>
                    Open(new TextInputDialog(T("gui-admin-prompt-plot-fee", P.PlotFee), T("gui-button-save"), fee => Cadmin("plot fee " + fee), true)),
                    T("gui-admin-action-plot-fee"));
            });
        }

        /// <summary>A direction, then how many blocks: /cadmin plot grow|shrink.</summary>
        private void PickFace(string verb)
        {
            string[] names = Faces.Select(f => T("gui-face-" + f)).ToArray();
            Open(new PickDialog(T("gui-admin-prompt-face"), Faces, names, T("gui-button-next"), face =>
                Open(new TextInputDialog(T("gui-admin-prompt-blocks"), T("gui-button-save"),
                    blocks => Cadmin($"plot {verb} {face} {blocks}"), true))));
        }

        /// <summary>Every type the server offers but the current one; free for the admin.</summary>
        private void OpenTypePicker()
        {
            string[] codes = P.EnabledPlotTypes.Where(c => c != P.PlotType).ToArray();
            if (codes.Length == 0) return;
            Open(new PickDialog(T("gui-admin-prompt-plot-type"), codes, codes.Select(PlotTypeName).ToArray(), T("gui-button-set"),
                code => Cadmin("plot type " + code)));
        }
    }
}
