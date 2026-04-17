using System;
using System.Diagnostics;
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
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.try_begin_processing");

        activity?.SetTag("inbox.event.id", eventId);
        activity?.SetTag("inbox.event.name", eventName);
        activity?.SetTag("inbox.consumer.name", consumerName);
        activity?.SetTag("inbox.max_retry.count", maxRetryCount);

        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var existing = await _inboxRepository.FirstOrDefaultAsync(x =>
                x.EventId == eventId &&
                x.ConsumerName == consumerName);

            if (existing != null)
            {
                activity?.SetTag("inbox.existing.message.id", existing.Id);
                activity?.SetTag("inbox.existing.status", existing.Status.ToString());
                activity?.SetTag("inbox.existing.retry.count", existing.RetryCount);

                if (existing.Status == InboxMessageStatus.Processed)
                {
                    activity?.SetTag("inbox.decision", "duplicate");
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.Duplicate();
                }

                if (existing.Status == InboxMessageStatus.Processing)
                {
                    activity?.SetTag("inbox.decision", "in_progress");
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.InProgress();
                }

                if (existing.Status == InboxMessageStatus.DeadLettered)
                {
                    activity?.SetTag("inbox.decision", "dead_lettered");
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

                    activity?.SetTag("inbox.decision", "process_existing");
                    activity?.SetTag("inbox.previous.status", previousStatus.ToString());

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
                activity?.SetTag("inbox.decision", "process_new");
                activity?.SetTag("inbox.new.message.id", inboxMessage.Id);

                await uow.CompleteAsync();
                return InboxExecutionDecision.Process(inboxMessage.Id);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                activity?.SetTag("inbox.unique_violation", true);

                var conflictRow = await _inboxRepository.FirstOrDefaultAsync(x =>
                    x.EventId == eventId &&
                    x.ConsumerName == consumerName);

                if (conflictRow == null)
                    throw;

                activity?.SetTag("inbox.conflict.message.id", conflictRow.Id);
                activity?.SetTag("inbox.conflict.status", conflictRow.Status.ToString());

                if (conflictRow.Status == InboxMessageStatus.Processed)
                {
                    activity?.SetTag("inbox.decision", "duplicate_after_conflict");
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.Duplicate();
                }

                if (conflictRow.Status == InboxMessageStatus.Processing)
                {
                    activity?.SetTag("inbox.decision", "in_progress_after_conflict");
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.InProgress();
                }

                if (conflictRow.Status == InboxMessageStatus.DeadLettered)
                {
                    activity?.SetTag("inbox.decision", "dead_lettered_after_conflict");
                    await uow.CompleteAsync();
                    return InboxExecutionDecision.DeadLettered();
                }

                conflictRow.MarkProcessing();
                await _inboxRepository.UpdateAsync(conflictRow, autoSave: true);

                activity?.SetTag("inbox.decision", "process_conflict_row");

                await uow.CompleteAsync();
                return InboxExecutionDecision.Process(conflictRow.Id);
            }
        }
    }

    public async Task MarkProcessedAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.mark_processed");
        activity?.SetTag("inbox.message.id", inboxMessageId);

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
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.mark_failed");
        activity?.SetTag("inbox.message.id", inboxMessageId);
        activity?.SetTag("inbox.error", error);
        activity?.SetTag("inbox.error_code", errorCode);

        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var msg = await _inboxRepository.GetAsync(inboxMessageId);

            activity?.SetTag("inbox.retry.count.before", msg.RetryCount);
            activity?.SetTag("inbox.max_retry.count", msg.MaxRetryCount);

            if (msg.HasRetryQuota())
            {
                var next = msg.RetryCount + 1;
                var delay = InboxRetryPolicy.GetDelay(next);

                msg.MarkRetry(error, errorCode, delay);
                InboxMetrics.MessagesRetried.Add(1);

                activity?.SetTag("inbox.result", "retry");
                activity?.SetTag("inbox.retry.count.after", msg.RetryCount);
                activity?.SetTag("inbox.next_retry_time", msg.NextRetryTime?.ToString("O"));
            }
            else
            {
                msg.MarkDeadLettered(error, errorCode, "Max retry exceeded");
                InboxMetrics.MessagesDeadLettered.Add(1);

                activity?.SetTag("inbox.result", "dead_lettered");
                activity?.SetTag("inbox.dead_letter_reason", "Max retry exceeded");
            }

            await _inboxRepository.UpdateAsync(msg, autoSave: true);
            await uow.CompleteAsync();
        }
    }

    public async Task RequeueAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.requeue");
        activity?.SetTag("inbox.message.id", inboxMessageId);

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