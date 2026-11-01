using canclaimsextended.src.client.gui.widgets;
using canclaimsextended.src.network;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.client
{
    /// <summary>Emblem rules as the server last sent them.</summary>
    public static class ClientEmblems
    {
        public static void Apply(EmblemsPacket packet)
        {
            EmblemHandler.SetClientRules(packet.Patterns, packet.Colors, packet.MaxLayers);
            // Arms drawn under the old rules may now resolve differently.
            EmblemCache.Invalidate();
        }

        public static void Clear() => EmblemCache.Invalidate();
    }
}
