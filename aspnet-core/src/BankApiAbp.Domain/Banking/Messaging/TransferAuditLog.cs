using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Banking.Messaging;

public class TransferAuditLog : CreationAuditedEntity<Guid>
{
    public Guid EventId { get; protected set; }
    public Guid TransferId { get; protected set; }
    public Guid FromAccountId { get; protected set; }
    public Guid ToAccountId { get; protected set; }
    public Guid UserId { get; protected set; }
    public decimal Amount { get; protected set; }
    public string? Description { get; protected set; }
    public string? IdempotencyKey { get; protected set; }
    public DateTime OccurredAtUtc { get; protected set; }
    public string EventName { get; protected set; }

    protected TransferAuditLog()
    {
    }

    public TransferAuditLog(
        Guid id,
        Guid eventId,
        Guid transferId,
        Guid fromAccountId,
        Guid toAccountId,
        Guid userId,
        decimal amount,
        string? description,
        string? idempotencyKey,
        DateTime occurredAtUtc,
        string eventName) : base(id)
    {
        EventId = eventId;
        TransferId = transferId;
        FromAccountId = fromAccountId;
        ToAccountId = toAccountId;
        UserId = userId;
        Amount = amount;
        Description = description;
        IdempotencyKey = idempotencyKey;
        OccurredAtUtc = occurredAtUtc;
        EventName = eventName;
    }
}