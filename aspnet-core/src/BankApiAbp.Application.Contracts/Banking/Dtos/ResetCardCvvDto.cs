using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class ResetCardCvvDto : EntityDto
{
    public string CardNo { get; set; } = default!;
    public string NewCvv { get; set; } = default!;
}
