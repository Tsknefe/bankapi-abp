using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking.Messaging;

public class EventAdminAppService : ApplicationService
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly IInboxManager _inboxManager;
    private readonly IInboxEventDispatcher _dispatcher;

    public EventAdminAppService(
        IRepository<InboxMessage, Guid> inboxRepository,
        IInboxManager inboxManager,
        IInboxEventDispatcher dispatcher)
    {
        _inboxRepository = inboxRepository;
        _inboxManager = inboxManager;
        _dispatcher = dispatcher;
    }

    public async Task<List<InboxMessageDto>> GetRetryableInboxMessagesAsync()
    {
        var queryable = await _inboxRepository.GetQueryableAsync();

        var list = await queryable
            .Where(x =>
                x.Status == InboxMessageStatus.Failed ||
                x.Status == InboxMessageStatus.Retrying ||
                x.Status == InboxMessageStatus.DeadLettered)
            .OrderByDescending(x => x.LastAttemptTime)
            .ToListAsync();

        return list.Select(x => new InboxMessageDto
        {
            Id = x.Id,
            EventId = x.EventId,
            EventName = x.EventName,
            ConsumerName = x.ConsumerName,
            Status = x.Status,
            RetryCount = x.RetryCount,
            MaxRetryCount = x.MaxRetryCount,
            LastAttemptTime = x.LastAttemptTime,
            NextRetryTime = x.NextRetryTime,
            ProcessedAt = x.ProcessedAt,
            Error = x.Error,
            LastErrorCode = x.LastErrorCode,
            DeadLetteredAt = x.DeadLetteredAt,
            DeadLetterReason = x.DeadLetterReason
        }).ToList();
    }

    public async Task RetryInboxMessageAsync(Guid inboxMessageId)
    {
        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);

        if (inboxMessage.Status != InboxMessageStatus.Failed &&
            inboxMessage.Status != InboxMessageStatus.Retrying &&
            inboxMessage.Status != InboxMessageStatus.DeadLettered)
        {
            throw new BusinessException(
                $"Inbox message is not retryable. Current status: {inboxMessage.Status}");
        }

        await _inboxManager.RequeueAsync(inboxMessageId);

        await _dispatcher.DispatchAsync(inboxMessageId);
    }
}