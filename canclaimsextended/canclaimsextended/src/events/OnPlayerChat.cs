using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    public static class OnPlayerChat
    {
        // The server puts the text as typed (already escaped) after this marker in data.
        const string RAW_MARKER = "withoutPrefix:";

        /// <summary>
        /// Plain chat lines of a player in city mode go there instead; in general chat the city tag
        /// is put in front of the name.
        /// </summary>
        public static void Event_OnPlayerChat(IServerPlayer player, int channelId, ref string message, ref string data, BoolRef consumed)
        {
            if (canclaimsextended.dataStorage == null
                || !canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo))
            {
                return;
            }
            switch (ChatModes.Get(player.PlayerUID))
            {
                case ChatMode.City when playerInfo.hasCity():
                    MessageHandler.sendChatInCity(playerInfo.City, player, RawText(message, data));
                    consumed.value = true;
                    return;
                case ChatMode.City:
                    // Lost the city meanwhile: back to general chat.
                    ChatModes.Set(player.PlayerUID, ChatMode.General);
                    break;
            }
            if (channelId != GlobalConstants.GeneralChatGroup || !playerInfo.hasCity()) return;
            message = Tags(playerInfo) + WithTitles(playerInfo, player.PlayerName, message);
        }

        /// <summary>The city-given titles around the player's name, where the name first appears.</summary>
        static string WithTitles(PlayerInfo playerInfo, string playerName, string message)
        {
            string before = playerInfo.Prefix ?? "";
            string after = playerInfo.AfterName ?? "";
            if (before.Length == 0 && after.Length == 0) return message;
            Config cfg = canclaimsextended.config;
            string titled = (before.Length > 0 ? MessageHandler.Colored(before, cfg.PREFIX_COLOR_PLAYER) + " " : "")
                + playerName
                + (after.Length > 0 ? " " + MessageHandler.Colored(after, cfg.POSTFIX_COLOR_PLAYER) : "");
            int at = message.IndexOf(playerName, System.StringComparison.Ordinal);
            return at < 0 ? titled + " " + message : message.Substring(0, at) + titled + message.Substring(at + playerName.Length);
        }

        /// <summary>The city tag a general chat line of this player starts with.</summary>
        public static string Tags(PlayerInfo playerInfo)
        {
            Config cfg = canclaimsextended.config;
            string tags = "";
            if (cfg.SHOW_CITY_NAME_IN_CHAT && playerInfo.hasCity())
            {
                tags += MessageHandler.Tag(playerInfo.City.getPartNameReplaceUnder(), cfg.CITY_COLOR_NAME);
            }
            return tags;
        }

        static string RawText(string message, string data)
        {
            int at = data?.IndexOf(RAW_MARKER) ?? -1;
            return at >= 0 ? data.Substring(at + RAW_MARKER.Length) : message;
        }
    }
}
