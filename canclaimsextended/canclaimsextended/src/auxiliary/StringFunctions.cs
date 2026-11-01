using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.part;

namespace canclaimsextended.src.auxiliary
{
    public static class StringFunctions
    {
        public static string MakeStringPlayersName(IEnumerable<PlayerInfo> players, string separator)
        {
            return string.Join(separator, players.Select(p => p.getPartNameReplaceUnder()));
        }
        public static string ReplaceUnderscore(string val)
        {
            return val.Replace("_", " ");
        }
    }
}
