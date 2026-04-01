using System;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Messaging;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking.Messaging.Handlers;

public class TransferAuditLogHandler :
    IDistributedEventHandler<MoneyTransferredEto>,
    ITransientDependency
{
    private const string ConsumerName = nameof(TransferAuditLogHandler);

    private readonly IInboxManager _inboxManager;
    private readonly ILogger<TransferAuditLogHandler> _logger;
    private readonly IRepository<TransferAuditLog, Guid> _repo;

    public TransferAuditLogHandler(
        IInboxManager inboxManager,
        IRepository<TransferAuditLog, Guid> repo,
        ILogger<TransferAuditLogHandler> logger)
    {
        _inboxManager = inboxManager;
        _repo = repo;
        _logger = logger;
    }

    public async Task HandleEventAsync(MoneyTransferredEto eventData)
    {
        var payloadJson = JsonSerializer.Serialize(eventData);

        var decision = await _inboxManager.TryBeginProcessingAsync(
            eventData.EventId,
            nameof(MoneyTransferredEto),
            ConsumerName,
            payloadJson:payloadJson);

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

            if (decision.IsDeadLettered)
            {
                _logger.LogWarning(
                    "Distributed event is dead-lettered and skipped in normal flow. EventId={EventId}, Consumer={ConsumerName}",
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

            _logger.LogInformation(
                "AUDIT DB INSERT START. EventId={EventId}",
                eventData.EventId);

            var audit = new TransferAuditLog(
                Guid.NewGuid(),
                eventData.EventId,
                eventData.TransferId,
                eventData.FromAccountId,
                eventData.ToAccountId,
                eventData.UserId,
                eventData.Amount,
                eventData.Description,
                eventData.IdempotencyKey,
                eventData.OccurredAtUtc,
                nameof(MoneyTransferredEto)
            );

            await _repo.InsertAsync(audit, autoSave: true);

            _logger.LogInformation(
                "AUDIT DB INSERT DONE. EventId={EventId}, TransferId={TransferId}",
                eventData.EventId,
                eventData.TransferId);

            await _inboxManager.MarkProcessedAsync(decision.InboxMessageId!.Value);

            _logger.LogInformation(
                "Transfer audit handler completed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);
        }
        catch (Exception ex)
        {
            await _inboxManager.MarkFailedAsync(
                decision.InboxMessageId!.Value,
                ex.ToString(),
                ex.GetType().Name);

            _logger.LogError(
                ex,
                "Transfer audit handler failed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);

            throw;
        }
    }
}