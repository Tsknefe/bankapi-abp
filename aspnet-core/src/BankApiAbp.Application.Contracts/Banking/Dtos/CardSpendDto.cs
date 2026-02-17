using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class CardSpendDto
{
    [Required]
    public string CardNo { get; set; } = null!;
    [Required]
    [StringLength(4, MinimumLength = 3)]
    public string Cvv { get; set; } = default!;

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
