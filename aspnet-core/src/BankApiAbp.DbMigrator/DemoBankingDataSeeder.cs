using System;
using System.Threading.Tasks;
using BankApiAbp.Banking;
using BankApiAbp.Entities;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace BankApiAbp.DbMigrator;

public class DemoBankingDataSeeder : ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly ILogger<DemoBankingDataSeeder> _logger;

    public DemoBankingDataSeeder(
        IdentityUserManager userManager,
        IRepository<Customer, Guid> customers,
        IRepository<Account, Guid> accounts,
        ILogger<DemoBankingDataSeeder> logger)
    {
        _userManager = userManager;
        _customers = customers;
        _accounts = accounts;
        _logger = logger;
    }

    [UnitOfWork]
    public async Task SeedAsync()
    {
        await SeedAdminBankingAsync();
    }

    private async Task SeedAdminBankingAsync()
    {
        var adminUser = await _userManager.FindByNameAsync("admin");
        if (adminUser == null)
        {
            _logger.LogWarning("User not found for banking seed: admin");
            return;
        }

        var adminCustomerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var customer = await _customers.FindAsync(adminCustomerId);
        if (customer == null)
        {
            customer = new Customer(
                adminCustomerId,
                adminUser.Id,
                "Admin Customer",
                "10000000001",
                new DateTime(1999, 1, 1),
                "Konya");

            await _customers.InsertAsync(customer, autoSave: true);
            _logger.LogInformation("Customer created: {CustomerName}", customer.Name);
        }

        var adminAccountAId = Guid.Parse("90000000-0000-0000-0000-000000000001");
        var adminAccountBId = Guid.Parse("90000000-0000-0000-0000-000000000002");

        var accountA = await _accounts.FindAsync(adminAccountAId);
        if (accountA == null)
        {
            accountA = new Account(
                adminAccountAId,
                customer.Id,
                "Admin Account A",
                "TR90000000000000000000000000000001",
                (AccountType)0,
                10000m);

            await _accounts.InsertAsync(accountA, autoSave: true);
            _logger.LogInformation("Account created: {AccountName}", accountA.Name);
        }

        var accountB = await _accounts.FindAsync(adminAccountBId);
        if (accountB == null)
        {
            accountB = new Account(
                adminAccountBId,
                customer.Id,
                "Admin Account B",
                "TR90000000000000000000000000000002",
                (AccountType)0,
                5000m);

            await _accounts.InsertAsync(accountB, autoSave: true);
            _logger.LogInformation("Account created: {AccountName}", accountB.Name);
        }
    }
}