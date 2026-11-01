using System.Collections.Generic;
using System.Text;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.geometry;
using canclaimsextended.src.items;
using canclaimsextended.src.messages;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace canclaimsextended.src.selection
{
    /// <summary>
    /// Selections of all players and their highlight. While a player holds the claim wand they see
    /// the plots around them (their own city in one color, other cities in another) and their
    /// selection, green when it can be claimed and red when it cannot.
    /// </summary>
    public class SelectionHandler
    {
        public const int HIGHLIGHT_SLOT = 7101;
        const int MAX_SHOWN_BOXES = 256;

        readonly Dictionary<string, PlotSelection> selections = new();
        // What was last sent to each player, so an unchanged picture is not sent again every second.
        readonly Dictionary<string, string> lastSent = new();
        // Players who see territory borders without the wand (/city borders on). The client draws
        // the borders; the server only keeps the choice, so it lasts over a reconnect.
        readonly HashSet<string> bordersAlways = new();

        public SelectionHandler()
        {
            auxiliary.ServerTicks.Every(canclaimsextended.sapi, _ => RefreshAll(), 1000);
            canclaimsextended.sapi.Event.PlayerDisconnect += player =>
            {
                lastSent.Remove(player.PlayerUID);
                // A selection is for the session; kept, every player who ever selected would stay here.
                selections.Remove(player.PlayerUID);
            };
            canclaimsextended.sapi.Event.PlayerNowPlaying += player =>
            {
                if (bordersAlways.Contains(player.PlayerUID)) network.PlotSyncServer.SendBordersState(player, true);
            };
        }

        public bool BordersAlwaysShown(IPlayer player) => bordersAlways.Contains(player.PlayerUID);

        public void SetBordersAlwaysShown(IServerPlayer player, bool show)
        {
            if (show) bordersAlways.Add(player.PlayerUID);
            else bordersAlways.Remove(player.PlayerUID);
            network.PlotSyncServer.SendBordersState(player, show);
        }

        public PlotSelection Get(IPlayer player, bool create = false)
        {
            if (!selections.TryGetValue(player.PlayerUID, out var sel) && create)
            {
                sel = new PlotSelection();
                selections[player.PlayerUID] = sel;
            }
            return sel;
        }

        public void SetCorner(IServerPlayer player, int corner, BlockPos pos)
        {
            PlotSelection sel = Get(player, true);
            // A held mouse button repeats the click every frame; only a new corner is news.
            BlockPos current = corner == 1 ? sel.Corner1 : sel.Corner2;
            if (current != null && current.Equals(pos)) return;
            if (corner == 1) sel.Corner1 = pos.Copy();
            else sel.Corner2 = pos.Copy();
            Changed(player);
        }

        public void Clear(IServerPlayer player)
        {
            if (!selections.Remove(player.PlayerUID)) return;
            Refresh(player);
            MessageHandler.sendMsgToPlayer(player, Lang.GetL(player.LanguageCode, "canclaimsextended:selection_cleared"));
        }

        /// <summary>Tells the player what they have selected now and redraws it.</summary>
        public void Changed(IServerPlayer player)
        {
            MessageHandler.sendMsgToPlayer(player, Describe(player));
            Refresh(player);
        }

        /// <summary>The box that /city claim would take, if both corners are set.</summary>
        public bool TryGetClaimBox(IPlayer player, out BlockBox box)
        {
            PlotSelection sel = Get(player);
            if (sel == null || !sel.IsComplete || !sel.TryGetRawBox(out BlockBox raw))
            {
                box = default;
                return false;
            }
            box = ClaimRules.Normalize(raw);
            return true;
        }

        /// <summary>
        /// The check the selection is shown with: a new plot of the player's city. A city itself is
        /// founded by chunk, without a selection.
        /// </summary>
        public static ClaimCheck CheckFor(IPlayer player, BlockBox box)
        {
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo);
            // The city an admin acts for (AdminActing) when the window is built for it.
            City city = AdminActing.CityOf(playerInfo);
            if (city != null)
            {
                return ClaimRules.CheckPlotClaim(city, box);
            }
            return ClaimCheck.Fail("canclaimsextended:selection_needs_city", canclaimsextended.config.COMMAND_CITY);
        }

        /// <summary>
        /// The plot of the player's own city they stand in, when the box could be added to it with
        /// /plot extend.
        /// </summary>
        public static bool CanExtendHere(IServerPlayer player, BlockBox box)
        {
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo);
            City city = AdminActing.CityOf(playerInfo);
            if (city == null || player.Entity == null) return false;
            if (!PlotLookup.TryGetSubPlotAtFeet(player, out Plot plot)) return false;
            return city.Equals(plot.getCity()) && ClaimRules.CheckExtend(plot, box).Ok;
        }

        public string Describe(IServerPlayer player)
        {
            // Sent to this player only, so in their own language.
            string lang = player.LanguageCode;
            PlotSelection sel = Get(player);
            if (sel == null || (sel.Corner1 == null && sel.Corner2 == null))
            {
                return Lang.GetL(lang, "canclaimsextended:selection_empty", canclaimsextended.config.COMMAND_CITY);
            }
            if (!sel.IsComplete)
            {
                return Lang.GetL(lang, sel.Corner1 == null ? "canclaimsextended:selection_need_corner1" : "canclaimsextended:selection_need_corner2");
            }
            TryGetClaimBox(player, out BlockBox box);
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo);
            // As CheckFor decides it: an admin acting for a city has that city.
            bool hasCity = AdminActing.CityOf(playerInfo) != null;

            StringBuilder sb = new StringBuilder();
            sb.Append(Lang.GetL(lang, "canclaimsextended:selection_size", box.SizeX, box.SizeY, box.SizeZ, box.Volume));
            if (canclaimsextended.economyProvider.SupportsPlayerWallet && hasCity)
            {
                sb.Append(' ').Append(Lang.GetL(lang, "canclaimsextended:selection_price", economy.PlotPricing.ClaimCost(box)));
            }
            ClaimCheck check = CheckFor(player, box);
            // A box the plot the player stands in may take can also become part of it.
            bool canExtend = CanExtendHere(player, box);
            sb.Append('\n');
            if (check.Ok)
            {
                sb.Append(Lang.GetL(lang, "canclaimsextended:selection_can_claim", canclaimsextended.config.COMMAND_PLOT));
            }
            else if (!canExtend)
            {
                sb.Append(Lang.GetL(lang, "canclaimsextended:selection_cannot_claim", Lang.GetL(lang, check.LangKey, check.Args)));
            }
            if (canExtend)
            {
                if (check.Ok) sb.Append('\n');
                sb.Append(Lang.GetL(lang, "canclaimsextended:selection_can_extend", canclaimsextended.config.COMMAND_PLOT));
            }
            return sb.ToString();
        }

        void RefreshAll()
        {
            foreach (IPlayer player in canclaimsextended.sapi.World.AllOnlinePlayers)
            {
                if (player is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing)
                {
                    Refresh(sp);
                }
            }
        }

        public void Refresh(IServerPlayer player)
        {
            if (player.Entity == null) return;
            PlotSelection sel = Get(player);
            bool holdsWand = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemClaimWand;
            bool show = holdsWand || (sel?.AlwaysShow ?? false);

            var positions = new List<BlockPos>();
            var colors = new List<int>();
            if (show)
            {
                AddNearbyPlots(player, positions, colors);
                AddSelection(player, sel, positions, colors);
            }

            string signature = Signature(positions, colors);
            lastSent.TryGetValue(player.PlayerUID, out string previous);
            if (signature == (previous ?? "")) return;
            lastSent[player.PlayerUID] = signature;
            canclaimsextended.sapi.World.HighlightBlocks(player, HIGHLIGHT_SLOT, positions, colors,
                EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cubes);
        }

        void AddNearbyPlots(IServerPlayer player, List<BlockPos> positions, List<int> colors)
        {
            canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo);
            City ownCity = playerInfo?.City;
            BlockPos at = player.Entity.Pos.AsBlockPos;
            int r = canclaimsextended.config.SELECTION_VIEW_RADIUS;
            BlockBox area = new BlockBox(at.X - r, at.Y - r, at.Z - r, at.X + r, at.Y + r, at.Z + r);
            // Each plot in its own shade of its city color; the alpha still tells own city from others.
            int ownAlpha = canclaimsextended.config.PLOT_COLOR_OWN_CITY[0];
            int otherAlpha = canclaimsextended.config.PLOT_COLOR_OTHER_CITY[0];
            int noCity = Color(canclaimsextended.config.PLOT_COLOR_OTHER_CITY);
            // The plot underfoot first and whole, far parts included, in its own color.
            Plot current = null;
            if (PlotLookup.TryGetSubPlotAtFeet(player, out Plot here))
            {
                current = here;
                int bright = Color(canclaimsextended.config.PLOT_COLOR_CURRENT);
                foreach (BlockBox b in current.Shape.Boxes)
                {
                    AddBox(b, bright, positions, colors);
                }
            }
            foreach (Plot plot in canclaimsextended.dataStorage.QueryPlots(area))
            {
                if (plot.Equals(current)) continue;
                City city = plot.getCity();
                int color = city == null ? noCity
                    : PlotTint.Tint(city.cityColor, plot.Guid, city.Equals(ownCity) ? ownAlpha : otherAlpha);
                foreach (BlockBox b in plot.Shape.Boxes)
                {
                    if (positions.Count / 2 >= MAX_SHOWN_BOXES) return;
                    AddBox(b, color, positions, colors);
                }
            }
        }

        void AddSelection(IServerPlayer player, PlotSelection sel, List<BlockPos> positions, List<int> colors)
        {
            if (sel == null || !sel.TryGetRawBox(out BlockBox raw)) return;
            if (!sel.IsComplete)
            {
                AddBox(raw, Color(canclaimsextended.config.SELECTION_COLOR_CORNER), positions, colors);
                return;
            }
            BlockBox box = ClaimRules.Normalize(raw);
            bool ok = CheckFor(player, box).Ok || CanExtendHere(player, box) || AdminCanUse(player, box);
            AddBox(box, Color(ok ? canclaimsextended.config.SELECTION_COLOR_OK : canclaimsextended.config.SELECTION_COLOR_BAD), positions, colors);
        }

        /// <summary>
        /// For an admin: the box could become a plot of the city whose land it is on, or a part of the
        /// plot they stand in - what /cadmin plot create and extend would take, so the colour matches
        /// the admin buttons rather than the admin's own city.
        /// </summary>
        static bool AdminCanUse(IServerPlayer player, BlockBox box)
        {
            if (AdminActing.Active || !AdminRole.IsAdmin(player)
                || !canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo admin))
            {
                return false;
            }
            if (canclaimsextended.dataStorage.GetCityAt(new BlockPos(box.MinX, box.MinY, box.MinZ), out City city)
                && AdminActing.Run(admin, city, () => ClaimRules.CheckPlotClaim(city, box)).Ok)
            {
                return true;
            }
            return PlotLookup.TryGetSubPlotUnderPlayer(player, out Plot here)
                && ManageRules.CheckAdminExtend(admin, here, box).Ok;
        }

        static void AddBox(BlockBox b, int color, List<BlockPos> positions, List<int> colors)
        {
            // The cube shape spans from the first position to the second, so max stays exclusive.
            positions.Add(new BlockPos(b.MinX, b.MinY, b.MinZ));
            positions.Add(new BlockPos(b.MaxX, b.MaxY, b.MaxZ));
            colors.Add(color);
        }

        // HighlightBlocks reads red from the lowest byte, as ColorFromRgba writes it.
        static int Color(int[] argb)
        {
            return ColorUtil.ColorFromRgba(argb[1], argb[2], argb[3], argb[0]);
        }

        static string Signature(List<BlockPos> positions, List<int> colors)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < positions.Count; i++)
            {
                BlockPos p = positions[i];
                sb.Append(p.X).Append(',').Append(p.Y).Append(',').Append(p.Z).Append(';');
            }
            foreach (int c in colors)
            {
                sb.Append(c).Append(';');
            }
            return sb.ToString();
        }
    }
}
