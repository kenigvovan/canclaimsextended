using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// What a box has to pass to become a plot inside a city's territory. Used by the plot commands
    /// and by the live preview of the selection, so both always agree. The territory itself is
    /// ChunkRules.
    /// </summary>
    public static class ClaimRules
    {
        /// <summary>The box that would actually be claimed: the selection cut to the world height.</summary>
        public static BlockBox Normalize(BlockBox b)
        {
            int worldHeight = canclaimsextended.sapi.WorldManager.MapSizeY;
            return new BlockBox(b.MinX, Math.Max(0, b.MinY), b.MinZ, b.MaxX, Math.Min(worldHeight, b.MaxY), b.MaxZ);
        }

        /// <summary>Checks for a new plot of the city.</summary>
        public static ClaimCheck CheckPlotClaim(City city, BlockBox box)
        {
            ClaimCheck check = CheckShapeAndFree(box);
            if (!check.Ok) return check;
            check = CheckInsideTerritory(city, box);
            if (!check.Ok) return check;
            check = CheckPlotLimit(city);
            if (!check.Ok) return check;
            return CheckCityVolume(city, box.Volume);
        }

        /// <summary>A technical (admin) city, or an admin under /cadmin as: no city-wide limits.</summary>
        public static bool NoCityLimits(City city) => city.isTechnicalCity() || AdminActing.Active;

        /// <summary>Under /cadmin as the size limits of one plot (volume, height, parts) do not apply either.</summary>
        static bool NoPlotLimits => AdminActing.Active;

        /// <summary>Room under the city's plot limit.</summary>
        public static ClaimCheck CheckPlotLimit(City city)
        {
            if (NoCityLimits(city)) return ClaimCheck.Success;
            int maxPlots = Settings.GetMaxNumberOfPlotForCity(city);
            return city.getCityPlots().Count >= maxPlots
                ? ClaimCheck.Fail("canclaimsextended:max_amount_claimed", maxPlots)
                : ClaimCheck.Success;
        }

        /// <summary>A valid box that overlaps no plot; all the admin commands ask for.</summary>
        public static ClaimCheck CheckShapeAndFree(BlockBox box)
        {
            ClaimCheck check = CheckShape(box);
            return check.Ok ? CheckFree(box) : check;
        }

        /// <summary>The city stays within its volume limit with this much more plot volume.</summary>
        public static ClaimCheck CheckCityVolume(City city, long addedVolume)
        {
            if (NoCityLimits(city)) return ClaimCheck.Success;
            long maxVolume = Settings.GetMaxVolumeForCity(city);
            long volumeAfter = city.GetTotalVolume() + addedVolume;
            return volumeAfter > maxVolume
                ? ClaimCheck.Fail("canclaimsextended:max_volume_claimed", volumeAfter, maxVolume)
                : ClaimCheck.Success;
        }

        public static ClaimCheck CheckShape(BlockBox box)
        {
            Config c = canclaimsextended.config;
            int smallest = Math.Min(box.SizeX, Math.Min(box.SizeY, box.SizeZ));
            if (smallest < c.PLOT_MIN_DIM)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_too_thin", c.PLOT_MIN_DIM);
            }
            if (NoPlotLimits) return ClaimCheck.Success;
            if (box.SizeY > c.PLOT_MAX_HEIGHT)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_too_high", box.SizeY, c.PLOT_MAX_HEIGHT);
            }
            if (box.Volume > c.PLOT_MAX_VOLUME)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_too_big", box.Volume, c.PLOT_MAX_VOLUME);
            }
            return ClaimCheck.Success;
        }

        /// <summary>No other plot shares a block with the box.</summary>
        public static ClaimCheck CheckFree(BlockBox box)
        {
            Plot other = canclaimsextended.dataStorage.QueryPlots(box).FirstOrDefault();
            if (other == null)
            {
                return ClaimCheck.Success;
            }
            return other.hasCity()
                ? ClaimCheck.Fail("canclaimsextended:intersects_city", other.getCity().getPartNameReplaceUnder())
                : ClaimCheck.Fail("canclaimsextended:plot_already_claimed");
        }

        /// <summary>Every chunk under the box belongs to the city.</summary>
        public static bool InsideTerritory(City city, BlockBox box)
        {
            return DataStorage.ChunksIn(box).All(city.HasChunk);
        }

        public static bool InsideTerritory(City city, PlotShape shape)
        {
            return shape.Boxes.All(b => InsideTerritory(city, b));
        }

        public static ClaimCheck CheckInsideTerritory(City city, BlockBox box)
        {
            return InsideTerritory(city, box)
                ? ClaimCheck.Success
                : ClaimCheck.Fail("canclaimsextended:plot_outside_territory", city.getPartNameReplaceUnder(), canclaimsextended.config.COMMAND_CITY);
        }

        /// <summary>Checks for adding the box to an existing plot of a city.</summary>
        public static ClaimCheck CheckExtend(Plot plot, BlockBox box)
        {
            ClaimCheck check = CheckShapeAndFree(box);
            if (!check.Ok) return check;
            if (canclaimsextended.config.PLOT_PARTS_MUST_TOUCH && !plot.Shape.Touches(new PlotShape(box)))
            {
                return ClaimCheck.Fail("canclaimsextended:extend_should_touch_plot");
            }
            int maxBoxes = canclaimsextended.config.PLOT_MAX_BOXES;
            if (!NoPlotLimits && plot.Shape.Boxes.Count >= maxBoxes)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_max_boxes", maxBoxes);
            }
            long plotVolumeAfter = plot.Volume + box.Volume;
            if (!NoPlotLimits && plotVolumeAfter > canclaimsextended.config.PLOT_MAX_VOLUME)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_too_big", plotVolumeAfter, canclaimsextended.config.PLOT_MAX_VOLUME);
            }
            City city = plot.getCity();
            if (city == null)
            {
                return ClaimCheck.Success;
            }
            check = CheckInsideTerritory(city, box);
            return check.Ok ? CheckCityVolume(city, box.Volume) : check;
        }

        /// <summary>
        /// The box with one face moved by delta blocks, outwards when positive. False when the face
        /// would reach the opposite one or the direction is unknown.
        /// </summary>
        public static bool TryMoveFace(BlockBox box, string direction, int delta, out BlockBox moved)
        {
            int step = delta;
            int minX = box.MinX, minY = box.MinY, minZ = box.MinZ, maxX = box.MaxX, maxY = box.MaxY, maxZ = box.MaxZ;
            switch (direction)
            {
                case "east": maxX += step; break;
                case "west": minX -= step; break;
                case "south": maxZ += step; break;
                case "north": minZ -= step; break;
                case "up": maxY += step; break;
                case "down": minY -= step; break;
                default: moved = default; return false;
            }
            if (maxX <= minX || maxY <= minY || maxZ <= minZ)
            {
                moved = default;
                return false;
            }
            int worldHeight = canclaimsextended.sapi.WorldManager.MapSizeY;
            moved = new BlockBox(minX, Math.Max(0, minY), minZ, maxX, Math.Min(worldHeight, maxY), maxZ);
            return true;
        }

        /// <summary>
        /// Checks for one box of a plot taking a new size: the shape rules, nothing else in the
        /// grown part, inside the territory, and the limits of the plot and the city.
        /// </summary>
        public static ClaimCheck CheckResize(Plot plot, int index, BlockBox newBox)
        {
            ClaimCheck check = CheckShape(newBox);
            if (!check.Ok) return check;
            IReadOnlyList<BlockBox> boxes = plot.Shape.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i != index && boxes[i].Intersects(newBox))
                {
                    return ClaimCheck.Fail("canclaimsextended:plot_resize_overlaps_own", i + 1);
                }
            }
            Plot other = canclaimsextended.dataStorage.QueryPlots(newBox).FirstOrDefault(p => !p.Equals(plot));
            if (other != null)
            {
                return other.hasCity()
                    ? ClaimCheck.Fail("canclaimsextended:intersects_city", other.getCity().getPartNameReplaceUnder())
                    : ClaimCheck.Fail("canclaimsextended:plot_already_claimed");
            }
            long plotVolumeAfter = plot.Volume - boxes[index].Volume + newBox.Volume;
            if (!NoPlotLimits && plotVolumeAfter > canclaimsextended.config.PLOT_MAX_VOLUME)
            {
                return ClaimCheck.Fail("canclaimsextended:plot_too_big", plotVolumeAfter, canclaimsextended.config.PLOT_MAX_VOLUME);
            }
            City city = plot.getCity();
            if (city == null)
            {
                return ClaimCheck.Success;
            }
            check = CheckInsideTerritory(city, newBox);
            if (!check.Ok) return check;
            long added = newBox.Volume - boxes[index].Volume;
            if (added > 0)
            {
                check = CheckCityVolume(city, added);
                if (!check.Ok) return check;
            }
            return ShapeStaysValid(WithBoxReplaced(plot.Shape, index, newBox))
                ? ClaimCheck.Success
                : ClaimCheck.Fail("canclaimsextended:plot_cut_splits");
        }

        public static PlotShape WithBoxReplaced(PlotShape shape, int index, BlockBox newBox)
        {
            return new PlotShape(shape.Boxes.Select((b, i) => i == index ? newBox : b));
        }

        /// <summary>
        /// The plot after a box was taken off: whole when PLOT_PARTS_MUST_TOUCH asks for it,
        /// anything otherwise.
        /// </summary>
        public static bool ShapeStaysValid(PlotShape newShape)
        {
            return !canclaimsextended.config.PLOT_PARTS_MUST_TOUCH || newShape.IsConnected();
        }

        /// <summary>The checks of /plot cut: not the last box, and the rest stays valid without it.</summary>
        public static bool CanCutBox(PlotShape shape, int index)
        {
            IReadOnlyList<BlockBox> boxes = shape.Boxes;
            if (boxes.Count <= 1) return false;
            return ShapeStaysValid(new PlotShape(boxes.Where((_, i) => i != index)));
        }
    }
}
