using System;
using System.Threading.Tasks;
using BankApiAbp.Accounts;
using BankApiAbp.Cards;
using BankApiAbp.Transactions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public class BankingAppService : ApplicationService
{
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<DebitCard, Guid> _debitCards;
    private readonly IRepository<CreditCard, Guid> _creditCards;
    private readonly IRepository<Transaction, Guid> _tx;

    public BankingAppService(
        IRepository<Account, Guid> accounts,
        IRepository<DebitCard, Guid> debitCards,
        IRepository<CreditCard, Guid> creditCards,
        IRepository<Transaction, Guid> tx)
    {
        _accounts = accounts;
        _debitCards = debitCards;
        _creditCards = creditCards;
        _tx = tx;
    }

    public async Task DepositAsync(Guid accountId, decimal amount, string? description = null)
    {
        var account = await _accounts.GetAsync(accountId);
        account.Deposit(amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.Deposit,
            amount,
            description,
            accountId,
            null,
            null
        ), autoSave: true);
    }

    public async Task WithdrawAsync(Guid accountId, decimal amount, string? description = null)
    {
        var account = await _accounts.GetAsync(accountId);
        account.Withdraw(amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.Withdraw,
            amount,
            description,
            accountId,
            null,
            null
        ), autoSave: true);
    }

    public async Task SpendWithDebitAsync(string cardNo, decimal amount, string? description = null)
    {
        var card = await _debitCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Debit card not found.");

        card.EnsureUsable();

        var account = await _accounts.GetAsync(card.AccountId);
        account.SpendFromAccount(amount);

        await _accounts.UpdateAsync(account, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.DebitSpend,
            amount,
            description,
            null,
            card.Id,
            null
        ), autoSave: true);
    }

    public async Task SpendWithCreditAsync(string cardNo, decimal amount, string? description = null)
    {
        var card = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Credit card not found.");

        card.Spend(amount);

        await _creditCards.UpdateAsync(card, autoSave: true);

        await _tx.InsertAsync(new Transaction(
            GuidGenerator.Create(),
            TransactionType.CreditSpend,
            amount,
            description,
            null,
            null,
            card.Id
        ), autoSave: true);
    }
}
