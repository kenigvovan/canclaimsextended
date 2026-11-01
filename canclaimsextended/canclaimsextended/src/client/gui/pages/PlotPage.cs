using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The plot the player stands in: what it is, who owns it, what is allowed on it.</summary>
    public sealed class PlotPage : GuiPage
    {
        private const string Prefix = "plotset";

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            if (string.IsNullOrEmpty(P.PlotGuid))
            {
                // On the land of a city outside its plots: the city's own rights apply, set with /city set,
                // or in a group chunk the group's for its members.
                string text = string.IsNullOrEmpty(P.HereCity) ? T("gui-plot-none")
                    : !string.IsNullOrEmpty(P.HereChunkGroup) && P.HereIsOwnCity ? T("gui-plot-group-land", Pretty(P.HereCity), Pretty(P.HereChunkGroup))
                    : T("gui-plot-city-land", Pretty(P.HereCity));
                Card.Note(compo, left, left.fixedY, T("gui-section-plot"), text, slot =>
                    new ActionRow(compo, slot).Add(GuiIcons.Wand, "plot-wand", () => City("wand"), T("gui-action-wand")));
                return;
            }

            bool forSale = P.PlotPrice >= 0;
            bool mine = P.PlotOwner == P.PlayerName;
            bool cityLand = string.IsNullOrEmpty(P.PlotOwner);
            bool ourCity = HasCity && P.PlotCity == P.CityName;
            bool maySell = mine || (cityLand && ourCity && P.IsMayor);

            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-name"), Key = "pname",
                              Value = PlotLabel() },
                new CardRow { Label = T("gui-label-city"), Value = Pretty(P.PlotCity), Key = "pcity" },
                new CardRow { Label = T("gui-label-owner"), Value = cityLand ? T("gui-value-city-land") : P.PlotOwner, Key = "powner" },
                new CardRow { Label = T("gui-label-size"), Value = $"{P.PlotSizeX} x {P.PlotSizeY} x {P.PlotSizeZ}", Key = "psize",
                              Tooltip = T("gui-label-size-tooltip") },
                new CardRow { Label = T("gui-label-volume-plot"), Value = P.PlotVolume.ToString(), Key = "pvolume" },
                new CardRow { Label = T("gui-label-parts"), Value = P.PlotBoxes.ToString(), Key = "pboxes", Tooltip = T("gui-label-parts-tooltip") },
                new CardRow { Label = T("gui-label-price"), Value = forSale ? P.PlotPrice.ToString() : T("gui-value-not-for-sale"), Key = "pprice",
                              ValueColor = forSale ? GuiColors.Success : GuiColors.Label },
                new CardRow { Label = T("gui-label-plot-type"), Value = PlotTypeName(P.PlotType), Key = "ptype" },
                new CardRow { Label = T("gui-label-plot-fee"), Value = P.PlotFee.ToString(), Key = "pfee", Tooltip = T("gui-label-plot-fee-tooltip") },
            };

            double yLeft = Card.RowsWithActions(compo, left, left.fixedY, T("gui-section-plot"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (forSale && !mine && ourCity && !(cityLand && P.IsMayor))
                {
                    int price = P.PlotPrice;
                    actions.Add(GuiIcons.Buy, "plot-buy", () =>
                        Open(new ConfirmDialog(T("gui-confirm-buy", price), () => Plot("claim " + price))), T("gui-action-buy"));
                }
                if (maySell)
                {
                    actions.Add(GuiIcons.Sell, "plot-sell", () =>
                        Open(new TextInputDialog(T("gui-prompt-price"), T("gui-button-sell"), price => Plot("fs " + price), true)),
                        T("gui-action-sell"));
                    if (forSale)
                    {
                        actions.Add(GuiIcons.Remove, "plot-nfs", () => Plot("nfs"), T("gui-action-nfs"));
                    }
                }
                if (mine)
                {
                    actions.Add(GuiIcons.Leave, "plot-giveback", () =>
                        Open(new ConfirmDialog(T("gui-confirm-giveback"), () => Plot("unclaim"))), T("gui-action-giveback"));
                }
                if (P.CanManagePlot)
                {
                    actions.Add(GuiIcons.Edit, "plot-rename", () =>
                        Open(new TextInputDialog(T("gui-prompt-plot-name"), T("gui-button-save"), name => Plot("set name " + name))),
                        T("gui-action-plot-name"));
                }
                if (P.CanSetPlotFee)
                {
                    actions.Add(GuiIcons.Edit, "plot-fee", () =>
                        Open(new TextInputDialog(T("gui-prompt-plot-fee", P.MaxPlotFee, P.PlotFee), T("gui-button-save"),
                            fee => Plot("set fee " + fee), true)),
                        T("gui-action-plot-fee"));
                }
                if (P.CanSetPlotType && P.PlotTypes.Count > 0)
                {
                    actions.Add(GuiIcons.PlotType, "plot-type", OpenTypePicker, T("gui-action-plot-type"));
                    // A paid change waits for /agree; the button sends it without typing.
                    if (P.HasEconomy)
                    {
                        actions.Add(GuiIcons.Accept, "plot-agree", Agree, T("gui-action-agree"));
                    }
                }
            });

            bool[] flags = { P.PlotPvp, P.PlotFire, P.PlotBlast, P.PlotNoMobSpawn };
            bool readOnly = !P.CanManagePlot;

            double yRight = PermTable.Rights(compo, right, right.fixedY, Prefix, P.PlotPerms, readOnly,
                (group, type, on) => Plot($"set perm {group} {type} {OnOff(on)}"));
            PermTable.FlagSwitches(compo, right, yRight, Prefix, flags, readOnly,
                (flag, on) => Plot($"set {flag} {OnOff(on)}"));
            if (readOnly)
            {
                Card.Note(compo, left, yLeft, null, T("gui-plot-readonly-hint"));
            }

            ctx.AfterCompose(() => PermTable.ApplyValues(compo, Prefix, P.PlotPerms, flags));
        }

        /// <summary>Every type but the current one, named with its one-time price and daily upkeep.</summary>
        private void OpenTypePicker()
        {
            var types = P.PlotTypes.Where(t => t.Code != P.PlotType).ToList();
            if (types.Count == 0) return;
            string[] codes = types.Select(t => t.Code).ToArray();
            string[] names = types.Select(t => T("gui-plot-type-option", PlotTypeName(t.Code), t.SetCost, t.UnitUpkeep)).ToArray();
            Open(new PickDialog(T("gui-prompt-plot-type"), codes, names, T("gui-button-set"), code => Plot("set type " + code)));
        }
    }
}
