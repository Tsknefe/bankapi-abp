using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class MyDebitCardsInput : PagedAndSortedResultRequestDto
{
    public Guid? AccountId { get; set; }
    public string? CardNo { get; set; }
}
