using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace canclaimsextended.src.rights
{
    /// <summary>
    /// Reads a set of rights written by any earlier build: names that were renamed are translated,
    /// names this build does not know are dropped instead of failing the whole file. Writes names.
    /// After a read, <see cref="Changed"/> tells whether what was read differs from what was stored.
    /// </summary>
    public class PermissionSetConverter : JsonConverter<HashSet<EnumPlayerPermissions>>
    {
        /// <summary>Old name -> what it stands for now. A right split in two keeps both halves.</summary>
        static readonly Dictionary<string, EnumPlayerPermissions[]> Renamed = new()
        {
            { "CITY_CLAIM_PLOT", new[] { EnumPlayerPermissions.CITY_CLAIM_CHUNK, EnumPlayerPermissions.CITY_CREATE_PLOT } },
            { "CITY_UNCLAIM_PLOT", new[] { EnumPlayerPermissions.CITY_UNCLAIM_CHUNK, EnumPlayerPermissions.CITY_DELETE_PLOT } },
            { "CITY_BUY_EXTRA_PLOT", new[] { EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK } },
        };

        /// <summary>Set by a read that translated or dropped something; the caller resets it.</summary>
        public bool Changed { get; set; }
        /// <summary>Names dropped since the last reset, for the log.</summary>
        public List<string> Unknown { get; } = new();

        public void Reset()
        {
            Changed = false;
            Unknown.Clear();
        }

        public override HashSet<EnumPlayerPermissions> ReadJson(JsonReader reader, Type objectType,
            HashSet<EnumPlayerPermissions> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var result = new HashSet<EnumPlayerPermissions>();
            if (reader.TokenType == JsonToken.Null) return result;
            foreach (JToken token in JArray.Load(reader))
            {
                if (token.Type == JTokenType.Integer)
                {
                    // Numbers are kept as they are: renamed rights kept their values.
                    int value = token.Value<int>();
                    if (Enum.IsDefined(typeof(EnumPlayerPermissions), value)) result.Add((EnumPlayerPermissions)value);
                    else Drop(value.ToString());
                    continue;
                }
                string name = token.Value<string>() ?? "";
                if (Enum.TryParse(name, false, out EnumPlayerPermissions known) && Enum.IsDefined(typeof(EnumPlayerPermissions), known))
                {
                    result.Add(known);
                }
                else if (Renamed.TryGetValue(name, out EnumPlayerPermissions[] now))
                {
                    result.UnionWith(now);
                    Changed = true;
                }
                else
                {
                    Drop(name);
                }
            }
            return result;
        }

        void Drop(string name)
        {
            Unknown.Add(name);
            Changed = true;
        }

        public override void WriteJson(JsonWriter writer, HashSet<EnumPlayerPermissions> value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            foreach (EnumPlayerPermissions perm in value)
            {
                writer.WriteValue(perm.ToString());
            }
            writer.WriteEndArray();
        }
    }
}
