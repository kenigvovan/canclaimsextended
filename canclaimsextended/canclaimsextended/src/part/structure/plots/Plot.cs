using System.Collections.Generic;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.interfaces;
using canclaimsextended.src.perms;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part.structure
{
    public class Plot : Part, IGetStatus
    {
        City city;
        PlayerInfo ownerOfPlot;
        public PlotType Type { get; set; }
        /// <summary>What the type adds to the plot; set together with Type by PlotTypeChanger.</summary>
        public plots.PlotDesc Desc { get; set; } = new plots.PlotDesc();
        /// <summary>
        /// Where the plot is. Changed only through DataStorage.ReshapePlot, which keeps the spatial
        /// index in step with it.
        /// </summary>
        public PlotShape Shape { get; internal set; }
        double customTax = 0;
        public int Price { get; set; } = -1;
        public bool IsForSale => Price != -1;
        PermsHandler permsHandler = new PermsHandler();
        public long TimeStampClaimed { get; set; } = 0;
        /// <summary>The plots group the plot belongs to, null when none.</summary>
        public PlotsGroup Group { get; set; }
        /// <summary>
        /// Short number of the plot within its city, shown as #n over every part of it. Given once
        /// by City.NextPlotNumber and kept when other plots go; 0 for the city land.
        /// </summary>
        public int Number { get; set; }
        /// <summary>
        /// The land of a city outside its plots (City.Land): no shape, no owner, the city's own
        /// rights and flags. Never in the plot index, the city's plot list or the database.
        /// </summary>
        public bool IsCityLand { get; }

        public Plot(string guid, PlotShape shape) : base("", guid)
        {
            Shape = shape;
        }

        /// <summary>The land of the city; shares the city's PermsHandler, so it follows /city set at once.</summary>
        internal Plot(City city, PermsHandler cityPerms) : this(city, cityPerms, "land_" + city.Guid, null)
        {
        }

        /// <summary>Land with rights of its own, such as the chunks of a plots group.</summary>
        internal Plot(City city, PermsHandler perms, string guid, PlotsGroup group) : base("", guid)
        {
            this.city = city;
            permsHandler = perms;
            Group = group;
            IsCityLand = true;
        }

        /// <summary>Empty for the city land, which has no shape.</summary>
        public BlockBox Bounds => Shape?.Bounds ?? default;
        public long Volume => Shape?.Volume ?? 0;

        public void setPlotOwner(PlayerInfo playerInfo)
        {
            ownerOfPlot = playerInfo;
        }
        public PermsHandler getPermsHandler()
        {
            return permsHandler;
        }
        public bool hasPlotOwner()
        {
            return ownerOfPlot != null;
        }
        public bool hasCity()
        {
            return city != null;
        }
        public PlayerInfo getPlotOwner()
        {
            return ownerOfPlot;
        }
        public void resetOwner()
        {
            ownerOfPlot = null;
        }
        /// <summary>
        /// The owner's plot goes back to the city: no owner, not for sale, and the city's rights and
        /// flags instead of the ones the owner set (the group's flags when it is in one).
        /// </summary>
        public void ReturnToCity()
        {
            ownerOfPlot = null;
            Price = -1;
            if (city == null) return;
            permsHandler.setPerm(city.getPermsHandler());
            if (Group != null) PlotsGroupHandler.CopyFlags(Group, this);
        }
        public City getCity()
        {
            return city;
        }
        public void setCity(City city)
        {
            this.city = city;
        }
        public double getCustomTax()
        {
            return customTax;
        }
        public bool setCustomTax(double val)
        {
            if (customTax == val)
            {
                return false;
            }
            customTax = val;
            return true;
        }
        public override bool saveToDatabase(bool update = true)
        {
            // The land is kept as the city's rights and its chunks.
            if (IsCityLand) return false;
            return canclaimsextended.Db.savePlot(this, update);
        }
        public List<string> getStatus(PlayerInfo forPlayer = null)
        {
            List<string> outStrings = new List<string>();
            if (IsCityLand)
            {
                if (Group == null)
                {
                    outStrings.Add(Lang.Get("canclaimsextended:city_land_status", getCity().getPartNameReplaceUnder()) + "\n");
                    outStrings.Add(permsHandler.getStringForChat() + "\n");
                    return outStrings;
                }
                // Members get the group's row; everyone else the city's, under the group's flags.
                outStrings.Add(Lang.Get("canclaimsextended:group_land_status", getCity().getPartNameReplaceUnder(), Group.getPartNameReplaceUnder()) + "\n");
                outStrings.Add(Lang.Get("canclaimsextended:group_land_members", RightsText(Group.Perms.CitizenPerms)) + "\n");
                outStrings.Add(permsHandler.getStringForChat() + "\n");
                return outStrings;
            }
            outStrings.Add(Lang.Get("canclaimsextended:plot_number_status", Number) + (GetPartName() != "" ? " " + GetPartName() : "") + "\n");
            if (hasCity())
            {
                outStrings.Add(Lang.Get("canclaimsextended:city") + getCity().getPartNameReplaceUnder() + "\n");
            }
            if (hasPlotOwner())
            {
                outStrings.Add(Lang.Get("canclaimsextended:plot_owner", getPlotOwner().GetPartName()) + "\n");
            }
            if (IsForSale)
            {
                outStrings.Add(Lang.Get("canclaimsextended:plot_for_sale_status", Price) + "\n");
            }
            outStrings.Add(Lang.Get("canclaimsextended:plot_type_status", Lang.Get(plots.PlotTypes.Of(Type).LangKey)) + "\n");
            BlockBox b = Bounds;
            outStrings.Add(Lang.Get("canclaimsextended:plot_size", b.SizeX, b.SizeY, b.SizeZ, Volume) + "\n");
            outStrings.Add(Lang.Get("canclaimsextended:plot_bounds", b.MinX, b.MinY, b.MinZ, b.MaxX - 1, b.MaxY - 1, b.MaxZ - 1) + "\n");
            outStrings.Add(permsHandler.getStringForChat() + "\n");
            return outStrings;
        }

        /// <summary>use, build, attack as + and -, the way getStringForChat writes a row.</summary>
        static string RightsText(bool[] row)
        {
            var sb = new System.Text.StringBuilder();
            foreach (bool on in row) sb.Append(on ? '+' : '-');
            return sb.ToString();
        }

        // Identity is the guid: a plot keeps it through a reshape.
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            return obj is Plot other && Guid == other.Guid;
        }
    }
}
