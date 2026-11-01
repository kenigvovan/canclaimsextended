using Vintagestory.API.Config;

namespace canclaimsextended.src.client.gui.pages
{
    /// <summary>
    /// The rank commands the pages run, in one place
    /// (/city rank create|delete|grant|revoke|addperm|removeperm).
    /// </summary>
    internal static class RankCommands
    {
        // Through RunCityCommand, so an admin acting for a city edits its ranks.
        static void Rank(string args) => GuiStateClient.RunCityCommand(GuiStateClient.Latest.CommandCity, "rank " + args);

        public static void Create(string rank) => Rank("create " + rank);
        public static void Delete(string rank) => Rank("delete " + rank);
        public static void Grant(string rank, string player) => Rank("grant " + rank + " " + player);
        public static void Revoke(string rank, string player) => Rank("revoke " + rank + " " + player);
        public static void AddPerm(string rank, string permission) => Rank("addperm " + rank + " " + permission);
        public static void RemovePerm(string rank, string permission) => Rank("removeperm " + rank + " " + permission);

        /// <summary>Human name of an EnumPlayerPermissions value, or the value itself when there is none.</summary>
        public static string PermissionName(string permission)
        {
            string key = "canclaimsextended:perm-" + permission;
            return Lang.HasTranslation(key, true, false) ? Lang.Get(key) : permission;
        }
    }
}
