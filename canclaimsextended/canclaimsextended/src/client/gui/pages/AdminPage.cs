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
    /// The admin versions of the land, city and player commands in one place: what is under the
    /// admin (chunk, plot, selection), the cities, players and the server. Every button runs /cadmin.
    /// </summary>
    public sealed class AdminPage : GuiPage
    {
        public override bool IsVisible(GuiStatePacket p) => ShowAdminTabs(p);
        // The chunk, plot and selection under the admin change as they walk and use the wand.
        public override bool Polls(GuiStatePacket p) => true;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            double yLeft = HereCard(compo, left, left.fixedY);
            yLeft = SelectionCard(compo, left, yLeft);
            ServerCard(compo, left, yLeft);

            double yRight = ActingCard(compo, right, right.fixedY);
            yRight = CitiesCard(compo, right, yRight);
            PlayersCard(compo, right, yRight);
        }

        /// <summary>The chunk and the plot the admin stands in.</summary>
        private double HereCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool owned = !string.IsNullOrEmpty(P.HereCity);
            var chunk = new ItemRow
            {
                Label = T("gui-admin-chunk", P.HereChunkX, P.HereChunkZ),
                Value = owned ? Pretty(P.HereCity) : T("gui-admin-nobody"),
                Key = "adm-chunk"
            };
            if (owned)
            {
                string city = P.HereCity;
                chunk.Buttons.Add(new RowButton(GuiIcons.Remove, () => Open(new ConfirmDialog(T("gui-admin-confirm-unclaim", Pretty(city)),
                    () => Cadmin("city unclaim " + city))), T("gui-admin-action-unclaim")));
            }
            else
            {
                chunk.Buttons.Add(new RowButton(GuiIcons.Found, () => Open(new TextInputDialog(T("gui-admin-prompt-city-create"),
                    T("gui-button-add"), name => Cadmin("city create " + name.Replace(" ", "_")))), T("gui-admin-action-city-create")));
                chunk.Buttons.Add(new RowButton(GuiIcons.Wand, () => PickCity(T("gui-admin-prompt-claim-for"),
                    city => Cadmin("city claim " + city)), T("gui-admin-action-claim")));
            }

            bool inPlot = !string.IsNullOrEmpty(P.PlotGuid);
            var plot = new ItemRow
            {
                Label = T("gui-admin-plot"),
                Value = inPlot ? PlotText() : T("gui-admin-none"),
                Key = "adm-plot"
            };
            if (inPlot)
            {
                plot.Buttons.Add(new RowButton(GuiIcons.Info, () => Cadmin("plot info"), T("gui-admin-action-plot-info")));
            }
            return Card.Items(compo, column, y, T("gui-section-admin-here"), new List<ItemRow> { chunk, plot }, null);
        }

        private static string PlotText()
        {
            string name = string.IsNullOrEmpty(P.PlotName) ? "#" + P.PlotNumber : Pretty(P.PlotName);
            return string.IsNullOrEmpty(P.PlotCity) ? name : name + " · " + Pretty(P.PlotCity);
        }

        /// <summary>The wand selection: a new plot of any city, or a part of the plot the admin stands in.</summary>
        private double SelectionCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool hasSelection = P.SelHasCorner1 && P.SelHasCorner2 && P.SelVolume > 0;
            var row = new ItemRow
            {
                Label = T("gui-admin-selection"),
                Value = hasSelection ? T("gui-admin-selection-size", P.SelSizeX, P.SelSizeY, P.SelSizeZ, P.SelVolume) : T("gui-admin-none"),
                Key = "adm-sel"
            };
            if (hasSelection)
            {
                row.Buttons.Add(new RowButton(GuiIcons.Plot, PickSelectionUse, T("gui-admin-action-selection-use")));
            }
            var items = new List<ItemRow> { row };
            // Standing in a plot that the box cannot join: say why, or a new plot is the only button left.
            if (hasSelection && !P.SelCanAdminExtend && !string.IsNullOrEmpty(P.SelAdminExtendProblem))
            {
                string reason = Problem(P.SelAdminExtendProblem, P.SelAdminExtendProblemArgs);
                items.Add(new ItemRow
                {
                    Label = T("gui-admin-extend-cannot"),
                    Value = reason,
                    ValueColor = GuiColors.Danger,
                    Tooltip = T("gui-admin-extend-cannot") + " " + reason,
                    Key = "adm-sel-noext"
                });
            }
            // Faces two to a line: the window does not scroll and the land tab's card would not fit here.
            if (hasSelection)
            {
                foreach (var (a, b) in FacePairs)
                {
                    items.Add(FaceRow(a, b));
                }
            }
            int step = State.SelectionStep;
            return Card.Items(compo, column, y, T("gui-admin-section-selection-step", step), items, null, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Wand, "adm-wand", () => Sel("wand"), T("gui-action-wand"));
                if (hasSelection)
                {
                    actions.Add(GuiIcons.Edit, "adm-step", NextSelectionStep, T("gui-action-face-step"));
                }
                if (P.SelHasCorner1 || P.SelHasCorner2)
                {
                    actions.Add(GuiIcons.Remove, "adm-sel-clear", () => Sel("sel clear"), T("gui-action-sel-clear"));
                }
            });
        }

        private const string ExtendChoice = "__extend__";

        /// <summary>
        /// One button for the selection: added to the plot the admin stands in (first, when it can be)
        /// or made a new plot of a city - two buttons side by side were easy to mix up.
        /// </summary>
        private void PickSelectionUse()
        {
            var values = new List<string>();
            var names = new List<string>();
            if (P.SelCanAdminExtend)
            {
                values.Add(ExtendChoice);
                string plot = string.IsNullOrEmpty(P.PlotName) ? "#" + P.PlotNumber : Pretty(P.PlotName);
                names.Add(T("gui-admin-sel-extend", plot, Pretty(P.PlotCity)));
            }
            foreach (var city in P.AdminCities)
            {
                values.Add(city.Name);
                names.Add(T("gui-admin-sel-new", Pretty(city.Name)));
            }
            if (values.Count == 0) return;
            Open(new PickDialog(T("gui-admin-prompt-selection-use"), values.ToArray(), names.ToArray(), T("gui-button-set"),
                choice => Cadmin(choice == ExtendChoice ? "plot extend" : "plot create " + choice)));
        }

        private static readonly (string, string)[] FacePairs = { ("up", "down"), ("north", "south"), ("east", "west") };

        /// <summary>Two opposite faces of the selection, each moved out or in by the step.</summary>
        private ItemRow FaceRow(string a, string b)
        {
            int step = State.SelectionStep;
            RowButton Button(string verb, string face) => new RowButton(verb == "grow" ? GuiIcons.Grow : GuiIcons.Shrink,
                () => Sel($"sel {verb} {face} {step}"), T("gui-admin-face-" + verb, T("gui-face-" + face), step));
            return new ItemRow
            {
                Label = T("gui-face-" + a) + " / " + T("gui-face-" + b),
                Key = "adm-face-" + a,
                Buttons = { Button("grow", a), Button("shrink", a), Button("grow", b), Button("shrink", b) }
            };
        }


        private double ServerCard(GuiComposer compo, ElementBounds column, double y)
        {
            return Card.Note(compo, column, y, T("gui-section-admin-server"), T("gui-admin-server-hint"), slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Grow, "adm-reload", () => Cadmin("reload"), T("gui-admin-action-reload"));
                actions.Add(GuiIcons.Log, "adm-backup", () => Cadmin("backup"), T("gui-admin-action-backup"));
            });
        }

        /// <summary>
        /// The city whose mayor the window acts as: its city, plot, rank and group tabs show
        /// that city and their buttons run through /cadmin as.
        /// </summary>
        private double ActingCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool acting = !string.IsNullOrEmpty(P.ActingCity);
            string text = acting ? T("gui-admin-acting-on", Pretty(P.ActingCity)) : T("gui-admin-acting-off");
            return Card.Note(compo, column, y, T("gui-section-admin-acting"), text, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Grant, "adm-act", () => PickCity(T("gui-admin-prompt-act"), StartActing), T("gui-admin-action-act"));
                if (acting)
                {
                    actions.Add(GuiIcons.Remove, "adm-act-stop", StopActing, T("gui-admin-action-act-stop"));
                }
            });
        }

        private void StartActing(string city)
        {
            GuiStateClient.ActingCity = city;
            GuiStateClient.Request();
        }

        private void StopActing()
        {
            GuiStateClient.ActingCity = "";
            GuiStateClient.Request();
        }

        /// <summary>Every city with what an admin may change on it; the full list is behind the pick button.</summary>
        private double CitiesCard(GuiComposer compo, ElementBounds column, double y)
        {
            var items = new List<ItemRow>();
            for (int i = 0; i < P.AdminCities.Count; i++)
            {
                GuiAdminCityEntry city = P.AdminCities[i];
                items.Add(new ItemRow
                {
                    Label = Pretty(city.Name),
                    Value = CityValue(city),
                    ValueColor = city.Technical || city.IsDefault ? GuiColors.Success : null,
                    Tooltip = T("gui-admin-city-tooltip", Pretty(city.Name), city.Mayor.Length > 0 ? Pretty(city.Mayor) : T("gui-admin-none"),
                        city.BonusChunks, city.BonusPlots),
                    Key = "adm-city-" + i,
                    Buttons = { new RowButton(GuiIcons.Edit, () => PickCityAction(city), T("gui-admin-action-city")) }
                });
            }
            return Card.Items(compo, column, y, T("gui-section-admin-cities"), Limit(items, 8), T("gui-admin-no-cities"),
                slot => new ActionRow(compo, slot).Add(GuiIcons.Edit, "adm-city-pick",
                    () => PickCity(T("gui-admin-prompt-city"), name => PickCityAction(P.AdminCities.Find(c => c.Name == name))),
                    T("gui-admin-action-city-pick")));
        }

        private static string CityValue(GuiAdminCityEntry city)
        {
            var parts = new List<string>();
            if (city.IsDefault) parts.Add(T("gui-admin-default-city"));
            if (city.Technical) parts.Add(T("gui-admin-technical"));
            if (city.BonusChunks != 0) parts.Add(T("gui-admin-bonus-chunks", city.BonusChunks));
            if (city.BonusPlots != 0) parts.Add(T("gui-admin-bonus-plots", city.BonusPlots));
            return string.Join(" · ", parts);
        }

        private static readonly string[] CityActions = { "act", "technical", "default", "bonuschunks", "bonusplots", "setmayor", "delete" };

        private static string CityActionName(GuiAdminCityEntry city, string action)
        {
            switch (action)
            {
                case "technical": return T(city.Technical ? "gui-admin-city-technical-off" : "gui-admin-city-technical-on");
                case "default": return T(city.IsDefault ? "gui-admin-city-default-off" : "gui-admin-city-default-on");
                default: return T("gui-admin-city-" + action);
            }
        }

        private void PickCityAction(GuiAdminCityEntry city)
        {
            if (city == null) return;
            string name = city.Name;
            string[] names = CityActions.Select(a => CityActionName(city, a)).ToArray();
            Open(new PickDialog(T("gui-admin-prompt-city-action", Pretty(name)), CityActions, names, T("gui-button-next"), action =>
            {
                switch (action)
                {
                    case "act":
                        StartActing(name);
                        break;
                    case "technical":
                        Cadmin($"city technical {name} {OnOff(!city.Technical)}");
                        break;
                    case "default":
                        // Without a city the command clears the setting.
                        Cadmin(city.IsDefault ? "city setdefault" : "city setdefault " + name);
                        break;
                    case "bonuschunks":
                        Open(new TextInputDialog(T("gui-admin-prompt-bonus-chunks", Pretty(name), city.BonusChunks), T("gui-button-save"),
                            value => Cadmin($"city bonuschunks {name} {value}"), true));
                        break;
                    case "bonusplots":
                        Open(new TextInputDialog(T("gui-admin-prompt-bonus-plots", Pretty(name), city.BonusPlots), T("gui-button-save"),
                            value => Cadmin($"city bonusplots {name} {value}"), true));
                        break;
                    case "setmayor":
                        Open(new TextInputDialog(T("gui-admin-prompt-mayor", Pretty(name)), T("gui-button-save"),
                            value => Cadmin($"city setmayor {name} {value}")));
                        break;
                    case "delete":
                        Open(new ConfirmDialog(T("gui-admin-confirm-city-delete", Pretty(name)), () => Cadmin("city delete " + name)));
                        break;
                }
            }));
        }

        /// <summary>Player commands by name: info, kick from the city, put into the default city.</summary>
        private double PlayersCard(GuiComposer compo, ElementBounds column, double y)
        {
            return Card.Note(compo, column, y, T("gui-section-admin-players"), T("gui-admin-players-hint"), slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Info, "adm-player-info", () => AskPlayer("player info"), T("gui-admin-action-player-info"));
                actions.Add(GuiIcons.Leave, "adm-player-kick", () => AskPlayer("player kick"), T("gui-admin-action-player-kick"));
                actions.Add(GuiIcons.Invite, "adm-player-default", () => AskPlayer("player joindefault"), T("gui-admin-action-player-default"));
            });
        }

        private void AskPlayer(string command)
        {
            Open(new TextInputDialog(T("gui-admin-prompt-player"), T("gui-button-next"), name => Cadmin(command + " " + name)));
        }

        private void PickCity(string prompt, System.Action<string> onPicked)
        {
            if (P.AdminCities.Count == 0) return;
            string[] values = P.AdminCities.Select(c => c.Name).ToArray();
            Open(new PickDialog(prompt, values, values.Select(Pretty).ToArray(), T("gui-button-next"), onPicked));
        }
    }
}
