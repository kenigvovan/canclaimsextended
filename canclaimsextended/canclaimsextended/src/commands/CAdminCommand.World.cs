using canclaimsextended.src.part.structure;
using Vintagestory.API.Common;

namespace canclaimsextended.src.commands
{
    /// <summary>/cadmin world ...: flags for the whole world.</summary>
    public partial class CAdminCommand
    {
        public static TextCommandResult WorldInfoCmd(TextCommandCallingArgs args)
        {
            return TextCommandResult.Success(string.Join("", canclaimsextended.dataStorage.World.getStatus()));
        }

        /// <summary>/cadmin world set &lt;flag&gt; &lt;on|off&gt;; flag codes are WorldInfo.Codes.</summary>
        public static TextCommandResult WorldSet(TextCommandCallingArgs args)
        {
            WorldInfo world = canclaimsextended.dataStorage.World;
            string code = (string)args[0];
            if (!world.TrySet(code, IsOn(args[1])))
            {
                return TextCommandResult.Error("canclaimsextended:world_flag_unknown");
            }
            world.saveToDatabase();
            // The flags change who may fight, burn and blast on every plot.
            canclaimsextended.dataStorage.ResetCacheForAll();
            return SuccessWithParams("canclaimsextended:world_flag_set", code, (string)args[1]);
        }
    }
}
