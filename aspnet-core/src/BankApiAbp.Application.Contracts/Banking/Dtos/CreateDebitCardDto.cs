using System;
using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class CreateDebitCardDto
{
    [Required]
    public Guid AccountId { get; set; }

    [Required]
    [StringLength(16, MinimumLength = 16)]
    public string CardNo { get; set; } = default!;

    [Required]
    public DateTime ExpireAt { get; set; }

    [Required]
    [StringLength(4)]
    public string Cvv { get; set; } = default!;
}
