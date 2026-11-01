using System;
using System.Collections.Generic;
using System.Globalization;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>The player's city: what it is, its money, its people. Without a city: how to found one.</summary>
    public sealed class CityPage : GuiPage
    {
        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            if (!HasCity)
            {
                BuildNoCity(ctx, left, right);
                return;
            }

            var compo = ctx.Compo;
            double yLeft = CityCard(compo, left, left.fixedY);
            if (P.HasEconomy)
            {
                yLeft = TreasuryCard(compo, left, yLeft);
            }
            yLeft = EmblemCard(compo, left, yLeft);
            double yRight = CitizensCard(compo, right, right.fixedY);
            if (MayorOr("SHOW_INVITES_SENT"))
            {
                InvitesSentCard(compo, right, yRight);
            }
        }

        private double CityCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-name"), Value = Pretty(P.CityName), Key = "name" },
                new CardRow { Label = T("gui-label-mayor"), Value = P.MayorName, Key = "mayor" },
                new CardRow { Label = T("gui-label-founded"), Value = DateText(P.CreatedAt), Key = "founded" },
                new CardRow { Label = T("gui-label-chunks"),
                              Value = P.ChunksCount + " / " + P.MaxChunks + (P.ExtraChunksCount > 0 ? " + " + P.ExtraChunksCount : ""),
                              Key = "chunks", Tooltip = T("gui-label-chunks-tooltip", P.MaxExtraChunks) },
                new CardRow { Label = T("gui-label-plots"), Value = P.PlotsCount + " / " + P.MaxPlots, Key = "plots" },
                new CardRow { Label = T("gui-label-volume"), Value = P.Volume + " / " + P.MaxVolume, Key = "volume",
                              Tooltip = T("gui-label-volume-tooltip") },
                new CardRow { Label = T("gui-label-open"), Value = T(P.OpenCity ? "gui-yes" : "gui-no"), Key = "open",
                              ValueColor = P.OpenCity ? GuiColors.Success : GuiColors.Label },
            };
            return Card.RowsWithActions(compo, column, y, T("gui-section-city"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (!P.IsMayor)
                {
                    actions.Add(GuiIcons.Leave, "city-leave", () =>
                        Open(new ConfirmDialog(T("gui-confirm-leave", P.CityName), () => City("leave"))), T("gui-action-leave"));
                }
                else
                {
                    actions.Add(GuiIcons.Remove, "city-delete", () =>
                        Open(new TextInputDialog(T("gui-prompt-delete-city", P.CityName), T("gui-button-delete"),
                            name => City("delete " + name))), T("gui-action-delete-city"));
                }
                actions.Add(GuiIcons.Wand, "city-wand", () => City("wand"), T("gui-action-wand"));
            });
        }

        private double TreasuryCard(GuiComposer compo, ElementBounds column, double y)
        {
            var money = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-treasury"), Value = P.CityBalance, Key = "treasury" },
                new CardRow { Label = T("gui-label-upkeep"), Value = P.Upkeep, Key = "upkeep", Tooltip = T("gui-label-upkeep-tooltip") },
                new CardRow { Label = T("gui-label-debt"), Value = string.IsNullOrEmpty(P.Debt) ? "0" : P.Debt, Key = "debt",
                              ValueColor = IsZero(P.Debt) ? GuiColors.Label : GuiColors.Danger },
                new CardRow { Label = T("gui-label-fee"), Value = P.CityFee.ToString(), Key = "fee", Tooltip = T("gui-label-fee-tooltip") },
            };
            if (!MayorOr("CITY_SET_GLOBAL_FEE", "CITY_SET_ALL"))
            {
                return Card.Rows(compo, column, y, T("gui-section-treasury"), money);
            }
            return Card.RowsWithActions(compo, column, y, T("gui-section-treasury"), money, slot =>
                new ActionRow(compo, slot).Add(GuiIcons.Edit, "city-fee", () =>
                    Open(new TextInputDialog(T("gui-prompt-fee", P.MaxCityFee.ToString(CultureInfo.InvariantCulture)), T("gui-button-save"),
                        fee => City("set fee " + fee), true)),
                    T("gui-action-fee")));
        }

        private const double EmblemSize = 56;

        /// <summary>The city's arms, with the way to the editor for those who may change them.</summary>
        private double EmblemCard(GuiComposer compo, ElementBounds column, double y)
        {
            double height = Card.HeaderHeight + EmblemSize + Card.Padding * 2;
            ElementBounds inner = Card.Frame(compo, column, y, height, T("gui-section-emblem"));

            compo.AddEmblem(P.CityEmblem, inner.FlatCopy().WithFixedSize(EmblemSize, EmblemSize), "city-emblem");

            var text = inner.FlatCopy().WithFixedSize(inner.fixedWidth - EmblemSize - Card.Gap - Card.ActionSize - Card.Gap, EmblemSize);
            text.fixedX += EmblemSize + Card.Gap;
            compo.AddStaticText(string.IsNullOrEmpty(P.CityEmblem) ? T("gui-emblem-none") : T("gui-emblem-has"),
                CairoFont.WhiteSmallText().WithColor(GuiColors.Label), text, "city-emblem-text");

            if (P.CanSetEmblem)
            {
                var slot = inner.FlatCopy().WithFixedSize(Card.ActionSize, Card.ActionSize);
                slot.fixedX += inner.fixedWidth - Card.ActionSize;
                new ActionRow(compo, slot).Add(GuiIcons.Edit, "city-emblem-edit", () => GoTo(EnumGuiTab.Emblem), T("gui-action-emblem"));
            }
            return y + height + Card.Gap;
        }

        private double CitizensCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool canKick = Has("CITY_KICK");
            var citizens = new List<ItemRow>();
            foreach (string name in P.Citizens)
            {
                var row = new ItemRow { Label = name, Key = "cit-" + name };
                if (name == P.MayorName)
                {
                    row.Value = T("gui-value-mayor");
                }
                else if (canKick && name != P.PlayerName)
                {
                    string target = name;
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () =>
                        Open(new ConfirmDialog(T("gui-confirm-kick", target), () => City("kick " + target))), T("gui-action-kick")));
                }
                citizens.Add(row);
            }
            Action<ElementBounds> invite = null;
            if (Has("CITY_INVITE"))
            {
                invite = slot => new ActionRow(compo, slot).Add(GuiIcons.Invite, "city-invite", () =>
                    Open(new TextInputDialog(T("gui-prompt-invite"), T("gui-button-invite"), name => City("invite " + name))),
                    T("gui-action-invite"));
            }
            return Card.Items(compo, column, y, T("gui-section-citizens", P.Citizens.Count), Limit(citizens, 9),
                T("gui-empty-citizens"), invite);
        }

        private void InvitesSentCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool canUninvite = Has("CITY_UNINVITE");
            var sent = new List<ItemRow>();
            foreach (string name in P.InvitationsSent)
            {
                var row = new ItemRow { Label = name, Key = "sent-" + name };
                if (canUninvite)
                {
                    string target = name;
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => City("uninvite " + target), T("gui-action-uninvite")));
                }
                sent.Add(row);
            }
            Card.Items(compo, column, y, T("gui-section-invites-sent"), Limit(sent, 5), T("gui-empty-invites-sent"));
        }

        private void BuildNoCity(PageBuildContext ctx, ElementBounds left, ElementBounds right)
        {
            var compo = ctx.Compo;
            string hint = T("gui-found-hint", P.CommandCity);
            if (!P.ChunkCanFound && !string.IsNullOrEmpty(P.HereCity))
            {
                hint += "\n" + T("gui-found-taken", Pretty(P.HereCity));
            }
            Card.Note(compo, left, left.fixedY, T("gui-section-found"), hint, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (P.ChunkCanFound)
                {
                    actions.Add(GuiIcons.Found, "found-city", () =>
                        Open(new TextInputDialog(T("gui-prompt-found"), T("gui-button-found"), name => City("create " + name))),
                        T("gui-action-found"));
                    if (P.HasEconomy)
                    {
                        actions.Add(GuiIcons.Accept, "found-agree", Agree, T("gui-action-agree"));
                    }
                }
            });

            PlayerPage.InvitationsCard(compo, right, right.fixedY);
        }

        private static string DateText(long epochSeconds)
        {
            if (epochSeconds <= 0) return "-";
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToLocalTime().ToString("dd.MM.yyyy");
        }

        private static bool IsZero(string value)
        {
            return string.IsNullOrEmpty(value)
                || (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == 0);
        }
    }
}
