using System;

namespace BankApiAbp.Banking.Dtos;

public class AccountListItemDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = default!;
    public string Iban { get; set; } = default!;
    public decimal Balance { get; set; }
    public AccountType AccountType { get; set; }
    public bool IsActive { get; set; }
}
