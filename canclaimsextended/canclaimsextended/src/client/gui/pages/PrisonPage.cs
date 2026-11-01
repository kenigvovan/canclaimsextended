using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The city's criminals and its prison plots with their cells.</summary>
    public sealed class PrisonPage : GuiPage
    {
        static readonly string[] CriminalRights = { "CITY_ADD_CRIMINAL", "CITY_REMOVE_CRIMINAL", "CITY_CRIMINAL_ALL" };
        static readonly string[] PrisonRights = { "CITY_PRISON_LIST", "CITY_PRISON_ADD_CELL", "CITY_PRISON_REMOVE_CELL", "CITY_PRISON_ALL" };

        /// <summary>The tab shows to the mayor and to whoever holds a criminal or prison right.</summary>
        public override bool IsVisible(GuiStatePacket p)
        {
            if (string.IsNullOrEmpty(p.CityName)) return false;
            if (p.IsMayor) return true;
            return CriminalRights.Concat(PrisonRights).Any(r => p.Permissions != null && p.Permissions.Contains(r));
        }

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = CriminalsCard(compo, left, left.fixedY);
            Card.Note(compo, left, yLeft, null, T("gui-prison-hint"));
            PrisonsCard(compo, right, right.fixedY);
        }

        private double CriminalsCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool canRemove = MayorOr("CITY_REMOVE_CRIMINAL", "CITY_CRIMINAL_ALL");
            var criminals = new List<ItemRow>();
            foreach (string name in P.Criminals)
            {
                string target = name;
                var row = new ItemRow { Label = name, Key = "crim-" + name };
                if (canRemove)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => City("criminal remove " + target), T("gui-action-criminal-remove")));
                }
                criminals.Add(row);
            }
            System.Action<ElementBounds> actions = null;
            if (MayorOr("CITY_ADD_CRIMINAL", "CITY_CRIMINAL_ALL"))
            {
                actions = slot => new ActionRow(compo, slot).Add(GuiIcons.Prison, "crim-add", () =>
                    Open(new TextInputDialog(T("gui-prompt-criminal"), T("gui-button-add"), name => City("criminal add " + name))),
                    T("gui-action-criminal-add"));
            }
            return Card.Items(compo, column, y, T("gui-section-criminals", P.Criminals.Count), Limit(criminals, 8),
                T("gui-empty-criminals"), actions);
        }

        /// <summary>Prison plots and their cells; cells are placed and removed where the player stands.</summary>
        private void PrisonsCard(GuiComposer compo, ElementBounds column, double y)
        {
            var prisons = new List<ItemRow>();
            for (int i = 0; i < P.Prisons.Count; i++)
            {
                GuiPrisonEntry prison = P.Prisons[i];
                string name = string.IsNullOrEmpty(prison.Name) ? PlotTypeName("prison") : prison.Name;
                prisons.Add(new ItemRow
                {
                    Label = name,
                    Value = T("gui-value-cells", prison.Cells, prison.MaxCells),
                    ValueColor = prison.Cells == 0 ? GuiColors.Danger : GuiColors.Value,
                    Key = "prison-" + i
                });
            }
            bool canAdd = MayorOr("CITY_PRISON_ADD_CELL", "CITY_PRISON_ALL");
            bool canRemove = MayorOr("CITY_PRISON_REMOVE_CELL", "CITY_PRISON_ALL");
            System.Action<ElementBounds> actions = null;
            if (canAdd || canRemove)
            {
                actions = slot =>
                {
                    var row = new ActionRow(compo, slot);
                    if (canAdd) row.Add(GuiIcons.Accept, "cell-add", () => City("prison addcell"), T("gui-action-cell-add"));
                    if (canRemove) row.Add(GuiIcons.Remove, "cell-remove", () => City("prison removecell"), T("gui-action-cell-remove"));
                };
            }
            Card.Items(compo, column, y, T("gui-section-prisons"), Limit(prisons, 6), T("gui-empty-prisons"), actions);
        }
    }
}
