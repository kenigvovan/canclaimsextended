using canclaimsextended.src.geometry;

namespace canclaimsextended.src.client
{
    /// <summary>A plot as the client knows it: enough to draw it and name it.</summary>
    public class ClientPlot
    {
        public string Guid;
        public string Name;
        public string CityGuid;
        public string CityName;
        public int CityColor;
        /// <summary>The city color in this plot's own shade, see PlotTint; alpha is set where drawn.</summary>
        public int TintColor;
        public string OwnerName;
        public PlotShape Shape;
        /// <summary>See network.PlotMarkerFlags.</summary>
        public int Flags;
        public bool ForSale;
        public int Price;
        /// <summary>#n within its city; the same over every part of the plot.</summary>
        public int Number;
        /// <summary>Plots group of the plot, empty when none.</summary>
        public string GroupName = "";
    }
}
