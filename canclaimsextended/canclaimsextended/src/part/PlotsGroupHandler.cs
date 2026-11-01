using System;
using System.Collections.Generic;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.delayed.invitations;
using canclaimsextended.src.messages;
using canclaimsextended.src.part.structure;
using Vintagestory.API.Config;

namespace canclaimsextended.src.part
{
    /// <summary>
    /// Everything that changes a plots group: kept here so the commands, the invitations, the
    /// daily fees and the city cleanup all leave the plots, the members and the database in step.
    /// </summary>
    public static class PlotsGroupHandler
    {
        static database.DatabaseHandler Db => canclaimsextended.Db;

        public static PlotsGroup FindByGuid(string guid)
        {
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList())
            {
                foreach (PlotsGroup group in city.PlotsGroups)
                {
                    if (group.Guid == guid) return group;
                }
            }
            return null;
        }

        public static bool TryFind(City city, string rawName, out PlotsGroup group)
        {
            string name = Filter.FilterName(rawName ?? "");
            group = city.PlotsGroups.FirstOrDefault(g => string.Equals(g.GetPartName(), name, StringComparison.OrdinalIgnoreCase));
            return group != null;
        }

        /// <summary>A new group with the city's flags and citizen rights to start from.</summary>
        public static PlotsGroup Create(City city, string name)
        {
            var group = new PlotsGroup(name, Guid.NewGuid().ToString()) { City = city };
            group.Perms.setPerm(city.getPermsHandler());
            RefreshLand(group);
            city.PlotsGroups.Add(group);
            group.saveToDatabase();
            return group;
        }

        /// <summary>
        /// Brings the land of the group's chunks in step: the city's rights, which members trade for
        /// the group's, and the group's flags. Called on creation, after loading, and whenever the
        /// city's rights or the group's flags change.
        /// </summary>
        public static void RefreshLand(PlotsGroup group)
        {
            if (group.City == null) return;
            var landPerms = new perms.PermsHandler();
            landPerms.setPerm(group.City.getPermsHandler());
            CopyFlags(group, landPerms);
            if (group.Land == null)
            {
                group.Land = new Plot(group.City, landPerms, "land_" + group.Guid, group);
            }
            else
            {
                group.Land.getPermsHandler().setPerm(landPerms);
            }
        }

        /// <summary>The city's rights changed: the land of each of its groups follows.</summary>
        public static void RefreshLands(City city)
        {
            foreach (PlotsGroup group in city.PlotsGroups)
            {
                RefreshLand(group);
            }
        }

        /// <summary>Plots and chunks go back to the city rights, invitations are dropped.</summary>
        public static void Delete(PlotsGroup group)
        {
            foreach (Plot plot in group.Plots.ToList())
            {
                plot.Group = null;
                Db.savePlotExtra(plot);
                canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            }
            foreach (CityChunk chunk in group.Chunks.ToList())
            {
                RemoveChunk(chunk);
            }
            InvitationHandler.deleteAllInvitationsForSender(group);
            group.City?.PlotsGroups.Remove(group);
            Db.deletePlotsGroup(group);
            ResetMembersCache(group);
            group.Members.Clear();
        }

        /// <summary>All groups of a city about to be demolished.</summary>
        public static void DeleteAll(City city)
        {
            foreach (PlotsGroup group in city.PlotsGroups.ToList())
            {
                Delete(group);
            }
        }

        public static void AddMember(PlotsGroup group, PlayerInfo player)
        {
            if (!group.Members.Add(player)) return;
            // Joining at the announced fee is agreeing to it.
            if (group.HasPendingFee) group.PendingFeeAccepted.Add(player.Guid);
            group.saveToDatabase();
            player.PlayerCache.setPlot(null);
            canclaimsextended.dataStorage.NotifyPlayerRightsChanged(player);
        }

        public static void RemoveMember(PlotsGroup group, PlayerInfo player)
        {
            if (!group.Members.Remove(player)) return;
            group.PendingFeeAccepted.Remove(player.Guid);
            group.saveToDatabase();
            player.PlayerCache.setPlot(null);
            canclaimsextended.dataStorage.NotifyPlayerRightsChanged(player);
        }

        /// <summary>Called when the player leaves the city: groups are for its citizens only.</summary>
        public static void RemoveFromCityGroups(PlayerInfo player, City city)
        {
            foreach (PlotsGroup group in city.PlotsGroups)
            {
                RemoveMember(group, player);
                InvitationHandler.removeInvitationIfExists(group, player);
            }
        }

        /// <summary>The plot takes the group's flags; its members get the group rights there.</summary>
        public static void AddPlot(PlotsGroup group, Plot plot)
        {
            plot.Group = group;
            CopyFlags(group, plot);
            plot.saveToDatabase();
            Db.savePlotExtra(plot);
            canclaimsextended.dataStorage.ClearCacheForPlayersInPlot(plot);
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
        }

        public static void RemovePlot(Plot plot)
        {
            plot.Group = null;
            Db.savePlotExtra(plot);
            canclaimsextended.dataStorage.ClearCacheForPlayersInPlot(plot);
            canclaimsextended.dataStorage.NotifyPlotChanged(plot);
        }

        /// <summary>The land of the chunk takes the group's flags; its members get the group rights there.</summary>
        public static void AddChunk(PlotsGroup group, CityChunk chunk)
        {
            chunk.Group = group;
            Db.saveChunk(chunk.City, chunk);
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyChunkChanged(chunk);
        }

        public static void RemoveChunk(CityChunk chunk)
        {
            chunk.Group = null;
            Db.saveChunk(chunk.City, chunk);
            canclaimsextended.dataStorage.ResetCacheForAll();
            canclaimsextended.dataStorage.NotifyChunkChanged(chunk);
        }

        /// <summary>Sets a flag on the group, every plot of it and the land of its chunks. False for an unknown flag.</summary>
        public static bool SetFlag(PlotsGroup group, string flag, bool value)
        {
            if (!group.Perms.trySetFlag(flag, value)) return false;
            group.saveToDatabase();
            foreach (Plot plot in group.Plots)
            {
                CopyFlags(group, plot);
                plot.saveToDatabase();
                canclaimsextended.dataStorage.NotifyPlotChanged(plot);
            }
            RefreshLand(group);
            foreach (CityChunk chunk in group.Chunks)
            {
                canclaimsextended.dataStorage.NotifyChunkChanged(chunk);
            }
            canclaimsextended.dataStorage.ResetCacheForAll();
            return true;
        }

        public static void SetPerm(PlotsGroup group, perms.type.PermType type, bool value)
        {
            group.Perms.setPerm(perms.PermGroup.CITIZEN, type, value);
            group.saveToDatabase();
            ResetMembersCache(group);
        }

        internal static void CopyFlags(PlotsGroup group, Plot plot)
        {
            CopyFlags(group, plot.getPermsHandler());
        }

        static void CopyFlags(PlotsGroup group, perms.PermsHandler to)
        {
            to.pvpFlag = group.Perms.pvpFlag;
            to.fireFlag = group.Perms.fireFlag;
            to.blastFlag = group.Perms.blastFlag;
            to.noMobSpawnFlag = group.Perms.noMobSpawnFlag;
        }

        static void ResetMembersCache(PlotsGroup group)
        {
            foreach (PlayerInfo member in group.Members)
            {
                member.PlayerCache.setPlot(null);
                canclaimsextended.dataStorage.NotifyPlayerRightsChanged(member);
            }
        }

        /// <summary>
        /// Lower or equal applies at once. A raise is announced and charged only after
        /// PLOTSGROUP_FEE_RAISE_DELAY_HOURS; announcing a smaller raise over a pending one keeps its
        /// deadline and the members who accepted. Returns true when the fee changed right away.
        /// </summary>
        public static bool SetFee(PlotsGroup group, double fee)
        {
            if (fee <= group.Fee)
            {
                group.Fee = fee;
                group.ClearPendingFee();
                group.saveToDatabase();
                MessageToMembers(group, Lang.Get("canclaimsextended:plotsgroup_fee_lowered", group.getPartNameReplaceUnder(), fee));
                return true;
            }
            bool keep = group.HasPendingFee && fee <= group.PendingFee;
            if (!keep)
            {
                group.PendingFeeAccepted.Clear();
                group.PendingFeeAt = TimeFunctions.GetEpochSeconds() + canclaimsextended.config.PLOTSGROUP_FEE_RAISE_DELAY_HOURS * 3600L;
            }
            group.PendingFee = fee;
            group.saveToDatabase();
            MessageToMembers(group, Lang.Get("canclaimsextended:plotsgroup_fee_raise_announced", group.getPartNameReplaceUnder(), group.Fee, fee,
                canclaimsextended.config.COMMAND_CITY, group.GetPartName(), TimeFunctions.GetDateFromEpochSeconds(group.PendingFeeAt)));
            return false;
        }

        public static bool AcceptFee(PlotsGroup group, PlayerInfo player)
        {
            if (!group.HasPendingFee || !group.Members.Contains(player)) return false;
            group.PendingFeeAccepted.Add(player.Guid);
            group.saveToDatabase();
            return true;
        }

        /// <summary>Raises whose time has come: who did not accept leaves the group, the mayor never has to.</summary>
        public static void SettleDueRaises()
        {
            long now = TimeFunctions.GetEpochSeconds();
            foreach (City city in canclaimsextended.dataStorage.GetCitiesList())
            {
                foreach (PlotsGroup group in city.PlotsGroups)
                {
                    if (!group.HasPendingFee || group.PendingFeeAt > now) continue;
                    foreach (PlayerInfo member in group.Members.ToList())
                    {
                        if (city.isMayor(member) || group.PendingFeeAccepted.Contains(member.Guid)) continue;
                        RemoveMember(group, member);
                        MessageHandler.sendMsgToPlayerInfo(member, Lang.Get("canclaimsextended:plotsgroup_left_fee_not_accepted",
                            group.getPartNameReplaceUnder(), group.PendingFee));
                    }
                    group.Fee = group.PendingFee;
                    group.ClearPendingFee();
                    group.saveToDatabase();
                }
            }
        }

        /// <summary>
        /// Each member but the mayor pays the fee into the city treasury; who cannot pay leaves the
        /// group. Only for groups that hold a plot or a chunk, and only with a real economy.
        /// </summary>
        public static void CollectFees(City city)
        {
            var money = canclaimsextended.economyProvider;
            if (!money.SupportsPlayerWallet) return;
            foreach (PlotsGroup group in city.PlotsGroups)
            {
                if (group.Fee <= 0 || !group.HasLand) continue;
                decimal fee = (decimal)group.Fee;
                foreach (PlayerInfo member in group.Members.ToList())
                {
                    if (city.isMayor(member)) continue;
                    if (money.GetBalance(member.Guid) >= fee
                        && money.Transfer(member.Guid, city.MoneyAccountName, fee) == economy.MoneyOperationResult.Success)
                    {
                        continue;
                    }
                    RemoveMember(group, member);
                    MessageHandler.sendMsgToPlayerInfo(member, Lang.Get("canclaimsextended:plotsgroup_fee_kicked", group.getPartNameReplaceUnder(), fee));
                }
            }
        }

        static void MessageToMembers(PlotsGroup group, string msg)
        {
            foreach (PlayerInfo member in group.Members)
            {
                MessageHandler.sendMsgToPlayerInfo(member, msg);
            }
        }

        /// <summary>Builds a group invitation; also brings one back after a restart.</summary>
        public static Invitation MakeGroupInvitation(interfaces.ISender sender, interfaces.IReceiver receiver, long expires)
        {
            if (sender is not PlotsGroup group || receiver is not PlayerInfo target) return null;
            return new Invitation(group, target, expires,
                () =>
                {
                    // The player may have left the city, or the group may be gone, while it waited.
                    if (group.City == null || !group.City.PlotsGroups.Contains(group) || !group.City.isCitizen(target))
                    {
                        MessageHandler.sendMsgToPlayerInfo(target, Lang.Get("canclaimsextended:plotsgroup_invitation_outdated"));
                        return;
                    }
                    AddMember(group, target);
                    MessageToMembers(group, Lang.Get("canclaimsextended:plotsgroup_player_joined", target.getPartNameReplaceUnder(), group.getPartNameReplaceUnder()));
                },
                () =>
                {
                    if (group.City?.getMayor() != null)
                    {
                        MessageHandler.sendMsgToPlayerInfo(group.City.getMayor(),
                            Lang.Get("canclaimsextended:plotsgroup_invitation_denied", target.GetPartName(), group.getPartNameReplaceUnder()));
                    }
                });
        }
    }
}
