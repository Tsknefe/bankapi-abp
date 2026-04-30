using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp;
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
        using var activity = InboxTracing.ActivitySource.StartActivity("Inbox.TryBeginProcessing");

        activity?.SetTag("event.id", eventId);
        activity?.SetTag("event.name", eventName);
        activity?.SetTag("consumer.name", consumerName);

        var computedPayloadHash = !string.IsNullOrWhiteSpace(payloadJson)
            ? ComputeHash(payloadJson)
            : payloadHash;

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var existing = await _inboxRepository.FirstOrDefaultAsync(x =>
            x.EventId == eventId &&
            x.ConsumerName == consumerName);

        if (existing != null)
        {
            ValidatePayloadHash(existing, computedPayloadHash, eventId, consumerName);

            activity?.SetTag("inbox.message.id", existing.Id);
            activity?.SetTag("status", existing.Status);
            activity?.SetTag("retry.count", existing.RetryCount);

            if (existing.Status == InboxMessageStatus.Processed)
            {
                activity?.SetTag("decision", "duplicate");
                await uow.CompleteAsync();
                return InboxExecutionDecision.Duplicate();
            }

            if (existing.Status == InboxMessageStatus.Processing)
            {
                activity?.SetTag("decision", "in_progress");
                await uow.CompleteAsync();
                return InboxExecutionDecision.InProgress();
            }

            if (existing.Status == InboxMessageStatus.DeadLettered)
            {
                activity?.SetTag("decision", "dead_lettered");
                await uow.CompleteAsync();
                return InboxExecutionDecision.DeadLettered();
            }

            existing.MarkProcessing();
            await _inboxRepository.UpdateAsync(existing, autoSave: true);

            activity?.SetTag("decision", "process_existing");

            await uow.CompleteAsync();
            return InboxExecutionDecision.Process(existing.Id);
        }

        var msg = new InboxMessage(
            Guid.NewGuid(),
            eventId,
            eventName,
            consumerName,
            computedPayloadHash,
            payloadJson,
            maxRetryCount);

        msg.MarkProcessing();

        try
        {
            await _inboxRepository.InsertAsync(msg, autoSave: true);

            activity?.SetTag("decision", "process_new");
            activity?.SetTag("inbox.message.id", msg.Id);

            await uow.CompleteAsync();
            return InboxExecutionDecision.Process(msg.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            activity?.SetTag("unique_violation", true);

            var conflict = await _inboxRepository.FirstOrDefaultAsync(x =>
                x.EventId == eventId &&
                x.ConsumerName == consumerName);

            if (conflict == null)
                throw;

            ValidatePayloadHash(conflict, computedPayloadHash, eventId, consumerName);

            activity?.SetTag("inbox.message.id", conflict.Id);
            activity?.SetTag("status", conflict.Status);

            if (conflict.Status == InboxMessageStatus.Processed)
            {
                await uow.CompleteAsync();
                return InboxExecutionDecision.Duplicate();
            }

            if (conflict.Status == InboxMessageStatus.Processing)
            {
                await uow.CompleteAsync();
                return InboxExecutionDecision.InProgress();
            }

            if (conflict.Status == InboxMessageStatus.DeadLettered)
            {
                await uow.CompleteAsync();
                return InboxExecutionDecision.DeadLettered();
            }

            conflict.MarkProcessing();
            await _inboxRepository.UpdateAsync(conflict, autoSave: true);

            await uow.CompleteAsync();
            return InboxExecutionDecision.Process(conflict.Id);
        }
    }

    public async Task MarkProcessedAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("Inbox.MarkProcessed");

        activity?.SetTag("inbox.message.id", inboxMessageId);

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var msg = await _inboxRepository.GetAsync(inboxMessageId);
        msg.MarkProcessed();

        await _inboxRepository.UpdateAsync(msg, autoSave: true);
        await uow.CompleteAsync();
    }

    public async Task MarkFailedAsync(Guid inboxMessageId, string error, string? errorCode = null)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("Inbox.MarkFailed");

        activity?.SetTag("inbox.message.id", inboxMessageId);
        activity?.SetTag("error", error);

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var msg = await _inboxRepository.GetAsync(inboxMessageId);

        if (msg.HasRetryQuota())
        {
            var delay = InboxRetryPolicy.GetDelay(msg.RetryCount + 1);

            msg.MarkRetry(error, errorCode, delay);
            activity?.SetTag("result", "retry");
        }
        else
        {
            msg.MarkDeadLettered(error, errorCode, "Max retry exceeded");
            activity?.SetTag("result", "dead_lettered");
        }

        await _inboxRepository.UpdateAsync(msg, autoSave: true);
        await uow.CompleteAsync();
    }

    public async Task RequeueAsync(Guid inboxMessageId)
    {
        using var activity = InboxTracing.ActivitySource.StartActivity("Inbox.Requeue");

        activity?.SetTag("inbox.message.id", inboxMessageId);

        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var msg = await _inboxRepository.GetAsync(inboxMessageId);
        msg.Requeue();

        await _inboxRepository.UpdateAsync(msg, autoSave: true);
        await uow.CompleteAsync();
    }

    private void ValidatePayloadHash(
        InboxMessage existing,
        string? incomingHash,
        Guid eventId,
        string consumerName)
    {
        if (string.IsNullOrWhiteSpace(incomingHash))
            return;

        if (string.IsNullOrWhiteSpace(existing.PayloadHash))
            return;

        if (existing.PayloadHash == incomingHash)
            return;

        _logger.LogError(
            "INBOX PAYLOAD MISMATCH! EventId={EventId}, Consumer={Consumer}, ExistingHash={ExistingHash}, IncomingHash={IncomingHash}",
            eventId,
            consumerName,
            existing.PayloadHash,
            incomingHash);

        throw new BusinessException("INBOX_PAYLOAD_MISMATCH")
            .WithData("EventId", eventId)
            .WithData("Consumer", consumerName);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }

        return ex.InnerException?.Message.Contains(
            "UNIQUE constraint failed",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ComputeHash(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}