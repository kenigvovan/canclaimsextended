using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>One rank: who has it and what it allows. Not in the tab row, so it ends with a way back.</summary>
    public sealed class RankInfoPage : GuiPage
    {
        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;
            NavRow.Build(compo, ctx.Main, 5, new NavButton(GuiIcons.Back, () => GoTo(EnumGuiTab.Ranks), T("gui-action-back")));

            GuiRankEntry rank = P.Ranks.FirstOrDefault(r => r.Name == State.SelectedRank);
            if (rank == null)
            {
                Card.Note(compo, left, left.fixedY, T("gui-section-rank"), T("gui-rank-gone"));
                return;
            }

            bool canDelete = MayorOr("CITY_DELETE_CITY_RANK");
            bool canGrant = MayorOr("CITY_SET_RANK");
            bool canRevoke = MayorOr("CITY_REMOVE_RANK");
            bool canAddPerm = MayorOr("CITY_ADD_PERMISSION_TO_RANK");
            bool canRemovePerm = MayorOr("CITY_REMOVE_PERMISSION_FROM_RANK");
            string rankName = rank.Name;

            var rows = new List<CardRow>
            {
                new CardRow { Label = T("gui-label-name"), Value = rankName, Key = "ri-name" },
                new CardRow { Label = T("gui-label-rank-holders"), Value = rank.Holders.Count.ToString(), Key = "ri-holders" },
                new CardRow { Label = T("gui-label-rank-perms"), Value = rank.Permissions.Count.ToString(), Key = "ri-perms" },
            };
            double yLeft = Card.RowsWithActions(compo, left, left.fixedY, T("gui-section-rank"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (canGrant)
                {
                    // Only citizens who do not hold the rank yet are offered.
                    string[] candidates = P.Citizens.Where(c => !rank.Holders.Contains(c)).ToArray();
                    if (candidates.Length > 0)
                    {
                        actions.Add(GuiIcons.Grant, "rank-grant", () =>
                            Open(new PickDialog(T("gui-prompt-rank-grant", rankName), candidates, candidates, T("gui-button-grant"),
                                player => RankCommands.Grant(rankName, player))), T("gui-action-rank-grant"));
                    }
                }
                if (canAddPerm)
                {
                    string[] missing = P.RankablePermissions.Where(x => !rank.Permissions.Contains(x)).ToArray();
                    if (missing.Length > 0)
                    {
                        string[] names = missing.Select(RankCommands.PermissionName).ToArray();
                        actions.Add(GuiIcons.Rights, "rank-addperm", () =>
                            Open(new PickDialog(T("gui-prompt-rank-addperm", rankName), missing, names, T("gui-button-add"),
                                perm => RankCommands.AddPerm(rankName, perm))), T("gui-action-rank-addperm"));
                    }
                }
                if (canDelete)
                {
                    actions.Add(GuiIcons.Remove, "rank-delete", () =>
                        Open(new ConfirmDialog(T("gui-confirm-rank-delete", rankName), () =>
                        {
                            RankCommands.Delete(rankName);
                            State.SelectedRank = "";
                            State.Tab = EnumGuiTab.Ranks;
                        })), T("gui-action-rank-delete"));
                }
            });

            var holders = new List<ItemRow>();
            foreach (string holder in rank.Holders)
            {
                string target = holder;
                var row = new ItemRow { Label = holder, Key = "rh-" + holder };
                if (canRevoke)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => RankCommands.Revoke(rankName, target), T("gui-action-rank-revoke")));
                }
                holders.Add(row);
            }
            Card.Items(compo, left, yLeft, T("gui-section-rank-holders"), Limit(holders, 7), T("gui-empty-rank-holders"));

            var perms = new List<ItemRow>();
            foreach (string perm in rank.Permissions)
            {
                string target = perm;
                var row = new ItemRow { Label = RankCommands.PermissionName(perm), Key = "rp-" + perm };
                if (canRemovePerm)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => RankCommands.RemovePerm(rankName, target), T("gui-action-rank-removeperm")));
                }
                perms.Add(row);
            }
            Card.Items(compo, right, right.fixedY, T("gui-section-rank-perms"), Limit(perms, 14), T("gui-empty-rank-perms"));
        }
    }
}
