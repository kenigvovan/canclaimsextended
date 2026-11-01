using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace canclaimsextended.src.economy.bridge
{
    /// <summary>
    /// Plugs caneconomy in as the money provider when that mod is enabled. Without it the
    /// no-op provider stays and everything is free.
    /// </summary>
    public class BridgeModSystem : ModSystem
    {
        public override double ExecuteOrder() => 1.0;

        private CanEconomyAdapter _adapter;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            if (!api.ModLoader.IsModEnabled("caneconomy")) return;
            api.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, InstallAdapter);
            // caneconomy fills its coin index only in the RunGame phase.
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => _adapter?.RefreshCoinDenominations());
        }

        public override void Dispose()
        {
            base.Dispose();
            if (_adapter == null) return;
            UninstallAdapter();
        }

        // Kept in their own methods: JIT-compiling code that touches caneconomy types loads caneconomy.dll.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void InstallAdapter()
        {
            _adapter = new CanEconomyAdapter();
            canclaimsextended.economyProvider = _adapter;
            // Bank chests are made with a sign; the handlers do nothing unless the economy is REAL_MONEY.
            caneconomy.caneconomy.OnBlockRemovedBlockEntityOpenableContainer += RealBankActions.OnBlockRemoved;
            caneconomy.caneconomy.OnReceivedClientPacketBlockEntitySign += RealBankActions.OnButtonSave;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void UninstallAdapter()
        {
            caneconomy.caneconomy.OnBlockRemovedBlockEntityOpenableContainer -= RealBankActions.OnBlockRemoved;
            caneconomy.caneconomy.OnReceivedClientPacketBlockEntitySign -= RealBankActions.OnButtonSave;
            _adapter = null;
            canclaimsextended.economyProvider = new NoopMoneyProvider();
        }
    }
}
