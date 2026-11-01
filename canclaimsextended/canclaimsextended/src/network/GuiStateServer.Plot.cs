using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>The plot tab: the plot under the player, its parts, and the plots of the city they may reshape.</summary>
    public static partial class GuiStateServer
    {
        static void FillPlot(GuiStatePacket state, Plot plot, PlayerInfo playerInfo)
        {
            state.PlotGuid = plot.Guid;
            state.PlotName = plot.GetPartName();
            state.PlotNumber = plot.Number;
            state.PlotCity = plot.getCity()?.GetPartName() ?? "";
            state.PlotOwner = plot.getPlotOwner()?.GetPartName() ?? "";
            state.PlotPrice = plot.Price;
            state.PlotFee = (int)plot.getCustomTax();
            var bounds = plot.Bounds;
            state.PlotSizeX = bounds.SizeX;
            state.PlotSizeY = bounds.SizeY;
            state.PlotSizeZ = bounds.SizeZ;
            state.PlotVolume = plot.Volume;
            state.PlotBoxes = plot.Shape.Boxes.Count;

            PermsHandler perms = plot.getPermsHandler();
            state.PlotPerms = PermsRow(perms);
            state.PlotPvp = perms.pvpFlag;
            state.PlotFire = perms.fireFlag;
            state.PlotBlast = perms.blastFlag;
            state.PlotNoMobSpawn = perms.noMobSpawnFlag;

            state.CanManagePlot = ManageRules.MayManage(playerInfo, plot);

            // Same rule as /plot set type: the mayor or PLOT_SET_ALL_CITY_PLOTS, on their own city's plot.
            state.PlotType = PlotTypes.Of(plot.Type).Code;
            City plotCity = plot.getCity();
            state.CanSetPlotType = plotCity != null && plotCity.Equals(AdminActing.CityOf(playerInfo))
                && ManageRules.IsMayorOr(playerInfo, plotCity, EnumPlayerPermissions.PLOT_SET_ALL_CITY_PLOTS);
            // Same rule as /plot set fee.
            state.CanSetPlotFee = plotCity != null && plotCity.Equals(AdminActing.CityOf(playerInfo))
                && ManageRules.IsMayorOr(playerInfo, plotCity, EnumPlayerPermissions.PLOT_SET_FEE);
            state.MaxPlotFee = (int)canclaimsextended.config.MAX_PLOT_FEE;
            if (state.CanSetPlotType)
            {
                foreach (string code in PlotTypes.EnabledCodes)
                {
                    PlotTypes.TryGet(code, out PlotInfo info);
                    state.PlotTypes.Add(new GuiPlotTypeEntry
                    {
                        Code = code,
                        SetCost = Money((decimal)info.SetCost()),
                        UnitUpkeep = Money((decimal)info.UnitUpkeep())
                    });
                }
            }
        }

        /// <summary>The boxes of the plot under the player, and whether they may give it up.</summary>
        static void FillPlotParts(GuiStatePacket state, Plot plot, IServerPlayer player, PlayerInfo playerInfo)
        {
            BlockPos feet = player.Entity.Pos.AsBlockPos;
            BlockPos below = feet.DownCopy();
            AddParts(state.PlotParts, plot, b => b.Contains(feet) || b.Contains(below));
            City plotCity = plot.getCity();
            state.CanUnclaim = plotCity != null && plotCity.Equals(AdminActing.CityOf(playerInfo))
                && !plot.hasPlotOwner()
                && ManageRules.IsMayorOr(playerInfo, plotCity, EnumPlayerPermissions.CITY_DELETE_PLOT);
        }

        /// <summary>The boxes of the plot in /plot boxes order, each with whether /plot cut would take it.</summary>
        static void AddParts(List<GuiBoxEntry> parts, Plot plot, System.Func<BlockBox, bool> isHere)
        {
            IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                BlockBox b = boxes[i];
                parts.Add(new GuiBoxEntry
                {
                    SizeX = b.SizeX, SizeY = b.SizeY, SizeZ = b.SizeZ, Volume = b.Volume,
                    MinX = b.MinX, MinY = b.MinY, MinZ = b.MinZ,
                    MaxX = b.MaxX, MaxY = b.MaxY, MaxZ = b.MaxZ,
                    IsHere = isHere(b),
                    CanCut = ClaimRules.CanCutBox(plot.Shape, i)
                });
            }
        }

        /// <summary>
        /// Plots of the city the player may reshape, for /plot extend #n and /plot cut #n part from
        /// anywhere; by the same checks the commands use.
        /// </summary>
        static void FillCityPlots(GuiStatePacket state, City city, IServerPlayer player, PlayerInfo playerInfo)
        {
            bool hasBox = canclaimsextended.selections.TryGetClaimBox(player, out BlockBox box);
            foreach (Plot plot in city.getCityPlots().OrderBy(p => p.Number))
            {
                if (!ManageRules.MayReshape(playerInfo, plot)) continue;
                var entry = new GuiPlotEntry
                {
                    Number = plot.Number,
                    Name = plot.GetPartName(),
                    Volume = plot.Volume,
                    CanExtend = hasBox && ClaimRules.CheckExtend(plot, box).Ok
                };
                AddParts(entry.Parts, plot, _ => false);
                entry.CanCut = entry.Parts.Any(p => p.CanCut);
                state.CityPlots.Add(entry);
            }
        }
    }
}
