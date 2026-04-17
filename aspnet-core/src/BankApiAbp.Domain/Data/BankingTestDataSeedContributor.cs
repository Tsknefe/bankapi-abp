using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

using BankApiAbp.Entities;
using BankApiAbp.Banking;
using BankApiAbp.TestData;
using Microsoft.AspNetCore.Identity;

namespace BankApiAbp.Data;

public class BankingTestDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IIdentityUserRepository _userRepository;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<Customer, Guid> _customerRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly ILogger<BankingTestDataSeedContributor> _logger;

    public BankingTestDataSeedContributor(
        IIdentityUserRepository userRepository,
        IdentityUserManager userManager,
        IIdentityRoleRepository roleRepository,
        IRepository<Customer, Guid> customerRepository,
        IRepository<Account, Guid> accountRepository,
        ILogger<BankingTestDataSeedContributor> logger)
    {
        _userRepository = userRepository;
        _userManager = userManager;
        _roleRepository = roleRepository;
        _customerRepository = customerRepository;
        _accountRepository = accountRepository;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        await EnsureUserWithAccountsAsync(
            userId: TestUserIds.TestBasicUserId,
            username: "test_basic",
            email: "test_basic@bankapi.local",
            password: "Qwe123!",
            customerId: TestUserIds.TestBasicCustomerId,
            customerName: "Test Basic User",
            tcNo: "11111111110",
            birthDate: new DateTime(2000, 1, 1),
            birthPlace: "Konya",
            accountAId: TestAccountIds.BasicAccountA,
            accountBId: TestAccountIds.BasicAccountB,
            accountAName: "Basic Account A",
            accountBName: "Basic Account B",
            ibanA: "TR000000000000000000000001",
            ibanB: "TR000000000000000000000002",
            initialBalanceA: 1000m,
            initialBalanceB: 1000m
        );

        await EnsureUserWithAccountsAsync(
            userId: TestUserIds.TestRateLimitUserId,
            username: "test_ratelimit",
            email: "test_ratelimit@bankapi.local",
            password: "Qwe123!",
            customerId: TestUserIds.TestRateLimitCustomerId,
            customerName: "Test RateLimit User",
            tcNo: "22222222220",
            birthDate: new DateTime(2000, 2, 2),
            birthPlace: "Ankara",
            accountAId: TestAccountIds.RateLimitAccountA,
            accountBId: TestAccountIds.RateLimitAccountB,
            accountAName: "RateLimit Account A",
            accountBName: "RateLimit Account B",
            ibanA: "TR000000000000000000000003",
            ibanB: "TR000000000000000000000004",
            initialBalanceA: 1000m,
            initialBalanceB: 1000m
        );

        await EnsureUserWithAccountsAsync(
            userId: TestUserIds.TestConcurrentUserId,
            username: "test_concurrent",
            email: "test_concurrent@bankapi.local",
            password: "Qwe123!",
            customerId: TestUserIds.TestConcurrentCustomerId,
            customerName: "Test Concurrent User",
            tcNo: "33333333330",
            birthDate: new DateTime(2000, 3, 3),
            birthPlace: "Istanbul",
            accountAId: TestAccountIds.ConcurrentAccountA,
            accountBId: TestAccountIds.ConcurrentAccountB,
            accountAName: "Concurrent Account A",
            accountBName: "Concurrent Account B",
            ibanA: "TR000000000000000000000005",
            ibanB: "TR000000000000000000000006",
            initialBalanceA: 1000m,
            initialBalanceB: 1000m
        );
    }

    private async Task EnsureUserWithAccountsAsync(
        Guid userId,
        string username,
        string email,
        string password,
        Guid customerId,
        string customerName,
        string tcNo,
        DateTime birthDate,
        string birthPlace,
        Guid accountAId,
        Guid accountBId,
        string accountAName,
        string accountBName,
        string ibanA,
        string ibanB,
        decimal initialBalanceA,
        decimal initialBalanceB)
    {
        var normalizedUserName = username.ToUpperInvariant();
        var user = await _userRepository.FindByNormalizedUserNameAsync(normalizedUserName);

        if (user == null)
        {
            user = new IdentityUser(userId, username, email);

            var createResult = await _userManager.CreateAsync(user, password);
            createResult.CheckErrors();

            _logger.LogInformation("Test user created: {Username}", username);
        }

        await EnsureAdminRoleAsync(user);

        var customer = await _customerRepository.FirstOrDefaultAsync(x => x.Id == customerId);
        if (customer == null)
        {
            customer = new Customer(
                customerId,
                user.Id,
                customerName,
                tcNo,
                birthDate,
                birthPlace
            );

            await _customerRepository.InsertAsync(customer, autoSave: true);

            _logger.LogInformation("Test customer created: {CustomerName}", customerName);
        }

        await EnsureAccountAsync(
            accountId: accountAId,
            customerId: customerId,
            accountName: accountAName,
            iban: ibanA,
            initialBalance: initialBalanceA
        );

        await EnsureAccountAsync(
            accountId: accountBId,
            customerId: customerId,
            accountName: accountBName,
            iban: ibanB,
            initialBalance: initialBalanceB
        );
    }

    private async Task EnsureAdminRoleAsync(IdentityUser user)
    {
        var adminRole = await _roleRepository.FindByNormalizedNameAsync("ADMIN");
        if (adminRole == null)
        {
            _logger.LogWarning("Admin role bulunamadı. Kullanıcı role atanmadı: {Username}", user.UserName);
            return;
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(adminRole.Name!))
        {
            var result = await _userManager.AddToRoleAsync(user, adminRole.Name!);
            result.CheckErrors();

            _logger.LogInformation("User added to Admin role: {Username}", user.UserName);
        }
    }

    private async Task EnsureAccountAsync(
        Guid accountId,
        Guid customerId,
        string accountName,
        string iban,
        decimal initialBalance)
    {
        var existing = await _accountRepository.FirstOrDefaultAsync(x => x.Id == accountId);
        if (existing != null)
        {
            return;
        }

        var account = new Account(
            accountId,
            customerId,
            accountName,
            iban,
            AccountType.Current,
            initialBalance
        );

        await _accountRepository.InsertAsync(account, autoSave: true);

        _logger.LogInformation("Test account created: {Iban}", iban);
    }
}