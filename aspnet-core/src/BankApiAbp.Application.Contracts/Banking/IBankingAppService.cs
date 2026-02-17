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
    Task<PagedResultDto<TransactionDto>> GetTransactionsAsync(GetTransactionsInput input);

    Task DepositAsync(DepositDto input);
    Task WithdrawAsync(WithdrawDto input);
    Task DebitCardSpendAsync(CardSpendDto input);
    Task CreditCardSpendAsync(CardSpendDto input);
    Task CreditCardPayAsync(CreditCardPayDto input);
    
}
