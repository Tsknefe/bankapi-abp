using System;
using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class WithdrawDto
{
    [Required]
    public Guid AccountId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
