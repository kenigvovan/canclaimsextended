using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>Client asks for a fresh GuiStatePacket: when the window opens and after an action.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class GuiRequestPacket
    {
        public int Unused;
        /// <summary>An admin's window shows this city as theirs, as its mayor would see it (AdminActing); empty = their own.</summary>
        public string ActingCity = "";
    }
}
