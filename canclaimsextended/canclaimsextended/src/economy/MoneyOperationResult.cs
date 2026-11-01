namespace canclaimsextended.src.economy
{
    public enum MoneyOperationResult
    {
        None,
        Success,
        WrongParameter,
        SourceAccountNotFound,
        TargetAccountNotFound,
        SourceNotEnoughMoney,
        FailedSourceWithdraw,
        FailedTargetDeposit,
        GreaterThanMaxBalance,
        NotSupported
    }
}
