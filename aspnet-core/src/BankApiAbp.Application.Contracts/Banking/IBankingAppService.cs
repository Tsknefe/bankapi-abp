using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using BankApiAbp.Banking.Dtos;

namespace BankApiAbp.Banking;

public interface IBankingAppService : IApplicationService
{
    Task<IdResponseDto> CreateCustomerAsync(CreateCustomerDto input);
    Task<IdResponseDto> CreateAccountAsync(CreateAccountDto input);
    Task<IdResponseDto> CreateDebitCardAsync(CreateDebitCardDto input);
    Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input);

    Task DepositAsync(DepositDto input);
    Task WithdrawAsync(WithdrawDto input);
    Task DebitCardSpendAsync(CardSpendDto input);
    Task CreditCardSpendAsync(CardSpendDto input);
    Task CreditCardPayAsync(CreditCardPayDto input);
}
