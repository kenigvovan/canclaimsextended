using canclaimsextended.src.economy;
using Vintagestory.API.Server;

namespace canclaimsextended.scenarios.Infrastructure;

public enum FakeMoneyOp { Withdraw, Deposit, Transfer }

/// <summary>
/// An economy with real balances, for scenarios that need "not enough money", a paid action that
/// waits for /agree, or a failing operation - none of which ever happens with the default
/// NoopMoneyProvider (everything free, balance decimal.MaxValue).
///
/// Every account exists and starts at 0: the mod does not always create accounts before using
/// them, and a fake that refused unknown accounts would fail flows the real economy lets through.
/// </summary>
public sealed class FakeMoneyProvider : IMoneyProvider
{
    private readonly object sync = new();
    private readonly Dictionary<string, decimal> balances = new();
    private readonly List<(FakeMoneyOp Op, string? Account, MoneyOperationResult Result)> plannedFailures = new();
    private readonly List<string> log = new();

    /// <summary>Every operation in order, failed ones included: "Transfer a->b 40: Success".</summary>
    public IReadOnlyList<string> Log { get { lock (sync) return log.ToList(); } }

    public void SetBalance(string account, decimal amount)
    {
        lock (sync) balances[account] = amount;
    }

    /// <summary>Sum of all balances. Moving money must not change it.</summary>
    public decimal Total()
    {
        lock (sync) return balances.Values.Sum();
    }

    /// <summary>
    /// Makes the next <paramref name="op"/> fail with <paramref name="result"/> and change nothing.
    /// With <paramref name="account"/> set, only an operation touching that account counts.
    /// </summary>
    public void FailNext(FakeMoneyOp op, string? account = null,
        MoneyOperationResult result = MoneyOperationResult.FailedSourceWithdraw)
    {
        lock (sync) plannedFailures.Add((op, account, result));
    }

    public decimal GetBalance(string account)
    {
        lock (sync) return Balance(account);
    }

    public MoneyOperationResult Withdraw(string account, decimal amount)
    {
        lock (sync)
        {
            MoneyOperationResult result = TakePlannedFailure(FakeMoneyOp.Withdraw, account)
                ?? (amount < 0 ? MoneyOperationResult.WrongParameter
                    : Balance(account) < amount ? MoneyOperationResult.SourceNotEnoughMoney
                    : MoneyOperationResult.Success);
            if (result == MoneyOperationResult.Success) balances[account] = Balance(account) - amount;
            log.Add($"Withdraw {account} {amount}: {result}");
            return result;
        }
    }

    public MoneyOperationResult Deposit(string account, decimal amount)
    {
        lock (sync)
        {
            MoneyOperationResult result = TakePlannedFailure(FakeMoneyOp.Deposit, account)
                ?? (amount < 0 ? MoneyOperationResult.WrongParameter : MoneyOperationResult.Success);
            if (result == MoneyOperationResult.Success) balances[account] = Balance(account) + amount;
            log.Add($"Deposit {account} {amount}: {result}");
            return result;
        }
    }

    public MoneyOperationResult Transfer(string fromAccount, string toAccount, decimal amount)
    {
        lock (sync)
        {
            MoneyOperationResult result = TakePlannedFailure(FakeMoneyOp.Transfer, fromAccount, toAccount)
                ?? (amount < 0 ? MoneyOperationResult.WrongParameter
                    : Balance(fromAccount) < amount ? MoneyOperationResult.SourceNotEnoughMoney
                    : MoneyOperationResult.Success);
            if (result == MoneyOperationResult.Success)
            {
                balances[fromAccount] = Balance(fromAccount) - amount;
                balances[toAccount] = Balance(toAccount) + amount;
            }
            log.Add($"Transfer {fromAccount}->{toAccount} {amount}: {result}");
            return result;
        }
    }

    public bool NewAccount(string account, IDictionary<string, object>? meta = null)
    {
        lock (sync) balances.TryAdd(account, 0m);
        return true;
    }

    public bool UpdateAccount(string account, IDictionary<string, object>? meta = null) => true;

    public bool DeleteAccount(string account)
    {
        lock (sync) balances.Remove(account);
        return true;
    }


    public bool SupportsPlayerWallet => true;
    public bool SupportsRemoteCoinTransfer => true;

    // Coins are not what these scenarios are about.
    public decimal TakeCoinItemsFromActiveSlot(IServerPlayer player) => 0m;
    public void GiveCoinItemsToPlayer(IServerPlayer player, decimal amount) { }

    public IReadOnlyList<CoinDisplayInfo> CoinDenominations { get; } = new List<CoinDisplayInfo>();

    private decimal Balance(string account) => balances.TryGetValue(account, out decimal b) ? b : 0m;

    private MoneyOperationResult? TakePlannedFailure(FakeMoneyOp op, params string[] accounts)
    {
        int i = plannedFailures.FindIndex(f => f.Op == op && (f.Account == null || accounts.Contains(f.Account)));
        if (i < 0) return null;
        MoneyOperationResult result = plannedFailures[i].Result;
        plannedFailures.RemoveAt(i);
        return result;
    }
}
