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
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
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

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
    }


    public async Task DebitCardSpendAsync(CardSpendDto input)
    {
        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetDebitCardOwnedByCardNoAsync(cardNo);

                var now = Clock.Now;
                card.EnsureUsable(now);
                card.VerifyCvv(input.Cvv);

                var account = await GetAccountOwnedAsync(card.AccountId);

                var start = now.Date;
                var end = start.AddDays(1);

                var txQ = await _tx.GetQueryableAsync();

                var spentToday = await AsyncExecuter.SumAsync(
                    txQ.Where(t => t.DebitCardId == card.Id
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

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
    }


    public async Task CreditCardSpendAsync(CardSpendDto input)
    {
        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

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

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
    }

    public async Task CreditCardPayAsync(CreditCardPayDto input)
    {
        if (input.Amount <= 0)
            throw new BusinessException("Amount must be greater than zero");

        var cardNo = NormalizeCardNo(input.CardNo);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var card = await GetCreditCardOwnedByCardNoAsync(cardNo);
                var now = Clock.Now;

                card.EnsureUsable(now);
                card.VerifyCvv(input.Cvv);

                var account = await GetAccountOwnedAsync(input.AccountId);

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

                return;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                if (attempt == 3) throw ConcurrencyFriendly();
                await SmallBackoffAsync(attempt);
            }
        }
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
    private static UserFriendlyException ConcurrencyFriendly()
    => new UserFriendlyException("İşlem aynı anda başka bir istekle çakıştı. Lütfen tekrar deneyin.");

    private static bool IsConcurrency(Exception ex)
     => ex is Volo.Abp.Data.AbpDbConcurrencyException;



    private static Task SmallBackoffAsync(int attempt)
    {
        var ms = attempt switch { 1 => 20, 2 => 40, _ => 80 };
        return Task.Delay(ms);
    }

    public async Task<AccountSummaryDto> GetAccountSummaryAsync(Guid accountId)
    {
        var acc = await GetAccountOwnedAsync(accountId);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var qAcc = txQ.Where(t => t.AccountId == acc.Id);

        var todayQ = qAcc.Where(t => t.CreationTime >= todayStart && t.CreationTime < tomorrow);
        var todayCount = await AsyncExecuter.CountAsync(todayQ);

        var todayIn = await AsyncExecuter.SumAsync(
            todayQ.Where(t => t.TxType == TransactionType.Deposit),
            t => (decimal?)t.Amount) ?? 0m;

        var todayOut = await AsyncExecuter.SumAsync(
            todayQ.Where(t => t.TxType == TransactionType.Withdraw),
            t => (decimal?)t.Amount) ?? 0m;

        var monthQ = qAcc.Where(t => t.CreationTime >= monthStart && t.CreationTime < nextMonth);
        var monthCount = await AsyncExecuter.CountAsync(monthQ);

        var monthIn = await AsyncExecuter.SumAsync(
            monthQ.Where(t => t.TxType == TransactionType.Deposit),
            t => (decimal?)t.Amount) ?? 0m;

        var monthOut = await AsyncExecuter.SumAsync(
            monthQ.Where(t => t.TxType == TransactionType.Withdraw),
            t => (decimal?)t.Amount) ?? 0m;

        return new AccountSummaryDto
        {
            AccountId = acc.Id,
            Balance = acc.Balance,

            Today = todayStart,
            TodayTxCount = todayCount,
            TodayInTotal = todayIn,
            TodayOutTotal = todayOut,

            MonthStart = monthStart,
            MonthTxCount = monthCount,
            MonthInTotal = monthIn,
            MonthOutTotal = monthOut
        };
    }
    public async Task<PagedResultDto<TransactionDto>> GetAccountStatementAsync(GetAccountStatementInput input)
    {
        _ = await GetAccountOwnedAsync(input.AccountId);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.AccountId == input.AccountId);

        if (input.From.HasValue)
            q = q.Where(t => t.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(t => t.CreationTime <= input.To.Value);

        q = q.OrderByDescending(t => t.CreationTime);

        var total = await AsyncExecuter.CountAsync(q);
        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

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
            }).ToList()
        );
    }
    public async Task<CardSpendSummaryDto> GetDebitCardSpendSummaryAsync(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);
        var card = await GetDebitCardOwnedByCardNoAsync(cardNo);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.DebitCardId == card.Id && t.TxType == TransactionType.DebitCardSpend);

        var todaySpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= todayStart && t.CreationTime < tomorrow),
            t => (decimal?)t.Amount) ?? 0m;

        var monthSpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= monthStart && t.CreationTime < nextMonth),
            t => (decimal?)t.Amount) ?? 0m;

        return new CardSpendSummaryDto
        {
            CardNo = card.CardNo,
            Today = todayStart,
            TodaySpend = todaySpend,
            MonthStart = monthStart,
            MonthSpend = monthSpend
        };
    }
    public async Task<CardSpendSummaryDto> GetCreditCardSpendSummaryAsync(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

        var now = Clock.Now;
        var todayStart = now.Date;
        var tomorrow = todayStart.AddDays(1);

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var txQ = await _tx.GetQueryableAsync();

        var q = txQ.Where(t => t.CreditCardId == card.Id && t.TxType == TransactionType.CreditCardSpend);

        var todaySpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= todayStart && t.CreationTime < tomorrow),
            t => (decimal?)t.Amount) ?? 0m;

        var monthSpend = await AsyncExecuter.SumAsync(
            q.Where(t => t.CreationTime >= monthStart && t.CreationTime < nextMonth),
            t => (decimal?)t.Amount) ?? 0m;

        return new CardSpendSummaryDto
        {
            CardNo = card.CardNo,
            Today = todayStart,
            TodaySpend = todaySpend,
            MonthStart = monthStart,
            MonthSpend = monthSpend
        };
    }
    public async Task<PagedResultDto<CustomerListItemDto>> GetMyCustomersAsync(CustomerListInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var q = (await _customers.GetQueryableAsync())
            .Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(x =>
                x.Name.Contains(f) ||
                x.TcNo.Contains(f) ||
                x.BirthPlace.Contains(f));
        }

        var total = await AsyncExecuter.CountAsync(q);

        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "Name" : input.Sorting;

        q = q.OrderBy(x=>x.Name);

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<CustomerListItemDto>(
            total,
            items.Select(c => new CustomerListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                TcNo = c.TcNo,
                BirthPlace = c.BirthPlace
            }).ToList()
        );
    }
    public async Task<PagedResultDto<AccountListItemDto>> GetMyAccountsAsync(MyAccountsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from a in accountsQ
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select a;

        if (input.CustomerId.HasValue)
            q = q.Where(a => a.CustomerId == input.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(a => a.Name.Contains(f) || a.Iban.Contains(f));
        }

        var total = await AsyncExecuter.CountAsync(q);

        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "Name" : input.Sorting;
        q = q.OrderBy(x=>x.Name);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<AccountListItemDto>(
            total,
            items.Select(a => new AccountListItemDto
            {
                Id = a.Id,
                CustomerId = a.CustomerId,
                Name = a.Name,
                Iban = a.Iban,
                Balance = a.Balance,
                AccountType = a.AccountType,
                IsActive = a.IsActive
            }).ToList()
        );
    }
    public async Task<PagedResultDto<DebitCardListItemDto>> GetMyDebitCardsAsync(MyDebitCardsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from dc in debitCardsQ
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select dc;

        if (input.AccountId.HasValue)
            q = q.Where(dc => dc.AccountId == input.AccountId.Value);

        if (!string.IsNullOrWhiteSpace(input.CardNo))
        {
            var cardNo = NormalizeCardNo(input.CardNo);
            q = q.Where(dc => dc.CardNo == cardNo);
        }

        var total = await AsyncExecuter.CountAsync(q);

        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "CardNo" : input.Sorting;
        q = q.OrderBy(x => x.CardNo);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<DebitCardListItemDto>(
            total,
            items.Select(dc => new DebitCardListItemDto
            {
                Id = dc.Id,
                AccountId = dc.AccountId,
                CardNo = dc.CardNo,
                ExpireAt = dc.ExpireAt,
                DailyLimit = dc.DailyLimit,
                IsActive = dc.IsActive
            }).ToList()
        );
    }
    public async Task<PagedResultDto<CreditCardListItemDto>> GetMyCreditCardsAsync(MyCreditCardsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var creditCardsQ = await _creditCards.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();

        var q =
            from cc in creditCardsQ
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId
            select cc;

        if (input.CustomerId.HasValue)
            q = q.Where(cc => cc.CustomerId == input.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(input.CardNo))
        {
            var cardNo = NormalizeCardNo(input.CardNo);
            q = q.Where(cc => cc.CardNo == cardNo);
        }

        var total = await AsyncExecuter.CountAsync(q);

        var sorting = string.IsNullOrWhiteSpace(input.Sorting) ? "CardNo" : input.Sorting;
        q = q.OrderBy(x=>x.CardNo);

        var items = await AsyncExecuter.ToListAsync(q.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<CreditCardListItemDto>(
            total,
            items.Select(cc => new CreditCardListItemDto
            {
                Id = cc.Id,
                CustomerId = cc.CustomerId,
                CardNo = cc.CardNo,
                ExpireAt = cc.ExpireAt,
                Limit = cc.Limit,
                CurrentDebt = cc.CurrentDebt,
                IsActive = cc.IsActive
            }).ToList()
        );
    }
    public async Task<PagedResultDto<TransactionListItemDto>> GetMyTransactionsAsync(MyTransactionsInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var txQ = await _tx.GetQueryableAsync();
        var accountsQ = await _accounts.GetQueryableAsync();
        var customersQ = await _customers.GetQueryableAsync();
        var debitCardsQ = await _debitCards.GetQueryableAsync();
        var creditCardsQ = await _creditCards.GetQueryableAsync();

        // Account path
        var qAcc =
            from t in txQ
            join a in accountsQ on t.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "Account", Iban = a.Iban, CardNo = (string?)null, CustomerName = c.Name };

        // DebitCard path
        var qDc =
            from t in txQ
            join dc in debitCardsQ on t.DebitCardId equals dc.Id
            join a in accountsQ on dc.AccountId equals a.Id
            join c in customersQ on a.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "DebitCard", Iban = (string?)null, CardNo = dc.CardNo, CustomerName = c.Name };

        // CreditCard path
        var qCc =
            from t in txQ
            join cc in creditCardsQ on t.CreditCardId equals cc.Id
            join c in customersQ on cc.CustomerId equals c.Id
            where c.UserId == userId
            select new { t, OwnerType = "CreditCard", Iban = (string?)null, CardNo = cc.CardNo, CustomerName = c.Name };

        // unify
        var q = qAcc.Concat(qDc).Concat(qCc);

        // filters
        if (input.AccountId.HasValue)
            q = q.Where(x => x.t.AccountId == input.AccountId.Value);

        if (input.DebitCardId.HasValue)
            q = q.Where(x => x.t.DebitCardId == input.DebitCardId.Value);

        if (input.CreditCardId.HasValue)
            q = q.Where(x => x.t.CreditCardId == input.CreditCardId.Value);

        if (input.From.HasValue)
            q = q.Where(x => x.t.CreationTime >= input.From.Value);

        if (input.To.HasValue)
            q = q.Where(x => x.t.CreationTime <= input.To.Value);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(x =>
                (x.t.Description != null && x.t.Description.Contains(f)) ||
                (x.Iban != null && x.Iban.Contains(f)) ||
                (x.CardNo != null && x.CardNo.Contains(f)) ||
                (x.CustomerName != null && x.CustomerName.Contains(f)));
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderByDescending(x => x.t.CreationTime);

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<TransactionListItemDto>(
            total,
            items.Select(x => new TransactionListItemDto
            {
                Id = x.t.Id,
                OwnerType = x.OwnerType,

                AccountId = x.t.AccountId,
                DebitCardId = x.t.DebitCardId,
                CreditCardId = x.t.CreditCardId,

                Iban = x.Iban,
                CardNo = x.CardNo,
                CustomerName = x.CustomerName,

                TxType = (int)x.t.TxType,
                Amount = x.t.Amount,
                Description = x.t.Description,
                CreationTime = x.t.CreationTime
            }).ToList()
        );
    }


}
