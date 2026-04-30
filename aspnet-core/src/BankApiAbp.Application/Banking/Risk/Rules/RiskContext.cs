using System;

public class RiskContext
{
    public Guid UserId { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Now { get; set; }
}