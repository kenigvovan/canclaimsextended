using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// Plots groups of the city: the list with the plot under the player on the left, the chosen
    /// group on the right, and the group invitations the player received.
    /// </summary>
    public sealed class PlotsGroupsPage : GuiPage
    {
        static readonly string[] PermCodes = Codes.PermTypes;
        static readonly string[] FlagCodes = Codes.Flags;
        static readonly string[] FlagRights = { "CITY_PLOTSGROUP_SET_PVP", "CITY_PLOTSGROUP_SET_FIRE", "CITY_PLOTSGROUP_SET_BLAST", "CITY_PLOTSGROUP_SET_MOBSPAWN" };

        /// <summary>Name of the group shown on the right; survives rebuilds, dropped when the group is gone.</summary>
        string Selected
        {
            get => State.SelectedPlotsGroup;
            set => State.SelectedPlotsGroup = value ?? "";
        }

        public override bool IsVisible(GuiStatePacket p) => HasCityIn(p);

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            GuiPlotsGroupEntry group = P.PlotsGroups.FirstOrDefault(g => g.Name == Selected);
            if (group == null && P.PlotsGroups.Count > 0)
            {
                group = P.PlotsGroups[0];
                Selected = group.Name;
            }

            double yLeft = left.fixedY;
            if (HasCity)
            {
                yLeft = GroupsCard(compo, left, yLeft);
                yLeft = HereCard(compo, left, yLeft);
                if (group != null) yLeft = LandCard(compo, left, yLeft, group);
            }
            // The explanation is for a city still without groups; with groups the column is full
            // and the window does not scroll.
            if (P.PlotsGroups.Count == 0)
            {
                Card.Note(compo, left, yLeft, null, T("gui-plotsgroups-hint"));
            }

            double yRight = right.fixedY;
            if (group != null)
            {
                yRight = MembersCard(compo, right, yRight, group);
                yRight = RightsCard(ctx, right, yRight, group);
                yRight = FeeCard(compo, right, yRight, group);
            }
            InvitationsCard(compo, right, yRight);
        }

        private double GroupsCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool canDelete = MayorOr("CITY_PLOTSGROUP_REMOVE");
            var rows = new List<ItemRow>();
            for (int i = 0; i < P.PlotsGroups.Count; i++)
            {
                GuiPlotsGroupEntry g = P.PlotsGroups[i];
                string name = g.Name;
                var row = new ItemRow
                {
                    Label = (name == Selected ? "> " : "") + Pretty(name),
                    // Short: the value gets at most 40% of the row. The full text is the tooltip.
                    Value = T("gui-value-plotsgroup-short", g.Members.Count, g.Plots, g.Chunks),
                    Tooltip = T("gui-value-plotsgroup", g.Members.Count, g.Plots, g.Chunks),
                    ValueColor = g.IsMember ? GuiColors.Value : null,
                    Key = "pg-" + i
                };
                row.Buttons.Add(new RowButton(GuiIcons.Info, () => { Selected = name; Gui.BuildMainWindow(); }, T("gui-action-plotsgroup-show")));
                if (canDelete)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () =>
                        Open(new ConfirmDialog(T("gui-confirm-plotsgroup-delete", Pretty(name)), () => City("plotsgroup delete " + name))),
                        T("gui-action-plotsgroup-delete")));
                }
                rows.Add(row);
            }
            System.Action<ElementBounds> actions = null;
            if (MayorOr("CITY_PLOTSGROUP_CREATE"))
            {
                actions = slot => new ActionRow(compo, slot).Add(GuiIcons.PlotsGroups, "pg-create", () =>
                    Open(new TextInputDialog(T("gui-prompt-plotsgroup-name"), T("gui-button-create"), name => City("plotsgroup create " + name))),
                    T("gui-action-plotsgroup-create"));
            }
            return Card.Items(compo, column, y, T("gui-section-plotsgroups", P.PlotsGroups.Count), Limit(rows, 7),
                T("gui-empty-plotsgroups"), actions);
        }

        /// <summary>
        /// Where the player stands, line by line: the plot (by its number) and the chunk (by its
        /// position), each with its group, and buttons naming exactly what they add to the chosen
        /// group or take out. In a plot both lines show: either may go into a group.
        /// </summary>
        private double HereCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool plotHere = !string.IsNullOrEmpty(P.PlotGuid) && P.PlotCity == P.CityName;
            bool chunkHere = P.HereIsOwnCity;
            if (!plotHere && !chunkHere) return y;

            string plotName = T("gui-plotsgroup-here-plot", T("marker-number", P.PlotNumber));
            string chunkName = T("gui-plotsgroup-here-chunk", P.HereChunkX, P.HereChunkZ);
            var lines = new List<string>();
            if (plotHere) lines.Add(plotName + ": " + GroupState(P.PlotGroupName));
            if (chunkHere) lines.Add(chunkName + ": " + GroupState(P.HereChunkGroup));

            bool mayAdd = MayorOr("CITY_PLOTSGROUP_ADD_PLOT") && Selected.Length > 0;
            bool mayRemove = MayorOr("CITY_PLOTSGROUP_REMOVE_PLOT");
            bool addPlot = plotHere && mayAdd && Selected != P.PlotGroupName;
            bool removePlot = plotHere && mayRemove && !string.IsNullOrEmpty(P.PlotGroupName);
            bool addChunk = chunkHere && mayAdd && Selected != P.HereChunkGroup;
            bool removeChunk = chunkHere && mayRemove && !string.IsNullOrEmpty(P.HereChunkGroup);

            System.Action<ElementBounds> actions = null;
            if (addPlot || removePlot || addChunk || removeChunk)
            {
                actions = slot =>
                {
                    var row = new ActionRow(compo, slot);
                    if (addPlot) row.Add(GuiIcons.Plot, "pg-plot-add", () => City("plotsgroup addplot " + Selected),
                        T("gui-action-plotsgroup-add", plotName, Pretty(Selected)));
                    if (removePlot) row.Add(GuiIcons.Remove, "pg-plot-remove", () => City("plotsgroup removeplot"),
                        T("gui-action-plotsgroup-remove", plotName, Pretty(P.PlotGroupName)));
                    if (addChunk) row.Add(GuiIcons.City, "pg-chunk-add", () => City("plotsgroup addchunk " + Selected),
                        T("gui-action-plotsgroup-add", chunkName, Pretty(Selected)));
                    if (removeChunk) row.Add(GuiIcons.Remove, "pg-chunk-remove", () => City("plotsgroup removechunk"),
                        T("gui-action-plotsgroup-remove", chunkName, Pretty(P.HereChunkGroup)));
                };
            }
            return Card.Note(compo, column, y, T("gui-section-plotsgroup-here"), string.Join("\n", lines), actions);
        }

        private static string GroupState(string group)
        {
            return string.IsNullOrEmpty(group) ? T("gui-plotsgroup-plot-none") : T("gui-plotsgroup-plot-in", Pretty(group));
        }

        /// <summary>What the chosen group holds: its plots by number, its chunks by position.</summary>
        private double LandCard(GuiComposer compo, ElementBounds column, double y, GuiPlotsGroupEntry group)
        {
            string plots = group.PlotNumbers.Count == 0 ? T("gui-plotsgroup-land-none")
                : Shorten(group.PlotNumbers.Select(n => T("marker-number", n)).ToList());
            string chunks = group.ChunkCoords.Count == 0 ? T("gui-plotsgroup-land-none")
                : Shorten(group.ChunkCoords.Select(c => "(" + c + ")").ToList());
            string text = T("gui-plotsgroup-land-plots", plots) + "\n" + T("gui-plotsgroup-land-chunks", chunks);
            return Card.Note(compo, column, y, T("gui-section-plotsgroup-land", Pretty(group.Name)), text);
        }

        /// <summary>The first few, then how many more: the window does not scroll.</summary>
        private static string Shorten(List<string> items)
        {
            const int shown = 8;
            string text = string.Join(", ", items.Take(shown));
            return items.Count > shown ? text + " " + T("gui-more", items.Count - shown) : text;
        }

        private double MembersCard(GuiComposer compo, ElementBounds column, double y, GuiPlotsGroupEntry group)
        {
            string name = group.Name;
            bool canKick = MayorOr("CITY_PLOTSGROUP_KICK_PLAYER");
            var rows = new List<ItemRow>();
            for (int i = 0; i < group.Members.Count; i++)
            {
                string member = group.Members[i];
                var row = new ItemRow { Label = member, Key = "pgm-" + i };
                if (member == P.PlayerName)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Leave, () => City("plotsgroup leave " + name), T("gui-action-plotsgroup-leave")));
                }
                else if (canKick)
                {
                    row.Buttons.Add(new RowButton(GuiIcons.Remove, () => City("plotsgroup kick " + name + " " + member), T("gui-action-plotsgroup-kick")));
                }
                rows.Add(row);
            }
            System.Action<ElementBounds> actions = null;
            if (MayorOr("CITY_PLOTSGROUP_ADD_PLAYER"))
            {
                string[] candidates = P.Citizens.Where(c => !group.Members.Contains(c)).ToArray();
                if (candidates.Length > 0)
                {
                    actions = slot => new ActionRow(compo, slot).Add(GuiIcons.Invite, "pg-invite", () =>
                        Open(new PickDialog(T("gui-prompt-plotsgroup-invite"), candidates, candidates, T("gui-button-invite"),
                            player => City("plotsgroup invite " + name + " " + player))),
                        T("gui-action-plotsgroup-invite"));
                }
            }
            return Card.Items(compo, column, y, T("gui-section-plotsgroup-members", Pretty(name), group.Members.Count), Limit(rows, 6),
                T("gui-empty-plotsgroup-members"), actions);
        }

        /// <summary>
        /// What members may do on the group's plots and chunks, and the flags there, as switches like
        /// on the plot page. Each flag has its own right.
        /// </summary>
        private double RightsCard(PageBuildContext ctx, ElementBounds column, double y, GuiPlotsGroupEntry group)
        {
            var compo = ctx.Compo;
            string name = group.Name;
            const string prefix = "pgset";
            y = PermTable.MemberRights(compo, column, y, prefix, T("gui-section-plotsgroup-rights"), T("gui-plotsgroup-members-row"),
                !MayorOr("CITY_PLOTSGROUP_SET"),
                (type, on) => City("plotsgroup set perm " + name + " " + type + " " + OnOff(on)));
            bool[] flags = { group.Pvp, group.Fire, group.Blast, group.NoMobSpawn };
            y = PermTable.FlagSwitches(compo, column, y, prefix, flags,
                flag => MayorOr(FlagRights[System.Array.IndexOf(FlagCodes, flag)]),
                (flag, on) => City("plotsgroup set flag " + name + " " + flag + " " + OnOff(on)));
            bool[] rights = new bool[PermCodes.Length];
            for (int i = 0; i < rights.Length; i++) rights[i] = i < group.Perms.Length && group.Perms[i];
            ctx.AfterCompose(() => PermTable.ApplyValues(compo, prefix, rights, flags));
            return y;
        }

        private double FeeCard(GuiComposer compo, ElementBounds column, double y, GuiPlotsGroupEntry group)
        {
            if (!P.HasEconomy) return y;
            string name = group.Name;
            string text = T("gui-plotsgroup-fee", group.Fee);
            if (group.HasPendingFee)
            {
                text += "\n" + T("gui-plotsgroup-fee-pending", group.PendingFee, group.PendingHoursLeft)
                    + (group.IAccepted ? "\n" + T("gui-plotsgroup-fee-accepted") : "");
            }
            bool canSet = MayorOr("CITY_PLOTSGROUP_SET_FEE");
            bool canAccept = group.HasPendingFee && group.IsMember && !group.IAccepted;
            System.Action<ElementBounds> actions = null;
            if (canSet || canAccept)
            {
                actions = slot =>
                {
                    var row = new ActionRow(compo, slot);
                    if (canSet) row.Add(GuiIcons.Edit, "pg-fee-set", () =>
                        Open(new TextInputDialog(T("gui-prompt-plotsgroup-fee"), T("gui-button-set"), fee => City("plotsgroup set fee " + name + " " + fee))),
                        T("gui-action-plotsgroup-fee"));
                    if (canAccept) row.Add(GuiIcons.Accept, "pg-fee-accept", () => City("plotsgroup acceptfee " + name), T("gui-action-plotsgroup-acceptfee"));
                };
            }
            return Card.Note(compo, column, y, T("gui-section-plotsgroup-fee"), text, actions);
        }

        private void InvitationsCard(GuiComposer compo, ElementBounds column, double y)
        {
            if (P.PlotsGroupInvitations.Count == 0) return;
            var rows = InvitationRows(P.PlotsGroupInvitations, "pgi-",
                name => City("plotsgroup join " + name), name => City("plotsgroup deny " + name));
            Card.Items(compo, column, y, T("gui-section-plotsgroup-invitations"), Limit(rows, 4), "", null);
        }
    }
}
