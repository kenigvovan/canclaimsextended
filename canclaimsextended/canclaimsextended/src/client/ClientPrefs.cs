using System;
using Vintagestory.API.Client;

namespace canclaimsextended.src.client
{
    /// <summary>The player's own display choices, kept in ModConfig on their machine.</summary>
    public class ClientPrefs
    {
        const string FileName = "canclaimsextended_client.json";

        public bool ShowBanner = true;
        public bool MarkersAlwaysShow = false;
        /// <summary>An admin playing as a player: the admin tabs of the window are left out.</summary>
        public bool HideAdminTabs = false;
        /// <summary>The city an admin manages as its mayor from the window (the spawn town, say); empty = none.</summary>
        public string ActingCity = "";

        public static ClientPrefs Load(ICoreClientAPI capi)
        {
            try
            {
                return capi.LoadModConfig<ClientPrefs>(FileName) ?? new ClientPrefs();
            }
            catch (Exception e)
            {
                capi.Logger.Error("[canclaimsextended] {0} could not be read: {1}", FileName, e.Message);
                return new ClientPrefs();
            }
        }

        public void Save(ICoreClientAPI capi)
        {
            capi.StoreModConfig(this, FileName);
        }
    }
}
