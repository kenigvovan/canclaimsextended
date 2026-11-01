using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// What everyone may do on the city's land, and its flags. A change here is copied onto every
    /// city plot that has no owner of its own, which the page says up front.
    /// </summary>
    public sealed class CitySettingsPage : GuiPage
    {
        public override bool IsVisible(network.GuiStatePacket p)
            => HasCityIn(p) && (p.IsMayor || p.Permissions.Any(x => x.StartsWith("CITY_SET_")));

        private const string Prefix = "cityset";

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            bool canRights = MayorOr("CITY_SET_ALL", "CITY_SET_PLOT_ACCESS_PERMISSIONS");
            bool canFlags = MayorOr("CITY_SET_ALL", "CITY_SET_PVP", "CITY_SET_FIRE", "CITY_SET_BLAST", "CITY_SET_MOBSPAWN");
            bool canOpen = MayorOr("CITY_SET_ALL", "CITY_SET_OPEN_STATE");
            bool canMsg = MayorOr("CITY_SET_ALL", "CITY_SET_INV_MSG");

            bool[] flags = { P.CityPvp, P.CityFire, P.CityBlast, P.CityNoMobSpawn };

            double yLeft = PermTable.Rights(compo, left, left.fixedY, Prefix, P.CityPerms, !canRights,
                (group, type, on) => City($"set perm {group} {type} {OnOff(on)}"));
            Card.Note(compo, left, yLeft, null, T("gui-citysettings-hint"));

            double yRight = PermTable.FlagSwitches(compo, right, right.fixedY, Prefix, flags, !canFlags,
                (flag, on) => City($"set {flag} {OnOff(on)}"));

            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-open"), Value = T(P.OpenCity ? "gui-yes" : "gui-no"), Key = "open",
                              ValueColor = P.OpenCity ? GuiColors.Success : GuiColors.Label, Tooltip = T("gui-label-open-tooltip") },
                new CardRow { Label = T("gui-label-invmsg"), Value = string.IsNullOrEmpty(P.InvMsg) ? "-" : P.InvMsg, Key = "invmsg" },
            };
            Card.RowsWithActions(compo, right, yRight, T("gui-section-joining"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (canOpen)
                {
                    actions.Add(GuiIcons.Accept, "toggle-open", () => City("set open " + OnOff(!P.OpenCity)),
                        T(P.OpenCity ? "gui-action-close-city" : "gui-action-open-city"));
                }
                if (canMsg)
                {
                    actions.Add(GuiIcons.Edit, "edit-invmsg", () =>
                        Open(new TextInputDialog(T("gui-prompt-invmsg"), T("gui-button-save"), text => City("set invmsg " + text))),
                        T("gui-action-invmsg"));
                }
            });

            ctx.AfterCompose(() => PermTable.ApplyValues(compo, Prefix, P.CityPerms, flags));
        }
    }
}
