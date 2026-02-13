using System;
using System.ComponentModel.DataAnnotations;

namespace BankApiAbp.Banking.Dtos;

public class CreateCustomerDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = default!;

    [Required]
    [StringLength(11, MinimumLength = 11)]
    public string TcNo { get; set; } = default!;

    [Required]
    public DateTime BirthDate { get; set; }

    [Required]
    [StringLength(50)]
    public string BirthPlace { get; set; } = default!;
}
