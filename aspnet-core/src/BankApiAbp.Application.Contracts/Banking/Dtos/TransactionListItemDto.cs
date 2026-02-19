using System;

namespace BankApiAbp.Banking.Dtos;

public class TransactionListItemDto
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = default!; 

    public Guid? AccountId { get; set; }
    public Guid? DebitCardId { get; set; }
    public Guid? CreditCardId { get; set; }

    public string? Iban { get; set; }
    public string? CardNo { get; set; }
    public string? CustomerName { get; set; }

    public int TxType { get; set; }           
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
}
