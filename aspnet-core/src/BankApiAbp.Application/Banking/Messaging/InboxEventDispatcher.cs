using System;
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
        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);

        if (string.IsNullOrWhiteSpace(inboxMessage.PayloadJson))
        {
            throw new BusinessException(message: "Inbox message payload is empty.");
        }

        if (inboxMessage.EventName != nameof(MoneyTransferredEto))
        {
            throw new BusinessException(message: $"Unsupported event type: {inboxMessage.EventName}");
        }

        var eventData = JsonSerializer.Deserialize<MoneyTransferredEto>(inboxMessage.PayloadJson);

        if (eventData == null)
        {
            throw new BusinessException(message: "Inbox message payload could not be deserialized.");
        }

        if (inboxMessage.ConsumerName == nameof(TransferAuditLogHandler))
        {
            await _auditHandler.HandleEventAsync(eventData);
            return;
        }

        if (inboxMessage.ConsumerName == nameof(TransferNotificationHandler))
        {
            await _notificationHandler.HandleEventAsync(eventData);
            return;
        }

        throw new BusinessException(message: $"Unsupported consumer: {inboxMessage.ConsumerName}");
    }
}