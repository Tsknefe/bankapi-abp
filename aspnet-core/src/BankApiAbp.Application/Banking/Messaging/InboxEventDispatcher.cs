using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Handlers;
using BankApiAbp.Banking.Messaging.Handlers;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking.Messaging;

public class InboxEventDispatcher : IInboxEventDispatcher, ITransientDependency
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly TransferAuditLogHandler _auditHandler;
    private readonly TransferNotificationHandler _notificationHandler;

    public InboxEventDispatcher(
        IRepository<InboxMessage, Guid> inboxRepository,
        TransferAuditLogHandler auditHandler,
        TransferNotificationHandler notificationHandler)
    {
        _inboxRepository = inboxRepository;
        _auditHandler = auditHandler;
        _notificationHandler = notificationHandler;
    }

    public async Task DispatchAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.dispatch");

        activity?.SetTag("inbox.message.id", inboxMessageId);

        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);

        activity?.SetTag("inbox.event.id", inboxMessage.EventId);
        activity?.SetTag("inbox.event.name", inboxMessage.EventName);
        activity?.SetTag("inbox.consumer.name", inboxMessage.ConsumerName);
        activity?.SetTag("inbox.retry.count", inboxMessage.RetryCount);
        activity?.SetTag("inbox.status", inboxMessage.Status.ToString());

        if (string.IsNullOrWhiteSpace(inboxMessage.PayloadJson))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Inbox message payload is empty.");
            throw new BusinessException(message: "Inbox message payload is empty.");
        }

        if (inboxMessage.EventName != nameof(MoneyTransferredEto))
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"Unsupported event type: {inboxMessage.EventName}");
            throw new BusinessException(message: $"Unsupported event type: {inboxMessage.EventName}");
        }

        var eventData = JsonSerializer.Deserialize<MoneyTransferredEto>(inboxMessage.PayloadJson);

        if (eventData == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Inbox message payload could not be deserialized.");
            throw new BusinessException(message: "Inbox message payload could not be deserialized.");
        }

        if (inboxMessage.ConsumerName == nameof(TransferAuditLogHandler))
        {
            activity?.SetTag("inbox.dispatch.target", nameof(TransferAuditLogHandler));
            await _auditHandler.HandleEventAsync(eventData);
            activity?.SetTag("inbox.dispatch.result", "success");
            return;
        }

        if (inboxMessage.ConsumerName == nameof(TransferNotificationHandler))
        {
            activity?.SetTag("inbox.dispatch.target", nameof(TransferNotificationHandler));
            await _notificationHandler.HandleEventAsync(eventData);
            activity?.SetTag("inbox.dispatch.result", "success");
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error, $"Unsupported consumer: {inboxMessage.ConsumerName}");
        throw new BusinessException(message: $"Unsupported consumer: {inboxMessage.ConsumerName}");
    }
}