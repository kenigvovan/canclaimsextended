using System.Collections.Concurrent;

namespace canclaimsextended.src.messages
{
    /// <summary>The chat mode of every player who switched away from general chat; in memory only.</summary>
    public static class ChatModes
    {
        static readonly ConcurrentDictionary<string, ChatMode> modes = new();

        public static ChatMode Get(string playerUid)
        {
            return modes.TryGetValue(playerUid, out ChatMode mode) ? mode : ChatMode.General;
        }

        public static void Set(string playerUid, ChatMode mode)
        {
            if (mode == ChatMode.General) modes.TryRemove(playerUid, out _);
            else modes[playerUid] = mode;
        }

        public static void Clear()
        {
            modes.Clear();
        }
    }
}
