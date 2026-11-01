using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>A city level, for the admin editor: what a city may hold from this many citizens.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiCityLevelEntry
    {
        public int Citizens;
        public int Plots;
        public long Volume;
        public int Summon;
        /// <summary>Extra chunks the city may buy over Chunks.</summary>
        public int Extra;
        public int Chunks;
    }
}
