namespace canclaimsextended.src.auxiliary
{
    /// <summary>
    /// Words shared by the commands, the stored rights and the window. They are command arguments and
    /// part of the saved rights string, so they must not change.
    /// </summary>
    public static class Codes
    {
        /// <summary>Rights groups, in the order the window shows them and GuiStatePacket sends them.</summary>
        public static readonly string[] PermGroups = { "stranger", "citizen", "friend" };

        /// <summary>Rights types, by PermType index.</summary>
        public static readonly string[] PermTypes = { "use", "build", "attack" };

        /// <summary>Plot and city flags, in the order GuiStatePacket sends them.</summary>
        public static readonly string[] Flags = { "pvp", "fire", "blast", "nomobspawn" };

        /// <summary>Box faces, for grow, shrink and the selection.</summary>
        public static readonly string[] Directions = { "up", "down", "north", "south", "east", "west" };

        /// <summary>Selection steps the window cycles through, in blocks.</summary>
        public static readonly int[] SelectionSteps = { 1, 4, 16 };
    }
}
