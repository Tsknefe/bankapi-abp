using System;

namespace BankApiAbp.Banking.Messaging;

public class InboxMessageDto
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; }
    public DateTime? LastAttemptTime { get; set; }
    public DateTime? NextRetryTime { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public string? DeadLetterReason { get; set; }
}