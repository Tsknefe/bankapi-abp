using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class MyAccountsInput : PagedAndSortedResultRequestDto
{
    public Guid? CustomerId { get; set; }
    public string? Filter { get; set; } 
}
