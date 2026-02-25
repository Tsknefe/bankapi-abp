using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class CustomerListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
}
