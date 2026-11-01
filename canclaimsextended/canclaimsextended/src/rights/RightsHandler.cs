using System;
using System.Collections.Generic;
using System.IO;
using canclaimsextended.src.part;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.Server;

namespace canclaimsextended.src.rights
{
    /// <summary>
    /// What a player may do through commands, by role: everyone gets DEFAULT, a mayor also MAYOR,
    /// plus the rights of the city ranks they hold.
    /// The groups live in a json file the host can edit; see readOrCreateRightPerms.
    /// </summary>
    public static class RightsHandler
    {
        static Dictionary<string, HashSet<EnumPlayerPermissions>> PlayerPermissionsByGroups = new();

        private static Dictionary<string, HashSet<EnumPlayerPermissions>> getDefaultRankPermsDict()
        {
            return new Dictionary<string, HashSet<EnumPlayerPermissions>>
            {
                { "DEFAULT", new HashSet<EnumPlayerPermissions>
                    {
                        EnumPlayerPermissions.CITY_HERE,
                        EnumPlayerPermissions.CITY_INFO,
                        EnumPlayerPermissions.PLOT_CLAIM,
                        EnumPlayerPermissions.PLOT_UNCLAIM,
                        EnumPlayerPermissions.PLOT_SET_FS,
                        EnumPlayerPermissions.PLOT_SET_NFS
                    }
                },
                { "MAYOR", new HashSet<EnumPlayerPermissions>
                    {
                        EnumPlayerPermissions.CITY_CLAIM_CHUNK,
                        EnumPlayerPermissions.CITY_UNCLAIM_CHUNK,
                        EnumPlayerPermissions.CITY_CREATE_PLOT,
                        EnumPlayerPermissions.CITY_DELETE_PLOT,
                        EnumPlayerPermissions.CITY_SET_ALL,
                        EnumPlayerPermissions.PLOT_SET_ALL_CITY_PLOTS,
                        EnumPlayerPermissions.CITY_INVITE,
                        EnumPlayerPermissions.CITY_UNINVITE,
                        EnumPlayerPermissions.CITY_KICK,
                        EnumPlayerPermissions.SHOW_INVITES_SENT,
                        EnumPlayerPermissions.CITY_SET_PLOT_ACCESS_PERMISSIONS,
                        EnumPlayerPermissions.CITY_SET_PVP,
                        EnumPlayerPermissions.CITY_SET_FIRE,
                        EnumPlayerPermissions.CITY_SET_BLAST,
                        EnumPlayerPermissions.CITY_SET_MOBSPAWN,
                        EnumPlayerPermissions.CITY_SET_OPEN_STATE,
                        EnumPlayerPermissions.CITY_SET_INV_MSG,
                        EnumPlayerPermissions.CITY_WITHDRAW_MONEY,
                        EnumPlayerPermissions.CITY_CREATE_CITY_RANK,
                        EnumPlayerPermissions.CITY_DELETE_CITY_RANK,
                        EnumPlayerPermissions.CITY_ADD_PERMISSION_TO_RANK,
                        EnumPlayerPermissions.CITY_REMOVE_PERMISSION_FROM_RANK,
                        EnumPlayerPermissions.CITY_SET_RANK,
                        EnumPlayerPermissions.CITY_REMOVE_RANK,
                        EnumPlayerPermissions.CITY_SEE_CITY_RANKS,
                        EnumPlayerPermissions.CITY_SET_GLOBAL_FEE,
                        EnumPlayerPermissions.CITY_SET_SUMMON,
                        EnumPlayerPermissions.CITY_CRIMINAL_ALL,
                        EnumPlayerPermissions.CITY_ADD_CRIMINAL,
                        EnumPlayerPermissions.CITY_REMOVE_CRIMINAL,
                        EnumPlayerPermissions.CITY_PRISON_ALL,
                        EnumPlayerPermissions.CITY_PRISON_ADD_CELL,
                        EnumPlayerPermissions.CITY_PRISON_REMOVE_CELL,
                        EnumPlayerPermissions.CITY_PRISON_LIST,
                        EnumPlayerPermissions.CITY_SET_EMBLEM,
                        EnumPlayerPermissions.CITY_SET_NAME,
                        EnumPlayerPermissions.CITY_SET_PLOTS_COLOR,
                        EnumPlayerPermissions.CITY_SET_DAILY_MSG,
                        EnumPlayerPermissions.CITY_SET_OTHERS_PREFIX,
                        EnumPlayerPermissions.PLOT_SET_FEE,
                        EnumPlayerPermissions.CITY_BUY_OUTPOST,
                        EnumPlayerPermissions.CITY_BUY_EXTRA_CHUNK,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_CREATE,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLAYER,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_KICK_PLAYER,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_ADD_PLOT,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_REMOVE_PLOT,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_LIST,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET_PVP,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FIRE,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET_BLAST,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET_MOBSPAWN,
                        EnumPlayerPermissions.CITY_PLOTSGROUP_SET_FEE
                    }
                }
            };
        }

        public static void reapplyRights(PlayerInfo playerInfo)
        {
            if (canclaimsextended.sapi.World.PlayerByUid(playerInfo.Guid) is not IServerPlayer)
            {
                return;
            }
            playerInfo.PlayerPermissionsHandler.ClearPermissions();
            if (PlayerPermissionsByGroups.TryGetValue("DEFAULT", out var defaultPerms))
            {
                playerInfo.PlayerPermissionsHandler.AddPermissions(defaultPerms);
            }
            City city = playerInfo.City;
            // The mayor himself: these rights are kept, and an admin acting for the city
            // (AdminActing) must not walk away with them.
            if (city != null && playerInfo.Equals(city.getMayor())
                && PlayerPermissionsByGroups.TryGetValue("MAYOR", out var mayorPerms))
            {
                playerInfo.PlayerPermissionsHandler.AddPermissions(mayorPerms);
            }
            // Rights of the city ranks held, limited to what ranks may carry now.
            if (city != null)
            {
                foreach (string title in playerInfo.getCityTitles())
                {
                    if (!city.CustomCityRanks.TryGetValue(title, out var rank)) continue;
                    foreach (var perm in rank.Permissions)
                    {
                        if (canclaimsextended.config.AVAILABLE_CITY_PERMISSIONS.Contains(perm))
                        {
                            playerInfo.PlayerPermissionsHandler.AddPermission(perm);
                        }
                    }
                }
            }
            // Joining, leaving and mayor changes all pass here; the plot markers depend on them.
            canclaimsextended.dataStorage?.NotifyPlayerRightsChanged(playerInfo);
        }

        /// <summary>
        /// Reads the groups from the json file, writing the defaults when there is none. Groups the
        /// file lacks are added from the defaults; groups already there are left as the host set them.
        /// </summary>
        public static void readOrCreateRightPerms()
        {
            string filePath = canclaimsextended.config.FilePath(canclaimsextended.config.PERMS_FILE_NAME);

            // Sets go through the converter first: rights renamed since the file was written are
            // translated, unknown ones dropped, instead of the whole file falling back to defaults.
            var permsConverter = new PermissionSetConverter();
            JsonSerializerSettings settings = new() { Formatting = Formatting.Indented };
            settings.Converters.Add(permsConverter);
            settings.Converters.Add(new StringEnumConverter());

            Dictionary<string, HashSet<EnumPlayerPermissions>> loaded = null;
            bool changed = false;
            if (File.Exists(filePath))
            {
                try
                {
                    loaded = JsonConvert.DeserializeObject<Dictionary<string, HashSet<EnumPlayerPermissions>>>(File.ReadAllText(filePath), settings);
                }
                catch (Exception ex)
                {
                    // Not overwritten: the host's file may only have a typo in it.
                    canclaimsextended.sapi.Logger.Error("[canclaimsextended] {0} could not be read, defaults are used: {1}", filePath, ex.Message);
                    PlayerPermissionsByGroups = getDefaultRankPermsDict();
                    return;
                }
                if (permsConverter.Changed)
                {
                    if (permsConverter.Unknown.Count > 0)
                    {
                        canclaimsextended.sapi.Logger.Warning("[canclaimsextended] {0}: unknown rights dropped: {1}", filePath, string.Join(", ", permsConverter.Unknown));
                    }
                    // The host's version stays next to the rewritten one.
                    try
                    {
                        File.Copy(filePath, filePath + ".bak", true);
                    }
                    catch (Exception ex)
                    {
                        canclaimsextended.sapi.Logger.Warning("[canclaimsextended] Could not back up {0}: {1}", filePath, ex);
                    }
                    canclaimsextended.sapi.Logger.Notification("[canclaimsextended] {0} used old right names; translated and rewritten, the old file is kept as .bak", filePath);
                    changed = true;
                }
            }

            changed |= loaded == null;
            loaded ??= new Dictionary<string, HashSet<EnumPlayerPermissions>>();
            foreach (var group in getDefaultRankPermsDict())
            {
                if (loaded.ContainsKey(group.Key)) continue;
                loaded.Add(group.Key, group.Value);
                changed = true;
            }
            PlayerPermissionsByGroups = loaded;

            if (!changed) return;
            try
            {
                File.WriteAllText(filePath, JsonConvert.SerializeObject(loaded, settings));
            }
            catch (Exception ex)
            {
                canclaimsextended.sapi.Logger.Warning("[canclaimsextended] Could not write {0}: {1}", filePath, ex);
            }
        }
    }
}
