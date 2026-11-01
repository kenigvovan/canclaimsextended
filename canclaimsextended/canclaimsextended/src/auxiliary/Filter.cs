using System.Text.RegularExpressions;

namespace canclaimsextended.src.auxiliary
{
    public static class Filter
    {
        static readonly Regex rgxName = new Regex("[^a-zA-Z0-9-_]");
        static readonly Regex rgxNameWithSpaces = new Regex("[^a-zA-Z0-9-_ ]");

        // A missing optional argument arrives as null.
        public static string FilterName(string inputString)
        {
            return rgxName.Replace(inputString ?? "", "");
        }
        public static string FilterNameWithSpaces(string inputString)
        {
            return rgxNameWithSpaces.Replace(inputString ?? "", "").Trim();
        }
        /// <summary>True if the name is not in BLOCKED_NAMES.</summary>
        public static bool IsAllowedName(string inputString)
        {
            return !canclaimsextended.config.BLOCKED_NAMES.Contains(inputString);
        }
    }
}
