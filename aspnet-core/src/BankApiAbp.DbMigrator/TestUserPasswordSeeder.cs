using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.DbMigrator;

public class TestUserPasswordSeeder : ITransientDependency
{
    public Task SeedAsync()
    {
        return Task.CompletedTask;
    }
}