using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;
using static canclaimsextended.src.commands.register.ServerCommands;

namespace canclaimsextended.src.commands.register
{
    /// <summary>/agree, the chat commands and /citizen.</summary>
    static class MiscCommandsRegistrar
    {
        public static void Register(ICoreServerAPI sapi, CommandArgumentParsers parsers, Config cfg)
        {
            sapi.ChatCommands.Create(cfg.COMMAND_AGREE)
                .RequiresPlayer().RequiresPrivilege(Privilege.chat)
                .WithDesc(D("agree"))
                .HandleWith(AgreeCommand.OnCommand);
            RegisterChats(sapi, parsers, cfg);
            RegisterCitizen(sapi, parsers, cfg.COMMAND_CITIZEN);
        }

        static void RegisterChats(ICoreServerAPI sapi, CommandArgumentParsers parsers, Config cfg)
        {
            sapi.ChatCommands.Create(cfg.COMMAND_CITY_CHAT).RequiresPlayer().RequiresPrivilege(Privilege.chat)
                .WithDesc(D("chat-city"))
                .WithArgs(parsers.OptionalAll("text")).HandleWith(ChatCommands.CityChat);
            sapi.ChatCommands.Create(cfg.COMMAND_GENERAL_CHAT).RequiresPlayer().RequiresPrivilege(Privilege.chat)
                .WithDesc(D("chat-general")).HandleWith(ChatCommands.GeneralChat);
        }

        static void RegisterCitizen(ICoreServerAPI sapi, CommandArgumentParsers parsers, string name)
        {
            sapi.ChatCommands.Create(name)
                .RequiresPlayer().RequiresPrivilege(Privilege.chat)
                .BeginSub("info").WithDesc(D("citizen-info")).WithArgs(parsers.OptionalWord("playerName")).HandleWith(CitizenCommand.Info).EndSub()
                .BeginSub("prison")
                    .WithDesc(D("citizen-prison"))
                    .HandleWith(CityCommandPrison.PrisonerInfo)
                    .BeginSub("info").HandleWith(CityCommandPrison.PrisonerInfo).EndSub()
                    .BeginSub("payout").WithDesc(D("citizen-prison-payout")).HandleWith(CityCommandPrison.Payout).EndSub()
                .EndSub()
                .BeginSub("friend")
                    .WithDesc(D("citizen-friend"))
                    .HandleWith(CitizenCommand.FriendList)
                    .BeginSub("list").HandleWith(CitizenCommand.FriendList).EndSub()
                    .BeginSub("add").WithArgs(parsers.Word("playerName")).HandleWith(CitizenCommand.FriendAdd).EndSub()
                    .BeginSub("remove").WithArgs(parsers.Word("playerName")).HandleWith(CitizenCommand.FriendRemove).EndSub()
                .EndSub();
        }
    }
}
