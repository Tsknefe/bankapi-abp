using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class CardSpendDto
{
    [Required]
    public string CardNo { get; set; } = null!;

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
