using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace BankApiAbp.Banking;

public class BankingPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IIdentityRoleRepository _roleRepository;

    public BankingPermissionDataSeedContributor(
        IPermissionDataSeeder permissionDataSeeder,
        IIdentityRoleRepository roleRepository)
    {
        _permissionDataSeeder = permissionDataSeeder;
        _roleRepository = roleRepository;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var adminRole =
            await _roleRepository.FindByNormalizedNameAsync("ADMIN")
            ?? await _roleRepository.FindByNormalizedNameAsync("ADMINISTRATOR");

        if (adminRole == null)
            return;

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
            providerName: RolePermissionValueProvider.ProviderName,
            providerKey: adminRole.Id.ToString(),
            grantedPermissions: permissions);
    }
}