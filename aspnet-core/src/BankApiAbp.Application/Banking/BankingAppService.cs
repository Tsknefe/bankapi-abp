using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Cards;
using BankApiAbp.Entities;
using BankApiAbp.Transactions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace BankApiAbp.Banking;

public class BankingAppService : ApplicationService, IBankingAppService
{
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<DebitCard, Guid> _debitCards;
    private readonly IRepository<CreditCard, Guid> _creditCards;
    private readonly IRepository<Transaction, Guid> _tx;

    public BankingAppService(
        IRepository<Customer, Guid> customers,
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

        var userId = CurrentUserIdOrThrow();
        var existing = await _customers.FirstOrDefaultAsync(x => x.TcNo == input.TcNo && x.UserId == userId);

        if (existing != null)
            throw new UserFriendlyException("This customer already exists with TcNo");

        var customer = new Customer(
            GuidGenerator.Create(),
            userId,
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
        var userId = CurrentUserIdOrThrow();

        var customer = await GetCustomerOwnedAsync(input.CustomerId);

        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var ibanExistsForUser = await AsyncExecuter.AnyAsync(
       from a in accountsQ
       join c in customersQ on a.CustomerId equals c.Id
       where c.UserId == userId && a.Iban == input.Iban
       select a.Id
   );

        if (ibanExistsForUser)
            throw new UserFriendlyException("Bu IBAN zaten mevcut ");

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
        var userId = CurrentUserIdOrThrow();

        var cardNo = NormalizeCardNo(input.CardNo);
        var acc = await GetAccountOwnedAsync(input.AccountId);

        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var cardExistsForUser = await AsyncExecuter.AnyAsync(
            from dc in debitCardsQ
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId && dc.CardNo == cardNo
            select dc.Id
        );

        if (cardExistsForUser)
            throw new UserFriendlyException("Bu debit kart numarası zaten mevcut.");

        var card = new DebitCard(
            GuidGenerator.Create(),
            input.AccountId,
            cardNo,
            input.ExpireAt,
            input.Cvv
        );

        await _debitCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }

    public async Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input)
    {
        var userId = CurrentUserIdOrThrow();

        var cardNo = NormalizeCardNo(input.CardNo);
        var cust = await GetCustomerOwnedAsync(input.CustomerId);

        var creditCardsQ = await _creditCards.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var cardExistsForUser = await AsyncExecuter.AnyAsync(
            from cc in creditCardsQ
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId && cc.CardNo == cardNo
            select cc.Id
        );

        if (cardExistsForUser)
            throw new UserFriendlyException("Bu kredi kart numarası zaten mevcut.");

        var card = new CreditCard(
            GuidGenerator.Create(),
            input.CustomerId,
            cardNo,
            input.ExpireAt,
            input.Cvv,
            input.Limit
        );

        await _creditCards.InsertAsync(card, autoSave: true);
        return new IdResponseDto { Id = card.Id };
    }


    public async Task DepositAsync(DepositDto input)
    {
        var account = await GetAccountOwnedAsync(input.AccountId);
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
        var account = await GetAccountOwnedAsync(input.AccountId);
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
        var cardNo = NormalizeCardNo(input.CardNo);
        var card = await GetDebitCardOwnedByCardNoAsync(cardNo);
        if (card == null) throw new EntityNotFoundException(typeof(DebitCard), input.CardNo);

        /* if (!card.IsActive)
             throw new UserFriendlyException("Card is not active.");*/

        var now = Clock.Now;
        card.EnsureUsable(now);
        card.VerifyCvv(input.Cvv);

        var account = await GetAccountOwnedAsync(card.AccountId);

        var start = now.Date;
        var end = start.AddDays(1);

        var spentToday = await AsyncExecuter.SumAsync(
            (await _tx.GetQueryableAsync())
            .Where(t => t.DebitCardId == card.Id
            && t.TxType == TransactionType.DebitCardSpend
            && t.CreationTime >= start
            && t.CreationTime < end),
            t => (decimal?)t.Amount) ?? 0m;

        if (spentToday + input.Amount > card.DailyLimit)
        {
            throw new UserFriendlyException(
                $"Daily Limit exceeded. Limit={card.DailyLimit}, SpentToday={spentToday}, Amount={input.Amount}");
        }

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
        var cardNo = NormalizeCardNo(input.CardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);
        if (card == null)
            throw new UserFriendlyException("Credit card not found.");

        var now = Clock.Now;
        card.EnsureUsable(now);
        card.VerifyCvv(input.Cvv);

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

        var cardNo = NormalizeCardNo(input.CardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);
        if (card is null)
            throw new BusinessException("CreditCardNotFound")
                .WithData("CardNo", input.CardNo);

        var now = Clock.Now;
        card.EnsureUsable(now);
        card.VerifyCvv(input.Cvv);

        var account = await GetAccountOwnedAsync(input.AccountId);

        if (!account.IsActive)
            throw new UserFriendlyException("Hesap aktif değil.");

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
    public async Task<AccountDto> GetAccountAsync(Guid id)
    {
        var acc = await GetAccountOwnedAsync(id);

        return new AccountDto
        {
            Id = acc.Id,
            CustomerId = acc.CustomerId,
            Name = acc.Name,
            Iban = acc.Iban,
            Balance = acc.Balance,
            AccountType = acc.AccountType,
            IsActive = acc.IsActive,
        };
    }
    public async Task<CreditCardDto> GetCreditCardDto(string cardNo)
    {

        cardNo = NormalizeCardNo(cardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

        if (card == null)
            throw new UserFriendlyException("Credit card not found");
        return new CreditCardDto
        {
            Id = card.Id,
            CustomerId = card.CustomerId,
            CardNo = card.CardNo,
            ExpireAt = card.ExpireAt,
            Limit = card.Limit,
            CurrentDebt = card.CurrentDebt,
            IsActive = card.IsActive
        };
    }
    public async Task<PagedResultDto<TransactionDto>> GetTransactionsAsync(GetTransactionsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var txQ = await _tx.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();
        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var creditCardsQ = await _creditCards.GetQueryableAsync();

        var baseQ =
            from t in txQ

            join a in accountsQ on t.AccountId equals a.Id into aJoin
            from a in aJoin.DefaultIfEmpty()
            join cA in customersQ on a.CustomerId equals cA.Id into cAJoin
            from cA in cAJoin.DefaultIfEmpty()

            join dc in debitCardsQ on t.DebitCardId equals dc.Id into dcJoin
            from dc in dcJoin.DefaultIfEmpty()
            join aDc in accountsQ on dc.AccountId equals aDc.Id into aDcJoin
            from aDc in aDcJoin.DefaultIfEmpty()
            join cDc in customersQ on aDc.CustomerId equals cDc.Id into cDcJoin
            from cDc in cDcJoin.DefaultIfEmpty()

            join cc in creditCardsQ on t.CreditCardId equals cc.Id into ccJoin
            from cc in ccJoin.DefaultIfEmpty()
            join cC in customersQ on cc.CustomerId equals cC.Id into cCJoin
            from cC in cCJoin.DefaultIfEmpty()

            let ownerUserId =
                t.AccountId != null ? cA.UserId :
                t.DebitCardId != null ? cDc.UserId :
                cC.UserId

            where ownerUserId == userId
            select t;

        var q = baseQ;

        if (input.AccountId.HasValue)
            q = q.Where(x => x.AccountId == input.AccountId);

        if (input.DebitCardId.HasValue)
            q = q.Where(x => x.DebitCardId == input.DebitCardId);

        if (input.CreditCardId.HasValue)
            q = q.Where(x => x.CreditCardId == input.CreditCardId);

        if (input.From.HasValue)
            q = q.Where(x => x.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(x => x.CreationTime <= input.To.Value);

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderByDescending(x => x.CreationTime);

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TransactionDto>(
            total,
            items.Select(t => new TransactionDto
            {
                Id = t.Id,
                TxType = t.TxType,
                Amount = t.Amount,
                Description = t.Description,
                CreationTime = t.CreationTime,
                AccountId = t.AccountId,
                DebitCardId = t.DebitCardId,
                CreditCardId = t.CreditCardId
            }).ToList());
    }

    private static string NormalizeCardNo(string? cardNo)
    {
        cardNo = (cardNo ?? "").Trim();
        if (cardNo.Length != 16 || !cardNo.All(char.IsDigit))
            throw new UserFriendlyException("CardNo 16 haneli ve sadece rakamlardan oluşmalı.");
        return cardNo;
    }

    private Guid CurrentUserIdOrThrow()
    {
        if (!CurrentUser.IsAuthenticated)
            throw new AbpAuthorizationException("Not authenticated.");
        return CurrentUser.GetId();
    }

    private async Task<Customer> GetCustomerOwnedAsync(Guid customerId)
    {
        var userId = CurrentUserIdOrThrow();
        var cust = await _customers.FindAsync(customerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu müşteriye erişimin yok.");

        return cust;
    }

    private async Task<Account> GetAccountOwnedAsync(Guid accountId)
    {
        var userId = CurrentUserIdOrThrow();

        var acc = await _accounts.FindAsync(accountId);
        if (acc == null) throw new UserFriendlyException("Hesap bulunamadı.");

        var cust = await _customers.FindAsync(acc.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu hesaba erişimin yok.");

        return acc;
    }

    private async Task<DebitCard> GetDebitCardOwnedByCardNoAsync(string cardNo)
    {
        var userId = CurrentUserIdOrThrow();

        var card = await _debitCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Debit card not found.");

        var acc = await _accounts.FindAsync(card.AccountId);
        if (acc == null) throw new UserFriendlyException("Hesap bulunamadı.");

        var cust = await _customers.FindAsync(acc.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu karta erişimin yok.");

        return card;
    }
    private async Task<CreditCard> GetCreditCardOwnedByCardNoAsync(string cardNo)
    {
        var userId = CurrentUserIdOrThrow();

        var card = await _creditCards.FirstOrDefaultAsync(x => x.CardNo == cardNo);
        if (card == null) throw new UserFriendlyException("Credit card not found.");

        var cust = await _customers.FindAsync(card.CustomerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu kredi kartına erişimin yok.");

        return card;
    }


}
