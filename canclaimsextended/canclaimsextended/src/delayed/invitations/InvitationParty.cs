using System;
using System.Collections.Generic;
using canclaimsextended.src.part;

namespace canclaimsextended.src.delayed.invitations
{
    /// <summary>
    /// How a side of an invitation is stored: a type ("city", "player", "plotsgroup", ...) and a guid.
    /// </summary>
    public static class InvitationParty
    {
        // City and player are known here; a feature with its own parties (plots groups) adds its resolver.
        static readonly Dictionary<string, Func<string, object>> resolvers = new()
        {
            { "city", guid => canclaimsextended.dataStorage.GetCityByGUID(guid, out City city) ? city : null },
            { "player", guid => canclaimsextended.dataStorage.GetPlayerByUid(guid, out PlayerInfo player) ? player : null }
        };

        public static void RegisterResolver(string type, Func<string, object> resolve)
        {
            resolvers[type] = resolve;
        }

        public static bool CanResolve(string type)
        {
            return resolvers.ContainsKey(type);
        }

        public static string TypeOf(object party)
        {
            return party switch
            {
                City => "city",
                PlayerInfo => "player",
                Part p => p.GetType().Name.ToLowerInvariant(),
                _ => ""
            };
        }

        public static string GuidOf(object party)
        {
            return (party as Part)?.Guid ?? "";
        }

        /// <summary>The party, or null when its type has no resolver or it no longer exists.</summary>
        public static object Resolve(string type, string guid)
        {
            return resolvers.TryGetValue(type, out var resolve) ? resolve(guid) : null;
        }
    }
}
