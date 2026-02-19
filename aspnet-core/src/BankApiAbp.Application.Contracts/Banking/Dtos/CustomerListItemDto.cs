using System;

namespace BankApiAbp.Banking.Dtos;

public class CustomerListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string TcNo { get; set; } = default!;
    public string BirthPlace { get; set; } = default!;
}
