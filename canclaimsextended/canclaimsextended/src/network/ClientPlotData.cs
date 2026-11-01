using ProtoBuf;

namespace canclaimsextended.src.network
{
    /// <summary>What a client needs to draw one plot on the map.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClientPlotData
    {
        public string Guid;
        public string Name = "";
        public string CityGuid = "";
        public string CityName = "";
        public int CityColor;
        public string OwnerName = "";
        /// <summary>See BoxCodec.</summary>
        public int[] Boxes;
        /// <summary>See PlotMarkerFlags.</summary>
        public int Flags;
        // Two fields rather than a price of -1: protobuf-net skips zero values, so a free plot
        // would arrive as "not for sale" against a -1 default.
        public bool ForSale;
        public int Price;
        /// <summary>Plot.Number: #n within its city.</summary>
        public int Number;
        /// <summary>Name of the plots group the plot is in, empty when none.</summary>
        public string GroupName = "";
    }
}
