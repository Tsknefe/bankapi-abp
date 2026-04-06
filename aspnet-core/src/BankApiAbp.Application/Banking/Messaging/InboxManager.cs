using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace BankApiAbp.Banking.Messaging;

public class InboxManager : IInboxManager, ITransientDependency
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly ILogger<InboxManager> _logger;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public InboxManager(
        IRepository<InboxMessage, Guid> inboxRepository,
        ILogger<InboxManager> logger,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _inboxRepository = inboxRepository;
        _logger = logger;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<InboxExecutionDecision> TryBeginProcessingAsync(
        Guid eventId,
        string eventName,
        string consumerName,
        string? payloadHash = null,
        string? payloadJson = null,
        int maxRetryCount = 3)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var existing = await _inboxRepository.FirstOrDefaultAsync(x =>
                x.EventId == eventId &&
                x.ConsumerName == consumerName);

            if (existing != null)
            {
                if (existing.Status == InboxMessageStatus.Processed)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.Duplicate();
                }

                if (existing.Status == InboxMessageStatus.Processing)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.InProgress();
                }

                if (existing.Status == InboxMessageStatus.DeadLettered)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.DeadLettered();
                }

                if (existing.Status == InboxMessageStatus.Pending ||
                    existing.Status == InboxMessageStatus.Failed ||
                    existing.Status == InboxMessageStatus.Retrying)
                {
                    var previousStatus = existing.Status;

                    existing.MarkProcessing();
                    await _inboxRepository.UpdateAsync(existing, autoSave: true);

                    _logger.LogInformation(
                        "Inbox moved to processing. Prev={Prev} Retry={Retry}",
                        previousStatus, existing.RetryCount);

                    await uow.CompleteAsync();
                    return InboxExecutionDecision.Process(existing.Id);
                }
            }

            var inboxMessage = new InboxMessage(
                Guid.NewGuid(),
                eventId,
                eventName,
                consumerName,
                payloadHash,
                payloadJson,
                maxRetryCount);

            inboxMessage.MarkProcessing();

            try
            {
                await _inboxRepository.InsertAsync(inboxMessage, autoSave: true);
                await uow.CompleteAsync();
                return InboxExecutionDecision.Process(inboxMessage.Id);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                var conflictRow = await _inboxRepository.FirstOrDefaultAsync(x =>
                    x.EventId == eventId &&
                    x.ConsumerName == consumerName);

                if (conflictRow == null)
                    throw;

                if (conflictRow.Status == InboxMessageStatus.Processed)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.Duplicate();
                }

                if (conflictRow.Status == InboxMessageStatus.Processing)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.InProgress();
                }

                if (conflictRow.Status == InboxMessageStatus.DeadLettered)
                {
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.DeadLettered();
                }

                conflictRow.MarkProcessing();
                await _inboxRepository.UpdateAsync(conflictRow, autoSave: true);

                await uow.CompleteAsync();
                return InboxExecutionDecision.Process(conflictRow.Id);
            }
        }
    }

    public async Task MarkProcessedAsync(Guid inboxMessageId)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var msg = await _inboxRepository.GetAsync(inboxMessageId);
            msg.MarkProcessed();
            await _inboxRepository.UpdateAsync(msg, autoSave: true);
            await uow.CompleteAsync();
        }
    }

    public async Task MarkFailedAsync(Guid inboxMessageId, string error, string? errorCode = null)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var msg = await _inboxRepository.GetAsync(inboxMessageId);

            if (msg.HasRetryQuota())
            {
                var next = msg.RetryCount + 1;
                var delay = InboxRetryPolicy.GetDelay(next);

                msg.MarkRetry(error, errorCode, delay);
                InboxMetrics.MessagesRetried.Add(1);
            }
            else
            {
                msg.MarkDeadLettered(error, errorCode, "Max retry exceeded");
                InboxMetrics.MessagesDeadLettered.Add(1);
            }

            await _inboxRepository.UpdateAsync(msg, autoSave: true);
            await uow.CompleteAsync();
        }
    }

    public async Task RequeueAsync(Guid inboxMessageId)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var msg = await _inboxRepository.GetAsync(inboxMessageId);
            msg.Requeue();
            await _inboxRepository.UpdateAsync(msg, autoSave: true);
            await uow.CompleteAsync();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }

        return ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
    }
}