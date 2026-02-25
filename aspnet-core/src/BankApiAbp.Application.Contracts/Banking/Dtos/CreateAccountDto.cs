using System;
using System.ComponentModel.DataAnnotations;
using BankApiAbp.Banking;

namespace BankApiAbp.Banking.Dtos;

public class CreateAccountDto
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(34)]
    public string Iban { get; set; } = default!;

    public AccountType AccountType { get; set; } = AccountType.Current;

    public decimal InitialBalance { get; set; } = 0;
}
