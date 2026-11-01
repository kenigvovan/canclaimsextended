using canclaimsextended.src.messages;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>
    /// /cc: with text, one line to the city; without, switch plain chat there
    /// (again to switch back). /gc returns to general chat.
    /// </summary>
    public class ChatCommands : BaseCommand
    {
        public static TextCommandResult CityChat(TextCommandCallingArgs args)
        {
            if (!TryResolveCity(args, out var player, out _, out var city, out var err)) return err;
            string text = Text(args);
            if (text.Length > 0)
            {
                MessageHandler.sendChatInCity(city, player, text);
                return TextCommandResult.Success();
            }
            return Toggle(player.PlayerUID, ChatMode.City);
        }

        public static TextCommandResult GeneralChat(TextCommandCallingArgs args)
        {
            if (!TryResolveCaller(args, out var player, out _, out var err)) return err;
            ChatModes.Set(player.PlayerUID, ChatMode.General);
            return TextCommandResult.Success("canclaimsextended:chat_mode_general");
        }

        static TextCommandResult Toggle(string uid, ChatMode mode)
        {
            ChatMode next = ChatModes.Get(uid) == mode ? ChatMode.General : mode;
            ChatModes.Set(uid, next);
            return TextCommandResult.Success(next switch
            {
                ChatMode.City => "canclaimsextended:chat_mode_city",
                _ => "canclaimsextended:chat_mode_general"
            });
        }

        // Typed as a command, so not escaped by the server the way a chat line is.
        static string Text(TextCommandCallingArgs args)
        {
            string raw = args.Parsers[0].IsMissing ? "" : (string)args.LastArg ?? "";
            return raw.Trim().Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
