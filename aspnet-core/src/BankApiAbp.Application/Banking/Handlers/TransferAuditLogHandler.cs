using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking.Messaging.Handlers;

public class TransferAuditLogHandler :
    IDistributedEventHandler<MoneyTransferredEto>,
    ITransientDependency
{
    private const string ConsumerName = nameof(TransferAuditLogHandler);

    private readonly IInboxManager _inboxManager;
    private readonly ILogger<TransferAuditLogHandler> _logger;

    public TransferAuditLogHandler(
        IInboxManager inboxManager,
        ILogger<TransferAuditLogHandler> logger)
    {
        _inboxManager = inboxManager;
        _logger = logger;
    }

    public async Task HandleEventAsync(MoneyTransferredEto eventData)
    {
        var decision = await _inboxManager.TryBeginProcessingAsync(
            eventData.EventId,
            nameof(MoneyTransferredEto),
            ConsumerName);

        if (!decision.ShouldProcess)
        {
            if (decision.IsDuplicate)
            {
                _logger.LogInformation(
                    "Duplicate distributed event skipped. EventId={EventId}, Consumer={ConsumerName}",
                    eventData.EventId, ConsumerName);
            }

            if (decision.IsInProgress)
            {
                _logger.LogWarning(
                    "Distributed event already in progress. EventId={EventId}, Consumer={ConsumerName}",
                    eventData.EventId, ConsumerName);
            }

            return;
        }

        try
        {
            _logger.LogInformation(
                "Transfer audit handler running. TransferId={TransferId}, From={FromAccountId}, To={ToAccountId}, Amount={Amount}",
                eventData.TransferId,
                eventData.FromAccountId,
                eventData.ToAccountId,
                eventData.Amount);

            await _inboxManager.MarkProcessedAsync(decision.InboxMessageId!.Value);

            _logger.LogInformation(
                "Transfer audit handler completed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId, ConsumerName);
        }
        catch (Exception ex)
        {
            await _inboxManager.MarkFailedAsync(decision.InboxMessageId!.Value, ex.ToString());

            _logger.LogError(
                ex,
                "Transfer audit handler failed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);

            throw;
        }
    }
}