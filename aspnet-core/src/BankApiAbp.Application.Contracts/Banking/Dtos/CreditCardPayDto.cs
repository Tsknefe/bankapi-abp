using System;
using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class CreditCardPayDto
{
    [Required]
    public string CardNo { get; set; }=default!;
    [Required]
    public Guid AccountId { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [StringLength(100)]
    public string? Description { get; set; }
}
