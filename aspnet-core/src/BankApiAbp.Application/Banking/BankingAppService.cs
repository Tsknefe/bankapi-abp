using System;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Cards;
using BankApiAbp.Entities;
using BankApiAbp.Transactions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public class BankingAppService : ApplicationService, IBankingAppService
{
    private readonly IRepository<Customer,Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<DebitCard, Guid> _debitCards;
    private readonly IRepository<CreditCard, Guid> _creditCards;
    private readonly IRepository<Transaction, Guid> _tx;

    public BankingAppService(
        IRepository<Customer,Guid> customers,
        IRepository<Account, Guid> accounts,
        IRepository<DebitCard, Guid> debitCards,
        IRepository<CreditCard, Guid> creditCards,
        IRepository<Transaction, Guid> tx)
    {
        _customers = customers;
        _accounts = accounts;
        _debitCards = debitCards;
        _creditCards = creditCards;
        _tx = tx;
    }
    public async Task<IdResponseDto> CreateCustomerAsync(CreateCustomerDto input)
    {
        var existing = await _customers.FirstOrDefaultAsync(x => x.TcNo == input.TcNo);
        if (existing != null)
            throw new UserFriendlyException("This customer already exists with TcNo");

        var customer = new Customer(
            GuidGenerator.Create(),
            input.Name,
            input.TcNo,
            input.BirthDate,
            input.BirthPlace
        );

        await _customers.InsertAsync(customer, autoSave: true);
        return new IdResponseDto { Id = customer.Id };
    }

    public async Task<IdResponseDto> CreateAccountAsync(CreateAccountDto input)
    {
        var customer = await _customers.FindAsync(input.CustomerId);
        if (customer == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        var existingIban = await _accounts.FirstOrDefaultAsync(x => x.Iban == input.Iban);
        if (existingIban != null)
            throw new UserFriendlyException("Bu IBAN zaten var.");

        var account = new Account(
            GuidGenerator.Create(),
            input.CustomerId,
            input.Name,
            input.Iban,
            input.AccountType,
            input.InitialBalance
        );
        await _accounts.InsertAsync(account, autoSave: true);
        return new IdResponseDto { Id = account.Id };
    }
    public async Task<IdResponseDto> CreateDebitCardAsync(CreateDebitCardDto input)
    {
        var acc = await _accounts.FindAsync(input.AccountId);
        if (acc == null) throw new UserFriendlyException("Hesap bulunamadı.");

        var existing = await _debitCards.FirstOrDefaultAsync(x => x.CardNo == input.CardNo);
        if (existing != null)
            throw new UserFriendlyException("Bu kart numarası zaten var.");

        var card = new DebitCard(
            GuidGenerator.Create(),
            input.AccountId,
            input.CardNo,
            input.ExpireAt,
            input.Cvv
        );

        await _debitCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }
    public async Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input)
    {
        var cust = await _customers.FindAsync(input.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        var existing = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == input.CardNo);
        if (existing != null)
            throw new UserFriendlyException("Bu kart numarası zaten var.");

        var card = new CreditCard(
            GuidGenerator.Create(),
            input.CustomerId,
            input.CardNo,
            input.ExpireAt,
            input.Cvv,
            input.Limit
        );

        await _creditCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }



    public async Task DepositAsync(DepositDto input)
    {
        var account = await _accounts.GetAsync(input.AccountId);
        account.Deposit(input.Amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.Deposit,
            input.Amount,
            input.Description,
            input.AccountId,
            null,
            null
        ), autoSave: true);
    }

    public async Task WithdrawAsync(WithdrawDto input)
    {
        var account = await _accounts.GetAsync(input.AccountId);
        account.Withdraw(input.Amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.Withdraw,
            input.Amount,
            input.Description,
            input.AccountId,
            null,
            null
        ), autoSave: true);
    }

    public async Task DebitCardSpendAsync(CardSpendDto input)
    {
        var card = await _debitCards.FirstOrDefaultAsync(x => x.CardNo == input.CardNo);
        if (card == null) throw new EntityNotFoundException(typeof(DebitCard), input.CardNo);

        if (!card.IsActive)
            throw new UserFriendlyException("Card is not active.");

        var account = await _accounts.GetAsync(card.AccountId);
        account.Withdraw(input.Amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.DebitCardSpend,
            input.Amount,
            input.Description,
            null,
            card.Id,
            null
        ), autoSave: true);
    }

    public async Task CreditCardSpendAsync(CardSpendDto input)
    {
        var card = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == input.CardNo);
        if (card == null) throw new UserFriendlyException("Credit card not found.");

        card.Spend(input.Amount);

        await _creditCards.UpdateAsync(card, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.CreditCardSpend,
            input.Amount,
            input.Description,
            null,
            null,
            card.Id
        ), autoSave: true);
    }
    public async Task CreditCardPayAsync(CreditCardPayDto input)
    {
        if (input.Amount <= 0)
            throw new BusinessException("Amount must be greater than zero");

        var card = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == input.CardNo);
        if (card is null)
            throw new BusinessException("CreditCardNotFound").WithData("CardNo", input.CardNo);

        var account = await _accounts.GetAsync(input.AccountId);

        if (account.Balance < input.Amount)
            throw new BusinessException("InsufficientBalance")
                .WithData("Balance", account.Balance)
                .WithData("Amount", input.Amount);

        if (card.CurrentDebt < input.Amount)
            throw new BusinessException("PaymentExceedsDebt")
                .WithData("CurrentDebt", card.CurrentDebt)
                .WithData("Amount", input.Amount);

        account.Withdraw(input.Amount);

        card.Pay(input.Amount);

        await _accounts.UpdateAsync(account, autoSave: true);
        await _creditCards.UpdateAsync(card, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.CreditCardPayment,
            input.Amount,
            input.Description,
            account.Id,   
            null,
            card.Id
        ), autoSave: true);
    }

}
