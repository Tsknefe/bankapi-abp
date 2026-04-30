using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking.Messaging;

public class InboxEventDispatcher : IInboxEventDispatcher, ITransientDependency
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly ITransferEventSideEffectService _sideEffects;

    public InboxEventDispatcher(
        IRepository<InboxMessage, Guid> inboxRepository,
        ITransferEventSideEffectService sideEffects)
    {
        _inboxRepository = inboxRepository;
        _sideEffects = sideEffects;
    }

    public async Task DispatchAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.dispatch");

        activity?.SetTag("inbox.message.id", inboxMessageId);

        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);

        activity?.SetTag("inbox.event.id", inboxMessage.EventId.ToString());
        activity?.SetTag("inbox.event.name", inboxMessage.EventName);
        activity?.SetTag("inbox.consumer.name", inboxMessage.ConsumerName);
        activity?.SetTag("inbox.retry.count", inboxMessage.RetryCount);
        activity?.SetTag("inbox.status", inboxMessage.Status);

        if (string.IsNullOrWhiteSpace(inboxMessage.PayloadJson))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Payload is empty.");
            throw new BusinessException("INBOX_PAYLOAD_EMPTY");
        }

        if (inboxMessage.EventName != nameof(MoneyTransferredEto))
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"Unsupported event type: {inboxMessage.EventName}");
            throw new BusinessException("INBOX_UNSUPPORTED_EVENT")
                .WithData("EventName", inboxMessage.EventName);
        }

        var eventData = JsonSerializer.Deserialize<MoneyTransferredEto>(inboxMessage.PayloadJson);

        if (eventData == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Payload deserialize failed.");
            throw new BusinessException("INBOX_PAYLOAD_DESERIALIZE_FAILED");
        }

        if (inboxMessage.ConsumerName == nameof(Handlers.TransferAuditLogHandler))
        {
            activity?.SetTag("inbox.dispatch.target", nameof(Handlers.TransferAuditLogHandler));
            await _sideEffects.WriteAuditLogAsync(eventData);
            activity?.SetTag("inbox.dispatch.result", "success");
            return;
        }

        if (inboxMessage.ConsumerName == nameof(BankApiAbp.Banking.Handlers.TransferNotificationHandler))
        {
            activity?.SetTag("inbox.dispatch.target", nameof(BankApiAbp.Banking.Handlers.TransferNotificationHandler));
            await _sideEffects.WriteNotificationLogAsync(eventData);
            activity?.SetTag("inbox.dispatch.result", "success");
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error, $"Unsupported consumer: {inboxMessage.ConsumerName}");

        throw new BusinessException("INBOX_UNSUPPORTED_CONSUMER")
            .WithData("ConsumerName", inboxMessage.ConsumerName);
    }
}