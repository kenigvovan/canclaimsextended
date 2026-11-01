using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The ranks of the city: each opens its own page, the mayor can create new ones.</summary>
    public sealed class RanksPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p) => HasCityIn(p);

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            var mine = P.Ranks.Where(r => r.Holders.Contains(P.PlayerName)).Select(r => r.Name).ToList();
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-ranks-count"), Value = P.Ranks.Count.ToString(), Key = "rk-count" },
                new CardRow { Label = T("gui-label-ranks-yours"), Value = mine.Count == 0 ? "-" : string.Join(", ", mine), Key = "rk-mine" },
            };
            bool canCreate = MayorOr("CITY_CREATE_CITY_RANK");
            double yLeft = canCreate
                ? Card.RowsWithActions(compo, left, left.fixedY, T("gui-section-ranks"), rows, slot =>
                    new ActionRow(compo, slot).Add(GuiIcons.Found, "rank-create", () =>
                        Open(new TextInputDialog(T("gui-prompt-rank-name"), T("gui-button-create"), RankCommands.Create)),
                        T("gui-action-rank-create")))
                : Card.Rows(compo, left, left.fixedY, T("gui-section-ranks"), rows);
            Card.Note(compo, left, yLeft, null, T("gui-ranks-hint"));

            var list = new List<ItemRow>();
            foreach (var rank in P.Ranks)
            {
                string name = rank.Name;
                var row = new ItemRow { Label = name, Value = T("gui-value-holders", rank.Holders.Count), Key = "rk-" + name };
                row.Buttons.Add(new RowButton(GuiIcons.Info, () =>
                {
                    State.SelectedRank = name;
                    GoTo(EnumGuiTab.RankInfo);
                }, T("gui-action-rank-open")));
                list.Add(row);
            }
            Card.Items(compo, right, right.fixedY, T("gui-section-rank-list"), Limit(list, 12), T("gui-empty-ranks"));
        }
    }
}
