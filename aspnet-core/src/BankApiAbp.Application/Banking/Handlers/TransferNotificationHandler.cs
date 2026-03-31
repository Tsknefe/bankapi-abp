using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Messaging;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking.Handlers;

public class TransferNotificationHandler :
    IDistributedEventHandler<MoneyTransferredEto>,
    ITransientDependency
{
    private const string ConsumerName = nameof(TransferNotificationHandler);

    private readonly IInboxManager _inboxManager;
    private readonly IRepository<TransferNotificationLog, Guid> _repo;
    private readonly ILogger<TransferNotificationHandler> _logger;

    public TransferNotificationHandler(
        IInboxManager inboxManager,
        IRepository<TransferNotificationLog, Guid> repo,
        ILogger<TransferNotificationHandler> logger)
    {
        _inboxManager = inboxManager;
        _repo = repo;
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
                    "Duplicate notification event skipped. EventId={EventId}, Consumer={ConsumerName}",
                    eventData.EventId,
                    ConsumerName);
            }

            if (decision.IsInProgress)
            {
                _logger.LogWarning(
                    "Notification event already in progress. EventId={EventId}, Consumer={ConsumerName}",
                    eventData.EventId,
                    ConsumerName);
            }

            return;
        }

        try
        {
            _logger.LogInformation(
                "NOTIFICATION DB INSERT START. EventId={EventId}",
                eventData.EventId);

            var notification = new TransferNotificationLog(
                Guid.NewGuid(),
                eventData.EventId,
                eventData.TransferId,
                eventData.UserId,
                eventData.FromAccountId,
                eventData.ToAccountId,
                eventData.Amount,
                eventData.Description,
                eventData.IdempotencyKey,
                eventData.OccurredAtUtc,
                "InApp",
                "Queued",
                nameof(MoneyTransferredEto)
            );

            await _repo.InsertAsync(notification, autoSave: true);

            _logger.LogInformation(
                "NOTIFICATION DB INSERT DONE. EventId={EventId}, TransferId={TransferId}",
                eventData.EventId,
                eventData.TransferId);

            await _inboxManager.MarkProcessedAsync(decision.InboxMessageId!.Value);

            _logger.LogInformation(
                "Transfer notification handler completed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);
        }
        catch (Exception ex)
        {
            await _inboxManager.MarkFailedAsync(decision.InboxMessageId!.Value, ex.ToString());

            _logger.LogError(
                ex,
                "Transfer notification handler failed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);

            throw;
        }
    }
}