using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BankApiAbp.Data;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.EntityFrameworkCore;

public class EntityFrameworkCoreBankApiAbpDbSchemaMigrator
    : IBankApiAbpDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreBankApiAbpDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve the BankApiAbpDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<BankApiAbpDbContext>()
            .Database
            .MigrateAsync();
    }
}
