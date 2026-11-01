using System.Collections.Generic;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The player: invitations waiting for an answer, and friends.</summary>
    public sealed class PlayerPage : GuiPage
    {
        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-name"), Value = P.PlayerName, Key = "pl-name" },
                new CardRow { Label = T("gui-label-city"), Value = HasCity ? Pretty(P.CityName) : "-", Key = "pl-city" },
            };
            if (P.HasEconomy)
            {
                rows.Add(new CardRow { Label = T("gui-label-balance"), Value = P.Balance, Key = "pl-balance" });
            }
            // Client-side display options; they change nothing on the server.
            bool banner = ClientSetup.BannerEnabled;
            bool markers = ClientSetup.MarkersAlwaysShown;
            rows.Add(new CardRow { Label = T("gui-label-banner"), Value = T(banner ? "gui-value-on" : "gui-value-off"), Key = "pl-banner" });
            rows.Add(new CardRow { Label = T("gui-label-markers"), Value = T(markers ? "gui-value-always" : "gui-value-with-wand"), Key = "pl-markers" });
            bool adminTabs = !ClientSetup.AdminTabsHidden;
            if (P.IsAdmin)
            {
                rows.Add(new CardRow { Label = T("gui-label-admin-tabs"), Value = T(adminTabs ? "gui-value-on" : "gui-value-off"), Key = "pl-admintabs" });
            }
            double yLeft = Card.RowsWithActions(compo, left, left.fixedY, T("gui-section-player"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.City, "pl-banner-toggle", () => { ClientSetup.ToggleBanner(); Gui.BuildMainWindow(); },
                    T(banner ? "gui-action-banner-off" : "gui-action-banner-on"));
                actions.Add(GuiIcons.Wand, "pl-markers-toggle", () => { ClientSetup.ToggleMarkers(); Gui.BuildMainWindow(); },
                    T(markers ? "gui-action-markers-wand" : "gui-action-markers-always"));
                if (P.IsAdmin)
                {
                    actions.Add(GuiIcons.Grant, "pl-admintabs-toggle", () =>
                    {
                        ClientSetup.ToggleAdminTabs();
                        // Seeing the window as a player: no acting for a city behind hidden tabs.
                        if (ClientSetup.AdminTabsHidden && GuiStateClient.ActingCity.Length > 0)
                        {
                            GuiStateClient.ActingCity = "";
                            GuiStateClient.Request();
                        }
                        Gui.BuildMainWindow();
                    },
                        T(adminTabs ? "gui-action-admin-tabs-hide" : "gui-action-admin-tabs-show"));
                }
            });
            yLeft = InvitationsCard(compo, left, yLeft, 4);
            if (P.IsPrisoned)
            {
                PrisonerCard(compo, left, yLeft);
            }

            var friends = new List<ItemRow>();
            foreach (string name in P.Friends)
            {
                string target = name;
                var row = new ItemRow { Label = name, Key = "fr-" + name };
                row.Buttons.Add(new RowButton(GuiIcons.Remove, () => Citizen("friend remove " + target), T("gui-action-unfriend")));
                friends.Add(row);
            }
            Card.Items(compo, right, right.fixedY, T("gui-section-friends", P.Friends.Count), Limit(friends, 12), T("gui-empty-friends"),
                slot => new ActionRow(compo, slot).Add(GuiIcons.Invite, "friend-add", () =>
                    Open(new TextInputDialog(T("gui-prompt-friend"), T("gui-button-add"), name => Citizen("friend add " + name))),
                    T("gui-action-friend")));
        }

        /// <summary>The sentence: hours left, the ransom and a button to pay it.</summary>
        private void PrisonerCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-prison-hours"), Value = P.PrisonHoursLeft.ToString(), Key = "pr-hours" },
                new CardRow { Label = T("gui-label-ransom"), Value = P.PrisonRansom, Key = "pr-ransom" },
            };
            if (!P.HasEconomy)
            {
                Card.Rows(compo, column, y, T("gui-section-prisoner"), rows);
                return;
            }
            string ransom = P.PrisonRansom;
            Card.RowsWithActions(compo, column, y, T("gui-section-prisoner"), rows, slot =>
                new ActionRow(compo, slot).Add(GuiIcons.Buy, "pr-payout", () =>
                    Open(new ConfirmDialog(T("gui-confirm-ransom", ransom), () => Citizen("prison payout"))),
                    T("gui-action-ransom")));
        }

        /// <summary>Invitations from cities, with accept and refuse on each. Also shown on the city page.</summary>
        internal static double InvitationsCard(GuiComposer compo, ElementBounds column, double y, int max = 6)
        {
            var invites = InvitationRows(P.InvitationsReceived, "inv-", city => City("join " + city), city => City("deny " + city));
            return Card.Items(compo, column, y, T("gui-section-invitations"), Limit(invites, max), T("gui-empty-invitations"));
        }
    }
}
