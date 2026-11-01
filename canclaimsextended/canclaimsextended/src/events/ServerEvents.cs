using System;
using canclaimsextended.src.auxiliary;
using Vintagestory.API.Server;

namespace canclaimsextended.src.events
{
    public static class ServerEvents
    {
        public static void AddEvents(ICoreServerAPI sapi)
        {
            sapi.Event.PlayerJoin += OnPlayerJoin.Event_OnPlayerJoin;
            sapi.Event.PlayerDisconnect += OnPlayerDisconnect.Event_OnPlayerDisconnect;
            sapi.Event.CanUseBlock += OnBlockAction.Event_OnBlockUse;
            sapi.Event.CanPlaceOrBreakBlock += OnBlockAction.Event_OnBlockDestroy;
            sapi.Event.OnTrySpawnEntity += OnTryEntitySpawn.Event_OnTrySpawnEntity;
            playerMovements.PlayerMovementTracker.Init(sapi);
            delayed.invitations.InvitationHandler.Init(sapi);
            sapi.Event.PlayerChat += OnPlayerChat.Event_OnPlayerChat;
            sapi.Event.PlayerDeath += OnPlayerDeath.Event_OnPlayerDeath;
            part.PrisonHandler.Init(sapi);

            // How each kind of invitation is built, so saved ones come back after a restart.
            // Registered before the database loads.
            delayed.invitations.InvitationKinds.Register("city", "player", part.CityMembership.MakeCityInvitation);
            delayed.invitations.InvitationParty.RegisterResolver("plotsgroup", guid => part.PlotsGroupHandler.FindByGuid(guid));
            delayed.invitations.InvitationKinds.Register("plotsgroup", "player", part.PlotsGroupHandler.MakeGroupInvitation);
            sapi.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, onModsAndConfigReady);
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => commands.register.ServerCommands.RegisterRunGameCommands(sapi));
            // Crop types are all loaded by RunGame.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => cropbehaviors.BehaviorOnlyOnFarmPlot.AttachToCrops(sapi));
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => blocks.BehaviorPlotTypeOnly.AttachToBlocks(sapi));
        }

        public static void onModsAndConfigReady()
        {
            // Not in StartServerSide: by now every mod has registered its commands, so a name
            // already taken by claims is seen and avoided.
            commands.register.ServerCommands.RegisterCommands(canclaimsextended.sapi);
            Settings.Load();
            if (!canclaimsextended.LoadDatabase())
            {
                // Running on top of a database that did not load would let the next save overwrite it.
                throw new InvalidOperationException("[canclaimsextended] Database could not be loaded, see the errors above. The server is stopped to keep the file intact.");
            }
            timers.DayTimer.Start(canclaimsextended.sapi);
            timers.BackupScheduler.Start(canclaimsextended.sapi);
        }
    }
}
