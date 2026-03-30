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
        string? payloadHash = null)
    {
        var queryable = await _inboxRepository.GetQueryableAsync();

        var existing = await queryable.FirstOrDefaultAsync(x =>
            x.EventId == eventId &&
            x.ConsumerName == consumerName);

        if (existing != null)
        {
            if (existing.Status == InboxMessageStatus.Processed)
            {
                _logger.LogInformation(
                    "Inbox duplicate processed event skipped. EventId={EventId}, Consumer={ConsumerName}",
                    eventId, consumerName);

                return InboxExecutionDecision.Duplicate();
            }

            if (existing.Status == InboxMessageStatus.Processing)
            {
                _logger.LogWarning(
                    "Inbox event already being processed. EventId={EventId}, Consumer={ConsumerName}",
                    eventId, consumerName);

                return InboxExecutionDecision.InProgress();
            }

            if (existing.Status == InboxMessageStatus.Failed)
            {
                existing.MarkProcessing();
                await _inboxRepository.UpdateAsync(existing, autoSave: true);

                _logger.LogInformation(
                    "Retrying failed inbox event. EventId={EventId}, Consumer={ConsumerName}, RetryCount={RetryCount}",
                    eventId, consumerName, existing.RetryCount);

                return InboxExecutionDecision.Process(existing.Id);
            }
        }

        var inboxMessage = new InboxMessage(
            Guid.NewGuid(),
            eventId,
            eventName,
            consumerName,
            payloadHash);

        try
        {
            await _inboxRepository.InsertAsync(inboxMessage, autoSave: true);

            _logger.LogInformation(
                "Inbox processing started. EventId={EventId}, Consumer={ConsumerName}",
                eventId, consumerName);

            return InboxExecutionDecision.Process(inboxMessage.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(
                "Inbox unique violation caught. EventId={EventId}, Consumer={ConsumerName}",
                eventId, consumerName);

            var retryQuery = await _inboxRepository.GetQueryableAsync();

            var conflictRow = await retryQuery.FirstOrDefaultAsync(x =>
                x.EventId == eventId &&
                x.ConsumerName == consumerName);

            if (conflictRow == null)
            {
                throw;
            }

            if (conflictRow.Status == InboxMessageStatus.Processed)
            {
                return InboxExecutionDecision.Duplicate();
            }

            if (conflictRow.Status == InboxMessageStatus.Processing)
            {
                return InboxExecutionDecision.InProgress();
            }

            if (conflictRow.Status == InboxMessageStatus.Failed)
            {
                conflictRow.MarkProcessing();
                await _inboxRepository.UpdateAsync(conflictRow, autoSave: true);

                return InboxExecutionDecision.Process(conflictRow.Id);
            }

            return InboxExecutionDecision.InProgress();
        }
    }

    public async Task MarkProcessedAsync(Guid inboxMessageId)
    {
        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);
        inboxMessage.MarkProcessed();
        await _inboxRepository.UpdateAsync(inboxMessage, autoSave: true);
    }

    public async Task MarkFailedAsync(Guid inboxMessageId, string error)
    {
        var inboxMessage = await _inboxRepository.GetAsync(inboxMessageId);
        inboxMessage.MarkFailed(error);
        await _inboxRepository.UpdateAsync(inboxMessage, autoSave: true);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg &&
               pg.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}