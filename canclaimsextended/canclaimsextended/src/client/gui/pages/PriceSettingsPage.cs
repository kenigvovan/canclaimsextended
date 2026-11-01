using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.economy;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>Price settings for admins: every value with a button that runs /cadmin setcfg.</summary>
    public sealed class PriceSettingsPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => ShowAdminTabs(p);

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = GroupCard(compo, left, left.fixedY, PriceSettings.GroupLand, T("gui-section-prices-land"));
            GroupCard(compo, left, yLeft, PriceSettings.GroupUpkeep, T("gui-section-prices-upkeep"));

            double yRight = TypesCard(compo, right, right.fixedY);
            GroupCard(compo, right, yRight, PriceSettings.GroupOther, T("gui-section-cfg-other"));
        }

        private double GroupCard(GuiComposer compo, ElementBounds column, double y, string group, string title)
        {
            var items = new List<ItemRow>();
            foreach (var entry in P.PriceSettings.Where(s => s.Group == group))
            {
                string label = T("gui-cfg-" + entry.Key);
                items.Add(new ItemRow
                {
                    Label = label,
                    Value = entry.Value,
                    Tooltip = T("gui-cfg-" + entry.Key + "-tooltip") + "\n" + T("gui-cfg-range", entry.Min, entry.Max),
                    Key = "cfg-" + entry.Key,
                    Buttons = { EditButton(entry, label, "gui-action-cfg-edit") }
                });
            }
            return Card.Items(compo, column, y, title, items, T("gui-empty-cfg"));
        }

        /// <summary>One line per plot type: the one-time price and the daily upkeep, each with its own button.</summary>
        private double TypesCard(GuiComposer compo, ElementBounds column, double y)
        {
            var items = new List<ItemRow>();
            foreach (var setCost in P.PriceSettings.Where(s => s.Key.StartsWith(PriceSettings.TypeSetCostPrefix)))
            {
                string code = setCost.Key.Substring(PriceSettings.TypeSetCostPrefix.Length);
                GuiPriceSettingEntry upkeep = Setting(PriceSettings.TypeUpkeepPrefix + code);
                if (upkeep == null) continue;

                string name = PlotTypeName(code);
                bool enabled = P.EnabledPlotTypes.Contains(code);
                var row = new ItemRow
                {
                    Label = enabled ? name : T("gui-cfg-type-disabled", name),
                    Value = T("gui-price-type-value", setCost.Value, upkeep.Value),
                    ValueColor = enabled ? null : GuiColors.Label,
                    Tooltip = T("gui-price-type-tooltip", setCost.Value, upkeep.Value),
                    Key = "cfg-type-" + code
                };
                row.Buttons.Add(EditButton(setCost, T("gui-cfg-type-set-cost", name), "gui-action-cfg-type-set-cost", GuiIcons.Sell));
                row.Buttons.Add(EditButton(upkeep, T("gui-cfg-type-upkeep", name), "gui-action-cfg-type-upkeep"));
                items.Add(row);
            }
            return Card.Items(compo, column, y, T("gui-section-prices-types"), items, T("gui-empty-plot-types"));
        }

        private RowButton EditButton(GuiPriceSettingEntry entry, string label, string tooltipKey, string icon = GuiIcons.Edit)
        {
            string key = entry.Key;
            string prompt = T("gui-cfg-prompt", label, entry.Value, entry.Min, entry.Max)
                + (entry.Integer ? "\n" + T("gui-cfg-integer") : "");
            // A space would split the value into two command arguments.
            return new RowButton(icon, () =>
                Open(new TextInputDialog(prompt, T("gui-button-save"), value => Cadmin("setcfg " + key + " " + value.Replace(" ", "")))),
                T(tooltipKey));
        }
    }
}
