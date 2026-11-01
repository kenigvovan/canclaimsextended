using System;
using System.Collections.Generic;
using System.Globalization;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.economy;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// What land costs on this server, for every player. How the numbers combine is behind a
    /// button: opened, it takes the place of the plot types, as the window has no scrolling.
    /// </summary>
    public sealed class PricesPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p) => p.HasEconomy;

        // The example the explanation counts with.
        private const long ExampleSide = 16;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = LandCard(compo, left, left.fixedY);
            UpkeepCard(compo, left, yLeft);

            if (State.PricesExplainOpen)
            {
                ExplainCard(compo, right, right.fixedY);
                return;
            }
            double yRight = TypesCard(compo, right, right.fixedY);
            Card.Note(compo, right, yRight, T("gui-section-prices-explain"), T("gui-prices-explain-closed"), slot =>
                new ActionRow(compo, slot).Add(GuiIcons.Info, "prices-explain-open", () => SetExplain(true), T("gui-action-prices-explain-open")));
        }

        private void SetExplain(bool open)
        {
            State.PricesExplainOpen = open;
            Gui.BuildMainWindow();
        }

        private static string V(string key) => Setting(key)?.Value ?? "-";

        private static double Num(string key)
        {
            string value = Setting(key)?.Value;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        }

        private static string Money(double value) => MoneyFormat.Format(value);

        private double LandCard(GuiComposer compo, ElementBounds column, double y)
        {
            double refund = Num("refund_percent");
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-price-unit"), Value = T("gui-price-unit-value", V("blocks_per_unit")), Key = "price-unit",
                              Tooltip = T("gui-price-unit-tooltip") },
                new CardRow { Label = T("gui-price-claim"), Value = V("claim_price_per_unit"), Key = "price-claim",
                              Tooltip = T("gui-price-claim-tooltip") },
                new CardRow { Label = T("gui-price-city"), Value = V("new_city_cost"), Key = "price-city",
                              Tooltip = T("gui-price-city-tooltip") },
                new CardRow { Label = T("gui-price-chunk"), Value = V("chunk_claim_price"), Key = "price-chunk",
                              Tooltip = T("gui-price-chunk-tooltip") },
                new CardRow { Label = T("gui-price-outpost"), Value = T("gui-price-plus-chunk", V("outpost_chunk_cost")), Key = "price-outpost",
                              Tooltip = T("gui-price-outpost-tooltip") },
                new CardRow { Label = T("gui-price-extra"), Value = T("gui-price-plus-chunk", V("extra_chunk_cost")), Key = "price-extra",
                              Tooltip = T("gui-price-extra-tooltip") },
                new CardRow { Label = T("gui-price-refund"), Value = refund > 0 ? Money(refund) + "%" : T("gui-no"), Key = "price-refund",
                              ValueColor = refund > 0 ? null : GuiColors.Label,
                              Tooltip = T("gui-price-refund-tooltip", Money(Num("refund_min_age_seconds") / 60)) },
            };
            return Card.Rows(compo, column, y, T("gui-section-prices-land"), rows);
        }

        private void UpkeepCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-price-base-care"), Value = V("city_base_care"), Key = "price-care",
                              Tooltip = T("gui-price-base-care-tooltip") },
                new CardRow { Label = T("gui-price-chunk-upkeep"), Value = V("chunk_upkeep"), Key = "price-chunk-upkeep",
                              Tooltip = T("gui-price-chunk-upkeep-tooltip") },
                new CardRow { Label = T("gui-price-nopvp"), Value = V("no_pvp_flag_cost"), Key = "price-nopvp",
                              Tooltip = T("gui-price-flag-tooltip") },
                new CardRow { Label = T("gui-price-nomobs"), Value = V("no_mobspawn_flag_cost"), Key = "price-nomobs",
                              Tooltip = T("gui-price-flag-tooltip") },
                new CardRow { Label = T("gui-price-max-debt"), Value = V("max_debt"), Key = "price-debt",
                              Tooltip = T("gui-price-max-debt-tooltip") },
            };
            if (HasCity)
            {
                rows.Add(new CardRow { Label = T("gui-price-your-city"), Value = P.Upkeep, Key = "price-yours",
                                       ValueColor = GuiColors.Section, Tooltip = T("gui-label-upkeep-tooltip") });
            }
            Card.Rows(compo, column, y, T("gui-section-prices-upkeep"), rows);
        }

        private double TypesCard(GuiComposer compo, ElementBounds column, double y)
        {
            var items = new List<ItemRow>();
            foreach (string code in P.EnabledPlotTypes)
            {
                string set = V(PriceSettings.TypeSetCostPrefix + code);
                string upkeep = V(PriceSettings.TypeUpkeepPrefix + code);
                items.Add(new ItemRow
                {
                    Label = PlotTypeName(code),
                    Value = T("gui-price-type-value", set, upkeep),
                    Tooltip = T("gui-price-type-tooltip", set, upkeep),
                    Key = "price-type-" + code
                });
            }
            return Card.Items(compo, column, y, T("gui-section-prices-types"), items, T("gui-empty-plot-types"));
        }

        /// <summary>The rules in words, with this server's numbers and a worked example.</summary>
        private void ExplainCard(GuiComposer compo, ElementBounds column, double y)
        {
            double perUnit = Math.Max(1, Num("blocks_per_unit"));
            long volume = ExampleSide * ExampleSide * ExampleSide;
            long units = Math.Max(1, (long)Math.Ceiling(volume / perUnit));
            double claim = units * Num("claim_price_per_unit");
            double upkeep = units * Num(PriceSettings.TypeUpkeepPrefix + "default");

            string text = T("gui-prices-explain",
                V("blocks_per_unit"), V("claim_price_per_unit"), V("city_base_care"), V("max_debt"),
                ExampleSide, volume, units, Money(claim), Money(upkeep),
                P.ChunkSize, V("chunk_claim_price"), V("chunk_upkeep"));
            Card.Note(compo, column, y, T("gui-section-prices-explain"), text, slot =>
                new ActionRow(compo, slot).Add(GuiIcons.Back, "prices-explain-close", () => SetExplain(false), T("gui-action-prices-explain-close")));
        }
    }
}
