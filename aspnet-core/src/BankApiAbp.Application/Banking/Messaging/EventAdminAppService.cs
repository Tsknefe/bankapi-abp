using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace BankApiAbp.Banking.Messaging;

public class EventAdminAppService : ApplicationService, IEventAdminAppService
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly IInboxManager _inboxManager;
    private readonly IInboxEventDispatcher _dispatcher;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EventAdminAppService(
        IRepository<InboxMessage, Guid> inboxRepository,
        IInboxManager inboxManager,
        IInboxEventDispatcher dispatcher,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _inboxRepository = inboxRepository;
        _inboxManager = inboxManager;
        _dispatcher = dispatcher;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<List<InboxMessageDto>> GetRetryableInboxMessagesAsync()
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var statuses = new[]
            {
                InboxMessageStatus.Failed,
                InboxMessageStatus.Retrying,
                InboxMessageStatus.DeadLettered
            };

            var items = await _inboxRepository.GetListAsync(x => statuses.Contains(x.Status));

            var list = items
                .OrderByDescending(x => x.LastAttemptTime)
                .Select(x => new InboxMessageDto
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
                })
                .ToList();

            await uow.CompleteAsync();
            return list;
        }
    }

    public async Task RetryInboxMessageAsync(Guid inboxMessageId)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);

            if (inboxMessage.Status != InboxMessageStatus.Failed &&
                inboxMessage.Status != InboxMessageStatus.Retrying &&
                inboxMessage.Status != InboxMessageStatus.DeadLettered)
            {
                throw new BusinessException(
                    message: $"Inbox message is not retryable. Current status: {inboxMessage.Status}");
            }

            await _inboxManager.RequeueAsync(inboxMessageId);
            await uow.CompleteAsync();
        }

        await _dispatcher.DispatchAsync(inboxMessageId);
    }
}