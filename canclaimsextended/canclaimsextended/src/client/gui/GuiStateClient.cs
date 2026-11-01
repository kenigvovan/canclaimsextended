using System;
using canclaimsextended.src.network;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client.gui
{
    /// <summary>
    /// Client end of the player window data: asks the server for a GuiStatePacket and keeps the
    /// latest one. Buttons run chat commands through RunCommand and get a fresh state shortly after.
    /// </summary>
    public static class GuiStateClient
    {
        static ICoreClientAPI capi;
        static IClientNetworkChannel channel;

        public static GuiStatePacket Latest { get; private set; }
        public static event Action<GuiStatePacket> Updated;

        /// <summary>
        /// The city an admin manages as its mayor from the window (admin tab); empty = their own. Kept
        /// across sessions in the client prefs; the server drops it for a non-admin or a city that is gone.
        /// </summary>
        public static string ActingCity
        {
            get => ClientSetup.SavedActingCity;
            set => ClientSetup.SavedActingCity = value ?? "";
        }

        /// <summary>The channel is wired in ClientSetup, OnState included.</summary>
        public static void Init(ICoreClientAPI api, IClientNetworkChannel clientChannel)
        {
            capi = api;
            channel = clientChannel;
            Latest = null;
        }

        internal static void OnState(GuiStatePacket packet)
        {
            // The server did not build the window for the city: not an admin, or it is gone.
            if (ActingCity.Length > 0 && string.IsNullOrEmpty(packet.ActingCity)) ActingCity = "";
            Latest = packet;
            Updated?.Invoke(packet);
        }

        public static void Request()
        {
            if (channel == null || !channel.Connected) return;
            channel.SendPacket(new GuiRequestPacket { ActingCity = ActingCity });
        }

        /// <summary>
        /// A /city or /plot command from the window: as the mayor of the acted-for city through
        /// /cadmin as when an admin acts for one, else as it is.
        /// </summary>
        public static void RunCityCommand(string root, string args)
        {
            if (ActingCity.Length > 0 && Latest != null)
            {
                RunCommand($"{Latest.CommandCadmin} as {ActingCity} {root} {args}");
                return;
            }
            RunCommand(root + " " + args);
        }

        public static void RunCommand(string commandWithoutSlash)
        {
            if (capi == null) return;
            capi.SendChatMessage("/" + commandWithoutSlash);
            // Asked again a little later, so the server has run the command by then.
            capi.Event.RegisterCallback(_ => Request(), ClientSettings.GUI_REFRESH_DELAY_MS);
        }

        public static void Dispose()
        {
            Updated = null;
            Latest = null;
            channel = null;
            capi = null;
        }
    }
}
