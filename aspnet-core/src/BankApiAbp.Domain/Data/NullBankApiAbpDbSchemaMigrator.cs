using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.Data;

/* This is used if database provider does't define
 * IBankApiAbpDbSchemaMigrator implementation.
 */
public class NullBankApiAbpDbSchemaMigrator : IBankApiAbpDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
