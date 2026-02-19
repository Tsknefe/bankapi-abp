using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using BankApiAbp.Banking.Dtos;
using System;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking;

public interface IBankingAppService : IApplicationService
{
    Task<IdResponseDto> CreateCustomerAsync(CreateCustomerDto input);
    Task<IdResponseDto> CreateAccountAsync(CreateAccountDto input);
    Task<IdResponseDto> CreateDebitCardAsync(CreateDebitCardDto input);
    Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input);
    Task<AccountDto> GetAccountAsync(Guid id);
    Task<CreditCardDto> GetCreditCardDto(string cardNo);

    Task DepositAsync(DepositDto input);
    Task WithdrawAsync(WithdrawDto input);
    Task DebitCardSpendAsync(CardSpendDto input);
    Task CreditCardSpendAsync(CardSpendDto input);
    Task CreditCardPayAsync(CreditCardPayDto input);
    Task<AccountSummaryDto> GetAccountSummaryAsync(Guid accountId);
    Task<PagedResultDto<TransactionDto>> GetAccountStatementAsync(GetAccountStatementInput input);
    Task<CardSpendSummaryDto> GetDebitCardSpendSummaryAsync(string cardNo);
    Task<CardSpendSummaryDto> GetCreditCardSpendSummaryAsync(string cardNo);
    Task<PagedResultDto<CustomerListItemDto>> GetMyCustomersAsync(CustomerListInput input);
    Task<PagedResultDto<AccountListItemDto>> GetMyAccountsAsync(MyAccountsInput input);
    Task<PagedResultDto<DebitCardListItemDto>> GetMyDebitCardsAsync(MyDebitCardsInput input);
    Task<PagedResultDto<CreditCardListItemDto>> GetMyCreditCardsAsync(MyCreditCardsInput input);
    Task<PagedResultDto<TransactionListItemDto>> GetMyTransactionsAsync(MyTransactionsInput input);
    Task<BankingSummaryDto> GetMySummaryAsync(int lastTxCount = 10);

}
