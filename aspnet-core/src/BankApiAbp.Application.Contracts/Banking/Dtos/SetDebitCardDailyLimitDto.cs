using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos;

public class SetDebitCardDailyLimitDto : EntityDto
{
    public string CardNo { get; set; } = default!;
    public decimal DailyLimit { get; set; }
}
