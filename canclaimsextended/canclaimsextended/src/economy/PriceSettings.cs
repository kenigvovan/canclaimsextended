using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using canclaimsextended.src.part.structure.plots;

namespace canclaimsextended.src.economy
{
    /// <summary>
    /// The price settings an admin may change in game (/cadmin setcfg and the admin prices tab):
    /// each key with its group, range and the config field behind it. The caller stores the config.
    /// </summary>
    public static class PriceSettings
    {
        public const string GroupLand = "land";
        public const string GroupUpkeep = "upkeep";
        public const string GroupTypes = "types";
        public const string GroupOther = "other";

        /// <summary>Per plot type keys: TypeSetCostPrefix + code and TypeUpkeepPrefix + code.</summary>
        public const string TypeSetCostPrefix = "type_set_cost_";
        public const string TypeUpkeepPrefix = "type_upkeep_";

        private const double MaxPrice = 1000000;

        public sealed class Entry
        {
            public string Key;
            public string Group;
            public double Min;
            public double Max;
            public bool Integer;
            public Func<double> Get;
            public Action<double> Set;
        }

        private static Config C => canclaimsextended.config;

        private static readonly List<Entry> entries = new List<Entry>
        {
            Int("blocks_per_unit", GroupLand, 1, MaxPrice, () => C.BLOCKS_PER_COST_UNIT, v => C.BLOCKS_PER_COST_UNIT = (int)v),
            Num("claim_price_per_unit", GroupLand, () => C.PLOT_CLAIM_PRICE_PER_UNIT, v => C.PLOT_CLAIM_PRICE_PER_UNIT = v),
            Num("new_city_cost", GroupLand, () => C.NEW_CITY_COST, v => C.NEW_CITY_COST = v),
            Num("chunk_claim_price", GroupLand, () => C.CHUNK_CLAIM_PRICE, v => C.CHUNK_CLAIM_PRICE = v),
            Num("outpost_chunk_cost", GroupLand, () => C.OUTPOST_CHUNK_COST, v => C.OUTPOST_CHUNK_COST = v),
            Num("extra_chunk_cost", GroupLand, () => C.EXTRA_CHUNK_COST, v => C.EXTRA_CHUNK_COST = v),
            new Entry { Key = "refund_percent", Group = GroupLand, Min = 0, Max = 100,
                        Get = () => C.PLOT_UNCLAIM_REFUND_PERCENT, Set = v => C.PLOT_UNCLAIM_REFUND_PERCENT = v },
            Int("refund_min_age_seconds", GroupLand, 0, 31536000, () => C.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS, v => C.PLOT_UNCLAIM_REFUND_MIN_AGE_SECONDS = (long)v),

            Num("city_base_care", GroupUpkeep, () => C.CITY_BASE_CARE, v => C.CITY_BASE_CARE = v),
            Num("chunk_upkeep", GroupUpkeep, () => C.CHUNK_DAILY_UPKEEP, v => C.CHUNK_DAILY_UPKEEP = v),
            Num("no_pvp_flag_cost", GroupUpkeep, () => C.PLOT_NO_PVP_FLAG_COST, v => C.PLOT_NO_PVP_FLAG_COST = v),
            Num("no_mobspawn_flag_cost", GroupUpkeep, () => C.PLOT_NO_MOBSPAWN_FLAG_COST, v => C.PLOT_NO_MOBSPAWN_FLAG_COST = v),
            Num("max_debt", GroupUpkeep, () => C.CITY_MAX_DEBT, v => C.CITY_MAX_DEBT = v),

            Num("city_name_change_cost", GroupOther, () => C.CITY_NAME_CHANGE_COST, v => C.CITY_NAME_CHANGE_COST = v),
            Num("summon_payment", GroupOther, () => C.SUMMON_PAYMENT, v => C.SUMMON_PAYMENT = v),
            Num("max_city_fee", GroupOther, () => C.MAX_CITY_FEE, v => C.MAX_CITY_FEE = v),
            Num("max_plot_fee", GroupOther, () => C.MAX_PLOT_FEE, v => C.MAX_PLOT_FEE = v),
            Num("max_plotsgroup_fee", GroupOther, () => C.MAX_PLOTSGROUP_FEE, v => C.MAX_PLOTSGROUP_FEE = v),
        };

        static PriceSettings()
        {
            foreach (PlotInfo info in PlotTypes.All)
            {
                string code = info.Code;
                entries.Add(Num(TypeSetCostPrefix + code, GroupTypes, info.SetCost, v => C.PLOT_TYPE_SET_COSTS[code] = v));
                entries.Add(Num(TypeUpkeepPrefix + code, GroupTypes, info.UnitUpkeep, v => C.PLOT_TYPE_UNIT_COSTS[code] = v));
            }
        }

        private static Entry Num(string key, string group, Func<double> get, Action<double> set)
        {
            return new Entry { Key = key, Group = group, Min = 0, Max = MaxPrice, Get = get, Set = set };
        }

        private static Entry Int(string key, string group, double min, double max, Func<double> get, Action<double> set)
        {
            return new Entry { Key = key, Group = group, Min = min, Max = max, Integer = true, Get = get, Set = set };
        }

        public static IReadOnlyList<Entry> All => entries;

        public static bool TryFind(string key, out Entry entry)
        {
            entry = entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            return entry != null;
        }

        public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses and assigns the value. On failure gives a lang key (with the domain) and its arguments.
        /// </summary>
        public static bool TrySet(string key, string raw, out string errorKey, out object[] errorArgs)
        {
            errorArgs = Array.Empty<object>();
            if (!TryFind(key, out Entry entry))
            {
                errorKey = "canclaimsextended:admin_cfg_unknown_key";
                errorArgs = new object[] { key };
                return false;
            }
            // A comma is accepted as the decimal mark too: that is what many players type.
            string text = (raw ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                errorKey = "canclaimsextended:admin_cfg_not_number";
                errorArgs = new object[] { raw };
                return false;
            }
            if (entry.Integer && value != Math.Floor(value))
            {
                errorKey = "canclaimsextended:admin_cfg_not_integer";
                errorArgs = new object[] { raw };
                return false;
            }
            if (value < entry.Min || value > entry.Max)
            {
                errorKey = "canclaimsextended:admin_cfg_out_of_range";
                errorArgs = new object[] { Format(value), Format(entry.Min), Format(entry.Max) };
                return false;
            }
            entry.Set(value);
            errorKey = null;
            return true;
        }
    }
}
