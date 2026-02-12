using OpenIddict.Abstractions;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.DbMigrator;

public class OpenIddictScopeSeeder : ITransientDependency
{
    private readonly IOpenIddictScopeManager _scopeManager;

    public OpenIddictScopeSeeder(IOpenIddictScopeManager scopeManager)
    {
        _scopeManager = scopeManager;
    }

    public async Task SeedAsync()
    {
        if (await _scopeManager.FindByNameAsync("BankApiAbp") != null)
            return;

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = "BankApiAbp",
            DisplayName = "BankApiAbp API"
        };

        descriptor.Resources.Add("BankApiAbp");

        await _scopeManager.CreateAsync(descriptor);
    }
}
