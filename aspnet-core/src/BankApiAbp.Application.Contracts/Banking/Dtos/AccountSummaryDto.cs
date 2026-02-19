using System;

namespace BankApiAbp.Banking.Dtos;

public class AccountSummaryDto
{
    public Guid AccountId { get; set; }
    public decimal Balance { get; set; }

    public DateTime Today { get; set; }
    public int TodayTxCount { get; set; }
    public decimal TodayInTotal { get; set; }
    public decimal TodayOutTotal { get; set; }

    public DateTime MonthStart { get; set; }
    public int MonthTxCount { get; set; }
    public decimal MonthInTotal { get; set; }
    public decimal MonthOutTotal { get; set; }
}
