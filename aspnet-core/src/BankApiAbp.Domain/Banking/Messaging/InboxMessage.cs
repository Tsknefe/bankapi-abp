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

    public int RetryCount { get; protected set; }

    public DateTime? LastAttemptTime { get; protected set; }

    protected InboxMessage()
    {
    }

    public InboxMessage(
        Guid id,
        Guid eventId,
        string eventName,
        string consumerName,
        string? payloadHash = null) : base(id)
    {
        EventId = eventId;
        EventName = Check.NotNullOrWhiteSpace(eventName, nameof(eventName), maxLength: 256);
        ConsumerName = Check.NotNullOrWhiteSpace(consumerName, nameof(consumerName), maxLength: 256);
        PayloadHash = payloadHash;
        Status = InboxMessageStatus.Processing;
        RetryCount = 0;
        LastAttemptTime = DateTime.UtcNow;
    }

    public void MarkProcessing()
    {
        Status = InboxMessageStatus.Processing;
        LastAttemptTime = DateTime.UtcNow;
        Error = null;
    }

    public void MarkProcessed()
    {
        Status = InboxMessageStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
        LastAttemptTime = DateTime.UtcNow;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Status = InboxMessageStatus.Failed;
        Error = error?.Length > 4000 ? error[..4000] : error;
        RetryCount++;
        LastAttemptTime = DateTime.UtcNow;
    }
}