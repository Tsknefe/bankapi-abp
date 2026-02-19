using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class MyCreditCardsInput : PagedAndSortedResultRequestDto
{
    public Guid? CustomerId { get; set; }
    public string? CardNo { get; set; }
}
