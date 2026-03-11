using System.Net.Http.Headers;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Accounts;

public class AccountSummaryTests
{
    private static readonly Guid AccountA =
        Guid.Parse("3a1f9cad-8add-0dd1-3772-511a6d1f7204");

    [Fact]
    public async Task Summary_Should_Return_200()
    {
        using var client = TestClientFactory.Create();

        var token = await GetToken(client);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"/api/app/banking/account-summary/{AccountA}");

        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");
    }

    private static async Task<string> GetToken(HttpClient client)
    {
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

        var token = System.Text.Json.JsonDocument
            .Parse(body)
            .RootElement
            .GetProperty("access_token")
            .GetString();

        return token!;
    }
}