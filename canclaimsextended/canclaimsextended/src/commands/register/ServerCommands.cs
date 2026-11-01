using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands.register
{
    /// <summary>
    /// Registers every server command. Each root command has a registrar of its own
    /// (CityCommandsRegistrar, PlotCommandsRegistrar, ...); this class picks the names and holds
    /// what they share.
    /// </summary>
    public class ServerCommands
    {
        public static void RegisterCommands(ICoreServerAPI sapi)
        {
            // All names are picked before any command is created. Messages that mention commands
            // read the names from the config.
            Config cfg = canclaimsextended.config;
            cfg.COMMAND_CITY = FreeName(sapi, cfg.COMMAND_CITY);
            cfg.COMMAND_PLOT = FreeName(sapi, cfg.COMMAND_PLOT);
            cfg.COMMAND_CITIZEN = FreeName(sapi, cfg.COMMAND_CITIZEN);
            cfg.COMMAND_AGREE = FreeName(sapi, cfg.COMMAND_AGREE);
            cfg.COMMAND_CADMIN = FreeName(sapi, cfg.COMMAND_CADMIN);
            cfg.COMMAND_CITY_CHAT = FreeName(sapi, cfg.COMMAND_CITY_CHAT);
            cfg.COMMAND_GENERAL_CHAT = FreeName(sapi, cfg.COMMAND_GENERAL_CHAT);

            var parsers = sapi.ChatCommands.Parsers;
            CityCommandsRegistrar.Register(sapi, parsers, cfg.COMMAND_CITY);
            PlotCommandsRegistrar.Register(sapi, parsers, cfg.COMMAND_PLOT);
            CadminCommandsRegistrar.Register(sapi, parsers, cfg.COMMAND_CADMIN);
            MiscCommandsRegistrar.Register(sapi, parsers, cfg);
        }

        /// <summary>
        /// Subcommands added to /city at RunGame: by then the economy bridge is surely installed and
        /// the city root exists.
        /// </summary>
        public static void RegisterRunGameCommands(ICoreServerAPI sapi)
        {
            CityCommandsRegistrar.RegisterRunGame(sapi, sapi.ChatCommands.Parsers, canclaimsextended.config.COMMAND_CITY);
        }

        /// <summary>A command description from the lang file: canclaimsextended:cmd-desc-&lt;key&gt;.</summary>
        internal static string D(string key) => Lang.Get("canclaimsextended:cmd-desc-" + key);

        /// <summary>The on/off argument of a switch; the handler reads it with BaseCommand.IsOn.</summary>
        internal static ICommandArgumentParser OnOff(CommandArgumentParsers parsers) => parsers.WordRange("state", "on", "off");

        /// <summary>The wanted name, or with an "x" in front when another mod (claims) already has it.</summary>
        static string FreeName(ICoreServerAPI sapi, string wanted)
        {
            if (sapi.ChatCommands.Get(wanted) == null)
            {
                return wanted;
            }
            string fallback = "x" + wanted;
            sapi.Logger.Warning("[canclaimsextended] /{0} is taken by another mod, using /{1}", wanted, fallback);
            return fallback;
        }
    }
}
