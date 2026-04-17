using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Banking.Messaging;

public class InboxMessage : CreationAuditedEntity<Guid>
{
    public Guid EventId { get; protected set; }

    public string EventName { get; protected set; }

    public string ConsumerName { get; protected set; }

    public string Status { get; protected set; }

    public string? PayloadHash { get; protected set; }

    public DateTime? ProcessedAt { get; protected set; }

    public string? Error { get; protected set; }

    public string? LastErrorCode { get; protected set; }

    public int RetryCount { get; protected set; }

    public int MaxRetryCount { get; protected set; }

    public DateTime? LastAttemptTime { get; protected set; }

    public DateTime? NextRetryTime { get; protected set; }

    public DateTime? DeadLetteredAt { get; protected set; }

    public string? DeadLetterReason { get; protected set; }
  
    public string? PayloadJson { get; protected set; }

    protected InboxMessage()
    {
        EventName = string.Empty;
        ConsumerName = string.Empty;
        Status = InboxMessageStatus.Pending;
    }

    public InboxMessage(
        Guid id,
        Guid eventId,
        string eventName,
        string consumerName,
        string? payloadHash = null,
        string? payloadJson = null,
        int maxRetryCount = 3) : base(id)
    {
        EventId = eventId;
        EventName = Check.NotNullOrWhiteSpace(eventName, nameof(eventName), maxLength: 256);
        ConsumerName = Check.NotNullOrWhiteSpace(consumerName, nameof(consumerName), maxLength: 256);
        PayloadHash = payloadHash;
        PayloadJson = payloadJson;
        MaxRetryCount = maxRetryCount;

        Status = InboxMessageStatus.Pending;
        RetryCount = 0;
    }
    public void MarkPending()
    {
        Status = InboxMessageStatus.Pending;
        Error = null;
        LastErrorCode = null;
        NextRetryTime = null;
        DeadLetterReason = null;
        DeadLetteredAt = null;
    }

    public void MarkProcessing()
    {
        Status = InboxMessageStatus.Processing;
        LastAttemptTime = DateTime.UtcNow;
        Error = null;
        LastErrorCode = null;
    }

    public void MarkProcessed()
    {
        Status = InboxMessageStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
        LastAttemptTime = DateTime.UtcNow;
        Error = null;
        LastErrorCode = null;
        NextRetryTime = null;
        DeadLetterReason = null;
        DeadLetteredAt = null;
    }

    public void MarkRetry(string? error, string? errorCode, TimeSpan delay)
    {
        Status = InboxMessageStatus.Retrying;
        Error = error?.Length > 4000 ? error[..4000] : error;
        LastErrorCode = errorCode;
        RetryCount++;
        LastAttemptTime = DateTime.UtcNow;
        NextRetryTime = DateTime.UtcNow.Add(delay);
    }

    public void MarkFailed(string? error, string? errorCode = null)
    {
        Status = InboxMessageStatus.Failed;
        Error = error?.Length > 4000 ? error[..4000] : error;
        LastErrorCode = errorCode;
        RetryCount++;
        LastAttemptTime = DateTime.UtcNow;
    }

    public void MarkDeadLettered(string? error, string? errorCode = null, string? reason = null)
    {
        Status = InboxMessageStatus.DeadLettered;
        Error = error?.Length > 4000 ? error[..4000] : error;
        LastErrorCode = errorCode;
        DeadLetterReason = reason?.Length > 1000 ? reason[..1000] : reason;
        RetryCount++;
        LastAttemptTime = DateTime.UtcNow;
        DeadLetteredAt = DateTime.UtcNow;
        NextRetryTime = null;
    }

    public void Requeue()
    {
        Status = InboxMessageStatus.Pending;
        Error = null;
        LastErrorCode = null;
        NextRetryTime = null;
        DeadLetterReason = null;
        DeadLetteredAt = null;
    }

    public bool HasRetryQuota()
    {
        return RetryCount < MaxRetryCount;
    }

    public bool IsProcessed()
    {
        return Status == InboxMessageStatus.Processed;
    }

    public bool IsProcessing()
    {
        return Status == InboxMessageStatus.Processing;
    }

    public bool IsDeadLettered()
    {
        return Status == InboxMessageStatus.DeadLettered;
    }
}