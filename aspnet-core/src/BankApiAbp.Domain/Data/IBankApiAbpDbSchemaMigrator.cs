using System.Threading.Tasks;

namespace BankApiAbp.Data;

public interface IBankApiAbpDbSchemaMigrator
{
    Task MigrateAsync();
}
