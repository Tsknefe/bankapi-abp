using System;

public class MoneyTransferredEto
{
    public Guid EventId { get; set; }
    public Guid TransferId { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    public string? IdempotencyKey { get; set; }
    public Guid UserId { get; set; }

    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}