namespace CoreLedger.Domain.Exceptions;

public class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(Guid accountId, decimal balance, decimal requestedAmount)
        : base($"Insufficient funds on account '{accountId}'. Current balance: {balance}, requested: {requestedAmount}.")
    {
    }
}
