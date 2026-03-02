using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Cards;
using BankApiAbp.Permissions;
using BankApiAbp.Transactions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    [Authorize(BankingPermissions.CreditCards.Create)]
    public async Task<IdResponseDto> CreateCreditCardAsync(CreateCreditCardDto input)
    {
        var userId = CurrentUserIdOrThrow();

        var cardNo = NormalizeCardNo(input.CardNo);
        _ = await GetCustomerOwnedAsync(input.CustomerId);

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

    [Authorize(BankingPermissions.CreditCards.Spend)]
    public async Task CreditCardSpendAsync(CardSpendDto input)
    {
        var userId = CurrentUserIdOrThrow();
        var operation = "creditcards.spend";
        var key = GetIdempotencyKeyOrThrow(operation);

        var cardNo = NormalizeCardNo(input.CardNo);
        var requestHash = BuildRequestHash(cardNo, input.Amount, input.Description, input.Cvv);

        var (isDuplicate, record) = await _idem.TryBeginAsync(userId, operation, key, requestHash);

        if (isDuplicate)
        {
            if (record.Status == "Completed" && (record.ResponseStatusCode == 204 || record.ResponseStatusCode == 200))
                return;

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            return;
        }

        try
        {
            var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

            await using var handle = await _distributedLock.TryAcquireAsync(
                $"creditcard:{card.Id}",
                TimeSpan.FromSeconds(10)
            );

            if (handle == null)
                throw new UserFriendlyException("Kart şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            await _retry.ExecuteAsync(async ct =>
            {
                var lockedCard = await GetCreditCardOwnedByCardNoAsync(cardNo);

                var now = Clock.Now;
                lockedCard.EnsureUsable(now);
                lockedCard.VerifyCvv(input.Cvv);

                lockedCard.Spend(input.Amount);

                await _creditCards.UpdateAsync(lockedCard, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.CreditCardSpend,
                    input.Amount,
                    input.Description,
                    null,
                    null,
                    lockedCard.Id
                ), autoSave: true);
            });

            await _idem.CompleteAsync(record, new { Ok = true }, 204); 
        }
        catch (Exception ex)
        {
            await _idem.FailAsync(record, ex);
            throw;
        }
    }


    [Authorize(BankingPermissions.CreditCards.Pay)]
    public async Task CreditCardPayAsync(CreditCardPayDto input)
    {
        var userId = CurrentUserIdOrThrow();
        var operation = "creditcards.pay";
        var key = GetIdempotencyKeyOrThrow(operation);

        var cardNo = NormalizeCardNo(input.CardNo);
        var requestHash = BuildRequestHash(cardNo, input.AccountId, input.Amount, input.Description /*, input.Cvv*/);

        var (isDuplicate, record) = await _idem.TryBeginAsync(userId, operation, key, requestHash);

        if (isDuplicate)
        {
            if (record.Status == "Completed" && (record.ResponseStatusCode == 204 || record.ResponseStatusCode == 200))
                return;

            await _idem.GetOrThrowDuplicateResponseAsync(record);
            return;
        }

        try
        {
            var card = await GetCreditCardOwnedByCardNoAsync(cardNo);
            var now = Clock.Now;

            card.EnsureUsable(now);
            card.VerifyCvv(input.Cvv);

            await using var accountHandle = await _distributedLock.TryAcquireAsync(
                $"account:{input.AccountId}",
                TimeSpan.FromSeconds(10)
            );
            if (accountHandle == null)
                throw new UserFriendlyException("Hesap şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            await using var cardHandle = await _distributedLock.TryAcquireAsync(
                $"creditcard:{card.Id}",
                TimeSpan.FromSeconds(10)
            );
            if (cardHandle == null)
                throw new UserFriendlyException("Kart şu anda başka bir işlem tarafından kullanılıyor. Lütfen tekrar deneyin.");

            await _retry.ExecuteAsync(async ct =>
            {
                var account = await _rowLock.LockAccountForUpdateAsync(input.AccountId, ct);
                await EnsureAccountOwnedAsync(account.Id, ct);

                var lockedCard = await GetCreditCardOwnedByCardNoAsync(cardNo);

                if (account.Balance < input.Amount)
                    throw new BusinessException("INSUFFICIENT_BALANCE")
                        .WithData("Balance", account.Balance)
                        .WithData("Amount", input.Amount);

                if (lockedCard.CurrentDebt < input.Amount)
                    throw new BusinessException("PAYMENT_EXCEEDS_DEBT")
                        .WithData("CurrentDebt", lockedCard.CurrentDebt)
                        .WithData("Amount", input.Amount);

                account.Withdraw(input.Amount);
                lockedCard.Pay(input.Amount);

                await _accounts.UpdateAsync(account, autoSave: true);
                await _creditCards.UpdateAsync(lockedCard, autoSave: true);

                await _tx.InsertAsync(new Transaction(
                    GuidGenerator.Create(),
                    TransactionType.CreditCardPayment,
                    input.Amount,
                    input.Description,
                    account.Id,
                    null,
                    lockedCard.Id
                ), autoSave: true);
            });

            await _idem.CompleteAsync(record, new { Ok = true }, 204);
        }
        catch (Exception ex)
        {
            await _idem.FailAsync(record, ex);
            throw;
        }
    }
    

    [Authorize(BankingPermissions.CreditCards.Read)]
    public async Task<CreditCardDto> GetCreditCardDto(string cardNo)
    {
        cardNo = NormalizeCardNo(cardNo);
        var card = await GetCreditCardOwnedByCardNoAsync(cardNo);

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

    [Authorize(BankingPermissions.CreditCards.SpendSummary)]
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

    [Authorize(BankingPermissions.CreditCards.List)]
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
            var cn = NormalizeCardNo(input.CardNo);
            q = q.Where(cc => cc.CardNo == cn);
        }

        var total = await AsyncExecuter.CountAsync(q);
        q = q.OrderBy(x => x.CardNo);

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
}