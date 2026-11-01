using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace canclaimsextended.src.commands
{
    /// <summary>/cadmin blockrule ...: blocks that may be placed only on plots of given types (PLOT_TYPE_ONLY_BLOCKS).</summary>
    public partial class CAdminCommand
    {
        /// <summary>/cadmin blockrule add &lt;key&gt; &lt;type&gt;: blocks of the key only on plots of that type; works at once.</summary>
        public static TextCommandResult BlockRuleAdd(TextCommandCallingArgs args)
        {
            string key = ((string)args[0] ?? "").Trim();
            string type = ((string)args[1] ?? "").ToLowerInvariant();
            if (key.Length == 0 || key == "be:")
            {
                return TextCommandResult.Error("canclaimsextended:admin_blockrule_bad_key");
            }
            if (!part.structure.plots.PlotTypes.TryGet(type, out _))
            {
                return ErrorWithParams("canclaimsextended:unknown_plot_type", string.Join(", ", part.structure.plots.PlotTypes.EnabledCodes));
            }
            var rules = canclaimsextended.config.PLOT_TYPE_ONLY_BLOCKS ??= new Dictionary<string, List<string>>();
            if (!rules.TryGetValue(key, out List<string> types) || types == null)
            {
                rules[key] = types = new List<string>();
            }
            if (!types.Contains(type)) types.Add(type);
            canclaimsextended.sapi.StoreModConfig(canclaimsextended.config, Config.FILE_NAME);
            blocks.BehaviorPlotTypeOnly.AttachToBlocks(canclaimsextended.sapi);
            return SuccessWithParams("canclaimsextended:admin_blockrule_added", key, string.Join(", ", types),
                blocks.BehaviorPlotTypeOnly.CountMatches(canclaimsextended.sapi, key));
        }

        /// <summary>/cadmin blockrule remove &lt;key&gt; [type]: one type off the key, or the whole key.</summary>
        public static TextCommandResult BlockRuleRemove(TextCommandCallingArgs args)
        {
            string key = ((string)args[0] ?? "").Trim();
            var rules = canclaimsextended.config.PLOT_TYPE_ONLY_BLOCKS;
            if (rules == null || !rules.TryGetValue(key, out List<string> types))
            {
                return ErrorWithParams("canclaimsextended:admin_blockrule_not_found", key);
            }
            if (!args.Parsers[1].IsMissing)
            {
                types?.Remove(((string)args[1] ?? "").ToLowerInvariant());
            }
            // A key without types would restrict nothing; it goes with its last type.
            if (args.Parsers[1].IsMissing || types == null || types.Count == 0)
            {
                rules.Remove(key);
            }
            canclaimsextended.sapi.StoreModConfig(canclaimsextended.config, Config.FILE_NAME);
            return rules.ContainsKey(key)
                ? SuccessWithParams("canclaimsextended:admin_blockrule_added", key, string.Join(", ", types),
                    blocks.BehaviorPlotTypeOnly.CountMatches(canclaimsextended.sapi, key))
                : SuccessWithParams("canclaimsextended:admin_blockrule_removed", key);
        }

        public static TextCommandResult BlockRuleList(TextCommandCallingArgs args)
        {
            var rules = canclaimsextended.config.PLOT_TYPE_ONLY_BLOCKS;
            if (rules == null || rules.Count == 0)
            {
                return TextCommandResult.Success("canclaimsextended:admin_blockrule_none");
            }
            var sb = new StringBuilder(Lang.Get("canclaimsextended:admin_blockrule_header"));
            foreach (var rule in rules)
            {
                sb.Append('\n').Append(Lang.Get("canclaimsextended:admin_blockrule_line", rule.Key,
                    string.Join(", ", rule.Value ?? new List<string>()), blocks.BehaviorPlotTypeOnly.CountMatches(canclaimsextended.sapi, rule.Key)));
            }
            return TextCommandResult.Success(sb.ToString());
        }

        /// <summary>/cadmin blockrule hand: the block in hand and the keys that would match it.</summary>
        public static TextCommandResult BlockRuleHand(TextCommandCallingArgs args)
        {
            Block block = (args.Caller.Player as IServerPlayer)?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Block;
            if (block == null)
            {
                return TextCommandResult.Error("canclaimsextended:admin_blockrule_no_block_in_hand");
            }
            return SuccessWithParams("canclaimsextended:admin_blockrule_hand", block.Code,
                string.Join(", ", blocks.BehaviorPlotTypeOnly.CandidateKeys(block)));
        }
    }
}
