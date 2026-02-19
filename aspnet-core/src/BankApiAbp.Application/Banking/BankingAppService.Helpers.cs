using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Users;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
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

    private static UserFriendlyException ConcurrencyFriendly()
        => new UserFriendlyException("İşlem aynı anda başka bir istekle çakıştı. Lütfen tekrar deneyin.");

    private static bool IsConcurrency(Exception ex)
        => ex is AbpDbConcurrencyException
           || ex is DbUpdateConcurrencyException;

    private static Task SmallBackoffAsync(int attempt)
    {
        var ms = attempt switch { 1 => 20, 2 => 40, _ => 80 };
        return Task.Delay(ms);
    }
}
