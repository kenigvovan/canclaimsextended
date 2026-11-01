using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.client.gui.dialogs;
using canclaimsextended.src.client.gui.pages;
using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace canclaimsextended.src.client.gui
{
    /// <summary>
    /// The player window: a row of tabs and the page under it, with an optional secondary window on
    /// the right for typing a name or confirming. Laid out as the claims window; the data is one
    /// GuiStatePacket, and every button runs a chat command.
    /// </summary>
    public class ClaimsExtGui : GuiDialog
    {
        // Not the hotkey code: the hotkey handler toggles the window itself, and matching the code
        // would make the engine toggle it a second time on the same keypress.
        public override string ToggleKeyCombinationCode => "canclaimsextendedgui-window";

        private const string MainKey = "canclaimsextendedgui";
        // Tabs that do not fit one row at the smallest tab size go on a second one.
        private const double Width = 680;
        private const double Height = 600;

        public ICoreClientAPI Capi => capi;
        public GuiWindowState State { get; } = new GuiWindowState();

        private GuiDialogPanel dialog;
        private List<EnumGuiTab> tabsOrder = new List<EnumGuiTab>();

        /// <summary>A tab in the bar; whether it shows is up to its page (GuiPage.IsVisible).</summary>
        private sealed class MainTab
        {
            public EnumGuiTab Tab;
            public string Icon;
            public string TooltipKey;
        }

        private static readonly MainTab[] MainTabs =
        {
            new MainTab { Tab = EnumGuiTab.City, Icon = GuiIcons.City, TooltipKey = "gui-tab-city" },
            new MainTab { Tab = EnumGuiTab.CitySettings, Icon = GuiIcons.Settings, TooltipKey = "gui-tab-citysettings" },
            new MainTab { Tab = EnumGuiTab.Plot, Icon = GuiIcons.Plot, TooltipKey = "gui-tab-plot" },
            new MainTab { Tab = EnumGuiTab.Land, Icon = GuiIcons.Wand, TooltipKey = "gui-tab-land" },
            new MainTab { Tab = EnumGuiTab.Player, Icon = GuiIcons.Player, TooltipKey = "gui-tab-player" },
            new MainTab { Tab = EnumGuiTab.Ranks, Icon = GuiIcons.Ranks, TooltipKey = "gui-tab-ranks" },
            new MainTab { Tab = EnumGuiTab.PlotsGroups, Icon = GuiIcons.PlotsGroups, TooltipKey = "gui-tab-plotsgroups" },
            new MainTab { Tab = EnumGuiTab.Log, Icon = GuiIcons.Log, TooltipKey = "gui-tab-log" },
            new MainTab { Tab = EnumGuiTab.Emblem, Icon = GuiIcons.Emblem, TooltipKey = "gui-tab-emblem" },
            new MainTab { Tab = EnumGuiTab.Summon, Icon = GuiIcons.Summon, TooltipKey = "gui-tab-summon" },
            new MainTab { Tab = EnumGuiTab.Prison, Icon = GuiIcons.Prison, TooltipKey = "gui-tab-prison" },
            new MainTab { Tab = EnumGuiTab.Prices, Icon = GuiIcons.Sell, TooltipKey = "gui-tab-prices" },
            new MainTab { Tab = EnumGuiTab.Admin, Icon = GuiIcons.Grant, TooltipKey = "gui-tab-admin" },
            new MainTab { Tab = EnumGuiTab.AdminPlot, Icon = GuiIcons.Rights, TooltipKey = "gui-tab-admin-plot" },
            new MainTab { Tab = EnumGuiTab.World, Icon = GuiIcons.World, TooltipKey = "gui-tab-world" },
            new MainTab { Tab = EnumGuiTab.PriceSettings, Icon = GuiIcons.Buy, TooltipKey = "gui-tab-pricesettings" },
            new MainTab { Tab = EnumGuiTab.CityLevels, Icon = GuiIcons.Grow, TooltipKey = "gui-tab-levels" },
            new MainTab { Tab = EnumGuiTab.BlockRules, Icon = GuiIcons.PlotType, TooltipKey = "gui-tab-blockrules" },
        };

        private static bool IsVisible(MainTab tab, GuiStatePacket p) => Pages.TryGetValue(tab.Tab, out var page) && page.IsVisible(p);

        private static readonly Dictionary<EnumGuiTab, GuiPage> Pages = new Dictionary<EnumGuiTab, GuiPage>
        {
            { EnumGuiTab.City, new CityPage() },
            { EnumGuiTab.CitySettings, new CitySettingsPage() },
            { EnumGuiTab.Plot, new PlotPage() },
            { EnumGuiTab.Player, new PlayerPage() },
            { EnumGuiTab.Ranks, new RanksPage() },
            { EnumGuiTab.RankInfo, new RankInfoPage() },
            { EnumGuiTab.Prison, new PrisonPage() },
            { EnumGuiTab.Summon, new SummonPage() },
            { EnumGuiTab.World, new WorldPage() },
            { EnumGuiTab.Land, new LandPage() },
            { EnumGuiTab.Log, new CityLogPage() },
            { EnumGuiTab.PlotsGroups, new PlotsGroupsPage() },
            { EnumGuiTab.Emblem, new EmblemPage() },
            { EnumGuiTab.Prices, new PricesPage() },
            { EnumGuiTab.PriceSettings, new PriceSettingsPage() },
            { EnumGuiTab.CityLevels, new CityLevelsPage() },
            { EnumGuiTab.BlockRules, new BlockRulesPage() },
            { EnumGuiTab.Admin, new AdminPage() },
            { EnumGuiTab.AdminPlot, new AdminPlotPage() },
        };

        public ClaimsExtGui(ICoreClientAPI capi) : base(capi)
        {
            GuiStateClient.Updated += OnStateUpdated;
        }

        // The last state drawn; a polled answer that changed nothing does not rebuild the window.
        private byte[] lastDrawn;

        private void OnStateUpdated(GuiStatePacket packet)
        {
            if (!IsOpened()) return;
            byte[] bytes = SerializerUtil.Serialize(packet);
            if (lastDrawn != null && bytes.AsSpan().SequenceEqual(lastDrawn)) return;
            // Serialized once: BuildMainWindow keeps these bytes instead of serializing again.
            drawnBytes = bytes;
            BuildMainWindow();
        }

        // Bytes of GuiStateClient.Latest already serialized by OnStateUpdated, for the next build.
        private byte[] drawnBytes;

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            GuiStateClient.Request();
            BuildMainWindow();
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            CloseDialog();
        }

        // Where the player stood when the state was last asked for, so the plot page follows them.
        private BlockPos askedAt;
        private long askedAtMs;

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);
            long now = capi.ElapsedMilliseconds;
            GuiStatePacket p = GuiStateClient.Latest;

            // A page that polls (GuiPage.Polls) is asked for again at an interval. Not while a dialog
            // is open, so a rebuild does not get in the way of typing.
            if (p != null && Pages.TryGetValue(State.Tab, out var page) && page.Polls(p))
            {
                if (dialog != null || now - askedAtMs < ClientSettings.GUI_PLOT_REFRESH_MS) return;
                askedAtMs = now;
                GuiStateClient.Request();
                return;
            }
            // The plot tab follows the player: asked again when they move to another block.
            if (State.Tab != EnumGuiTab.Plot) return;

            BlockPos pos = capi.World?.Player?.Entity?.Pos?.AsBlockPos;
            if (pos == null) return;
            if (askedAt != null && askedAt.Equals(pos)) return;
            if (now - askedAtMs < ClientSettings.GUI_PLOT_REFRESH_MS) return;
            askedAt = pos;
            askedAtMs = now;
            GuiStateClient.Request();
        }

        public void OpenDialog(GuiDialogPanel panel)
        {
            dialog = panel;
            State.DialogText = "";
            State.DialogPick = "";
            BuildUpperWindow();
        }

        public void CloseDialog()
        {
            dialog = null;
            State.DialogText = "";
            State.DialogPick = "";
            // Disposed and removed rather than set to null: the composer setter would throw on null.
            Composers[DialogLayout.ComposerKey]?.Dispose();
            Composers.Remove(DialogLayout.ComposerKey);
        }

        public void BuildMainWindow()
        {
            GuiStatePacket p = GuiStateClient.Latest;
            lastDrawn = p == null ? null : drawnBytes ?? SerializerUtil.Serialize(p);
            drawnBytes = null;

            ElementBounds globalBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds backgroundBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding).WithFixedSize(Width, Height);
            ElementBounds mainBounds = ElementBounds.FixedPos(EnumDialogArea.CenterTop, 0, 15).WithFixedSize(Width, Height);
            globalBounds.WithChildren(backgroundBounds);
            backgroundBounds.BothSizing = ElementSizing.Fixed;
            backgroundBounds.WithChildren(mainBounds);

            // SingleComposer lives under the "single" key; the old one is replaced, so free it first.
            SingleComposer?.Dispose();
            var compo = capi.Gui.CreateCompo(MainKey, globalBounds)
                .AddShadedDialogBG(backgroundBounds)
                // An admin acting as a city's mayor sees it in the title on every tab.
                .AddDialogTitleBar(string.IsNullOrEmpty(p?.ActingCity) ? Lang.Get("canclaimsextended:gui-title")
                    : Lang.Get("canclaimsextended:gui-title-acting", ClientText.Pretty(p.ActingCity)), () => TryClose());
            SingleComposer = compo;

            ElementBounds currentBounds = mainBounds.FlatCopy().WithAlignment(EnumDialogArea.CenterTop).WithFixedSize(mainBounds.fixedWidth, 40);
            mainBounds.WithChildren(currentBounds);
            currentBounds.fixedY += 15;

            if (p == null)
            {
                var waiting = currentBounds.BelowCopy(0, 40);
                compo.AddStaticText(Lang.Get("canclaimsextended:gui-loading"),
                    CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), waiting);
                compo.Compose();
                return;
            }

            // A tab that is gone (the player left the city) falls back to the city page.
            var visibleTabs = MainTabs.Where(t => IsVisible(t, p)).ToList();
            EnumGuiTab highlight = State.Tab == EnumGuiTab.RankInfo ? EnumGuiTab.Ranks : State.Tab;
            if (!visibleTabs.Any(t => t.Tab == highlight))
            {
                State.Tab = EnumGuiTab.City;
                highlight = EnumGuiTab.City;
            }
            tabsOrder = visibleTabs.Select(t => t.Tab).ToList();

            // Icons shrink to keep one row; below MinTabSize the rest goes on a second row.
            const double maxTabSize = 48;
            const double minTabSize = 36;
            const double tabGap = 8;
            double rowWidth = Width - 10;
            int perRow = Math.Max(1, (int)((rowWidth + tabGap) / (minTabSize + tabGap)));
            int rows = (visibleTabs.Count + perRow - 1) / perRow;
            int inRow = Math.Min(perRow, visibleTabs.Count);
            double tabSize = Math.Min(maxTabSize, (rowWidth + tabGap) / inRow - tabGap);
            var tabBounds = new List<ElementBounds>();
            for (int i = 0; i < visibleTabs.Count; i++)
            {
                ElementBounds slot = currentBounds.FlatCopy().WithAlignment(EnumDialogArea.LeftTop).WithFixedSize(tabSize, tabSize);
                slot.fixedX += 5 + (i % perRow) * (tabSize + tabGap);
                slot.fixedY += (i / perRow) * (tabSize + tabGap);
                tabBounds.Add(slot);
            }
            currentBounds.fixedHeight = rows * tabSize + (rows - 1) * tabGap;
            compo.AddIconToggleButtons(visibleTabs.Select(t => t.Icon).ToArray(), CairoFont.ButtonText(),
                OnTabToggled, tabBounds.ToArray(), "selectedTab");
            for (int i = 0; i < visibleTabs.Count; i++)
            {
                Tooltip.Add(compo, Lang.Get("canclaimsextended:" + visibleTabs[i].TooltipKey), tabBounds[i], "tabtip-" + i);
            }
            int selectedIdx = tabsOrder.IndexOf(highlight);
            if (selectedIdx >= 0) compo.GetToggleButton("selectedTab-" + selectedIdx)?.SetValue(true);

            var lineBounds = currentBounds.BelowCopy(0, 12).WithFixedHeight(5);
            compo.AddInset(lineBounds);

            var ctx = new PageBuildContext { Compo = compo, Line = lineBounds, Main = mainBounds };
            if (Pages.TryGetValue(State.Tab, out var page))
            {
                page.Build(this, ctx);
            }

            compo.Compose();
            ctx.RunAfterCompose();
            BuildUpperWindow();
        }

        private void BuildUpperWindow()
        {
            if (!IsOpened() || dialog == null || SingleComposer == null) return;
            Composers[DialogLayout.ComposerKey]?.Dispose();
            Composers[DialogLayout.ComposerKey] = dialog.Build(this, SingleComposer);
        }

        private void OnTabToggled(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= tabsOrder.Count) return;
            State.Tab = tabsOrder[tabIndex];
            CloseDialog();
            BuildMainWindow();
        }

        public override void Dispose()
        {
            GuiStateClient.Updated -= OnStateUpdated;
            base.Dispose();
        }
    }
}
