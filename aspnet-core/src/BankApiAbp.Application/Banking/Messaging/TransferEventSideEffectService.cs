using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Handlers;
using BankApiAbp.Banking.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking.Messaging;

public class TransferEventSideEffectService :
    ITransferEventSideEffectService,
    ITransientDependency
{
    private readonly IRepository<TransferAuditLog, Guid> _auditRepo;
    private readonly IRepository<TransferNotificationLog, Guid> _notificationRepo;
    private readonly ILogger<TransferEventSideEffectService> _logger;

    public TransferEventSideEffectService(
        IRepository<TransferAuditLog, Guid> auditRepo,
        IRepository<TransferNotificationLog, Guid> notificationRepo,
        ILogger<TransferEventSideEffectService> logger)
    {
        _auditRepo = auditRepo;
        _notificationRepo = notificationRepo;
        _logger = logger;
    }

    public async Task WriteAuditLogAsync(MoneyTransferredEto eventData)
    {
        using var activity =
            InboxTracing.ActivitySource.StartActivity("Inbox.SideEffect.WriteAuditLog");

        activity?.SetTag("event.id", eventData.EventId.ToString());
        activity?.SetTag("transfer.id", eventData.TransferId.ToString());
        activity?.SetTag("user.id", eventData.UserId.ToString());
        activity?.SetTag("amount", eventData.Amount);

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

        await _auditRepo.InsertAsync(audit, autoSave: true);

        _logger.LogInformation(
            "Transfer audit log written. EventId={EventId}, TransferId={TransferId}",
            eventData.EventId,
            eventData.TransferId);
    }

    public async Task WriteNotificationLogAsync(MoneyTransferredEto eventData)
    {
        using var activity =
            InboxTracing.ActivitySource.StartActivity("Inbox.SideEffect.WriteNotificationLog");

        activity?.SetTag("event.id", eventData.EventId.ToString());
        activity?.SetTag("transfer.id", eventData.TransferId.ToString());
        activity?.SetTag("user.id", eventData.UserId.ToString());
        activity?.SetTag("amount", eventData.Amount);
        activity?.SetTag("notification.channel", "InApp");
        activity?.SetTag("notification.status", "Queued");

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

        await _notificationRepo.InsertAsync(notification, autoSave: true);

        _logger.LogInformation(
            "Transfer notification log written. EventId={EventId}, TransferId={TransferId}",
            eventData.EventId,
            eventData.TransferId);
    }
}