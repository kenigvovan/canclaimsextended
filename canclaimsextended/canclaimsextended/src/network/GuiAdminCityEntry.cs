using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A city as the admin tab lists it; Name is the raw name the commands take.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiAdminCityEntry
    {
        public string Name = "";
        public string Mayor = "";
        public bool Technical;
        public int BonusChunks;
        public int BonusPlots;
        /// <summary>New players join this city on their first login (NEW_PLAYERS_DEFAULT_CITY).</summary>
        public bool IsDefault;
    }
}
