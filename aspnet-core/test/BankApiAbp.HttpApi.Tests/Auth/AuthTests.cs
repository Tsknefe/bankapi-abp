using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Auth;

public class AuthTests
{
    [Fact]
    public async Task Should_Get_Token()
    {
        using var client = TestClientFactory.Create();

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "BankApiAbp_Swagger",
            ["scope"] = "BankApiAbp",
            ["username"] = "efe",
            ["password"] = "Qwe123!"
        };

        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(payload)
        );

        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        body.Should().Contain("access_token");
    }
}