using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class GetAccountStatementInput : PagedAndSortedResultRequestDto
{
    public Guid AccountId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

}
