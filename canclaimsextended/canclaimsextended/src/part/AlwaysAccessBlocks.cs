using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Block classes of other mods anyone may use on any plot - mailboxes, market stalls - so trade
    /// mods keep working inside cities. Only use: breaking them still needs the build right.
    /// Entries of ALWAYS_ACCESS_BLOCKS are "modid:ClassName".
    /// </summary>
    public static class AlwaysAccessBlocks
    {
        static readonly HashSet<Type> types = new();

        public static bool Contains(Block block)
        {
            return block != null && types.Contains(block.GetType());
        }

        /// <summary>Looks the classes up in the loaded mods. Mod assemblies are loaded by StartServerSide.</summary>
        public static void Resolve(ICoreAPI api)
        {
            types.Clear();
            foreach (string entry in canclaimsextended.config.ALWAYS_ACCESS_BLOCKS)
            {
                string[] split = entry.Split(':');
                if (split.Length < 2) continue;
                bool found = false;
                foreach (var mod in api.ModLoader.Mods)
                {
                    // The mod id first; the file name carries versions and any casing.
                    bool isTargetMod = string.Equals(mod.Info?.ModID, split[0], StringComparison.OrdinalIgnoreCase)
                        || (mod.FileName?.StartsWith(split[0], StringComparison.OrdinalIgnoreCase) ?? false);
                    if (!isTargetMod) continue;
                    if (mod is not Vintagestory.Common.ModContainer container || container.Assembly == null) continue;

                    foreach (Type type in container.Assembly.GetTypes())
                    {
                        if (type.Name == split[1])
                        {
                            types.Add(type);
                            found = true;
                        }
                    }
                }
                if (!found)
                {
                    api.Logger.Notification("[canclaimsextended] ALWAYS_ACCESS_BLOCKS entry '{0}' found no class (mod not installed?)", entry);
                }
            }
        }

        public static void Clear()
        {
            types.Clear();
        }
    }
}
