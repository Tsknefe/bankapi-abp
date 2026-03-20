using BankApiAbp.Banking;
using BankApiAbp.Entities;
using FluentAssertions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public class TestDataSeeder : ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public TestDataSeeder(
        IdentityUserManager userManager,
        IdentityRoleManager roleManager,
        IRepository<IdentityUser, Guid> users,
        IRepository<Customer, Guid> customers,
        IRepository<Account, Guid> accounts,
        IPermissionDataSeeder permissionDataSeeder)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _users = users;
        _customers = customers;
        _accounts = accounts;
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public async Task SeedAsync()
    {
        var adminRole = await EnsureAdminRoleAsync();

        var basicUser = await EnsureUserAsync(
            TestUsers.BasicUserId,
            TestUsers.BasicUsername,
            "test_basic@bankapi.local",
            TestUsers.Password,
            adminRole.Name!);

        var rateLimitUser = await EnsureUserAsync(
            TestUsers.RateLimitUserId,
            TestUsers.RateLimitUsername,
            "test_ratelimit@bankapi.local",
            TestUsers.Password,
            adminRole.Name!);

        var concurrentUser = await EnsureUserAsync(
            TestUsers.ConcurrentUserId,
            TestUsers.ConcurrentUsername,
            "test_concurrent@bankapi.local",
            TestUsers.Password,
            adminRole.Name!);

        var basicCustomer = await EnsureCustomerAsync(
            TestUsers.BasicCustomerId,
            basicUser.Id,
            "Test Basic Customer",
            "10000000001");

        var rateLimitCustomer = await EnsureCustomerAsync(
            TestUsers.RateLimitCustomerId,
            rateLimitUser.Id,
            "Test RateLimit Customer",
            "10000000002");

        var concurrentCustomer = await EnsureCustomerAsync(
            TestUsers.ConcurrentCustomerId,
            concurrentUser.Id,
            "Test Concurrent Customer",
            "10000000003");

        await EnsureAccountAsync(
            TestUsers.BasicAccountA,
            basicCustomer.Id,
            "Basic Account A",
            "TR10000000000000000000000000000001",
            1000m);

        await EnsureAccountAsync(
            TestUsers.BasicAccountB,
            basicCustomer.Id,
            "Basic Account B",
            "TR10000000000000000000000000000002",
            1000m);

        await EnsureAccountAsync(
            TestUsers.RateLimitAccountA,
            rateLimitCustomer.Id,
            "RateLimit Account A",
            "TR20000000000000000000000000000001",
            1000m);

        await EnsureAccountAsync(
            TestUsers.RateLimitAccountB,
            rateLimitCustomer.Id,
            "RateLimit Account B",
            "TR20000000000000000000000000000002",
            1000m);

        await EnsureAccountAsync(
            TestUsers.ConcurrentAccountA,
            concurrentCustomer.Id,
            "Concurrent Account A",
            "TR30000000000000000000000000000001",
            1000m);

        await EnsureAccountAsync(
            TestUsers.ConcurrentAccountB,
            concurrentCustomer.Id,
            "Concurrent Account B",
            "TR30000000000000000000000000000002",
            1000m);
    }

    private async Task<IdentityRole> EnsureAdminRoleAsync()
    {
        var adminRole =
            await _roleManager.FindByNameAsync("admin")
            ?? await _roleManager.FindByNameAsync("administrator");

        if (adminRole == null)
        {
            adminRole = new IdentityRole(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                "admin");

            var result = await _roleManager.CreateAsync(adminRole);
            result.Succeeded.Should().BeTrue(
                string.Join(" | ", result.Errors.Select(x => $"{x.Code}:{x.Description}")));
        }

        await SeedBankingPermissionsAsync(adminRole.Id);
        return adminRole;
    }

    private async Task<IdentityUser> EnsureUserAsync(
        Guid userId,
        string username,
        string email,
        string password,
        string roleName)
    {
        var user = await _users.FindAsync(userId)
                   ?? await _userManager.FindByNameAsync(username)
                   ?? await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new IdentityUser(userId, username, email);

            var createResult = await _userManager.CreateAsync(user, password);
            createResult.Succeeded.Should().BeTrue(
                string.Join(" | ", createResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
        }
        else
        {
            if (user.Id != userId)
            {
                throw new Exception(
                    $"Seed user conflict. ExpectedId={userId}, ActualId={user.Id}, Username={username}");
            }

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, password);

            if (!resetResult.Succeeded)
            {
                var removePasswordResult = await _userManager.RemovePasswordAsync(user);

                if (!removePasswordResult.Succeeded)
                {
                    throw new Exception(
                        "Mevcut test kullanıcısının şifresi resetlenemedi: " +
                        string.Join(" | ", removePasswordResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
                }

                var addPasswordResult = await _userManager.AddPasswordAsync(user, password);
                addPasswordResult.Succeeded.Should().BeTrue(
                    string.Join(" | ", addPasswordResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
            }
        }

        var inRole = await _userManager.IsInRoleAsync(user, roleName);
        if (!inRole)
        {
            var addRoleResult = await _userManager.AddToRoleAsync(user, roleName);
            addRoleResult.Succeeded.Should().BeTrue(
                string.Join(" | ", addRoleResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
        }

        return user;
    }

    private async Task<Customer> EnsureCustomerAsync(
        Guid customerId,
        Guid userId,
        string name,
        string tcNo)
    {
        var customer = await _customers.FindAsync(customerId);
        if (customer != null)
            return customer;

        customer = new Customer(
            customerId,
            userId,
            name,
            tcNo,
            new DateTime(1999, 1, 1),
            "Konya");

        await _customers.InsertAsync(customer, autoSave: true);
        return customer;
    }

    private async Task EnsureAccountAsync(
        Guid accountId,
        Guid customerId,
        string name,
        string iban,
        decimal balance)
    {
        var account = await _accounts.FindAsync(accountId);
        if (account != null)
            return;

        account = new Account(
            accountId,
            customerId,
            name,
            iban,
            (AccountType)0,
            balance);

        await _accounts.InsertAsync(account, autoSave: true);
    }

    private async Task SeedBankingPermissionsAsync(Guid adminRoleId)
    {
        var permissions = new[]
        {
            BankingPermissions.Customers.Default,
            BankingPermissions.Customers.Create,
            BankingPermissions.Customers.Read,
            BankingPermissions.Customers.List,

            BankingPermissions.Accounts.Default,
            BankingPermissions.Accounts.Create,
            BankingPermissions.Accounts.Read,
            BankingPermissions.Accounts.List,
            BankingPermissions.Accounts.Deposit,
            BankingPermissions.Accounts.Transfer,
            BankingPermissions.Accounts.Withdraw,
            BankingPermissions.Accounts.Statement,
            BankingPermissions.Accounts.Summary,

            BankingPermissions.DebitCards.Default,
            BankingPermissions.DebitCards.Create,
            BankingPermissions.DebitCards.Read,
            BankingPermissions.DebitCards.List,
            BankingPermissions.DebitCards.Spend,
            BankingPermissions.DebitCards.SpendSummary,

            BankingPermissions.CreditCards.Default,
            BankingPermissions.CreditCards.Create,
            BankingPermissions.CreditCards.Read,
            BankingPermissions.CreditCards.List,
            BankingPermissions.CreditCards.Spend,
            BankingPermissions.CreditCards.Pay,
            BankingPermissions.CreditCards.SpendSummary,

            BankingPermissions.Transactions.Default,
            BankingPermissions.Transactions.List,
            BankingPermissions.Transactions.Read,

            BankingPermissions.Dashboard.Default,
            BankingPermissions.Dashboard.Summary
        };

        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            adminRoleId.ToString(),
            permissions);
    }
}