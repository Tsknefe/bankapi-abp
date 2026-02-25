using System;

namespace BankApiAbp.Banking.Dtos;

public class DebitCardListItemDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string CardNo { get; set; } = default!;
    public DateTime ExpireAt { get; set; }
    public decimal DailyLimit { get; set; }
    public bool IsActive { get; set; }
}
