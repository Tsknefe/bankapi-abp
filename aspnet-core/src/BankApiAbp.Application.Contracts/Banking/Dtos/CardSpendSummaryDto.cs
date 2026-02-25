using System;

namespace BankApiAbp.Banking.Dtos;

public class CardSpendSummaryDto
{
    public string CardNo { get; set; } = default!;
    public DateTime Today { get; set; }
    public decimal TodaySpend { get; set; }
    public DateTime MonthStart { get; set; }
    public decimal MonthSpend { get; set; }
}
