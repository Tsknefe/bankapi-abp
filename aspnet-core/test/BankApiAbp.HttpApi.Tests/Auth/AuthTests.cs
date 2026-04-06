using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Auth;

public class AuthTests
{
    [Fact]
    public async Task Should_Get_Token()
    {
        using var client = TestClientFactory.CreateClient();

        var token = await TestAuthHelpers.GetTokenAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        token.Should().NotBeNullOrWhiteSpace();
    }
}