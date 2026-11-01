using System.Collections.Generic;
using Vintagestory.API.Server;

namespace canclaimsextended.src.economy
{
    /// <summary>No economy mod installed: every account is bottomless and everything is free.</summary>
    public sealed class NoopMoneyProvider : IMoneyProvider
    {
        private static readonly IReadOnlyList<CoinDisplayInfo> emptyCoins = new List<CoinDisplayInfo>();

        public bool NewAccount(string account, IDictionary<string, object> meta = null) => true;
        public bool UpdateAccount(string account, IDictionary<string, object> meta = null) => true;
        public bool DeleteAccount(string account) => true;

        public decimal GetBalance(string account) => decimal.MaxValue;
        public MoneyOperationResult Withdraw(string account, decimal amount) => MoneyOperationResult.Success;
        public MoneyOperationResult Deposit(string account, decimal amount) => MoneyOperationResult.Success;
        public MoneyOperationResult Transfer(string fromAccount, string toAccount, decimal amount) => MoneyOperationResult.Success;

        public bool SupportsPlayerWallet => false;
        public bool SupportsRemoteCoinTransfer => false;

        public decimal TakeCoinItemsFromActiveSlot(IServerPlayer player) => 0m;
        public void GiveCoinItemsToPlayer(IServerPlayer player, decimal amount) { }

        public IReadOnlyList<CoinDisplayInfo> CoinDenominations => emptyCoins;
    }
}
