using System.Text.Json;
using System.Net.Http.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Accounts;

public class AccountDetailCacheTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;

    [Fact]
    public async Task GetAccount_Should_Return_Updated_Balance_After_Deposit()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var before = await GetAccountBalance(client, AccountA);

        var depositPayload = new
        {
            accountId = AccountA,
            amount = 7m,
            description = "account detail cache invalidation test"
        };

        var depositRequest = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/deposit");
        depositRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        depositRequest.Content = JsonContent.Create(depositPayload);

        var depositResponse = await client.SendAsync(depositRequest);
        var depositBody = await depositResponse.Content.ReadAsStringAsync();

        depositResponse.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)depositResponse.StatusCode}, Body={depositBody}");

        var after = await GetAccountBalance(client, AccountA);

        after.Should().Be(before + 7m);
    }

    private static async Task<decimal> GetAccountBalance(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/app/banking/{accountId}/account");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("balance", out var balanceProp))
            return balanceProp.GetDecimal();

        throw new Exception("Account detail response içinde balance alanı bulunamadı.");
    }
}