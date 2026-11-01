using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// Block rules for admins (PLOT_TYPE_ONLY_BLOCKS): the block in hand with the keys that would
    /// match it, and the rules there are. Every button runs /cadmin blockrule.
    /// </summary>
    public sealed class BlockRulesPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => ShowAdminTabs(p);
        // The block in hand changes with no command from the window.
        public override bool Polls(GuiStatePacket p) => true;

        private const string AllTypes = "__all__";

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = HandCard(compo, left, left.fixedY);
            Card.Note(compo, left, yLeft, null, T("gui-blockrules-hint"));
            RulesCard(compo, right, right.fixedY);
        }

        /// <summary>The block in hand: its code, what holds it now, and one line per key it could be held by.</summary>
        private double HandCard(GuiComposer compo, ElementBounds column, double y)
        {
            var items = new List<ItemRow>();
            if (!string.IsNullOrEmpty(P.HandBlockCode))
            {
                items.Add(new ItemRow
                {
                    Label = P.HandBlockCode,
                    Value = P.HandBlockAllowed.Count == 0 ? T("gui-blockrule-free") : TypeNames(P.HandBlockAllowed),
                    ValueColor = P.HandBlockAllowed.Count == 0 ? GuiColors.Label : null,
                    Key = "br-hand"
                });
                for (int i = 0; i < P.HandBlockKeys.Count; i++)
                {
                    string key = P.HandBlockKeys[i];
                    GuiBlockRuleEntry rule = P.BlockRules.Find(r => r.Key == key);
                    items.Add(new ItemRow
                    {
                        Label = key,
                        Value = rule == null ? "" : TypeNames(rule.Types),
                        Key = "br-hand-key-" + i,
                        Buttons = { new RowButton(GuiIcons.PlotType, () => PickType(key), T("gui-action-blockrule-add")) }
                    });
                }
            }
            return Card.Items(compo, column, y, T("gui-section-blockrules-hand"), Limit(items, 9), T("gui-empty-hand-block"));
        }

        private double RulesCard(GuiComposer compo, ElementBounds column, double y)
        {
            var items = new List<ItemRow>();
            for (int i = 0; i < P.BlockRules.Count; i++)
            {
                GuiBlockRuleEntry rule = P.BlockRules[i];
                string key = rule.Key;
                items.Add(new ItemRow
                {
                    Label = key,
                    Value = T("gui-blockrule-value", TypeNames(rule.Types), rule.Blocks),
                    // A key that matches nothing is most likely a typo.
                    ValueColor = rule.Blocks == 0 ? GuiColors.Danger : null,
                    Key = "br-rule-" + i,
                    Buttons =
                    {
                        new RowButton(GuiIcons.PlotType, () => PickType(key), T("gui-action-blockrule-add")),
                        new RowButton(GuiIcons.Remove, () => PickRemove(rule), T("gui-action-blockrule-remove"))
                    }
                });
            }
            return Card.Items(compo, column, y, T("gui-section-blockrules"), Limit(items, 14), T("gui-empty-blockrules"),
                slot => new ActionRow(compo, slot).Add(GuiIcons.Accept, "br-add-key", () =>
                    Open(new TextInputDialog(T("gui-blockrule-key-prompt"), T("gui-button-next"),
                        // The text dialog closes itself after this; the type picker opens on the next frame.
                        key => Gui.Capi.Event.EnqueueMainThreadTask(() => PickType(key.Replace(" ", "")), "cx-blockrule-type"))),
                    T("gui-action-blockrule-new")));
        }

        /// <summary>A plot type the key does not hold to yet, then /cadmin blockrule add.</summary>
        private void PickType(string key)
        {
            GuiBlockRuleEntry rule = P.BlockRules.Find(r => r.Key == key);
            string[] codes = P.EnabledPlotTypes.Where(c => rule == null || !rule.Types.Contains(c)).ToArray();
            if (codes.Length == 0) return;
            Open(new PickDialog(T("gui-prompt-blockrule-type", key), codes, codes.Select(PlotTypeName).ToArray(),
                T("gui-button-add"), code => Cadmin($"blockrule add {key} {code}")));
        }

        /// <summary>One type off the rule, or the whole rule.</summary>
        private void PickRemove(GuiBlockRuleEntry rule)
        {
            string key = rule.Key;
            if (rule.Types.Count <= 1)
            {
                Open(new ConfirmDialog(T("gui-confirm-blockrule-remove", key), () => Cadmin("blockrule remove " + key)));
                return;
            }
            string[] codes = rule.Types.Append(AllTypes).ToArray();
            string[] names = rule.Types.Select(PlotTypeName).Append(T("gui-blockrule-remove-all")).ToArray();
            Open(new PickDialog(T("gui-prompt-blockrule-remove", key), codes, names, T("gui-blockrule-button-remove"),
                code => Cadmin(code == AllTypes ? "blockrule remove " + key : $"blockrule remove {key} {code}")));
        }

        private static string TypeNames(IEnumerable<string> codes) => string.Join(", ", codes.Select(PlotTypeName));
    }
}
