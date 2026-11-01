using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using canclaimsextended.src.auxiliary;
using canclaimsextended.src.economy;
using canclaimsextended.src.part;
using canclaimsextended.src.part.structure;
using canclaimsextended.src.part.structure.plots;
using canclaimsextended.src.perms;
using canclaimsextended.src.rights;
using canclaimsextended.src.rules;
using Vintagestory.API.Server;

namespace canclaimsextended.src.network
{
    /// <summary>
    /// Answers a GuiRequestPacket with everything the player window shows for that player. The
    /// parts of the window are filled in GuiStateServer.City, .Plot, .Land and .Admin.
    /// </summary>
    public static partial class GuiStateServer
    {
        static IServerNetworkChannel channel;

        public static void Init(IServerNetworkChannel serverChannel)
        {
            channel = serverChannel;
            channel.SetMessageHandler<GuiRequestPacket>(OnRequest);
        }

        static void OnRequest(IServerPlayer player, GuiRequestPacket packet)
        {
            if (canclaimsextended.dataStorage == null || player.Entity == null) return;
            if (!canclaimsextended.dataStorage.GetPlayerByUid(player.PlayerUID, out PlayerInfo playerInfo)) return;
            // An admin acting for a city sees the window as its mayor; the buttons then go through
            // /cadmin as. Anyone else, or a city that is gone, gets their own window and the client
            // drops the choice (ActingCity comes back empty).
            if (!string.IsNullOrEmpty(packet?.ActingCity) && AdminRole.IsAdmin(player)
                && ManageRules.TryFindCity(packet.ActingCity, out City acting))
            {
                GuiStatePacket state = AdminActing.Run(playerInfo, acting, () => Build(player, playerInfo));
                state.ActingCity = acting.GetPartName();
                channel.SendPacket(state, player);
                return;
            }
            channel.SendPacket(Build(player, playerInfo), player);
        }

        public static GuiStatePacket Build(IServerPlayer player, PlayerInfo playerInfo)
        {
            Config cfg = canclaimsextended.config;
            var economy = canclaimsextended.economyProvider;
            var state = new GuiStatePacket
            {
                CommandCity = cfg.COMMAND_CITY,
                CommandPlot = cfg.COMMAND_PLOT,
                CommandCitizen = cfg.COMMAND_CITIZEN,
                CommandAgree = cfg.COMMAND_AGREE,
                PlayerName = playerInfo.GetPartName(),
                Permissions = playerInfo.PlayerPermissionsHandler.GetPermissions().Select(p => p.ToString()).ToList(),
                Friends = playerInfo.Friends.Select(f => f.GetPartName()).ToList(),
                // City invitations only; plots group invitations have a list of their own.
                InvitationsReceived = playerInfo.getReceivedInvitations().Where(i => i.getSender() is City).Select(i => i.getSender().getNameSender()).ToList(),
                HasEconomy = economy.SupportsPlayerWallet
            };
            // An admin acting as mayor (AdminActing) passes every permission check of the commands,
            // so the window shows every button the commands would take.
            if (AdminActing.IsActor(playerInfo))
            {
                state.Permissions = System.Enum.GetNames(typeof(EnumPlayerPermissions)).ToList();
            }
            if (state.HasEconomy)
            {
                state.Balance = Money(economy.GetBalance(playerInfo.Guid));
            }
            state.CommandCadmin = cfg.COMMAND_CADMIN;

            City city = AdminActing.CityOf(playerInfo);
            if (city != null)
            {
                FillCity(state, city, playerInfo, cfg);
                FillCityPlots(state, city, player, playerInfo);
            }
            // The plot pages are about real plots, not the land of a city.
            if (PlotLookup.TryGetSubPlotUnderPlayer(player, out Plot plot))
            {
                FillPlot(state, plot, playerInfo);
                FillPlotParts(state, plot, player, playerInfo);
            }
            state.CanClaimForCity = city != null && ManageRules.IsMayorOr(playerInfo, city, EnumPlayerPermissions.CITY_CREATE_PLOT);
            FillSelection(state, player, playerInfo);
            FillChunk(state, player, playerInfo, cfg);
            FillPlotsGroups(state, city, plot, playerInfo);
            FillPrisoner(state, playerInfo);
            FillAdmin(state, player, cfg);
            FillPrices(state);
            return state;
        }

        static void FillPrices(GuiStatePacket state)
        {
            foreach (var entry in PriceSettings.All)
            {
                state.PriceSettings.Add(new GuiPriceSettingEntry
                {
                    Key = entry.Key,
                    Group = entry.Group,
                    Value = PriceSettings.Format(entry.Get()),
                    Min = PriceSettings.Format(entry.Min),
                    Max = PriceSettings.Format(entry.Max),
                    Integer = entry.Integer
                });
            }
            state.EnabledPlotTypes = PlotTypes.EnabledCodes.ToList();
        }

        static void FillPrisoner(GuiStatePacket state, PlayerInfo playerInfo)
        {
            if (!playerInfo.isPrisoned()) return;
            state.IsPrisoned = true;
            long left = System.Math.Max(0, playerInfo.PrisonReleaseAt - TimeFunctions.GetEpochSeconds());
            state.PrisonHoursLeft = (left + 3599) / 3600;
            state.PrisonRansom = Money((decimal)PrisonHandler.RansomPrice(playerInfo));
        }

        /// <summary>Group (stranger, citizen, friend) x type (use, build, attack), row by row.</summary>
        static bool[] PermsRow(PermsHandler perms)
        {
            var row = new List<bool>(9);
            foreach (bool[] group in new[] { perms.StrangerPerms, perms.CitizenPerms, perms.ComradePerms })
            {
                for (int i = 0; i < 3; i++)
                {
                    row.Add(i < group.Length && group[i]);
                }
            }
            return row.ToArray();
        }

        // Invariant: the client parses these back, whatever the server's own culture.
        static string Money(decimal value) => MoneyFormat.Format(value);

        /// <summary>The arguments of a failed check as strings; the client translates the key with them.</summary>
        static List<string> ArgStrings(ClaimCheck check)
        {
            return check.Args.Select(a => System.Convert.ToString(a, CultureInfo.InvariantCulture)).ToList();
        }
    }
}
