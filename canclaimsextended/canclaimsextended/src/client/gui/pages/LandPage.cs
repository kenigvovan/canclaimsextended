using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// Claiming land from the window: the selection with what can be done with it, moving its
    /// faces without the wand, and the parts of the plot the player stands in.
    /// </summary>
    public sealed class LandPage : GuiPage
    {
        // The wand changes the selection with no command from the window.
        public override bool Polls(GuiStatePacket p) => true;

        private static readonly string[] Faces = Codes.Directions;

        protected override void BuildContent(PageBuildContext ctx)
        {
            Columns(ctx, out var left, out var right);
            var compo = ctx.Compo;

            // The window does not scroll: the right column is already full with the faces and the
            // parts of a plot, so the chunk goes on the left.
            double yLeft = ChunkCard(compo, left, left.fixedY);
            yLeft = SelectionCard(compo, left, yLeft);
            StatusCard(compo, left, yLeft);

            double yRight = FacesCard(compo, right, right.fixedY);
            PartsCard(compo, right, yRight);
        }

        /// <summary>The chunk the player stands in: whose it is, and claiming or giving it up.</summary>
        private double ChunkCard(GuiComposer compo, ElementBounds column, double y)
        {
            // Where and whose on one line: every line counts in a window without scrolling.
            string whose = string.IsNullOrEmpty(P.HereCity) ? T("gui-chunk-free")
                : P.HereIsOwnCity ? T(P.HereIsOutpost ? "gui-chunk-own-outpost" : P.HereIsExtra ? "gui-chunk-own-extra" : "gui-chunk-own")
                : T("gui-chunk-other", Pretty(P.HereCity));
            var lines = new List<string> { T("gui-chunk-where", P.HereChunkX, P.HereChunkZ) + " " + whose };
            if (P.HereIsOwnCity && !string.IsNullOrEmpty(P.HereChunkGroup)) lines.Add(T("gui-chunk-group", Pretty(P.HereChunkGroup)));

            if (P.ChunkCanClaim) lines.Add(PriceLine("gui-chunk-can-claim", P.ChunkPrice));
            if (P.ChunkCanClaimExtra) lines.Add(PriceLine("gui-chunk-can-extra", P.ExtraChunkPrice));
            if (P.ChunkCanOutpost) lines.Add(PriceLine("gui-chunk-can-outpost", P.OutpostPrice));
            if (!P.ChunkCanClaim && !P.ChunkCanClaimExtra && !P.ChunkCanOutpost && !string.IsNullOrEmpty(P.ChunkProblem))
            {
                lines.Add(T("gui-chunk-cannot", Problem(P.ChunkProblem, P.ChunkProblemArgs)));
            }

            return Card.Note(compo, column, y, T("gui-section-chunk"), string.Join("\n", lines), slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (P.ChunkCanClaim) actions.Add(GuiIcons.Plot, "chunk-claim", () => City("claim"), T("gui-action-chunk-claim"));
                if (P.ChunkCanClaimExtra) actions.Add(GuiIcons.Plot, "chunk-extra", () => City("extraclaim"), T("gui-action-chunk-extra"));
                if (P.ChunkCanOutpost) actions.Add(GuiIcons.Found, "chunk-outpost", () => City("outpost"), T("gui-action-chunk-outpost"));
                if (P.ChunkCanUnclaim)
                {
                    actions.Add(GuiIcons.Leave, "chunk-unclaim", () =>
                        Open(new ConfirmDialog(T("gui-confirm-chunk-unclaim"), () => City("unclaim"))), T("gui-action-chunk-unclaim"));
                }
                // Plots groups: the chunk's land gets the group's rights for its members.
                if (P.HereIsOwnCity && string.IsNullOrEmpty(P.HereChunkGroup) && P.PlotsGroups.Count > 0
                    && MayorOr("CITY_PLOTSGROUP_ADD_PLOT"))
                {
                    string[] names = P.PlotsGroups.Select(g => g.Name).ToArray();
                    actions.Add(GuiIcons.Invite, "chunk-group-add", () =>
                        Open(new PickDialog(T("gui-prompt-chunk-group"), names, names.Select(Pretty).ToArray(), T("gui-button-set"),
                            name => City("plotsgroup addchunk " + name))), T("gui-action-chunk-group-add"));
                }
                if (P.HereIsOwnCity && !string.IsNullOrEmpty(P.HereChunkGroup) && MayorOr("CITY_PLOTSGROUP_REMOVE_PLOT"))
                {
                    actions.Add(GuiIcons.Remove, "chunk-group-remove", () => City("plotsgroup removechunk"), T("gui-action-chunk-group-remove"));
                }
                if (P.HasEconomy && (P.ChunkCanClaim || P.ChunkCanClaimExtra || P.ChunkCanOutpost))
                {
                    actions.Add(GuiIcons.Accept, "chunk-agree", Agree, T("gui-action-agree"));
                }
            });
        }

        private static string PriceLine(string key, string price)
        {
            return string.IsNullOrEmpty(price) ? T(key) : T(key) + " " + T("gui-chunk-price", price);
        }

        private bool HasSelection => P.SelHasCorner1 && P.SelHasCorner2;

        private double SelectionCard(GuiComposer compo, ElementBounds column, double y)
        {
            var rows = new List<CardRow>();
            if (HasSelection)
            {
                rows.Add(new CardRow { Label = T("gui-label-size"), Value = $"{P.SelSizeX} x {P.SelSizeY} x {P.SelSizeZ}", Key = "selsize" });
                rows.Add(new CardRow { Label = T("gui-label-volume-plot"), Value = P.SelVolume.ToString(), Key = "selvolume" });
                if (!string.IsNullOrEmpty(P.SelPrice))
                {
                    rows.Add(new CardRow { Label = T("gui-label-price"), Value = P.SelPrice, Key = "selprice",
                                           Tooltip = T(HasCity ? "gui-land-price-tooltip" : "gui-land-price-found-tooltip") });
                }
            }
            else
            {
                rows.Add(new CardRow { Label = T("gui-land-corner1"), Value = P.SelHasCorner1 ? T("gui-yes") : T("gui-no"), Key = "selc1" });
                rows.Add(new CardRow { Label = T("gui-land-corner2"), Value = P.SelHasCorner2 ? T("gui-yes") : T("gui-no"), Key = "selc2" });
            }
            rows.Add(new CardRow { Label = T("gui-land-always-show"), Value = P.SelAlwaysShow ? T("gui-yes") : T("gui-no"), Key = "selshow",
                                   Tooltip = T("gui-land-always-show-tooltip") });

            return Card.RowsWithActions(compo, column, y, T("gui-section-selection"), rows, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Wand, "land-wand", () => Sel("wand"), T("gui-action-wand"));
                actions.Add(GuiIcons.Info, "land-show", () => City(P.SelAlwaysShow ? "sel hide" : "sel show"),
                    T(P.SelAlwaysShow ? "gui-action-sel-hide" : "gui-action-sel-show"));
                if (P.SelHasCorner1 || P.SelHasCorner2)
                {
                    actions.Add(GuiIcons.Remove, "land-clear", () => Sel("sel clear"), T("gui-action-sel-clear"));
                }
            });
        }

        /// <summary>What the selection may become, and the buttons doing it.</summary>
        private void StatusCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool claim = P.SelCanClaim && P.CanClaimForCity;
            bool extend = P.SelCanExtend;
            // Any plot of the city the selection fits, wherever the player stands.
            List<GuiPlotEntry> extendable = HasSelection ? P.CityPlots.Where(e => e.CanExtend).ToList() : new List<GuiPlotEntry>();

            Card.Note(compo, column, y, T("gui-section-land-status"), StatusText(), slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (claim)
                {
                    actions.Add(GuiIcons.Plot, "land-claim", () => Plot("create"), T("gui-action-land-claim"));
                }
                if (extend)
                {
                    actions.Add(GuiIcons.Extend, "land-extend", () => Plot("extend"), T("gui-action-land-extend"));
                }
                if (extendable.Count > 0)
                {
                    actions.Add(GuiIcons.Extend, "land-extend-pick", () =>
                        Open(new PickDialog(T("gui-prompt-extend-plot"), PlotValues(extendable), extendable.Select(PlotPickLabel).ToArray(),
                            T("gui-button-extend"), n => Plot("extend " + n))), T("gui-action-land-extend-pick"));
                }
                // A paid claim waits for /agree; the button sends it without typing.
                if (P.HasEconomy && (claim || extend || extendable.Count > 0))
                {
                    actions.Add(GuiIcons.Accept, "land-agree", Agree, T("gui-action-agree"));
                }
            });
        }

        private static string[] PlotValues(List<GuiPlotEntry> plots) => plots.Select(e => e.Number.ToString()).ToArray();

        private static string PlotPickLabel(GuiPlotEntry e)
        {
            return string.IsNullOrEmpty(e.Name)
                ? T("gui-land-plot-pick-noname", e.Number, e.Volume)
                : T("gui-land-plot-pick", e.Number, Pretty(e.Name), e.Volume);
        }

        /// <summary>Plot, then its part, then a confirm: /plot cut #n part from anywhere.</summary>
        private void PickPartToCut(List<GuiPlotEntry> cuttable)
        {
            Open(new PickDialog(T("gui-prompt-cut-plot"), PlotValues(cuttable), cuttable.Select(PlotPickLabel).ToArray(), T("gui-button-next"), n =>
            {
                GuiPlotEntry plot = cuttable.First(e => e.Number.ToString() == n);
                // Only parts the plot can lose; each keeps its number from /plot boxes.
                var cuts = plot.Parts.Select((b, i) => (Box: b, Number: i + 1)).Where(p => p.Box.CanCut).ToList();
                string[] parts = cuts.Select(p => p.Number.ToString()).ToArray();
                string[] partNames = cuts.Select(p => T("gui-land-part-at", p.Number, p.Box.SizeX, p.Box.SizeY, p.Box.SizeZ,
                    p.Box.MinX, p.Box.MinY, p.Box.MinZ)).ToArray();
                Open(new PickDialog(T("gui-prompt-cut-part", n), parts, partNames, T("gui-button-next"), part =>
                    Open(new ConfirmDialog(T("gui-confirm-land-cut-pick", part, n), () => Plot($"cut {n} {part}")))));
            }));
        }

        private string StatusText()
        {
            if (!P.SelHasCorner1 && !P.SelHasCorner2) return T("gui-land-sel-empty");
            if (!HasSelection) return T("gui-land-sel-one-corner");

            var lines = new List<string>();
            if (P.SelCanClaim) lines.Add(T(P.CanClaimForCity ? "gui-land-can-claim" : "gui-land-no-right"));
            if (P.SelCanExtend) lines.Add(T("gui-land-can-extend"));
            if (lines.Count == 0 && !string.IsNullOrEmpty(P.SelProblem))
            {
                // The key comes with its domain; the reason is read in the player's own language.
                lines.Add(T("gui-land-cannot", Problem(P.SelProblem, P.SelProblemArgs)));
            }
            return string.Join("\n", lines);
        }

        /// <summary>The player stands in a part of their city's plot and may change its shape.</summary>
        private bool CanResizeHere => HasCity && P.PlotCity == P.CityName && P.CanClaimForCity && P.PlotParts.Any(b => b.IsHere);

        /// <summary>
        /// Six faces, each moved out or in by the chosen step: of the selection, or of the plot part
        /// the player stands in when that mode is picked.
        /// </summary>
        private double FacesCard(GuiComposer compo, ElementBounds column, double y)
        {
            bool plotMode = State.FacesMovePlot && CanResizeHere;
            if (!HasSelection && !plotMode)
            {
                System.Action<ElementBounds> modeButton = null;
                if (CanResizeHere)
                {
                    modeButton = slot => new ActionRow(compo, slot).Add(GuiIcons.Extend, "land-faces-mode", ToggleFacesMode, T("gui-action-faces-plot"));
                }
                return Card.Note(compo, column, y, T("gui-section-faces"), T("gui-land-faces-hint"), modeButton);
            }
            int step = State.SelectionStep;
            void Run(string verb, string face)
            {
                if (plotMode) Plot($"{verb} {face} {step}");
                else Sel($"sel {verb} {face} {step}");
            }
            var items = new List<ItemRow>();
            foreach (string face in Faces)
            {
                string f = face;
                items.Add(new ItemRow
                {
                    Label = T("gui-face-" + f),
                    Key = "face-" + f,
                    Buttons =
                    {
                        new RowButton(GuiIcons.Grow, () => Run("grow", f), T("gui-action-face-grow", step)),
                        new RowButton(GuiIcons.Shrink, () => Run("shrink", f), T("gui-action-face-shrink", step))
                    }
                });
            }
            string title = T(plotMode ? "gui-section-faces-plot-step" : "gui-section-faces-step", step);
            return Card.Items(compo, column, y, title, items, null, slot =>
            {
                var actions = new ActionRow(compo, slot);
                actions.Add(GuiIcons.Edit, "land-step", NextSelectionStep, T("gui-action-face-step"));
                if (CanResizeHere)
                {
                    actions.Add(GuiIcons.Extend, "land-faces-mode", ToggleFacesMode,
                        T(plotMode ? "gui-action-faces-selection" : "gui-action-faces-plot"));
                }
                // A paid growth waits for /agree like a claim does.
                if (plotMode && P.HasEconomy)
                {
                    actions.Add(GuiIcons.Accept, "land-faces-agree", Agree, T("gui-action-agree"));
                }
            });
        }

        private void ToggleFacesMode()
        {
            State.FacesMovePlot = !State.FacesMovePlot;
            Gui.BuildMainWindow();
        }

        /// <summary>Boxes of the plot the player stands in, with giving up the plot or one box.</summary>
        private void PartsCard(GuiComposer compo, ElementBounds column, double y)
        {
            List<GuiPlotEntry> cuttable = P.CityPlots.Where(e => e.CanCut).ToList();
            void CutPickButton(ActionRow actions)
            {
                if (cuttable.Count == 0) return;
                actions.Add(GuiIcons.Shrink, "land-cut-pick", () => PickPartToCut(cuttable), T("gui-action-land-cut-pick"));
            }
            if (string.IsNullOrEmpty(P.PlotGuid))
            {
                System.Action<ElementBounds> slotAction = null;
                if (cuttable.Count > 0) slotAction = slot => CutPickButton(new ActionRow(compo, slot));
                Card.Note(compo, column, y, T("gui-section-parts"), T("gui-land-parts-none"), slotAction);
                return;
            }
            var items = new List<ItemRow>();
            for (int i = 0; i < P.PlotParts.Count; i++)
            {
                GuiBoxEntry b = P.PlotParts[i];
                items.Add(new ItemRow
                {
                    Label = T(b.IsHere ? "gui-land-part-here" : "gui-land-part", i + 1, b.SizeX, b.SizeY, b.SizeZ),
                    Value = b.Volume.ToString(),
                    ValueColor = b.IsHere ? GuiColors.Success : null,
                    Key = "part-" + i
                });
            }
            bool ourCityPlot = HasCity && P.PlotCity == P.CityName;
            bool mayCut = ourCityPlot && P.CanClaimForCity && P.PlotParts.Any(b => b.IsHere && b.CanCut);

            Card.Items(compo, column, y, T("gui-section-parts"), Limit(items, 6), T("gui-land-parts-none"), slot =>
            {
                var actions = new ActionRow(compo, slot);
                if (mayCut)
                {
                    actions.Add(GuiIcons.Shrink, "land-cut", () =>
                        Open(new ConfirmDialog(T("gui-confirm-land-cut"), () => Plot("cut"))), T("gui-action-land-cut"));
                }
                CutPickButton(actions);
                if (P.CanUnclaim)
                {
                    actions.Add(GuiIcons.Leave, "land-unclaim", () =>
                        Open(new ConfirmDialog(T("gui-confirm-land-unclaim"), () => Plot("delete"))), T("gui-action-land-unclaim"));
                }
            });
        }
    }
}
