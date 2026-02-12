using Xunit;

namespace BankApiAbp.EntityFrameworkCore;

[CollectionDefinition(BankApiAbpTestConsts.CollectionDefinitionName)]
public class BankApiAbpEntityFrameworkCoreCollection : ICollectionFixture<BankApiAbpEntityFrameworkCoreFixture>
{

}
