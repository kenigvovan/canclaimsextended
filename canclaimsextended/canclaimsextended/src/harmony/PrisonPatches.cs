using System;
using canclaimsextended.src.part;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.harmony
{
    /// <summary>
    /// Commands a prisoner may not run (BLOCKED_COMMANDS_PRISON), checked on the one dispatcher every
    /// server command goes through, so commands of other mods (teleports, homes) are covered too.
    /// A name matches the command or its first word: "summon" blocks /summon and /city summon.
    /// </summary>
    public static class PrisonPatches
    {
        public static bool Prefix_Execute(string commandName, IServerPlayer player, string args, Action<TextCommandResult> onCommandComplete)
        {
            // Anything unexpected falls through to vanilla instead of eating the command.
            if (player == null || canclaimsextended.dataStorage == null || canclaimsextended.config == null) return true;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo info) || !info.isPrisoned()) return true;
            var blocked = canclaimsextended.config.BLOCKED_COMMANDS_PRISON;
            if (blocked == null || blocked.Count == 0) return true;

            string first = string.IsNullOrEmpty(args) ? "" : args.Trim().Split(' ')[0];
            if (!blocked.Contains(commandName?.ToLowerInvariant() ?? "") && !blocked.Contains(first.ToLowerInvariant())) return true;

            onCommandComplete?.Invoke(TextCommandResult.Error(Lang.Get("canclaimsextended:prison_command_blocked")));
            return false;
        }
    }
}
