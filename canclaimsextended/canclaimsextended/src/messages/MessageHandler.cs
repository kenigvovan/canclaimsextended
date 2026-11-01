using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.messages
{
    public static class MessageHandler
    {
        public static void sendGlobalMsg(string msg)
        {
            foreach (IPlayer player in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                ((IServerPlayer)player).SendMessage(GlobalConstants.GeneralChatGroup,
                    Lang.Get("canclaimsextended:mod_default_mark") + msg, EnumChatType.Notification);
            }
        }
        /// <summary>
        /// The same as sendGlobalMsg, sendMsgInCity and sendMsgToPlayer, but each receiver reads the
        /// lang key in their own language, as the game does for command results.
        /// </summary>
        public static void sendGlobalLang(string key, params object[] args)
        {
            foreach (IPlayer player in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                if (player is not IServerPlayer sp) continue;
                sp.SendMessage(GlobalConstants.GeneralChatGroup,
                    Lang.GetL(sp.LanguageCode, "canclaimsextended:mod_default_mark") + Lang.GetL(sp.LanguageCode, key, args), EnumChatType.Notification);
            }
        }
        public static void sendLangInCity(City city, string key, params object[] args)
        {
            string prefix = "[" + city.getPartNameReplaceUnder() + "] ";
            foreach (IServerPlayer citizen in city.getOnlineCitizens())
            {
                citizen.SendMessage(GlobalConstants.GeneralChatGroup, prefix + Lang.GetL(citizen.LanguageCode, key, args), EnumChatType.Notification);
            }
        }
        public static void sendLangToPlayerInfo(PlayerInfo receiver, string key, params object[] args)
        {
            if (receiver == null) return;
            if (canclaimsextended.sapi.World.PlayerByUid(receiver.Guid) is IServerPlayer player)
            {
                sendMsgToPlayer(player, Lang.GetL(player.LanguageCode, key, args));
            }
        }
        public static void sendMsgInCity(City city, string msg)
        {
            string prefix = "[" + city.getPartNameReplaceUnder() + "] ";
            foreach (IServerPlayer citizen in city.getOnlineCitizens())
            {
                citizen.SendMessage(GlobalConstants.GeneralChatGroup, prefix + msg, EnumChatType.Notification);
            }
        }
        public static void sendMsgToPlayer(IServerPlayer receiver, string msg)
        {
            receiver?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
        }
        /// <summary>A chat line from a player to the citizens of the city: [City] Name: text.</summary>
        public static void sendChatInCity(City city, IServerPlayer from, string text)
        {
            string line = Tag(city.getPartNameReplaceUnder(), canclaimsextended.config.CITY_COLOR_NAME) + ChatLine(from, text);
            foreach (IServerPlayer citizen in city.getOnlineCitizens())
            {
                citizen.SendMessage(GlobalConstants.GeneralChatGroup, line, EnumChatType.OthersMessage);
            }
        }
        /// <summary>[name] in bold and color, ready to put in front of a chat line.</summary>
        public static string Tag(string name, string color)
        {
            return Colored("<strong>[" + name + "]</strong>", color) + " ";
        }
        /// <summary>The text in a chat color.</summary>
        public static string Colored(string text, string color)
        {
            return "<font color=\"" + color + "\">" + text + "</font>";
        }
        static string ChatLine(IServerPlayer from, string text)
        {
            return "<strong>" + from.PlayerName + ":</strong> " + text;
        }
        /// <summary>To the player behind this info, if they are online; nothing otherwise.</summary>
        public static void sendMsgToPlayerInfo(PlayerInfo receiver, string msg)
        {
            if (receiver == null) return;
            sendMsgToPlayer(canclaimsextended.sapi.World.PlayerByUid(receiver.Guid) as IServerPlayer, msg);
        }
    }
}
