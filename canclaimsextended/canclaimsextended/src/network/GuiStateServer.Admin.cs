using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.rights;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>The admin tabs: world flags, city levels, block rules and every city.</summary>
    public static partial class GuiStateServer
    {
        static void FillAdmin(GuiStatePacket state, IServerPlayer player, Config cfg)
        {
            state.IsAdmin = AdminRole.IsAdmin(player);
            if (!state.IsAdmin) return;
            WorldInfo world = canclaimsextended.dataStorage.World;
            foreach (string code in WorldInfo.Codes)
            {
                world.TryGet(code, out bool value);
                state.WorldFlags.Add(new GuiWorldFlagEntry { Code = code, Value = value });
            }
            foreach (var it in Settings.cityLevelsDict)
            {
                state.CityLevels.Add(new GuiCityLevelEntry
                {
                    Citizens = it.Key,
                    Plots = it.Value.AmountOfPlots,
                    Volume = it.Value.MaxVolume,
                    Summon = it.Value.SummonPoints,
                    Extra = it.Value.MaxExtraChunks,
                    Chunks = it.Value.MaxChunks
                });
            }
            FillBlockRules(state, player, cfg);
            events.OnPlayerJoin.TryFindDefaultCity(out City defaultCity);
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList().OrderBy(c => c.GetPartName()))
            {
                state.AdminCities.Add(new GuiAdminCityEntry
                {
                    Name = city.GetPartName(),
                    Mayor = city.getMayor()?.GetPartName() ?? "",
                    Technical = city.isTechnicalCity(),
                    BonusChunks = city.getBonusChunks(),
                    BonusPlots = city.getBonusPlots(),
                    IsDefault = city.Equals(defaultCity)
                });
            }
        }

        static void FillBlockRules(GuiStatePacket state, IServerPlayer player, Config cfg)
        {
            if (cfg.PLOT_TYPE_ONLY_BLOCKS != null)
            {
                foreach (var rule in cfg.PLOT_TYPE_ONLY_BLOCKS)
                {
                    state.BlockRules.Add(new GuiBlockRuleEntry
                    {
                        Key = rule.Key,
                        Types = rule.Value?.ToList() ?? new List<string>(),
                        Blocks = blocks.BehaviorPlotTypeOnly.CountMatches(canclaimsextended.sapi, rule.Key)
                    });
                }
            }
            Vintagestory.API.Common.Block hand = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Block;
            if (hand?.Code == null) return;
            state.HandBlockCode = hand.Code.ToString();
            state.HandBlockKeys = blocks.BehaviorPlotTypeOnly.CandidateKeys(hand);
            state.HandBlockAllowed = blocks.BehaviorPlotTypeOnly.AllowedTypes(hand);
        }
    }
}
