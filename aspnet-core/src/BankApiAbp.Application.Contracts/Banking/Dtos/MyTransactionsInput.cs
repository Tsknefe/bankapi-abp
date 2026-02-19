using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class MyTransactionsInput : PagedAndSortedResultRequestDto
{
    public Guid? AccountId { get; set; }
    public Guid? DebitCardId { get; set; }
    public Guid? CreditCardId { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public string? Filter { get; set; } 
}
