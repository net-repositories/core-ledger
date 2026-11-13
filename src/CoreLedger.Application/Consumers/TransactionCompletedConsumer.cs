using CoreLedger.Application.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace CoreLedger.Application.Consumers;

public class TransactionCompletedConsumer(ILogger<TransactionCompletedConsumer> logger)
    : IConsumer<TransactionCompletedEvent>
{
    private const decimal HighValueThreshold = 100_000m;

    public Task Consume(ConsumeContext<TransactionCompletedEvent> context)
    {
        var message = context.Message;

        logger.LogInformation(
            "Notification sent: Transaction {TransactionId} ({Type}) for {Amount} {Currency} completed at {OccurredAt}. From: {FromAccountId}, To: {ToAccountId}",
            message.TransactionId,
            message.Type,
            message.Amount,
            message.Currency,
            message.OccurredAt,
            message.FromAccountId,
            message.ToAccountId);

        if (message.Amount > HighValueThreshold)
        {
            logger.LogWarning(
                "ALERT: High-value transaction detected! TransactionId: {TransactionId}, Amount: {Amount} {Currency}, Type: {Type}, FromAccount: {FromAccountId}, ToAccount: {ToAccountId}",
                message.TransactionId,
                message.Amount,
                message.Currency,
                message.Type,
                message.FromAccountId,
                message.ToAccountId);
        }

        return Task.CompletedTask;
    }
}
