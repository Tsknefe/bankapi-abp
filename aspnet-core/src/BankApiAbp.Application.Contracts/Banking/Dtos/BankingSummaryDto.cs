using System.Collections.Generic;

namespace BankApiAbp.Banking.Dtos;

public class BankingSummaryDto
{
    public decimal TotalBalance { get; set; }
    public decimal TotalCreditDebt { get; set; }
    public int AccountsCount { get; set; }
    public int DebitCardsCount { get; set; }
    public int CreditCardsCount { get; set; }

    public List<RecentTransactionDto> RecentTransactions { get; set; } = new();
}
