using System.Text;
using canclaimsextended.src.auxiliary;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace canclaimsextended.src.commands
{
    /// <summary>/cadmin level ...: what a city may hold by its number of citizens.</summary>
    public partial class CAdminCommand
    {
        /// <summary>/cadmin level list: thresholds by citizens and what each allows.</summary>
        public static TextCommandResult LevelList(TextCommandCallingArgs args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var it in Settings.cityLevelsDict)
            {
                sb.Append(Lang.Get("canclaimsextended:admin_level_line", it.Key, it.Value.AmountOfPlots, it.Value.MaxVolume,
                    it.Value.SummonPoints, it.Value.MaxExtraChunks, it.Value.MaxChunks)).Append('\n');
            }
            return TextCommandResult.Success(sb.ToString());
        }

        public static TextCommandResult LevelAdd(TextCommandCallingArgs args)
        {
            int citizens = (int)args[0];
            if (!Settings.TryAddLevel(citizens, out string errorKey)) return ErrorWithParams(errorKey, citizens);
            return SuccessWithParams("canclaimsextended:admin_level_added", citizens);
        }

        public static TextCommandResult LevelRemove(TextCommandCallingArgs args)
        {
            int citizens = (int)args[0];
            if (!Settings.TryRemoveLevel(citizens, out string errorKey)) return ErrorWithParams(errorKey, citizens);
            return SuccessWithParams("canclaimsextended:admin_level_removed", citizens);
        }

        /// <summary>/cadmin level set &lt;citizens&gt; &lt;field&gt; &lt;value&gt;, written to the levels file at once.</summary>
        public static TextCommandResult LevelSet(TextCommandCallingArgs args)
        {
            int citizens = (int)args[0];
            string field = (string)args[1];
            string raw = (string)args[2];
            if (!long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long value))
            {
                return ErrorWithParams("canclaimsextended:admin_cfg_not_integer", raw);
            }
            if (!Settings.TrySetLevelField(citizens, field, value, out string errorKey, out object[] errorArgs))
            {
                return ErrorWithParams(errorKey, errorArgs.Length > 0 ? errorArgs : new object[] { citizens });
            }
            return SuccessWithParams("canclaimsextended:admin_level_set", citizens, field, value);
        }
    }
}
