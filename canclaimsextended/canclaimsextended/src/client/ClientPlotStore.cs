using System;
using System.Collections.Generic;
using canclaimsextended.src.geometry;
using canclaimsextended.src.network;

namespace canclaimsextended.src.client
{
    /// <summary>The plots the server has sent, indexed the same way as on the server.</summary>
    public class ClientPlotStore
    {
        readonly Dictionary<string, ClientPlot> byGuid = new();
        readonly PlotIndex<ClientPlot> index = new();
        // Plots never reach outside [0, world height): the server cuts them to it. Asked lazily,
        // the map size may still be unknown while mods start.
        readonly Func<int> worldHeightSource;

        /// <summary>Area whose picture is out of date: where a plot appeared, changed or went.</summary>
        public event Action<BlockBox> AreaChanged;

        public ClientPlotStore(Func<int> worldHeightSource)
        {
            this.worldHeightSource = worldHeightSource;
        }

        public int WorldHeight
        {
            get
            {
                int h = worldHeightSource();
                return h > 0 ? h : 256;
            }
        }

        public List<ClientPlot> Query(BlockBox box) => index.Query(box);

        public IEnumerable<ClientPlot> All() => byGuid.Values;

        public void ApplyZone(PlotZonePacket packet)
        {
            BlockBox zone = NetworkSetup.ZoneArea(packet.ZoneX, packet.ZoneZ, WorldHeight);
            var inPacket = new HashSet<string>();
            if (packet.Plots != null)
            {
                foreach (ClientPlotData data in packet.Plots)
                {
                    inPacket.Add(data.Guid);
                }
            }
            // Anything here the server no longer lists is gone.
            foreach (ClientPlot plot in index.Query(zone))
            {
                if (!inPacket.Contains(plot.Guid)) Remove(plot.Guid);
            }
            if (packet.Plots != null)
            {
                foreach (ClientPlotData data in packet.Plots)
                {
                    Upsert(data);
                }
            }
        }

        public void Upsert(ClientPlotData data)
        {
            PlotShape shape = BoxCodec.Decode(data.Boxes);
            if (shape == null) return;
            if (byGuid.TryGetValue(data.Guid, out ClientPlot plot))
            {
                BlockBox oldBounds = plot.Shape.Bounds;
                Fill(plot, data, shape);
                index.Reshape(plot, shape);
                AreaChanged?.Invoke(oldBounds);
            }
            else
            {
                plot = new ClientPlot();
                Fill(plot, data, shape);
                byGuid[plot.Guid] = plot;
                index.Add(plot, shape);
            }
            AreaChanged?.Invoke(shape.Bounds);
        }

        public void Remove(string guid)
        {
            if (!byGuid.Remove(guid, out ClientPlot plot)) return;
            index.Remove(plot);
            AreaChanged?.Invoke(plot.Shape.Bounds);
        }

        public void Clear()
        {
            byGuid.Clear();
            index.Clear();
        }

        static void Fill(ClientPlot plot, ClientPlotData data, PlotShape shape)
        {
            plot.Guid = data.Guid;
            plot.Name = data.Name ?? "";
            plot.CityGuid = data.CityGuid ?? "";
            plot.CityName = data.CityName ?? "";
            plot.CityColor = data.CityColor;
            plot.TintColor = auxiliary.PlotTint.Tint(data.CityColor, data.Guid, 0xFF);
            plot.OwnerName = data.OwnerName ?? "";
            plot.Flags = data.Flags;
            plot.ForSale = data.ForSale;
            plot.Price = data.Price;
            plot.Number = data.Number;
            plot.GroupName = data.GroupName ?? "";
            plot.Shape = shape;
        }
    }
}
