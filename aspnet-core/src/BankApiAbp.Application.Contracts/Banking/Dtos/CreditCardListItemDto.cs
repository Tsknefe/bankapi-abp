using System;

namespace BankApiAbp.Banking.Dtos;

public class CreditCardListItemDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CardNo { get; set; } = default!;
    public DateTime ExpireAt { get; set; }
    public decimal Limit { get; set; }
    public decimal CurrentDebt { get; set; }
    public bool IsActive { get; set; }
}
