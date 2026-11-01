using canclaimsextended.src.client.gui;
using canclaimsextended.src.client.map;
using canclaimsextended.src.network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace canclaimsextended.src.client
{
    /// <summary>Everything the client side sets up: the channel, the plot store, the map layer, markers and the window.</summary>
    public static class ClientSetup
    {
        // Ctrl+Shift+U is claims' own window; this one stays off it.
        // A new code: a binding saved for the old default (Ctrl+Shift+J) must not hold the new one back.
        const string HotkeyCode = "canclaimsextendedgui2";

        public static ClientPlotStore Store { get; private set; }
        public static ClientLandStore Land { get; private set; }
        static IClientNetworkChannel channel;
        static PlotMarkerRenderer markers;
        static BorderRenderer borders;
        static LocationBanner banner;
        static ClientPrefs prefs;
        static ClaimsExtGui window;
        static ICoreClientAPI clientApi;

        /// <summary>The client end of the channel with a handler for every packet the server sends.</summary>
        static IClientNetworkChannel WireNetwork(ICoreClientAPI capi)
        {
            return NetworkSetup.RegisterClient(capi)
                .SetMessageHandler<PlotZonePacket>(p => Store?.ApplyZone(p))
                .SetMessageHandler<PlotUpdatePacket>(p => { if (p.Plot != null) Store?.Upsert(p.Plot); })
                .SetMessageHandler<PlotRemovedPacket>(p => Store?.Remove(p.Guid))
                .SetMessageHandler<EmblemsPacket>(ClientEmblems.Apply)
                .SetMessageHandler<LandZonePacket>(p => Land?.ApplyZone(p))
                .SetMessageHandler<LandUpdatePacket>(p => Land?.ApplyUpdate(p))
                .SetMessageHandler<BordersStatePacket>(p => { if (borders != null) borders.AlwaysShow = p.Show; })
                .SetMessageHandler<GuiStatePacket>(GuiStateClient.OnState);
        }

        public static void Start(ICoreClientAPI capi)
        {
            clientApi = capi;
            Store = new ClientPlotStore(() => capi.World.BlockAccessor.MapSizeY);
            Land = new ClientLandStore();
            channel = WireNetwork(capi);

            capi.ModLoader.GetModSystem<WorldMapManager>()?.RegisterMapLayer<PlotsMapLayer>("canclaimsextended-plots", 2);

            prefs = ClientPrefs.Load(capi);
            markers = new PlotMarkerRenderer(capi) { AlwaysShow = prefs.MarkersAlwaysShow };
            capi.Event.RegisterRenderer(markers, EnumRenderStage.Ortho, "canclaimsextended-markers");
            borders = new BorderRenderer(capi, Land);
            capi.Event.RegisterRenderer(borders, EnumRenderStage.Opaque, "canclaimsextended-borders");
            capi.ChatCommands.Create("cxmarkers")
                .WithDescription(Lang.Get("canclaimsextended:cmd-desc-cxmarkers"))
                .HandleWith(_ =>
                {
                    ToggleMarkers();
                    return TextCommandResult.Success(Lang.Get(markers.AlwaysShow
                        ? "canclaimsextended:markers_always_on"
                        : "canclaimsextended:markers_with_wand"));
                });

            banner = new LocationBanner(capi, prefs);
            capi.Event.RegisterRenderer(banner, EnumRenderStage.Ortho, "canclaimsextended-banner");
            capi.ChatCommands.Create("cxbanner")
                .WithDescription(Lang.Get("canclaimsextended:cmd-desc-cxbanner"))
                .HandleWith(_ =>
                {
                    banner.Toggle();
                    return TextCommandResult.Success(Lang.Get(banner.Enabled
                        ? "canclaimsextended:banner_on"
                        : "canclaimsextended:banner_off"));
                });

            GuiStateClient.Init(capi, channel);
            GuiIcons.Register(capi);
            window = new ClaimsExtGui(capi);
            // A single key, free in vanilla (L for land); players can rebind it in the controls.
            capi.Input.RegisterHotKey(HotkeyCode, Lang.Get("canclaimsextended:gui-hotkey"), GlKeys.L, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler(HotkeyCode, _ => ToggleWindow());

            StartBoatShare(capi);

            capi.Event.LeftWorld += Stop;
        }

        const string ClientHarmonyId = "canclaimsextended.ClientPatches";
        static HarmonyLib.Harmony clientHarmony;
        static BoatShareDialog boatShareDialog;

        /// <summary>
        /// The helm checks ownership on the client first and the tooltip is drawn there, so boat
        /// sharing needs a client patch of its own. In single player the server patch runs on the
        /// same method as well; both only ever turn a "no" into a "yes", so that is harmless.
        /// </summary>
        static void StartBoatShare(ICoreClientAPI capi)
        {
            clientHarmony = new HarmonyLib.Harmony(ClientHarmonyId);
            var isOwner = typeof(EntityBehaviorOwnable).GetMethod("IsOwner");
            var infoText = typeof(EntityBehaviorOwnable).GetMethod("GetInfoText");
            try
            {
                if (isOwner != null)
                {
                    clientHarmony.Patch(isOwner, postfix: new HarmonyLib.HarmonyMethod(typeof(harmony.BoatSharePatches).GetMethod(nameof(harmony.BoatSharePatches.Postfix_Ownable_IsOwner))));
                }
                if (infoText != null)
                {
                    clientHarmony.Patch(infoText, postfix: new HarmonyLib.HarmonyMethod(typeof(harmony.BoatSharePatches).GetMethod(nameof(harmony.BoatSharePatches.Postfix_Ownable_GetInfoText))));
                }
            }
            catch (System.Exception e)
            {
                capi.Logger.Error("[canclaimsextended] boat sharing client patch failed: {0}", e);
            }

            boatShareDialog = new BoatShareDialog(capi);
            capi.Input.RegisterHotKey(BoatShareDialog.HotkeyCode, Lang.Get("canclaimsextended:boat-share-hotkey"), GlKeys.B,
                HotkeyType.GUIOrOtherControls, ctrlPressed: true);
            capi.Input.SetHotKeyHandler(BoatShareDialog.HotkeyCode, _ =>
            {
                if (boatShareDialog == null) return false;
                if (boatShareDialog.IsOpened()) boatShareDialog.TryClose();
                else boatShareDialog.TryOpen();
                return true;
            });
        }

        public static bool BannerEnabled => banner?.Enabled ?? false;
        public static void ToggleBanner() => banner?.Toggle();
        public static bool MarkersAlwaysShown => markers?.AlwaysShow ?? false;
        public static void ToggleMarkers()
        {
            if (markers == null || prefs == null) return;
            markers.AlwaysShow = prefs.MarkersAlwaysShow = !markers.AlwaysShow;
            prefs.Save(clientApi);
        }
        public static bool AdminTabsHidden => prefs?.HideAdminTabs ?? false;
        /// <summary>The acting city kept across sessions (GuiStateClient.ActingCity).</summary>
        public static string SavedActingCity
        {
            get => prefs?.ActingCity ?? "";
            set
            {
                if (prefs == null || prefs.ActingCity == value) return;
                prefs.ActingCity = value ?? "";
                prefs.Save(clientApi);
            }
        }
        public static void ToggleAdminTabs()
        {
            if (prefs == null) return;
            prefs.HideAdminTabs = !prefs.HideAdminTabs;
            prefs.Save(clientApi);
        }

        static bool ToggleWindow()
        {
            if (window == null) return false;
            if (window.IsOpened()) window.TryClose();
            else window.TryOpen();
            return true;
        }

        public static void Stop()
        {
            if (window != null)
            {
                window.TryClose();
                window.Dispose();
                window = null;
            }
            GuiStateClient.Dispose();
            if (boatShareDialog != null)
            {
                boatShareDialog.TryClose();
                boatShareDialog.Dispose();
                boatShareDialog = null;
            }
            clientHarmony?.UnpatchAll(ClientHarmonyId);
            clientHarmony = null;
            if (markers != null)
            {
                clientApi?.Event.UnregisterRenderer(markers, EnumRenderStage.Ortho);
                markers.Dispose();
                markers = null;
            }
            if (borders != null)
            {
                clientApi?.Event.UnregisterRenderer(borders, EnumRenderStage.Opaque);
                borders.Dispose();
                borders = null;
            }
            if (banner != null)
            {
                clientApi?.Event.UnregisterRenderer(banner, EnumRenderStage.Ortho);
                banner.Dispose();
                banner = null;
            }
            if (clientApi != null) clientApi.Event.LeftWorld -= Stop;
            clientApi = null;
            Store?.Clear();
            Store = null;
            Land?.Clear();
            Land = null;
            ClientEmblems.Clear();
            channel = null;
        }
    }
}
