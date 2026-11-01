using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// One page of the window. Pages are created once and reused, so they keep nothing between
    /// builds; what has to survive lives in GuiWindowState. Composing is the window's job.
    /// </summary>
    public abstract class GuiPage
    {
        protected ClaimsExtGui Gui { get; private set; }
        protected GuiWindowState State => Gui.State;
        protected static GuiStatePacket P => GuiStateClient.Latest;

        internal void Build(ClaimsExtGui gui, PageBuildContext ctx)
        {
            Gui = gui;
            BuildContent(ctx);
        }

        protected abstract void BuildContent(PageBuildContext ctx);

        /// <summary>Whether the page's tab is shown for this state; without a rule of its own it always is.</summary>
        public virtual bool IsVisible(GuiStatePacket p) => true;

        /// <summary>
        /// Whether the open page asks the server again every GUI_PLOT_REFRESH_MS: for what changes
        /// with no command from the window (the wand, the block in hand, a countdown).
        /// </summary>
        public virtual bool Polls(GuiStatePacket p) => false;

        protected static bool HasCityIn(GuiStatePacket p) => !string.IsNullOrEmpty(p.CityName);
        /// <summary>Admins may hide their tabs (player tab) to see the window as a player does.</summary>
        protected static bool ShowAdminTabs(GuiStatePacket p) => p.IsAdmin && !ClientSetup.AdminTabsHidden;

        protected static bool Has(string permission) => P.Permissions != null && P.Permissions.Contains(permission);
        /// <summary>The mayor may do everything; anyone else needs one of these permissions.</summary>
        protected static bool MayorOr(params string[] permissions) => P.IsMayor || permissions.Any(Has);
        protected static bool HasCity => !string.IsNullOrEmpty(P.CityName);

        // As the mayor of the acted-for city when an admin acts for one (GuiStateClient.ActingCity).
        protected static void City(string args) => GuiStateClient.RunCityCommand(P.CommandCity, args);
        protected static void Plot(string args) => GuiStateClient.RunCityCommand(P.CommandPlot, args);
        /// <summary>The player's own selection and wand: never through /cadmin as, they belong to no city.</summary>
        protected static void Sel(string args) => GuiStateClient.RunCommand(P.CommandCity + " " + args);
        protected static void Citizen(string args) => GuiStateClient.RunCommand(P.CommandCitizen + " " + args);
        protected static void Cadmin(string args) => GuiStateClient.RunCommand(P.CommandCadmin + " " + args);
        /// <summary>Confirms the paid action the server last asked about.</summary>
        protected static void Agree() => GuiStateClient.RunCommand(P.CommandAgree);

        /// <summary>A plot type as players read it, by its code.</summary>
        protected static string PlotTypeName(string code) => T("plot_type_" + (string.IsNullOrEmpty(code) ? "default" : code));

        protected static string OnOff(bool on) => on ? "on" : "off";
        /// <summary>A city name as players read it.</summary>
        protected static string Pretty(string name) => ClientText.Pretty(name);

        /// <summary>The plot the player stands in as the window names it: "#n Name", either part may be missing.</summary>
        protected static string PlotLabel() => ((P.PlotNumber > 0 ? T("marker-number", P.PlotNumber) + " " : "") + P.PlotName).Trim();

        /// <summary>Why the server said no: a lang key and its arguments from GuiStatePacket.</summary>
        protected static string Problem(string key, List<string> args) => Lang.Get(key, args.Cast<object>().ToArray());

        /// <summary>Invitations by the name of who sent them, each with accept and refuse.</summary>
        protected static List<ItemRow> InvitationRows(IList<string> names, string keyPrefix,
            System.Action<string> accept, System.Action<string> refuse)
        {
            var rows = new List<ItemRow>();
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                var row = new ItemRow { Label = Pretty(name), Key = keyPrefix + i };
                row.Buttons.Add(new RowButton(GuiIcons.Accept, () => accept(name), T("gui-action-accept")));
                row.Buttons.Add(new RowButton(GuiIcons.Remove, () => refuse(name), T("gui-action-refuse")));
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The next of Codes.SelectionSteps for the face buttons (land and admin tabs).</summary>
        protected void NextSelectionStep()
        {
            int[] steps = auxiliary.Codes.SelectionSteps;
            int i = System.Array.IndexOf(steps, State.SelectionStep);
            State.SelectionStep = steps[(i + 1) % steps.Length];
            Gui.BuildMainWindow();
        }

        /// <summary>A price setting as the server sent it; null for an unknown key.</summary>
        protected static GuiPriceSettingEntry Setting(string key) => P.PriceSettings.Find(s => s.Key == key);

        protected void Open(GuiDialogPanel dialog) => Gui.OpenDialog(dialog);

        protected void GoTo(EnumGuiTab tab)
        {
            State.Tab = tab;
            Gui.BuildMainWindow();
        }

        /// <summary>Two columns below the tab bar.</summary>
        protected static void Columns(PageBuildContext ctx, out ElementBounds left, out ElementBounds right)
        {
            var anchor = ctx.Line.BelowCopy(0, 14);
            anchor.Alignment = EnumDialogArea.LeftTop;
            double columnWidth = (ctx.Line.fixedWidth - Card.ColumnGap) / 2;
            left = anchor.FlatCopy().WithFixedWidth(columnWidth);
            right = anchor.FlatCopy().WithFixedWidth(columnWidth);
            right.fixedX += columnWidth + Card.ColumnGap;
        }

        protected static string T(string key, params object[] args) => Lang.Get("canclaimsextended:" + key, args);

        /// <summary>
        /// Cuts a list to what fits a card, the last line saying how many are left out. There is no
        /// scrolling in these cards; the full list is in chat (/city info, /citizen friend list).
        /// </summary>
        internal static List<ItemRow> Limit(List<ItemRow> rows, int max)
        {
            if (rows.Count <= max) return rows;
            var cut = rows.GetRange(0, max - 1);
            // Keyed after the row it replaces: two cut lists on one page must not share a key.
            cut.Add(new ItemRow { Label = T("gui-more", rows.Count - (max - 1)), Key = rows[max - 1].Key + "-more" });
            return cut;
        }
    }
}
