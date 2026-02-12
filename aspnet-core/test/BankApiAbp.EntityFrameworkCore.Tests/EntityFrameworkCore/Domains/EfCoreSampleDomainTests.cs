using BankApiAbp.Samples;
using Xunit;

namespace BankApiAbp.EntityFrameworkCore.Domains;

[Collection(BankApiAbpTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<BankApiAbpEntityFrameworkCoreTestModule>
{

}
