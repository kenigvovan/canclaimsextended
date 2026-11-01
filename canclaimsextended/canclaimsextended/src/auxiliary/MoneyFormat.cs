using System.Globalization;

namespace canclaimsextended.src.auxiliary
{
    /// <summary>Money as the window shows it, at most two decimals, whatever the culture of either side.</summary>
    public static class MoneyFormat
    {
        public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
