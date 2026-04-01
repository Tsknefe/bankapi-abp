using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking.Messaging;

public class InboxManager : IInboxManager, ITransientDependency
{
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly ILogger<InboxManager> _logger;

    public InboxManager(
        IRepository<InboxMessage, Guid> inboxRepository,
        ILogger<InboxManager> logger)
    {
        _inboxRepository = inboxRepository;
        _logger = logger;
    }

    public async Task<InboxExecutionDecision> TryBeginProcessingAsync(
        Guid eventId,
        string eventName,
        string consumerName,
        string? payloadHash = null,
        string? payloadJson = null,
        int maxRetryCount = 3)
    {
        var queryable = await _inboxRepository.GetQueryableAsync();

        var existing = await queryable.FirstOrDefaultAsync(x =>
            x.EventId == eventId &&
            x.ConsumerName == consumerName);

        if (existing != null)
        {
            if (existing.Status == InboxMessageStatus.Processed)
            {
                return InboxExecutionDecision.Duplicate();
            }

            if (existing.Status == InboxMessageStatus.Processing)
            {
                return InboxExecutionDecision.InProgress();
            }

            if (existing.Status == InboxMessageStatus.DeadLettered)
            {
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
            return InboxExecutionDecision.Process(inboxMessage.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var retryQuery = await _inboxRepository.GetQueryableAsync();

            var conflictRow = await retryQuery.FirstOrDefaultAsync(x =>
                x.EventId == eventId &&
                x.ConsumerName == consumerName);

            if (conflictRow == null)
                throw;

            if (conflictRow.Status == InboxMessageStatus.Processed)
                return InboxExecutionDecision.Duplicate();

            if (conflictRow.Status == InboxMessageStatus.Processing)
                return InboxExecutionDecision.InProgress();

            if (conflictRow.Status == InboxMessageStatus.DeadLettered)
                return InboxExecutionDecision.DeadLettered();

            conflictRow.MarkProcessing();
            await _inboxRepository.UpdateAsync(conflictRow, autoSave: true);

            return InboxExecutionDecision.Process(conflictRow.Id);
        }
    }

    public async Task MarkProcessedAsync(Guid inboxMessageId)
    {
        var msg = await _inboxRepository.GetAsync(inboxMessageId);
        msg.MarkProcessed();
        await _inboxRepository.UpdateAsync(msg, autoSave: true);
    }

    public async Task MarkFailedAsync(Guid inboxMessageId, string error, string? errorCode = null)
    {
        var msg = await _inboxRepository.GetAsync(inboxMessageId);

        if (msg.HasRetryQuota())
        {
            var next = msg.RetryCount + 1;
            var delay = InboxRetryPolicy.GetDelay(next);

            msg.MarkRetry(error, errorCode, delay);
        }
        else
        {
            msg.MarkDeadLettered(error, errorCode, "Max retry exceeded");
        }

        await _inboxRepository.UpdateAsync(msg, autoSave: true);
    }

    public async Task RequeueAsync(Guid inboxMessageId)
    {
        var msg = await _inboxRepository.GetAsync(inboxMessageId);
        msg.Requeue();
        await _inboxRepository.UpdateAsync(msg, autoSave: true);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg &&
               pg.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}