using FluentAssertions;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Auth;

public class AuthTests
{
    [Fact]
    public async Task Should_Get_Token()
    {
        var client = TestClientFactory.CreateClient();

        var token = await TestAuthHelpers.GetTokenAsync(client);

        token.Should().NotBeNullOrWhiteSpace();
    }
}