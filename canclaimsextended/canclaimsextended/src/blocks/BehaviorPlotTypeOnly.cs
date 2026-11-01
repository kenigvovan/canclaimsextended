using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.messages;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.blocks
{
    /// <summary>
    /// PLOT_TYPE_ONLY_BLOCKS: the block may be placed only on a plot of one of the listed types,
    /// e.g. the vinconomy registers only on shop plots. Creative players are not stopped. The rules
    /// are read on every placement, so changing the types of a key needs nothing more.
    /// </summary>
    public class BehaviorPlotTypeOnly : BlockBehavior
    {
        public BehaviorPlotTypeOnly(Block block) : base(block)
        {
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            // Only the server knows plot types; a refused placement is undone on the client.
            if (world.Side != EnumAppSide.Server || byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative) return true;
            List<string> allowed = AllowedTypes(block);
            if (allowed.Count == 0) return true;

            if (canclaimsextended.dataStorage.GetSubPlotAt(blockSel.Position, out Plot plot)
                && allowed.Contains(PlotTypes.Of(plot.Type).Code))
            {
                return true;
            }
            handling = EnumHandling.PreventSubsequent;
            failureCode = "__ignore__";
            string names = string.Join(", ", allowed.Select(code => Lang.Get("canclaimsextended:plot_type_" + code)));
            MessageHandler.sendMsgToPlayer(byPlayer as IServerPlayer, Lang.Get("canclaimsextended:block_only_on_plot_types", names));
            return false;
        }

        /// <summary>The types of every key that matches the block; empty when it is free to place.</summary>
        public static List<string> AllowedTypes(Block block)
        {
            var rules = canclaimsextended.config.PLOT_TYPE_ONLY_BLOCKS;
            if (rules == null) return new List<string>();
            return rules.Where(r => r.Value != null && Matches(block, r.Key))
                .SelectMany(r => r.Value).Distinct().ToList();
        }

        /// <summary>
        /// Adds the behaviour to every block a PLOT_TYPE_ONLY_BLOCKS key matches and that has none
        /// yet: a block code with * wildcards, or "be:Name" for blocks with that block entity
        /// behaviour. Run once the blocks are loaded (RunGame) and again when a key is added.
        /// </summary>
        public static void AttachToBlocks(ICoreServerAPI sapi)
        {
            var rules = canclaimsextended.config.PLOT_TYPE_ONLY_BLOCKS;
            if (rules == null || rules.Count == 0) return;
            foreach (Block block in sapi.World.Blocks)
            {
                if (block?.Code == null || !rules.Keys.Any(k => Matches(block, k))) continue;
                BlockBehavior[] existing = block.BlockBehaviors ?? Array.Empty<BlockBehavior>();
                if (existing.Any(b => b is BehaviorPlotTypeOnly)) continue;
                BlockBehavior[] extended = new BlockBehavior[existing.Length + 1];
                Array.Copy(existing, extended, existing.Length);
                extended[existing.Length] = new BehaviorPlotTypeOnly(block);
                block.BlockBehaviors = extended;
            }
        }

        // Counting walks every block; the window asks for it on each refresh.
        static readonly Dictionary<string, int> matchCounts = new();

        /// <summary>Forget the counts: on shutdown, and when the set of blocks behind a key may have changed.</summary>
        public static void ClearMatchCounts() => matchCounts.Clear();

        /// <summary>How many loaded blocks the key matches, for the admin to see a typo.</summary>
        public static int CountMatches(ICoreServerAPI sapi, string key)
        {
            if (matchCounts.TryGetValue(key, out int count)) return count;
            count = sapi.World.Blocks.Count(b => b?.Code != null && Matches(b, key));
            matchCounts[key] = count;
            return count;
        }

        /// <summary>
        /// Keys an admin may pick for the block: its exact code, the code with the variant cut to
        /// a *, and one per block entity behaviour.
        /// </summary>
        public static List<string> CandidateKeys(Block block)
        {
            var keys = new List<string>();
            if (block?.Code == null) return keys;
            keys.Add(block.Code.ToString());
            int dash = block.Code.Path.IndexOf('-');
            if (dash > 0)
            {
                keys.Add(block.Code.Domain + ":" + block.Code.Path.Substring(0, dash) + "-*");
            }
            keys.Add(block.Code.Domain + ":*");
            if (block.BlockEntityBehaviors != null)
            {
                keys.AddRange(block.BlockEntityBehaviors.Where(b => !string.IsNullOrEmpty(b.Name)).Select(b => "be:" + b.Name));
            }
            return keys.Distinct().ToList();
        }

        public static bool Matches(Block block, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (key.StartsWith("be:"))
            {
                string name = key.Substring(3);
                return block.BlockEntityBehaviors?.Any(b => b.Name == name) == true;
            }
            return block.WildCardMatch(new AssetLocation(key));
        }
    }
}
