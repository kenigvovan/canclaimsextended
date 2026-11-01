using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using canclaimsextended.src.part;
using Newtonsoft.Json;

namespace canclaimsextended.src.auxiliary
{
    public static class Settings
    {
        // Citizens count -> what a city of that size may hold.
        public static SortedDictionary<int, CityLevelInfo> cityLevelsDict = new();

        const string CITY_LEVELS_FILE = "canclaimsextended_city_levels.json";

        /// <summary>Reads the levels file anew: at server start and on /cadmin reload.</summary>
        public static void Load()
        {
            Clear();
            LoadCityLevels();
        }
        public static void Clear()
        {
            cityLevelsDict = new SortedDictionary<int, CityLevelInfo>();
        }
        private static void LoadCityLevels()
        {
            string filePath = canclaimsextended.config.FilePath(CITY_LEVELS_FILE);
            if (File.Exists(filePath))
            {
                try
                {
                    var loaded = JsonConvert.DeserializeObject<SortedDictionary<int, CityLevelInfo>>(File.ReadAllText(filePath));
                    // A level written as null would break every lookup that lands on it.
                    if (loaded != null && loaded.Count > 0 && loaded.Values.All(level => level != null))
                    {
                        cityLevelsDict = loaded;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Not overwritten: the host's file may only have a typo in it.
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] {0} could not be read, defaults are used: {1}", filePath, ex.Message);
                    FillDefaultCityLevels();
                    return;
                }
            }
            FillDefaultCityLevels();
            File.WriteAllText(filePath, JsonConvert.SerializeObject(cityLevelsDict, Formatting.Indented));
        }
        private static void FillDefaultCityLevels()
        {
            cityLevelsDict.Clear();
            cityLevelsDict.Add(1, new CityLevelInfo(4, 32768, 0, 2, 0));
            cityLevelsDict.Add(2, new CityLevelInfo(8, 65536, 0, 4, 1));
            cityLevelsDict.Add(3, new CityLevelInfo(12, 131072, 0, 6, 1));
            // The first summon point comes with four citizens: a small town can already gather.
            cityLevelsDict.Add(4, new CityLevelInfo(16, 196608, 1, 9, 2));
            cityLevelsDict.Add(8, new CityLevelInfo(24, 393216, 1, 16, 4));
            cityLevelsDict.Add(16, new CityLevelInfo(32, 786432, 1, 30, 6));
            cityLevelsDict.Add(30, new CityLevelInfo(48, 1572864, 2, 50, 10));
        }
        public static void SaveCityLevels()
        {
            File.WriteAllText(canclaimsextended.config.FilePath(CITY_LEVELS_FILE), JsonConvert.SerializeObject(cityLevelsDict, Formatting.Indented));
        }

        public const int MaxLevelCitizens = 10000;
        /// <summary>Fields of a level as /cadmin level set names them.</summary>
        // "extra" is the extra chunks: the admin page sends it by that name.
        public static readonly string[] LevelFields = { "citizens", "plots", "volume", "summon", "extra", "chunks" };

        /// <summary>A new level starts as a copy of the one a city of that size has now.</summary>
        public static bool TryAddLevel(int citizens, out string errorKey)
        {
            if (citizens < 1 || citizens > MaxLevelCitizens) { errorKey = "canclaimsextended:admin_level_bad_citizens"; return false; }
            if (cityLevelsDict.ContainsKey(citizens)) { errorKey = "canclaimsextended:admin_level_exists"; return false; }
            CityLevelInfo from = GetCityLevelInfo(citizens);
            cityLevelsDict[citizens] = new CityLevelInfo(from.AmountOfPlots, from.MaxVolume, from.SummonPoints, from.MaxChunks, from.MaxExtraChunks);
            SaveCityLevels();
            errorKey = null;
            return true;
        }

        public static bool TryRemoveLevel(int citizens, out string errorKey)
        {
            if (!cityLevelsDict.ContainsKey(citizens)) { errorKey = "canclaimsextended:admin_level_missing"; return false; }
            if (cityLevelsDict.Count == 1) { errorKey = "canclaimsextended:admin_level_last"; return false; }
            cityLevelsDict.Remove(citizens);
            SaveCityLevels();
            errorKey = null;
            return true;
        }

        /// <summary>Sets one field of a level; "citizens" moves the level to another threshold.</summary>
        public static bool TrySetLevelField(int citizens, string field, long value, out string errorKey, out object[] errorArgs)
        {
            errorArgs = System.Array.Empty<object>();
            if (!cityLevelsDict.TryGetValue(citizens, out CityLevelInfo level))
            {
                errorKey = "canclaimsextended:admin_level_missing";
                return false;
            }
            long min, max;
            switch (field)
            {
                case "citizens": min = 1; max = MaxLevelCitizens; break;
                case "plots": min = 1; max = 100000; break;
                case "volume": min = 1; max = 1000000000000; break;
                case "summon": min = 0; max = 1000; break;
                case "extra": min = 0; max = 100000; break;
                case "chunks": min = 1; max = 100000; break;
                default:
                    errorKey = "canclaimsextended:admin_level_bad_field";
                    errorArgs = new object[] { field, string.Join(", ", LevelFields) };
                    return false;
            }
            if (value < min || value > max)
            {
                errorKey = "canclaimsextended:admin_cfg_out_of_range";
                errorArgs = new object[] { value, min, max };
                return false;
            }
            switch (field)
            {
                case "citizens":
                    if (value == citizens) break;
                    if (cityLevelsDict.ContainsKey((int)value))
                    {
                        errorKey = "canclaimsextended:admin_level_exists";
                        errorArgs = new object[] { value };
                        return false;
                    }
                    cityLevelsDict.Remove(citizens);
                    cityLevelsDict[(int)value] = level;
                    break;
                case "plots": level.AmountOfPlots = (int)value; break;
                case "volume": level.MaxVolume = value; break;
                case "summon": level.SummonPoints = (int)value; break;
                case "extra": level.MaxExtraChunks = (int)value; break;
                case "chunks": level.MaxChunks = (int)value; break;
            }
            SaveCityLevels();
            errorKey = null;
            return true;
        }

        public static CityLevelInfo GetCityLevelInfo(int citizensCount)
        {
            foreach (int level in cityLevelsDict.Keys.Reverse())
            {
                if (citizensCount >= level)
                {
                    return cityLevelsDict[level];
                }
            }
            // A custom file may start above 1.
            return cityLevelsDict.Values.First();
        }
        public static int GetMaxNumberOfPlotForCity(City city)
        {
            return city.getBonusPlots() + GetCityLevelInfo(city.getCityCitizens().Count).AmountOfPlots;
        }
        public static int GetMaxChunksForCity(City city)
        {
            return city.getBonusChunks() + GetCityLevelInfo(city.getCityCitizens().Count).MaxChunks;
        }
        public static int GetMaxExtraChunksForCity(City city)
        {
            return GetCityLevelInfo(city.getCityCitizens().Count).MaxExtraChunks;
        }
        public static long GetMaxVolumeForCity(City city)
        {
            return GetCityLevelInfo(city.getCityCitizens().Count).MaxVolume;
        }
    }
}
