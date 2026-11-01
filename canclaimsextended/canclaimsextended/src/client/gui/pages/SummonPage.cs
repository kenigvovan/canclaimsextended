using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// Summon points of the city: what a teleport costs and takes, the player's cooldown, and the
    /// points themselves - also summon plots whose point is not placed yet.
    /// </summary>
    public sealed class SummonPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => HasCityIn(p) && p.SummonAllowed;

        // While a teleport or its cooldown runs, so the countdown and the buttons keep up.
        public override bool Polls(GuiStatePacket p) => p.SummonWaiting || p.SummonCooldownLeft != 0;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = InfoCard(compo, left, left.fixedY);
            Card.Note(compo, left, yLeft, null, T("gui-summon-hint", P.SummonDelaySeconds));
            PointsCard(compo, right, right.fixedY);
        }

        private double InfoCard(GuiComposer compo, ElementBounds column, double y)
        {
            int plots = P.SummonPoints.Count;
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-summon-plots"), Value = plots + " / " + P.SummonMaxPlots, Key = "sm-plots",
                              ValueColor = plots > P.SummonMaxPlots ? GuiColors.Danger : GuiColors.Value },
                new CardRow { Label = T("gui-label-summon-delay"), Value = T("gui-value-seconds", P.SummonDelaySeconds), Key = "sm-delay" },
                new CardRow
                {
                    Label = T("gui-label-summon-cooldown"),
                    Value = P.SummonWaiting ? T("gui-value-summon-waiting")
                        : P.SummonCooldownLeft > 0 ? T("gui-value-seconds", P.SummonCooldownLeft)
                        : T("gui-value-summon-ready"),
                    ValueColor = P.SummonWaiting || P.SummonCooldownLeft == 0 ? GuiColors.Success : GuiColors.Value,
                    Key = "sm-cooldown"
                },
            };
            if (P.HasEconomy)
            {
                rows.Insert(1, new CardRow { Label = T("gui-label-summon-cost"), Value = P.SummonPayment, Key = "sm-cost" });
            }
            if (P.SummonMinPlayers > 0)
            {
                rows.Add(new CardRow { Label = T("gui-label-summon-min-players"), Value = P.SummonMinPlayers.ToString(), Key = "sm-minplayers" });
            }

            bool canSet = MayorOr("CITY_SET_SUMMON");
            if (!canSet && !P.SummonWaiting)
            {
                return Card.Rows(compo, column, y, T("gui-section-summon"), rows);
            }
            return Card.RowsWithActions(compo, column, y, T("gui-section-summon"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (P.SummonWaiting)
                {
                    actions.Add(GuiIcons.Remove, "sm-cancel", () => City("summon cancel"), T("gui-action-summon-cancel"));
                }
                if (canSet)
                {
                    actions.Add(GuiIcons.Summon, "sm-set", () =>
                        Open(new TextInputDialog(T("gui-prompt-summon-name"), T("gui-button-set"), name => City("summon set " + name))),
                        T("gui-action-summon-set"));
                }
            });
        }

        private void PointsCard(GuiComposer compo, ElementBounds column, double y)
        {
            // Without an economy the summon is free.
            string payment = P.HasEconomy ? P.SummonPayment : "0";
            bool canTeleport = !P.IsPrisoned && !P.SummonWaiting && P.SummonCooldownLeft == 0;
            var points = new List<ItemRow>();
            foreach (GuiSummonEntry point in P.SummonPoints)
            {
                string coords = point.X + " " + point.Y + " " + point.Z;
                if (!point.IsSet)
                {
                    string plotName = string.IsNullOrEmpty(point.PlotName) ? PlotTypeName("summon") : Pretty(point.PlotName);
                    points.Add(new ItemRow
                    {
                        Label = plotName,
                        Value = T("gui-value-summon-not-set"),
                        ValueColor = GuiColors.Danger,
                        Tooltip = T("gui-tooltip-summon-not-set", coords),
                        Key = "sm-unset-" + points.Count
                    });
                    continue;
                }
                string name = point.Name;
                var row = new ItemRow
                {
                    Label = name,
                    Value = coords,
                    Tooltip = string.IsNullOrEmpty(point.PlotName) ? null : Pretty(point.PlotName),
                    Key = "sm-" + name
                };
                if (canTeleport)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Summon, () =>
                        Open(new ConfirmDialog(T("gui-confirm-summon", name, payment), () => City("summon tp " + name))),
                        T("gui-action-summon")));
                }
                points.Add(row);
            }
            int set = P.SummonPoints.Count(p => p.IsSet);
            Card.Items(compo, column, y, T("gui-section-summon-points", set), Limit(points, 10), T("gui-empty-summon"));
        }
    }
}
