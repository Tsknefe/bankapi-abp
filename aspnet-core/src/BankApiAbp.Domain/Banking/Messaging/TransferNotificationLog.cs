using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Banking.Messaging;

public class TransferNotificationLog : CreationAuditedEntity<Guid>
{
    public Guid EventId { get; protected set; }
    public Guid TransferId { get; protected set; }
    public Guid UserId { get; protected set; }
    public Guid FromAccountId { get; protected set; }
    public Guid ToAccountId { get; protected set; }
    public decimal Amount { get; protected set; }
    public string? Description { get; protected set; }
    public string? IdempotencyKey { get; protected set; }
    public DateTime OccurredAtUtc { get; protected set; }
    public string Channel { get; protected set; }
    public string Status { get; protected set; }
    public string EventName { get; protected set; }

    protected TransferNotificationLog()
    {
    }

    public TransferNotificationLog(
        Guid id,
        Guid eventId,
        Guid transferId,
        Guid userId,
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        string? description,
        string? idempotencyKey,
        DateTime occurredAtUtc,
        string channel,
        string status,
        string eventName) : base(id)
    {
        EventId = eventId;
        TransferId = transferId;
        UserId = userId;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        Amount = amount;
        Description = description;
        IdempotencyKey = idempotencyKey;
        OccurredAtUtc = occurredAtUtc;
        Channel = channel;
        Status = status;
        EventName = eventName;
    }
}