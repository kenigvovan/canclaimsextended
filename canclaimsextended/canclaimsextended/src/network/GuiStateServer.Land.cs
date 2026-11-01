using canclaimsextended.src.economy;
using canclaimsextended.src.geometry;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using canclaimsextended.src.selection;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>The land tab: the selection and the chunk under the player.</summary>
    public static partial class GuiStateServer
    {
        /// <summary>What the selection is and what may be done with it, by the same checks the commands use.</summary>
        static void FillSelection(GuiStatePacket state, IServerPlayer player, PlayerInfo playerInfo)
        {
            PlotSelection sel = canclaimsextended.selections.Get(player);
            if (sel == null) return;
            state.SelAlwaysShow = sel.AlwaysShow;
            state.SelHasCorner1 = sel.Corner1 != null;
            state.SelHasCorner2 = sel.Corner2 != null;
            if (!canclaimsextended.selections.TryGetClaimBox(player, out BlockBox box)) return;

            state.SelSizeX = box.SizeX;
            state.SelSizeY = box.SizeY;
            state.SelSizeZ = box.SizeZ;
            state.SelVolume = box.Volume;
            bool hasCity = AdminActing.CityOf(playerInfo) != null;
            if (state.HasEconomy && hasCity)
            {
                state.SelPrice = Money((decimal)PlotPricing.ClaimCost(box));
            }
            ClaimCheck check = SelectionHandler.CheckFor(player, box);
            state.SelCanClaim = hasCity && check.Ok;
            state.SelCanExtend = SelectionHandler.CanExtendHere(player, box);
            // /cadmin plot extend: any plot the admin stands in, without money or limits.
            if (AdminRole.IsAdmin(player) && PlotLookup.TryGetSubPlotUnderPlayer(player, out Plot here))
            {
                ClaimCheck extend = ManageRules.CheckAdminExtend(playerInfo, here, box);
                state.SelCanAdminExtend = extend.Ok;
                // Why the box cannot be added to the plot the admin stands in; the client translates it.
                if (!extend.Ok)
                {
                    state.SelAdminExtendProblem = extend.LangKey ?? "";
                    state.SelAdminExtendProblemArgs = ArgStrings(extend);
                }
            }
            if (!check.Ok)
            {
                state.SelProblem = check.LangKey ?? "";
                state.SelProblemArgs = ArgStrings(check);
            }
        }

        /// <summary>The chunk under the player: whose it is and what the player may do with it.</summary>
        static void FillChunk(GuiStatePacket state, IServerPlayer player, PlayerInfo playerInfo, Config cfg)
        {
            ChunkPos chunk = PlotLookup.ChunkUnderPlayer(player);
            state.HereChunkX = chunk.X;
            state.HereChunkZ = chunk.Z;
            state.ChunkSize = ChunkPos.Size;
            City city = AdminActing.CityOf(playerInfo);
            if (canclaimsextended.dataStorage.GetCityAtChunk(chunk, out City owner))
            {
                state.HereCity = owner.GetPartName();
                state.HereIsOwnCity = owner.Equals(city);
                if (owner.TryGetChunk(chunk, out CityChunk held))
                {
                    state.HereIsOutpost = held.IsOutpost;
                    state.HereIsExtra = held.ExtraBought;
                    state.HereChunkGroup = held.Group?.GetPartName() ?? "";
                }
            }
            if (state.HasEconomy)
            {
                state.ChunkPrice = Money((decimal)PlotPricing.ChunkClaimCost());
                state.OutpostPrice = Money((decimal)(PlotPricing.ChunkClaimCost() + cfg.OUTPOST_CHUNK_COST));
                state.ExtraChunkPrice = Money((decimal)(PlotPricing.ChunkClaimCost() + cfg.EXTRA_CHUNK_COST));
            }
            if (city == null)
            {
                state.ChunkCanFound = ChunkRules.CheckNewCity(chunk).Ok;
                return;
            }
            ClaimCheck claim = ChunkRules.CheckClaim(city, chunk);
            state.ChunkCanClaim = claim.Ok && ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CLAIM_CHUNK);
            state.ChunkCanClaimExtra = !claim.Ok && ChunkRules.CheckExtraClaim(city, chunk).Ok
                && ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK);
            state.ChunkCanOutpost = ChunkRules.CheckOutpost(city, chunk).Ok
                && ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_BUY_OUTPOST);
            state.ChunkCanUnclaim = state.HereIsOwnCity && ChunkRules.CheckUnclaim(city, chunk).Ok
                && ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_UNCLAIM_CHUNK);
            // On its own land the reason a claim fails is only noise.
            if (!claim.Ok && !state.HereIsOwnCity)
            {
                state.ChunkProblem = claim.LangKey ?? "";
                state.ChunkProblemArgs = ArgStrings(claim);
            }
        }
    }
}
